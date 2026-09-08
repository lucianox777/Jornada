# ADR — Cutover operacional do `initial_uuid` no Processor

## Status

**Proposto para validação técnica.** Este ADR não ativa o Linkage probabilístico e não altera a política de atribuição canônica por CPF.

## Contexto

A persistência progressiva já garante um `initial_uuid` imutável por `silver.pessoa_origem`, com histórico append-only e implementação equivalente em SQL Server e PostgreSQL. Essa persistência foi introduzida de forma aditiva e opt-in para permitir validação isolada antes de qualquer dependência operacional do Processor.

O cutover precisa satisfazer simultaneamente quatro propriedades:

1. nenhuma origem histórica pode ficar sem `initial_uuid` quando a garantia operacional for declarada ativa;
2. o preenchimento histórico não pode exigir uma transação única sobre toda a população;
3. uma nova observação processada deve adquirir a referência inicial na mesma transação do lote;
4. rollback do lote deve reverter também qualquer Pessoa e referência progressiva criadas nessa tentativa.

## Decisão

O cutover é executado em duas fases explícitas.

### 1. Backfill paginado e retomável

O `ProgressiveIdentityOriginStore.BackfillPageAsync` permanece o mecanismo de preenchimento histórico. Cada origem é confirmada em sua própria transação e o executor pode ser interrompido e retomado. O cutover operacional **falha fechado** enquanto existir qualquer `silver.pessoa_origem` sem linha correspondente em `identidade.pessoa_origem_progressiva`.

A migração de cutover não percorre toda a população. Isso evita transação longa, crescimento desnecessário de log/WAL e bloqueio incompatível com o volume esperado de uma base municipal.

### 2. Garantia transacional para novas observações

Após o backlog histórico chegar a zero, é ativado um trigger `AFTER INSERT` em `identidade.vinculo_fonte`. Para cada origem ainda sem referência progressiva, o trigger chama a operação idempotente de criação do `initial_uuid`.

Esse ponto foi escolhido porque o vínculo é gravado pelo Processor depois da observação Silver existir e antes do commit do lote. Assim, a criação da referência participa da mesma transação: se o processamento falhar, o `initial_uuid`, seu evento e a Pessoa criada são revertidos junto com o lote.

No SQL Server o trigger **não** é instalado em `silver.pessoa_origem`. O Processor usa `OUTPUT INSERTED` diretamente nesse INSERT, e uma trigger habilitada no alvo tornaria esse padrão incompatível sem uma alteração coordenada do writer. `identidade.vinculo_fonte` não possui essa limitação no fluxo atual.

## Invariantes após o cutover

- toda origem histórica possui exatamente um `initial_uuid` antes da ativação;
- toda nova origem que chega ao ponto de vínculo recebe exatamente um `initial_uuid` antes do commit;
- novas versões da mesma origem preservam o UUID inicial e não duplicam o evento `CRIACAO`;
- uma transação abortada não deixa uma Pessoa ou referência progressiva órfã;
- o `initial_uuid` continua distinto da atribuição canônica e não implica que a observação esteja resolvida;
- observações sem CPF continuam `NAO_RESOLVIDO`/`PENDENTE_PROBABILISTICO` enquanto não houver política de Linkage homologada;
- este cutover não ativa Runner probabilístico, não publica decisão de Linkage e não altera Gold/Serving por si só.

## Implantação

1. aplicar a persistência progressiva V1;
2. executar o backfill em páginas limitadas até uma página retornar zero;
3. confirmar por consulta que não existem origens sem `initial_uuid`;
4. aplicar a migração de cutover; ela recusa prosseguir se o backlog reaparecer;
5. manter a migração reentrante e validar o trigger em banco real antes de liberar o Processor.

Durante a janela entre os passos 1 e 4, o Processor deve ser coordenado para que o universo histórico não cresça depois da confirmação de backlog zero. A ativação institucional deve usar o gate serial já existente do pipeline ou uma janela operacional equivalente; não é seguro assumir quiescência sem coordenação.

## Rollback operacional

É possível desativar/remover o trigger para interromper a criação automática em novas observações. As referências progressivas já persistidas não devem ser apagadas nem recicladas: `initial_uuid` é uma referência histórica imutável. Portanto, rollback operacional interrompe a automação futura, mas não desfaz identidades já materializadas.

## Evidência exigida

O workflow dedicado de cutover deve provar em SQL Server e PostgreSQL:

- recusa antes do backfill;
- backfill paginado convergente;
- aplicação reentrante do cutover;
- criação automática por novo `vinculo_fonte`;
- estabilidade em nova versão da mesma origem;
- rollback sem resíduos de Pessoa/referência;
- ausência de ativação do Linkage probabilístico.

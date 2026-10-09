# C3.2f2c — Projeto de prova E2E de rollback e autorrecuperação (em preparação)

> **Nenhuma prova operacional foi executada nesta branch.**
> Preparação técnica para uma PR futura, após merge green da C3.2f2b (#849).
> A prova estrutural C3.2f2a (#848) não atesta origem de dados por si só.

## Ambiente único, descartável e autorizado

A execução deve ocorrer somente no **mesmo** GitHub Actions job E2E
que já contém SQL bootstrap, API/ResultadoApi, restart C3.2f1 e ingestão
C3.2f2b; **nunca criar outro job .NET nem repetir build da imagem**.
Escopo obrigatório: Compose
`jornada-workers-e2e-ci<GITHUB_RUN_ID><GITHUB_RUN_ATTEMPT>`,
banco exclusivamente `JornadaE2E`, marcador SQL Development,
labels de contêiner exatos e volumes exclusivos do projeto, sem
portas do host, bind mounts nem comunicação com Compose NODE.
O ensaio pode criar objetos/transações temporários **somente neste
banco sintético efêmero**. Jamais tocar JornadaLocal, IBGE original,
HML/PROD, SQL de usuário ou seu Docker host ordinário.

## Hipótese técnica: barreira transacional *sem alterar o produto*

`SqlProcessorRepository.PersistValidatedAsync`:
1. grava `ingestao.lote.status=PROCESSANDO` e comita **antes** da
   transação de persistência;
2. inicia uma transação SQL **Serializable** e grava primeira pessoa em
   `silver.pessoa_observacao`;
3. no mesmo commit, grava fatos/itens e publica `PROCESSADO`, com
   token `lease_id` e `ingestao.lote_heartbeat` ainda válidos.

Uma opção de instrumentação **somente para ensaio privado** é instalar,
depois do bootstrap SQL, um `AFTER INSERT` temporário na
`identidade.vinculo_fonte`, após a inserção da Pessoa em Silver,
com predicado de
`ingestao.entrega.idempotency_key='ci-e2e-recovery-<run>-<attempt>'`.
O próprio Processor faz INSERT em Silver com OUTPUT INSERTED sem INTO,
incompatível com trigger AFTER INSERT na tabela Silver. O vínculo de
identidade é escrito depois, na **mesma transação Serializable**, sem
OUTPUT; seu trigger é compatível. A auditoria do requeue real usa
AFTER UPDATE em ingestao.lote, onde os comandos existentes empregam
OUTPUT ... INTO; os eventos são transacionais e só persistem quando
o UPDATE de recuperação comita.

Para esse lote e **apenas esse lote**, ele bloqueia a transação por
um intervalo definido e limitado (`WAITFOR DELAY`, exemplo 90 s).
O gatilho NÃO pode existir no schema de produto, nem interferir no
ZIP baseline C3.2f2b. Se não puder isolar o trigger com exatidão,
não usar essa estratégia. O predicado deve ser conferido via teste
negativo no banco efêmero com Entrega não alvo.

### Condições anteriores à interrupção

O observador (outra conexão SQL ao banco efêmero) deve comprovar
simultaneamente:
- entrega real criada por HTTP com uma **chave nova** e ZIP sintético,
  hash e UUID aferidos pela API;
- lote em `PROCESSANDO`, com tentativa >=1, `lease_id` e
  `lease_owner` não nulos, `ingestao.lote_heartbeat.lease_id`
  exatamente igual ao do lote;
- ao menos uma inserção `silver.pessoa_observacao` do lote visível
  **apenas com READ UNCOMMITTED**, e zero linhas desse lote visíveis
  para leitura comprometida (com timeout curto e observação de
  bloqueio); esta diferença não prova sozinha rollback — apenas
  transação ainda não commitada;
- uma transação SQL ativa associada ao Processor no mesmo período,
  verificada por DMV/exec requests ou instrumentação equivalente,
  **sem confundir a transação de leitura do observador**;
- antes de SIGKILL, contadores `tentativa_count` e
  `recuperacao_count` guardados junto com o token antigo.

**Não** considerar `WAITFOR` instalado como prova de que ele foi
atingido; esperar evidência observável de escrita não commitada.

### Falha e recuperação

Depois das verificações, usar **somente o mecanismo de injeção já
validado pela C3.2f1** no contêiner `processor` allowlisted da rede
GitHub-hosted; deve provar exit 137, novo PID host,
`RestartCount` crescente e novo
`controle.runtime_componente.instance_id`, com API/Resultado/SQL/
workers irmãos estáveis.

Depois do SIGKILL:
1. observar transação encerrada e **rollback real** da inserção
   não commitada (`silver.pessoa_observacao` do lote ausente);
   a linha `ingestao.lote` ainda deverá estar em `PROCESSANDO`
   até vencer o token, sem publicar `PROCESSADO`;
2. monitorar vencimento efetivo do lease a partir da linha
   `ingestao.lote_heartbeat` com o **mesmo** `lease_id`.
   Snapshot `lote.lease_expira_em` isoladamente não serve;
3. acompanhar recuperação em loop do Processor:
   `recuperacao_count` aumenta, heartbeat/token antigo são
   eliminados por fencing, o lote volta a `PENDENTE` antes da
   reserva seguinte. Se intervalo de polling não conseguir observar
   `PENDENTE`, coletar evento SQL de recuperação inequívoco; não
   inventar transição;
4. ver nova reserva com novo token/owner e
   `tentativa_count` maior, seguida do commit `PROCESSADO`
   e Entrega `PROCESSADA`;
5. confirmar por entrega/lote 1 pessoa, 1 fato, 2
   `ingestao.item_processado`, Serving/Gold consistentes,
   `lease_id=NULL`, heartbeat removido e nenhuma duplicação por
   código de negócio;
6. fazer segundo POST HTTP do **mesmo ZIP e chave**, exigir
   `entregaId` idêntico, nenhuma Entrega/Lote extra e cardinalidades
   idênticas em Silver/Gold/Serving.

## Dimensionamento e tempo

O lease padrão do Processor é 120 s, heartbeat de 30 s e scan de
recuperação de 30 s. Para evitar que esta prova acrescente vários
minutos ao E2E, **somente no Compose CI isolado** e de forma
explícita pode-se usar
`Processor__LeaseDurationSeconds=35`,
`Processor__HeartbeatSeconds=5`,
`Processor__RecoveryScanSeconds=5`.
`IngestionProcessor` e `SqlProcessorRepository` aplicam piso de
30 s ao lease. Confirmar via SQL que heartbeat vivo renova o
token **antes** da falha, e que a expiração posterior se deve à
morte real, não a starvation do runner. Não alterar essas
configurações no Compose operacional original.

## Critérios para evitar falsos positivos

- Um gatilho com `WAITFOR` pode terminar por timeout e permitir
  **commit normal**: o teste deve abortar se não presenciar escrita
  não commitada **antes** de SIGKILL.
- Nunca concluir rollback por comparação de PIDs ou apenas por
  `recuperacao_count`. Exigir inserção observada como não commitada,
  seguida de ausência após falha.
- Nem toda `Silver` é materializada com nova linha para um mesmo
  código de origem: usar códigos sintéticos do ZIP e verificar
  cardinalidades reais por Entrega/Lote.
- Coleta SQL e log Docker precisam ocorrer em pontos independentes,
  gravando campos de timestamp `observed_at_utc` dos estágios;
  não construir um JSON de prova apenas com constantes `True`.
- Validador `scripts/e2e-lot-recovery-evidence-gate.py` só
  checa *coerência do JSON*. CI deve autenticar a proveniência
  capturando consultas SQL, exit code real e hashes do ZIP.
- Falhar fechado quando não houver versão segura do trigger
  privado, quando o processo não morrer realmente, quando houver
  divergência de token/lease ou quando SQL não provar rollback.
  Falhas ou timeouts nunca são motivo para dispensar gates.

## Critérios de avanço

Este arquivo é um plano; **C3.2f2c somente poderá ser aceita após E2E efetivo**.
Integrar somente depois de C3.2f2b verde na HEAD exata; construir
passos incrementais no mesmo E2E e validar operacionalmente antes
de qualquer merge. A Console global OFF↔ON (C3.3) e botões/Chromium
(C3.4) continuam etapas distintas.

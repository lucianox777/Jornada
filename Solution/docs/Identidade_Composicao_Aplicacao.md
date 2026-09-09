# Aplicação governada da composição de identidade

## Escopo desta fatia

Esta etapa implementa a primeira fronteira persistente entre um plano de composição `PREPARADA` e uma composição estrutural efetivamente aplicada na identidade progressiva.

Ela não cria endpoint, worker, agendamento, ativação automática de Linkage probabilístico nem publicação/recomposição de Gold/Serving. A issue #31 continua sendo gate independente de homologação estatística e institucional para resolução probabilística real.

## Estado derivado PREPARADA → APLICADA

`identidade.composicao_plano` continua imutável e permanece fisicamente com `estado='PREPARADA'`. Não há `UPDATE` dessa linha.

Uma decisão passa a ser considerada **APLICADA** somente quando existe exatamente um recibo append-only em `identidade.composicao_aplicacao` para o mesmo `decision_id`, contendo os mesmos hashes de request, plano e reservas do `PREPARADA`.

Assim, a transição é representada por fato adicional, não por mutação destrutiva do ledger anterior.

## Unidade transacional

`IdentityCompositionApplicationService.ApplyAsync` exige uma `DbConnection` aberta e uma `DbTransaction` ativa fornecidas pelo chamador. O serviço não faz commit autônomo.

Dentro da mesma transação:

1. trava e relê o `PREPARADA`;
2. verifica se já existe recibo `APLICADA`; se existir e os hashes/contagens forem idênticos, retorna replay sem repetir efeitos;
3. executa novamente a pré-aplicação autoritativa da PR #51;
4. exige replanning byte-a-byte idêntico ao plano persistido;
5. grava um evento progressivo append-only para cada alteração efetiva;
6. atualiza a linha progressiva correspondente somente na versão esperada;
7. persiste os snapshots históricos previstos pelo plano em `identidade.composicao_historico_aplicado`;
8. grava por último o recibo append-only `identidade.composicao_aplicacao`.

Qualquer falha antes do commit desfaz evento, mudança progressiva, histórico e recibo como uma única unidade.

## Replay e concorrência

O lock da linha `composicao_plano` serializa concorrentes do mesmo `decision_id`. Uma segunda execução que chegar depois da primeira aplicação observa o recibo existente e devolve replay.

O mesmo `decision_id` nunca deve criar duas aplicações, duas séries de eventos ou novos históricos.

## Serialização com writers determinísticos

A leitura autoritativa da composição usa um lock lógico por referência:

`JORNADA:COMPOSICAO:REF:{uuid}`

Nesta fatia, o writer determinístico `publicar_referencia_progressiva_deterministica` passa a adquirir o mesmo lock antes de publicar uma referência. Isso impede que, durante o fechamento/revalidação de um componente, outra origem entre silenciosamente na referência afetada sem participar da serialização.

SQL Server usa `sp_getapplock` com `LockOwner='Transaction'`; PostgreSQL usa `pg_advisory_xact_lock(hashtextextended(...))`.

## Eventos progressivos produzidos

A aplicação reutiliza o contrato append-only existente de `pessoa_origem_progressiva_evento`.

- destino existente: `ASSOCIACAO_EXISTENTE`;
- destino reservado novo: `NOVA_IDENTIDADE`, com `universo_referencia='COMPOSICAO:{decision_id}'`;
- destino removido por separação/reassociação indefinida: `INDEFINIDA`.

`evidencia_referencia` e `politica_versao` vêm da decisão de composição persistida. CPF em claro não é transportado nem registrado por este executor.

## Histórico aplicado

`identidade.composicao_historico_aplicado` guarda, por `decision_id` e referência anterior, o conjunto canônico ordenado de UUIDs iniciais que compunham aquela referência no momento da aplicação.

O JSON de membros possui SHA-256 próprio. A tabela é append-only.

Esse histórico é factual: diferentemente de `HistoryToAppend` dentro do plano `PREPARADA`, ele só existe se a transação de aplicação tiver sido efetivamente confirmada.

## O que APLICADA ainda não significa

Nesta fatia, `APLICADA` significa que a **identidade progressiva estrutural** e seu histórico foram alterados de forma transacional e auditável.

Ainda não significa:

- recomposição de `gold.*`;
- publicação final em Serving/BI;
- alteração de `vinculo_fonte` ou `identity_map` legado;
- ativação de um worker automático;
- autorização para uma decisão probabilística real ser gerada/aplicada.

A etapa seguinte deve definir a invalidação/recomposição dos derivados e a fronteira de publicação, preservando conservação dos fatos e consultas históricas.

## Provas exigidas em CI

O workflow dedicado executa SQL Server e PostgreSQL reais e deve provar, no mínimo:

- instalação repetida das novas tabelas/guards;
- aplicação de fusão preparada e revalidada;
- replay sem efeitos duplicados;
- rollback total;
- rejeição de decisão obsoleta;
- concorrência do mesmo `decision_id` convergindo para uma aplicação e um replay;
- nenhuma alteração de `vinculo_fonte`, `cpf_ancora` ou `gold.pessoa` nesta fatia.

A aprovação desse CI é prova de implementação técnica, não homologação estatística nem autorização operacional.

# Heartbeat do Processor isolado da persistência Serializable

## Causa e evidência

No ensaio sintético DEV de 20 mil pessoas em três ondas, a segunda tentativa
de processamento sofreu timeout no heartbeat. A transação de persistência
Serializable atualizava o próprio Lote e gravava linhas Silver e
`ingestao.item_processado` que referenciam o Lote por FK. Uma segunda conexão
tentava atualizar `ingestao.lote` no heartbeat, aguardava o lock e cancelava o
trabalho quando o timeout SQL era atingido. O erro 3980 posterior era consequência
do cancelamento da transação, não um erro de identidade sintética.

## Contrato de concorrência

1. `ReserveNextAsync` cria o lease em `ingestao.lote` e, na mesma transação
   curta, cria ou substitui uma linha independente de
   `ingestao.lote_heartbeat`, com o mesmo `lease_id` e `lease_owner`.
2. `SetProcessingAsync` confirma PROCESSANDO em outra transação curta, **antes**
   da transação longa que grava o payload. A Entrega continua em VALIDANDO
   até a atualização agregada final.
3. `HeartbeatAsync` renova **somente** a linha independente, sem atualizar
   `ingestao.lote` nem `ingestao.entrega`.
4. `lease_expira_em` e `heartbeat_em` em `ingestao.lote` passam a ser o
   **snapshot da reserva**; o heartbeat vivo e seu vencimento estão em
   `ingestao.lote_heartbeat`. A recuperação usa a linha viva correspondente
   ao `lease_id`; o snapshot só é fallback para lotes antigos sem linha viva.
5. A transação Serializable continua garantindo atomicidade do payload,
   `item_processado`, status terminal e recálculo da Entrega. O commit
   terminal exige token e proprietário ainda correntes e heartbeat não
   expirado; remove o heartbeat no mesmo commit. Rejeição, retry e POISON
   também eliminam a linha do lease no commit.
6. A recuperação dos expirados remove o heartbeat referente ao lease antigo
   na mesma transação que faz fencing e altera o estado do Lote.

## Prova

O teste `Heartbeat_renews_during_serializable_transaction_with_real_lote_foreign_key`
deixa aberta uma transação Serializable que insere `ingestao.item_processado`
com FK real no Lote e renova o heartbeat por uma segunda conexão. Outro
teste vence artificialmente o snapshot do Lote depois de renovar o heartbeat
vivo e confirma que a recuperação não rouba o lease.

## Instalação

O DDL consolidado `Jornada_Fase1.sql` inclui a tabela nova. Os provisionadores
DEV Windows/Linux, que usam o baseline v3.70, aplicam a migração idempotente
`20260922_Processor_Lease_Heartbeat_Isolation.sql`. Não houve publicação
de modelo de Linkage nem validação em HML/produção. A evidência local
completa das três ondas permanece pendente da próxima execução.

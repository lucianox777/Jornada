# Retenção de ingestao.item_processado - v3.39

`ingestao.item_processado` é trilha operacional granular e pode crescer para bilhões de linhas em cargas integrais/retransmissões. A v3.38 introduziu; a v3.39 endurece a habilitação e os testes:

- `ingestao.item_processado_resumo`, agregado por Entrega/hora/classe/resultado;
- `ingestao.sp_consolidar_expurgar_item_processado`;
- `Jornada.Operations.Maintenance.Worker`, **desabilitado por padrão** (`ItemProcessedRetention:Enabled=false`).

A janela `DetailRetentionDays` só pode ser habilitada após decisão de governança. Na distribuição, `Enabled=false` e `DetailRetentionDays=0`; habilitar sem informar explicitamente valor positivo faz o Worker falhar no startup (fail-safe). A consolidação preserva as métricas históricas consumidas por `serving.v_bi_qualidade_envios` e `serving.v_bi_carga_inicial`; Silver/Gold não são afetadas. A última confirmação/retransmissão por identidade de origem não deve ser eliminada da trilha granular enquanto for a confirmação mais recente disponível.

Antes de Produção, HML deve medir crescimento por dia, tempo do procedimento, impacto em log/locks e necessidade real de particionamento por `processado_em`.


A v3.39 inclui teste de integração dedicado que comprova que a última linha `RETRANSMITIDO` de cada identidade de origem permanece na trilha granular após consolidação/expurgo.

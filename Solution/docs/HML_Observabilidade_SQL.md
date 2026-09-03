# HML - observabilidade SQL e capacidade v3.39

A v3.39 **mantém `IsolationLevel.Serializable`** nas transações que preservam invariantes de ingestão. A eventual redução do isolamento depende de evidência obtida em HML.

Monitorar durante cargas representativas:

- deadlocks e `lock waits`;
- duração e taxa de rollback das transações;
- Query Store das consultas de reserva, idempotência, persistência, linkage e BI;
- tamanho e crescimento de `ingestao.item_processado`;
- estatísticas e page density/fragmentação dos índices críticos;
- Pessoas/hora durante `modo_carga_inicial`;
- backlog de lotes e idade do lote pendente mais antigo.

Não há requisito de `REBUILD/REORGANIZE` diário. Manutenção de índice e atualização de estatísticas devem ser guiadas pelo workload medido.

O script `database/Jornada_HML_Observabilidade.sql` contém consultas somente-leitura de apoio.


## Parâmetros obrigatórios de homologação v3.39

- definir explicitamente a janela `ItemProcessedRetention:DetailRetentionDays` antes de habilitar retenção;
- definir limiar de alerta para a idade de `orfao_mais_antigo_em`, a partir de `OrphanGraceHours`, periodicidade e comportamento observado;
- medir `AuditPersistenceMs` nos logs estruturados da API (p50/p95/p99) antes de considerar qualquer desacoplamento da auditoria;
- medir alocações/GC e I/O da Bronze após adoção de `ArrayPool<byte>`;
- não executar `REBUILD/REORGANIZE` por calendário fixo: usar Query Store, waits, estatísticas e evidência do workload.


## Watchdog operacional v3.53

`Jornada.Operations.Maintenance.Worker` pode habilitar `PipelineWatchdog`. Ele somente observa e emite logs estruturados; não altera banco nem agenda jobs. `database/Jornada_HML_Observabilidade.sql` contém consultas equivalentes para confirmar `linkage_run`/`modelo_linkage` estagnados, leases de lote vencidos, backlog PENDENTE e duração de `modo_carga_inicial`. Os limiares distribuídos são defaults técnicos e devem ser calibrados via `docs/HML_Parametros.md`.

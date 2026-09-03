# Jornada — Matriz única de parâmetros de HML — base normativa v3.62 / engenharia v3.72

Esta matriz centraliza valores que **não devem ser promovidos a Produção por simples herança do default técnico**. Cada linha precisa de evidência de HML e de um responsável pela decisão.

| Parâmetro | Componente | Default técnico atual | Critério para HML | Responsável | Status |
|---|---|---:|---|---|---|
| `ApiRateLimiting.StandardPermitLimit` | API | 120 req/min por credencial | teste de carga por Gestor/rota | Plataforma + Gestores | CALIBRAR |
| `ApiRateLimiting.IdentityPermitLimit` | API | 30 req/min por credencial | p95/p99 e pico de resolução CPF→UUID | Plataforma | CALIBRAR |
| `ApiRateLimiting.IngestionPermitLimit` | API | 20 req/min por credencial | perfil real de cargas e concorrência de upload | Plataforma + Gestores | CALIBRAR |
| `ApiRateLimiting.EdgeMultiplier` | API | 10× | carga de borda por IP confiável sem estrangular credenciais legítimas | Infra/Segurança | CALIBRAR |
| Staging `CleanupIntervalMinutes` | API | 30 min | volume e janela de uploads interrompidos | Operação | CALIBRAR |
| Staging `MaxAgeHours` | API | 6 h | política operacional de temporários | Operação | CALIBRAR |
| Processor `PollingMilliseconds` | Processor | 1000 ms | latência x carga SQL | Plataforma | CALIBRAR |
| `MaxPessoasPorEntrega` | Processor | 10.000 | massa real, memória e SLA | Plataforma + Gestores | CALIBRAR |
| `MaxRegistrosPorEntrega` | Processor | 10.000 | massa real, memória e SLA | Plataforma + Gestores | CALIBRAR |
| `LeaseDurationSeconds` | Processor | 120 s | p99 de lote | Plataforma | CALIBRAR |
| `HeartbeatSeconds` | Processor | 30 s | estabilidade de rede/SQL | Plataforma | CALIBRAR |
| `MaxProcessingAttempts` | Processor | 5 | padrão de falhas transitórias | Operação | CALIBRAR |
| `RetryBaseSeconds` / `RetryMaxSeconds` | Processor | 10 / 300 s | recuperação de dependências | Operação | CALIBRAR |
| Linkage thresholds | Linkage Runner | configuração/catálogo | corpus rotulado real | Dados + Gestores | CALIBRAR |
| `ProbabilisticLinkage.CommandTimeoutSeconds` | Linkage Runner | 900 s | p99 dos lotes de scoring/materialização + margem | Dados + Plataforma | CALIBRAR |
| `ProbabilisticLinkage.FreezeUniverseCommandTimeoutSeconds` | Linkage Runner | 900 s | p99 do congelamento de universo + margem | Dados + Plataforma | CALIBRAR |
| `SMOOTHING_ALPHA` | Parameters Worker | modelo | estabilidade de m/u | Dados | CALIBRAR |
| universo/high-watermark do linkage | Linkage | por run | tempo de congelamento e throughput | Dados + Plataforma | CALIBRAR |
| envelhecimento de pendências | BI/Serving | faixas no SQL | SLA institucional de tratamento | Governança | DECIDIR |
| retenção `item_processado.DetailRetentionDays` | Maintenance | 0 / retenção desativada | aprovação formal + volume | Governança | PENDENTE |
| retenção de Entregas/Bronze `RetentionDays` | Operations Maintenance | 0 / desabilitada | aprovar prazo; executar restore drill e observar `v_bi_retencao_bronze` | Governança + Infra | DECIDIR/ATIVAR |
| limiar de órfão Bronze | Bronze GC | configuração | maior janela legítima de transação/retry | Infra | CALIBRAR |
| `ReverseProxy.KnownProxies` / `KnownNetworks` | API | vazio | IPs/CIDRs reais da borda; configuração explícita fail-closed | Infra/Segurança | PENDENTE HML |
| `BronzeStorage.RootPath` | API/Processor | DEV local | storage durável compartilhado + backup/restore | Infra | PENDENTE HML |
| `PipelineWatchdog.IntervalMinutes` | Operations Maintenance | 5 min | frequência de observação x ruído operacional | Operação | CALIBRAR |
| `PipelineWatchdog.LinkageRunMaxMinutes` | Operations Maintenance | 120 min | P99 homologado dos runs + margem | Dados + Operação | CALIBRAR |
| `PipelineWatchdog.ModelGenerationMaxMinutes` | Operations Maintenance | 120 min | P99 homologado de GENERATE_DRAFT + margem | Dados + Operação | CALIBRAR |
| `PipelineWatchdog.ExpiredLeaseGraceMinutes` | Operations Maintenance | 5 min | janela de recuperação do Processor e atraso de observação | Plataforma | CALIBRAR |
| `PipelineWatchdog.PendingBacklogMaxAgeMinutes` | Operations Maintenance | 60 min | SLA operacional de processamento/backlog | Operação | CALIBRAR |
| `PipelineWatchdog.InitialLoadMaxHours` | Operations Maintenance | 24 h | duração prevista do procedimento de carga inicial | Operação + Projeto | CALIBRAR |

## Gate

Nenhum valor marcado `CALIBRAR` ou `PENDENTE` deve ser promovido automaticamente. O registro de homologação deve guardar valor aprovado, evidência, data e responsável.


## Atomicidade de correção governada — engenharia v3.65

As procedures governadas/cross-table possuem ownership transacional próprio quando não existe transação do chamador; quando chamadas pela API dentro de transação externa, preservam o ownership do serviço. HML deve executar os testes `IdentityGovernanceTests` e `PhoneNormalizationConformanceTests` no job `integration-sql`; não há parâmetro novo de configuração.

## Contrato executável de homologação — engenharia v3.72

A matriz acima também existe em `config/hml/parameters.json`. O JSON não substitui a decisão dos responsáveis; ele impede que a decisão seja registrada sem rastreabilidade mínima.

Validação estrutural, permitida enquanto HML ainda está pendente:

```bash
python3 scripts/hml-config-gate.py --root .
```

Validação de promoção, deliberadamente fail-closed:

```bash
python3 scripts/hml-config-gate.py --root . --require-approved
```

Para cada parâmetro `APROVADO`, o gate exige `approvedValue`, `approvedAtUtc`, `approvedBy` e `evidence.artifact` + `evidence.sha256`. O arquivo de baseline de desempenho (`config/hml/performance-baseline.json`) e a política de avaliação de linkage (`config/hml/linkage-evaluation-policy.json`) seguem a mesma regra. Nesta distribuição eles permanecem `PENDENTE`; isso é estado verdadeiro, não ausência silenciosa.

A passagem estrita pode ser executada com:

```bash
export JORNADA_HML_PERFORMANCE_REPORT=/caminho/scale-medium-....json
export JORNADA_HML_LINKAGE_REPORT=/caminho/linkage-evaluation-report.json
./scripts/hml-readiness-gate.sh
```

O gate não promove V2, não escolhe thresholds e não escreve parâmetros operacionais. Ele apenas prova que os critérios homologados existem e que a evidência fornecida satisfaz esses critérios.

## Contratos adicionais de homologação

A partir da release de engenharia v3.75, decisões externas que antes apareciam somente nesta matriz também possuem contratos executáveis:

- `config/governance/schema-approvals.json`: aprovação/hash dos schemas contratuais;
- `config/governance/retention-dr-policy.json`: retenção Bronze/item_processado e RPO/RTO;
- `config/governance/identity-pending-lifecycle.json`: envelhecimento e tratamento institucional de identidades sem resolução;
- `config/operations/scheduler-jobs.json`: jobs/cadências/retries do scheduler corporativo;
- `config/hml/sql-performance-policy.json`: Query Store, bloqueios, waits e deadlocks;
- `config/hml/api-projection-load-policy.json`: consulta em lote 1/10/100/1000.

Os arquivos distribuídos permanecem `PENDENTE`/`PENDENTE_HML`. O modo estrito de `hml-readiness-gate` falha até que as aprovações e evidências reais existam.

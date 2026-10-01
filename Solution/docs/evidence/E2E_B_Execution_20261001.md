# E2E-B / DT-17B — execução do baseline .NET 10

**Data:** 01/10/2026  
**PR de execução:** [#677](https://github.com/lucianox777/Jornada/pull/677)  
**HEAD efetivamente testado:** `1f647053c186834998b4526b9fb33b09df033c0a`  
**Merge em master:** `7121108dd44fec10ae911f077c06c93572dbd29e`  
**Pedido versionado:** [E2E_B_Baseline_20261001.yml](E2E_B_Baseline_20261001.yml)

## Resultado

A baseline E2E-B foi **EXECUTADA** após a DT-02, usando `Jornada.sln`, `net10.0`, .NET SDK 10.0.112, ASP.NET Core 10.0.12 e Microsoft.Data.SqlClient 7.1.1. O `jornada-ci` [#36886632283](https://github.com/lucianox777/Jornada/actions/runs/36886632283) concluiu com sucesso os oito gates substantivos exigidos: `dependency-lock`, `ddl-upgrade`, `deterministic-build`, `e2e`, `unit`, `security-analysis`, `integration-sql` e `harness-smoke`. Jobs condicionais fora do escopo permaneceram `skipped` e não foram contabilizados como prova.

O workflow [DT-05 Real Runner CPF Late E2E #36886632424](https://github.com/lucianox777/Jornada/actions/runs/36886632424) também concluiu `success` no mesmo HEAD, reexecutando as três ondas reais do Runner com CPF tardio em banco sintético isolado.

## Corpus e isolamento

- perfil demográfico: `DEMOGRAPHIC_PRIMARY_V1`;
- 30.000 pessoas; seed 42;
- fonte diária de nascimento: `birth_daily_sp_projection2024_2026.json`;
- SHA-256 da fonte diária: `8B8498EC2ABE7DCBFAFED1D361D2A3AFAFF77CAD5BCE29A5927DA517FC236A9B`;
- 39.268 linhas na fonte diária;
- bancos usados pelos gates: `JornadaE2E`, `JornadaTest`, `JornadaHarness` e `JornadaSyntheticDev`;
- `JornadaLocal` compartilhado não foi autorizado para reset.

## Artefatos preservados

| Artefato | ID | Digest SHA-256 |
|---|---:|---|
| `local-e2e-evidence` | 11174498463 | `e0353d4517883bae627cd7ccf7c779256288b43df2e2bea810d0131390b87478` |
| `unit-test-evidence` | 11174528027 | `384b01b2afc4696355e00ad751cbd1a7131352bb643815ac85b3f066ff57344a` |
| `integration-test-evidence` | 11175390834 | `71f7f65fe69d8d44f2af128c8fdc876a37753a96dbb519cb8bd6d977ebcfa2f0` |
| `fault-injection-evidence` | 11175340958 | `22f8339c9d020dec7807fdac99f9bcd843f65f995a491ceadb5de6ebb63e6801` |
| `security-analysis-evidence` | 11174063792 | `15b8aaf34a8b5e178d1ed1cb005bc98de05c72dfd3af2a5679de4029714d8f78` |
| `deterministic-build-evidence` | 11175170145 | `b2edb7bd229e3434168c1e674cc053c2ac25f3531508af9484f5422eabbf252f` |
| `e2-demographic-primary-corpus` | 11175445502 | `e406788c936fc6ceaf8372abac99ea34d94f80bd82d9a4e16be481e85927c936` |
| `dt05-cpf-late-e2e-evidence` | 11175002634 | `6d3cf152467c3c691b92686bfd67f8de7232d0d8a8d857b675c5ee0d01a8f0e5` |

## Limites

Esta execução é baseline técnica DEV/sintética para comparação A × B e prova pós-DT-02. Não comprova representatividade estatística populacional (#31), não é Ensaio institucional, não autoriza HML/Produção e não constitui SLA de Produção. A próxima etapa normativa é DT-12; depois dela, E2E-C/DT-17C deve repetir a bateria para comparar A × B × C.

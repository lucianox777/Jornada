# E2E-A / DT-17A — execução do baseline .NET 8

**Data:** 01/10/2026  
**PR de execução:** [#666](https://github.com/lucianox777/Jornada/pull/666)  
**HEAD efetivamente testado:** `406732a4351313d0e411a010f370073e8a4ce3c4`  
**Merge em master:** `c941e833588c96a16309501f13521b2e18efc9d1`  
**Pedido versionado:** [E2E_A_Baseline_20261001.yml](E2E_A_Baseline_20261001.yml)

## Resultado

A baseline E2E-A foi **EXECUTADA** no HEAD acima, antes da DT-02, usando `Jornada.sln` e .NET SDK 8.0.424. O `jornada-ci` [#36866260882](https://github.com/lucianox777/Jornada/actions/runs/36866260882) concluiu com sucesso os oito gates substantivos exigidos: `dependency-lock`, `ddl-upgrade`, `deterministic-build`, `e2e`, `unit`, `security-analysis`, `integration-sql` e `harness-smoke`. Jobs condicionais fora do escopo permaneceram `skipped` e não foram contabilizados como prova.

O workflow [DT-05 Real Runner CPF Late E2E #36866260886](https://github.com/lucianox777/Jornada/actions/runs/36866260886) também concluiu `success` no mesmo HEAD, reexecutando as três ondas reais do Runner com CPF tardio em banco sintético isolado.

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
| `local-e2e-evidence` | 11163797179 | `e4cd945cd9cd807f44e43a5d5521e039301c50df20a3c9805174f4b35836634c` |
| `unit-test-evidence` | 11164226984 | `7ab7f1bbff5527f11ede8f8e2f28a64cc4124ae10dccd62f3512371773da657f` |
| `integration-test-evidence` | 11164258056 | `dbaa30081e298e1f7424e59180796de7ae193f4b67c146baf582e49292543b2e` |
| `fault-injection-evidence` | 11164741452 | `dd07845ee290228342e2dceb25c3859756687cab6eeb2a77bfa4f6ee1693a171` |
| `security-analysis-evidence` | 11164132806 | `d82d0ee475969fea04ad9d6b8f8d4c0d418a70877cabe20d9cd1fbba903d4c2a` |
| `deterministic-build-evidence` | 11164151888 | `25e35e8514bb54eeeaa3da08d06d8249a551833af0096f0c4f0b07a3f4dc8e6f` |
| `e2-demographic-primary-corpus` | 11164012660 | `5d4a352b9c0c0a7a7920ea6037ece1403dc14cacb12cd05fe857ac1c7056b02e` |
| `dt05-cpf-late-e2e-evidence` | 11163666988 | `fe19e36c7fb6399b6ca9a6bccd5645c826194905120c10c2cbd0cd59c51c107f` |

## Limites

Esta execução é baseline técnica DEV/sintética para comparação A × B após a DT-02. Não comprova representatividade estatística populacional (#31), não é Ensaio institucional, não autoriza HML/Produção e não constitui SLA de Produção. As lacunas já registradas na DT-17 permanecem lacunas quando não cobertas por estes gates.

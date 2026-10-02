# E2E-C / DT-17C — execução do baseline pós-DT-12

**Data:** 01/10/2026  
**PR de execução:** [#690](https://github.com/lucianox777/Jornada/pull/690)  
**HEAD efetivamente testado:** `e51876b175ad212344033fd08d7f32687b2ed323`  
**Merge em master:** `e10a0a4057e5ca4268229aedf7469015480fd0d1`  
**Pedido versionado:** [E2E_C_Baseline_20261001.yml](E2E_C_Baseline_20261001.yml)

## Resultado

A baseline E2E-C foi **EXECUTADA** após a DT-12, usando `Jornada.sln`, `net10.0`, .NET SDK 10.0.112, ASP.NET Core 10.0.12 e Microsoft.Data.SqlClient 7.1.1. O `jornada-ci` [#36950535828](https://github.com/lucianox777/Jornada/actions/runs/36950535828) concluiu com sucesso os oito gates substantivos exigidos: `dependency-lock`, `ddl-upgrade`, `deterministic-build`, `e2e`, `unit`, `security-analysis`, `integration-sql` e `harness-smoke`. Jobs condicionais fora do escopo permaneceram `skipped` e não foram contabilizados como prova.

O workflow [DT-05 Real Runner CPF Late E2E #36950535827](https://github.com/lucianox777/Jornada/actions/runs/36950535827) também concluiu `success` no mesmo HEAD, reexecutando as três ondas reais do Runner com CPF tardio em banco sintético isolado.

## Pré-condição DT-12

- PR final de correção da limpeza: #689;
- `master` pós-limpeza: `552ae33368d840c3ef0b5b7758e0ca1fbe99c8c0`;
- cleanup run: #36945815354, `success`;
- 170 branches autorizadas removidas;
- 0/170 candidatas permaneciam no remoto antes do E2E-C.

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
| `local-e2e-evidence` | 11204011813 | `960104d303b938a1e384eb183b81c67cd09c82e2d04af0d2291b61db2e6f4d19` |
| `unit-test-evidence` | 11203334960 | `741ab3996e2020ba0e75682c769cf672c747613771e91681fd0bbfd0894eb547` |
| `integration-test-evidence` | 11203826773 | `72488e542d9c43843f9f72289b452d7de05f50ccd9ac4a0ec852ef2ce1b7eba9` |
| `fault-injection-evidence` | 11203871678 | `231b315edcf4113988d110a270521e236c3e8f1d1af7cc4cee4e9065072efcd9` |
| `security-analysis-evidence` | 11203706565 | `da31d9c414a03bba7dc136da4b344603b278f3acf376e73eb1126761e17ee86f` |
| `deterministic-build-evidence` | 11204255722 | `0098cb7064a0b3b667c61061115a67544728726db91370d45f964bca94698b3b` |
| `e2-demographic-primary-corpus` | 11204366317 | `fd9cf06292ac57efc51ad6db0eee58e1fdf484dc35b8e2fc46a2409bba4944bb` |
| `dt05-cpf-late-e2e-evidence` | 11203821122 | `904d731e2e2410534c4ff6a407dd563b20f3e95ece1bd7b1e099f30f5ea77b3e` |

## Comparação A × B × C

Nos três marcos foram preservados o mesmo perfil demográfico, quantidade de pessoas, seed, fonte diária de nascimento e política de isolamento. E2E-A foi executado em .NET 8 antes da DT-02; E2E-B em .NET 10 após a DT-02 e antes da DT-12; E2E-C em .NET 10 após a DT-12.

Nos três marcos, os oito gates substantivos e o E2E real de três ondas concluíram `success`. O E2E-C não revelou regressão detectada por esses gates após a limpeza de branches. Os digests dos artefatos não são usados como exigência de igualdade byte a byte entre A, B e C; eles identificam as evidências preservadas de cada execução.

## Limites

Esta execução é baseline técnica DEV/sintética. Não comprova representatividade estatística populacional (#31), não é Ensaio institucional, não autoriza HML/Produção e não constitui SLA de Produção. As lacunas externas e institucionais registradas na DT-17 permanecem abertas quando não cobertas por estes gates.

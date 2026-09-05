# Evidência de runtime — v3.93 — 2026-09-03

Fonte: execução externa real em Windows / Visual Studio Developer PowerShell, .NET SDK 8.0.424 e Docker SQL Server.

## Resultado observado

- `local-clean.ps1`: PASS; `.vs/CopilotIndices/CodeChunks.db` bloqueado foi apenas aviso best-effort e a limpeza continuou;
- `dotnet restore Jornada.sln --locked-mode`: PASS;
- restore locked explícito de `Jornada.Tests`: PASS;
- restore locked explícito de `Jornada.Integration.Tests`: PASS;
- build Release da Solution: PASS, **0 warnings / 0 errors**;
- `Jornada.Tests`: **153 PASS / 0 FAIL / 0 SKIP**;
- build explícito de `Jornada.Integration.Tests`: PASS, **0 warnings / 0 errors**;
- `Jornada.Integration.Tests`: **54 PASS / 4 FAIL / 0 SKIP / 58 total**.

## Quatro falhas residuais

1. `Concurrent_workers_reserve_distinct_lotes`: uma das duas reservas concorrentes retornou `null` na primeira tentativa.
2. `Successful_batch_publishes_person_fact_qc_and_completeness_atomically`: contagens esperadas 1/1/2 observaram 10/9/19 por reutilização do lote/entrega compartilhados entre cenários.
3. `Transaction_failure_after_partial_persistence_rolls_back_silver_identity_and_gold`: esperava `InvalidDataException`, mas a constraint `ck_pat_comprovado` produziu SQL Server 547 antes da validação de aplicação.
4. `Canonical_seed_populates_reference_and_serving_data`: projeção individual retornou 6 registros enquanto o total factual vigente chegou a 10; avaliações compatíveis eram 2 em vez de >=3 após invalidações derivadas de cenários anteriores.

## Interpretação incorporada à v3.94

A v3.93 confirmou que o bloqueador de reentrada DDL da v3.92 foi removido. A v3.94 trata os quatro resíduos sem afirmar aprovação antecipada: uma mudança de produção na ordem da validação de entrada e ajustes de teste/fixture/seed para refletir contratos já vigentes e isolamento do próprio cenário.

Esta evidência não declara Integration PASS para v3.93 nem para v3.94.

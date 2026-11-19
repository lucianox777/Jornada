# GOV-CI-CODEQL-DEPENDABOT-20261119 — Reconciliação de SHA CodeQL em PR de dependências

## Contexto e evidência

A PR #1450 (Dependabot, 77 arquivos) altera os pins do CodeQL no `.github/workflows/ci.yml` de `2892aa5e19bbd11bc0cff5427e3b750a04d9e3c2` (v4.38.2) para `33d7b85da1a04171e211befe2c739303b9a4c066` (v4.39.3), mantendo `init` e `analyze`. A execução #44595348811 fez checkout de `refs/remotes/pull/1450/merge` e falhou em `security-guards` e `unit-runtime`, no teste `test_security_parallel`, porque `Solution/tests/scripts/test_ci_required_gates.py` ainda exigia o SHA antigo. `classify-impact` e `deterministic-build` passaram.

## Encaminhamento técnico e justificativa

Na própria branch da PR #1450, alinhar exclusivamente as duas referências literais CodeQL do teste ao SHA presente no workflow proposto. Preservar o teste fail-closed, os pins de 40 caracteres, os dois passos CodeQL, `security-extended`, build manual, SARIF e agregação obrigatória. **Esta reconciliação não valida, por si só, a autenticidade externa do novo pin nem aprova a PR inteira.** Antes do merge, verificar a procedência do SHA e todos os demais contratos afetados pelas 77 alterações, com CI obrigatória verde.

## Impactos, precedência e limites

Correção limitada ao contrato estático da CI; não altera código de produção, TF, gerador, dados ou manifestos LOCKED. Mantém precedência de GOV-DOC-01 e P0 #1734: a integridade da `master` e a comprovação de CI pós-merge continuam pendentes. Não autoriza campanha científica, promoção ou relaxamento de gates.

## Rastreabilidade

- PR: #1450; execução: #44595348811; revisão problemática: `434698b` (`pull/1450/merge`).
- Arquivos: `.github/workflows/ci.yml` e `Solution/tests/scripts/test_ci_required_gates.py`.
- Estado: ajuste submetido à PR; CI da revisão corrigida e validação externa dos pins pendentes.

## Evidência complementar — CI #44596760194

Na execução do commit `806c517`, `security-guards`, `classify-impact` e `deterministic-build` passaram. `unit-runtime` falhou em `Essential V9 contract tests` porque `test_v9_v3_four_name_100k_handoff.py` ainda exigia `actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093`, enquanto a PR atualizou o workflow para `9000827ccba6bdab643e8b6fd33ac0654aef8333`. Ajuste estrito do literal de teste no commit `99490452`, sem suprimir a exigência de download independente no handoff científico. A nova CI e a procedência externa dos pins continuam pendentes; o merge segue bloqueado.

Na mesma rodada, `v9-memory-batch` falhou pela ausência do artefato nomeado no run histórico `38593005691`; `jornada-identity-progressive-foundation`, `DT-05 Real Runner CPF Late E2E`, `V9 TF4 mandatory TRAIN-AUDIT-VALIDATION cross-job integration (synthetic only)` e `v9-parallel-orchestrator` também falharam, com investigação de causalidade ainda pendente. Não substituir artefatos, não relaxar gates, não executar TRAIN/TEST científico.

## Reconciliação de escopo SQLSmoke no Identity Foundation — 19/11/2026

**Contexto e precedência:** a decisão vigente `Decisao_GOV_CI_Completa_V9_Coorte_TLS_20261113.md` determina que a suíte unitária sem SQL descartável exclua `SQLSmoke`, mantendo `--forbid-skipped` e o workflow físico SQL dedicado. A CI `jornada-identity-progressive-foundation` executava `--filter "TestCategory=Unit"`; o teste com categoria SQLSmoke também entrava nessa seleção, era ignorado por falta de SQL e o gate corretamente rejeitava um teste crítico ignorado (`1769` aprovados, `1` ignorado, saída `2`; run `44598038710`).

**Ajuste técnico:** commit `e9f4ac4` restringiu apenas o filtro desse workflow para `TestCategory=Unit&TestCategory!=SQLSmoke`, preservando `--forbid-skipped`, o mínimo de testes, a coleta TRX e o workflow físico SQL. Nenhuma alteração nos testes científicos, modelos, manifestos LOCKED ou critérios de aprovação. **Impacto:** o job unitário não exige SQL que não provisiona; o teste SQL continua sujeito ao seu executor especializado. **Rastreabilidade:** PR #1450; runs `44598038710` e `44598039110`; decisão de precedência de 13/11/2026. **Status:** implementação proposta, revalidação de CI pendente; não autoriza merge nem TRAIN/TEST.

## Correção de fixture TF4 sem TEST (19/11/2026)

Na CI da PR #1450, o workflow sintético `v9-tf4-chain-integration-smoke.yml` (run #44600128755) apresentou TRAIN/AUDIT/VALIDATION aprovados e TEST opcional ignorado, porém seu recibo fictício declarava `VALIDATION_SCIENTIFIC_WINNER_SELECTED_NOT_CERTIFIED`. O closeout rejeitou corretamente a combinação com `V9_E2E_CLOSEOUT_TEST_MANDATORY_IF_AND_ONLY_IF_VALIDATION_HAS_WINNER`. Para preservar a decisão vigente de TEST independente obrigatório quando há vencedor, a fixture **sem TEST solicitado** passa a declarar `NO_FINALIST_PASSES_DECLARED_SCIENTIFIC_GATES`, com campos de vencedor/modelo nulos, e a verificar `E2E_PHYSICAL_CORE_FINISHED_NO_WINNER_TEST_NOT_REQUESTED`. Não foram alterados o closeout científico, o protocolo de TEST, os LOCKs ou dados físicos. Verificação CI do novo commit pendente. Precedência: `Decisao_GOV_V9_E2E_Closeout_Obrigatorio_20261114.md` e GOV-DOC-01.

## Compatibilidade DT-05 com gate de modelo no startup (19/11/2026)

Na PR #1450, o job DT-05 `Synthetic Bronze to Runner, three waves` (run #44600129081) falhou antes da primeira publicação com `RUNNER_MODEL_CONFIGURATION_MISSING`. A exigência de `Linkage:SelectedModelVersion` no startup permanece obrigatória, conforme `Decisao_GOV_Runner_Startup_Modelo_Operador_Master_20261117.md`; a mesma incompatibilidade de harness já foi documentada em `Decisao_GOV_CI_Scale_Startup_Modelo_20261119.md`. Ajuste estritamente no ensaio `Solution/scripts/dt05-cpf-late-wave.ps1`: consultar a versão do único modelo ATIVO do banco sintético descartável, exigir inteiro positivo, fornecê-la como `Linkage__SelectedModelVersion` apenas durante a chamada do Runner, restaurando a variável anterior em `finally`. Não criar fallback de seleção no Runner, não alterar configuração produtiva, nem autorizar modelo/TEST científico. Aceitação depende de nova CI física. Rastreabilidade: commit `53f16dc01be23608f41d132aaf0265cb8ad2cbb8`, run #44600129081, GOV-DOC-01.

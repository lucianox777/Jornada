# GOV-V9-100K-VALID-NAME-REPRO-20261110 — reprodução de falha de cobertura TF e limites da inferência

**Data:** 10/11/2026. **Origem:** mandato PO de continuar após falha física manual, e entendimento já aprovado de que nomes reais não publicados no Censo IBGE podem inclusive surgir após 2022. **Estado:** reprodução unitária proposta em PR, ainda não executada/certificada. **Regra:** GOV-DOC-01.

## Evidência factual do ensaio
- [Execução manual #42897661860](https://github.com/lucianox777/Jornada/actions/runs/42897661860), `master` exata `57dcff811ba46c6ffa4810634aa75c864d8503a4`, terminou **FAILURE** na etapa de 24 ajustes V9.
- Etapas PASS: geração limpa 100k seed42, inicialização SQL descartável, preparação da referência TF, gate de memória/disco no modo nativo e AUTO. Auditoria científica posterior foi **skipped**. Nenhum 24/24, TEST ou FDR certificado.
- Artifact agregado `13801752020`, `results/interrupted.json`: `exceptionType=InvalidDataException`; `safeFailureStage=PHYSICAL_SQL_BATCHES`; `safeFailureCategory=PHYSICAL_SQL_PROBE`; `safeFailureReasonCode=UNCLASSIFIED_PHYSICAL_SQL_PROBE_AT_VALIDATION_TF_PUBLISHED_COVERAGE`; `completed=false`. **Checkpoint é última subfase iniciada, não identificação automática do predicado exato.**
- Instrumentação de recursos: 22 amostras; `cgroupOomEventDelta=0`, `sqlCgroupV2OomEventDeltaObserved=0`, `sqlContainerOomKilled=false`, `workerExitCode=1`; não há evidência positiva de OOM, nem garantia de ausência de eventos entre amostras.

## Causa candidata suportada pelo código e plano
- O `bound/plan.json` do artifact declara duas `ModelOptions` de partida (não confundir com as 24 avaliações planejadas): TF0 `A0.5_T0.95_TF0` e TF positiva `TF_ONE_MF_W1_M0_A0.5_T0.95`. Esta última configura TF positiva para **mãe/prenome** e `UnpublishedTermPolicy=0` (`RejectOperation`). A presença dessa configuração não prova qual opção ou nome foi a origem exata do erro físico.
- `V9FellegiSunterAdaptiveCalibrationAdapter.EvaluateValidation` chama `Audit`, `AuditUnion` e, quando `RejectOperation`, `RequireCompleteCoverage` no checkpoint `VALIDATION_TF_PUBLISHED_COVERAGE`. Este último lança `InvalidDataException` quando uma unidade nominal TF ativa não tem frequência publicada. Existem outros predicados possíveis na mesma subfase; confirmar causalidade exige teste de reprodução e observação segura.
- Teste unitário acrescentado a `V9FsAdaptiveFirstNameTfTrainValidationTests` constrói um nome correto hipoteticamente novo após 2022, não publicado, em par verdadeiro VALIDATION; exige que o comportamento atual `RejectOperation` seja reproduzido, incluindo último checkpoint e ausência do nome no erro. Ele **não convalida a rejeição como comportamento produtivo desejado**.

## Precedência e próximos gates
A ausência de publicação no IBGE não significa grafia incorreta, impossibilidade, ocorrência zero atual nem supressão com contagem [1,19] comprovada. Regras de frequência censurada só podem usar o intervalo quando sua hipótese é demonstrada. Validar tratamento de nome legítimo com frequência desconhecida contra TF0, simétrico, assimétrico, 81 células, estimativas M/U TRAIN condicionadas ao blocking, recall, FDR, abstenção e Resolve; nenhuma imputação/piso/fallback automático autorizado.

**Invariantes:** não mudar dados IBGE publicados, planos científicos, 24/16/8, parâmetro de TF, thresholds, TEST/Gold, snapshot `ref`, ativação ou workflow manual owner-only. Nenhum novo run 100k antes de reprodução, correção aprovada, checks no SHA exato e preflight real.

**Rastreabilidade:** issue #1349; PRs #1409, #1412; [decisão sobre base real e nomes novos pós-2022](Decisao_V9_Base_Inicial_Real_100k_Bootstrap_Configuracoes_20261110.md) (na PR #1412, ainda não mesclada). Este documento não afirma a existência do arquivo da PR #1412 na master. Preservar verificação de compatibilidade no merge.

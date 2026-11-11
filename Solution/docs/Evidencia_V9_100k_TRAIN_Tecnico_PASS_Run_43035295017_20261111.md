# GOV-V9-100K-TRAIN-PASS-43035295017-20261111 — evidência da campanha física concluída

**Data da evidência:** 11/11/2026 (America/Sao_Paulo). **Natureza:** registro operacional/técnico de execução e conclusão, sem nova decisão científica. **GOV-DOC-01:** complementa o diagnóstico de P95/fanout, a autorização excepcional de execução nesta conversa e os índices. **Status:** `PASS TÉCNICO DE TRAIN`; **NÃO CERTIFICADO CIENTIFICAMENTE**, `TEST` não usado, sem publicação e sem ativação.

## Fonte primária e proveniência imutável

- [Workflow manual `v9-multiparameter-100k-technical`, run #43035295017](https://github.com/lucianox777/Jornada/actions/runs/43035295017): `completed/success`, tentativa 1; acionado pelo proprietário em `master`; início 14:06:31 UTC, final 14:31:45 UTC, 11/11/2026.
- SHA exato executado: `504093b56cff0a66d5e965b29a96938bc443624d`, squash merge da [PR #1431](https://github.com/lucianox777/Jornada/pull/1431). Pré-requisitos no **mesmo SHA**: [CI completa #43027890561](https://github.com/lucianox777/Jornada/actions/runs/43027890561) `SUCCESS` e [SQL físico 30k #43027890092](https://github.com/lucianox777/Jornada/actions/runs/43027890092) `SUCCESS`; regressões físicas V3/TF/MU do mesmo SHA também passaram.
- [Artefato GitHub Actions #13864771198](https://github.com/lucianox777/Jornada/actions/runs/43035295017/artifacts): `v9-multiparam-100k-504093b56cff0a66d5e965b29a96938bc443624d-43035295017`, 105398 bytes de ZIP, digest divulgado pelo GitHub `sha256:29874d18e5863105998601c283bba95526f8f900935431951b0e3f74c0f48fae`.
- Evidência analisada diretamente: `bound/technical-audit.json`, `bound/technical-audit.md`, `bound/binding-receipt.json`, `results/mu-statistical-analysis.json`, `results/runtime-diagnostics.json`, `results/physical-memory-evidence.json`, `results/calibration-resource-budget.json` e `results/selection-reference.json`. Estes relatórios agregados não contêm e não devem introduzir PII/corpus, TEST ou credenciais.

## Contabilidade física e contrato de auditoria

| Item | Evidência |
| --- | --- |
| Corpus técnico | 100.000 pessoas sintéticas; seed 42; TRAIN_SELECTION apenas |
| Hipóteses | `evaluations=24`, `measured=24`, todas `MEASURED_*` |
| Papéis declarados | 16 testemunhas/diagnósticos + 8 candidatas estruturalmente elegíveis |
| Rejeições | 12 `FANOUT_P99_ACIMA_DO_ORCAMENTO`; 4 outras hipóteses são somente diagnósticas sem rejeição pré-replay |
| Cobertura TF | `publishedTfCoverageInsufficient=0`, `scientificallyNonEvaluable=0` |
| P95 de replay | 12 P95 numéricos reais (5.180,5378–11.684,5766 ms) e 12 `null` **somente** por rejeição pré-replay de fanout P99 |
| Auditor técnico | Job `Audit all V2 witness, V3/TF/MU fits, TP/FP/FN, FDR and resolution P95`: `SUCCESS`; JSON `schema=V9_100K_MULTIPARAM_TECHNICAL_AUDIT_V1` |
| Métricas auditadas | `physical24=PASS`; `corpusAndPlanSha=PASS`; `tpFpFnRecallFdrObserved=PASS`; `sourceResolutionP95=PASS_WHEN_REPLAY_PERFORMED` |
| Proveniência | `technicalPlanSha256=1a2a57c35f11a3cf551f1906e0ef7cb2a799947caae4e5f627d2f91e0d486455`; recibo e relatório consistentes |
| Regras dos braços | `tfUnpublishedTermPolicyChanges` registra na versão técnica somente a política `AbstainCandidate` para TF positiva: `previous=0`, `effective=1`; sem imputação de frequências IBGE |
| Falha anterior | [#43018148317](https://github.com/lucianox777/Jornada/actions/runs/43018148317) falhava porque exigia P95 também nas 12 opções rejeitadas por fanout; a correção #1431 foi confirmada nesta campanha |

As 8 candidatas estruturalmente elegíveis dividem-se em **4 TF0 e 4 TF positivas** (braço `TF_ONE_MF_W1_M0_A0.5_T0.95`); esses rótulos são de elegibilidade da busca, **não** decisão de adoção ou certificado populacional de FDR. A classificação por 3/19 passes, classe M/U e pesos pré-declarados continua sendo a prevista no experimento.

## Recursos medidos, escopo de medição e limites

- `totalMilliseconds=1.252.183` (20 min 52,183 s no processo C#); `totalWallClockIncludingInputPreparationMilliseconds=1.254.167`. A Action integral durou aproximadamente 25 min 14 s, incluindo preparação e auditoria.
- `peakWorkingSetBytes=7.785.758.720` (~7,25 GiB do processo); `effectiveParallelism=3` trabalhadores, 4 processadores visíveis; modo `AUTO_CPU_AND_LIVE_RAM_BOUNDED`. Não equiparar RSS do processo ao pico de memória de todos os containers/host.
- Monitor físico: `workerExitCode=0`, `cgroupOomEventDelta=0`, `cgroupOomKillDelta=0`, `sqlCgroupV2OomEventDeltaObserved=0`, `sqlCgroupV2OomKillDeltaObserved=0`, `sqlContainerOomKilled=false`; **ausência de sinal observado não prova impossibilidade de OOM**.
- A série de `diskHighWaterOutputAtRuntime` mede apenas tamanhos lógicos dos arquivos na pasta de saída (`PeakLogicalBytes=398442993`), **não** pico físico de disco geral; por isso `observedDiskPeak=NAO_COBERTO`. Os relatórios assinalam `independentPopulationFdr=NAO_COBERTO` e `HTTP_100K=NAO_COBERTO`.
- `selection-reference.json` declara `StopReason=COMPUTE_BUDGET_EXHAUSTED_NOT_OPTIMALITY`. Não converter a fronteira técnica/um finalista registrado em afirmação de ótimo global ou escolha operacional.

## Fronteira científica e próximos passos

**Aprovado unicamente:** completude física 24/24, rastreabilidade de corpus e parâmetros, reconciliação TP/FP/FN/recall/FDR *observado*, consistência de M/U/TF e tempos P95 quando replay é executado, integridade do contrato de auditoria. `testUsed=false`, `certified=false`, `activated=false`, `externalValidationUsed=false`.

**Não aprovado:** certificação FDR populacional `≤1%`, independência de clusters, performance HTTP 100k, comparação em dados reais, publicação, ativação, promoção para Gold/PROD, desbloqueio de TEST ou política TF global. Não alterar limites de fanout P99, teto P95, scoring, FDR, política IBGE nem aprovar vencedor científico nesta etapa.

**Precedência e rastreabilidade:** este documento é a evidência mais recente de sucesso e **não apaga** falhas históricas 12/24 [#42939482401](https://github.com/lucianox777/Jornada/actions/runs/42939482401) e 24/24 com auditor reprovado [#43018148317](https://github.com/lucianox777/Jornada/actions/runs/43018148317). A sequência de correção está registrada em [diagnóstico P95/fanout](Diagnostico_V9_100k_P95_Fanout_CI_Chromium_20261111.md), [protocolo GOV-OPS](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), [PR #1430](https://github.com/lucianox777/Jornada/pull/1430), [PR #1431](https://github.com/lucianox777/Jornada/pull/1431) e [issue #1349](https://github.com/lucianox777/Jornada/issues/1349). O passo seguinte deve ser deliberado no protocolo científico independente, sem reutilizar o TEST nesta comprovação técnica.

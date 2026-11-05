# Campanha 3σ — seed 52 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 52 |
| Run / job | [41949348953](https://github.com/lucianox777/Jornada/actions/runs/41949348953) / 126989318607 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:33:43Z / 2026-11-05T01:39:23Z |
| Duração | 5 min 40 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed52-frozen-transfer-41949348953-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `7b907f4889bf48e5d92eb1a31ff61b26ddf83ee9a1f77c0e8ad442aaa09b705f` |
| `gabarito.json` | `a0ce5a0583e3fd11aad25093821364e501710bf2a03e3e57032619714c9d367e` |
| `generation-manifest.json` | `9a52b5fd1040a1326ebc98533207b7f51fae2816848f933f6105a184f800479d` |
| `transfer-results.json` | `e5996fbd2a20476517eeb37a6ab0d22120d0c9c4d293415935378cb936acbc81` |
| `analysis.json` (artefato) | `86b581ab3f656481e6b1ca131c585a55748f32d6c79bc0e7bcac1c3ac4c71a5e` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 52 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 52: 10473 observações, 5694 identidades-base. Corpus `7b907f4889bf48e5d92eb1a31ff61b26ddf83ee9a1f77c0e8ad442aaa09b705f`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7526 | 0 | 352 | 100.0000% | 95.5319% | 0.0000% | 0.2475% | 189 | 2758 | 6.8732% | 0.0000% | 1013 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7563 | 0 | 315 | 100.0000% | 96.0015% | 0.0000% | 0.2462% | 156 | 2754 | 6.1310% | 0.0000% | 745 |
| T0.95_TF0 | 2 | 7421 | 0 | 457 | 100.0000% | 94.1990% | 0.0000% | 0.2512% | 294 | 2758 | 7.7767% | 0.0000% | 647 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7469 | 0 | 409 | 100.0000% | 94.8083% | 0.0000% | 0.2495% | 250 | 2754 | 7.3249% | 0.0000% | 718 |
| T0.95_TF0 | 4 | 6840 | 0 | 1038 | 100.0000% | 86.8241% | 0.0000% | 0.2741% | 875 | 2758 | 15.1339% | 0.0000% | 650 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7225 | 0 | 653 | 100.0000% | 91.7111% | 0.0000% | 0.2585% | 494 | 2754 | 10.3582% | 0.0000% | 760 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 37 | 0 | 0.4697% | [0.2433%, 0.6960%] | 37/7/5650 | 2.65e-06 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 48 | 0 | 0.6093% | [0.3560%, 0.8625%] | 45/8/5641 | 1.184e-07 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 385 | 0 | 4.8870% | [4.0781%, 5.6959%] | 195/21/5478 | 8.042e-37 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5694 | 10473 | 7878 | 2758 | 9 | 5.761% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5694 | 10473 | 7878 | 2754 | 9 | 5.731% | 20 |
| T0.95_TF0 | 2 | 5694 | 10473 | 7878 | 2758 | 9 | 5.847% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5694 | 10473 | 7878 | 2754 | 9 | 5.807% | 20 |
| T0.95_TF0 | 4 | 5694 | 10473 | 7878 | 2758 | 9 | 6.375% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5694 | 10473 | 7878 | 2754 | 9 | 6.015% | 20 |

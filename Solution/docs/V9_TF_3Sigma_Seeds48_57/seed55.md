# Campanha 3σ — seed 55 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 55 |
| Run / job | [41949367613](https://github.com/lucianox777/Jornada/actions/runs/41949367613) / 126989379452 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:33:53Z / 2026-11-05T01:39:21Z |
| Duração | 5 min 28 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed55-frozen-transfer-41949367613-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `ad30dd3b55eb6acbdc4fd66c7b931d16f7a23b48b9cb352a0c4289f8648726f2` |
| `gabarito.json` | `4e959966c066e665c336336389f776ae994a2344d92ce6d74d840f1994e7b9a5` |
| `generation-manifest.json` | `d6284444e30f7f7536dc5892a77df00c63f3509359a0fbd34a766f72dc214815` |
| `transfer-results.json` | `0f84679115c053f179c6279ad42b8660abf31b087921d5487ceaee7f7435a4b7` |
| `analysis.json` (artefato) | `c607510ebf058305204ec8421b1956561701b6fd030f2a5621957d2fcc15a097` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 55 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 55: 10136 observações, 5523 identidades-base. Corpus `ad30dd3b55eb6acbdc4fd66c7b931d16f7a23b48b9cb352a0c4289f8648726f2`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7313 | 0 | 336 | 100.0000% | 95.6073% | 0.0000% | 0.2549% | 194 | 2629 | 6.9499% | 0.0000% | 866 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7356 | 0 | 293 | 100.0000% | 96.1694% | 0.0000% | 0.2533% | 158 | 2622 | 6.0606% | 0.0000% | 616 |
| T0.95_TF0 | 2 | 7202 | 0 | 447 | 100.0000% | 94.1561% | 0.0000% | 0.2591% | 305 | 2629 | 7.6087% | 0.0000% | 582 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7250 | 0 | 399 | 100.0000% | 94.7836% | 0.0000% | 0.2573% | 264 | 2622 | 7.2464% | 0.0000% | 644 |
| T0.95_TF0 | 4 | 6647 | 0 | 1002 | 100.0000% | 86.9002% | 0.0000% | 0.2823% | 860 | 2629 | 14.8551% | 0.0000% | 598 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 6999 | 0 | 650 | 100.0000% | 91.5022% | 0.0000% | 0.2671% | 515 | 2622 | 10.5072% | 0.0000% | 648 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 43 | 0 | 0.5622% | [0.3378%, 0.7865%] | 41/6/5476 | 8.858e-08 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 48 | 0 | 0.6275% | [0.3770%, 0.8781%] | 50/13/5460 | 1.508e-06 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 352 | 0 | 4.6019% | [3.8106%, 5.3932%] | 183/18/5322 | 7.046e-36 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5523 | 10136 | 7649 | 2629 | 7 | 5.843% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5523 | 10136 | 7649 | 2622 | 7 | 5.807% | 20 |
| T0.95_TF0 | 2 | 5523 | 10136 | 7649 | 2629 | 7 | 5.939% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5523 | 10136 | 7649 | 2622 | 7 | 5.897% | 20 |
| T0.95_TF0 | 4 | 5523 | 10136 | 7649 | 2629 | 7 | 6.466% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5523 | 10136 | 7649 | 2622 | 7 | 6.121% | 20 |

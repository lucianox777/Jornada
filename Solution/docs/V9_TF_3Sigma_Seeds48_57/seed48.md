# Campanha 3σ — seed 48 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 48 |
| Run / job | [41948494857](https://github.com/lucianox777/Jornada/actions/runs/41948494857) / 126986516447 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:26:06Z / 2026-11-05T01:32:47Z |
| Duração | 6 min 41 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed48-frozen-transfer-41948494857-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `1eeb115b2ce1cdd7fe75f88fe192fe0ab0bd28c55cf746b114e1604edcae0770` |
| `gabarito.json` | `bfc81f7e31d0efe1d55617d72ceeb085a332c1badedbb349faf51bf9e48fdb94` |
| `generation-manifest.json` | `770393c66a63b8810a5218c4f57b990c9724e2c3cdb4c6f668ae5de364a42397` |
| `transfer-results.json` | `4f215dd9f6a6689d45848c3a87485fe76489f5a40f145d002d5da5c9f6ffd21e` |
| `analysis.json` (artefato) | `6801838582d8ddac97793965e0b1b72d65f7988d701b52e4eb0e060b7f54d0f1` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 48 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 48: 10197 observações, 5542 identidades-base. Corpus `1eeb115b2ce1cdd7fe75f88fe192fe0ab0bd28c55cf746b114e1604edcae0770`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7377 | 0 | 313 | 100.0000% | 95.9298% | 0.0000% | 0.2526% | 168 | 2652 | 6.4909% | 0.0000% | 1297 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7427 | 0 | 263 | 100.0000% | 96.5800% | 0.0000% | 0.2508% | 132 | 2638 | 5.4366% | 0.0000% | 1146 |
| T0.95_TF0 | 2 | 7268 | 0 | 422 | 100.0000% | 94.5124% | 0.0000% | 0.2566% | 277 | 2652 | 7.2488% | 0.0000% | 973 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7315 | 0 | 375 | 100.0000% | 95.1235% | 0.0000% | 0.2549% | 244 | 2638 | 6.6886% | 0.0000% | 1118 |
| T0.95_TF0 | 4 | 6759 | 0 | 931 | 100.0000% | 87.8934% | 0.0000% | 0.2773% | 786 | 2652 | 13.8386% | 0.0000% | 1026 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7106 | 0 | 584 | 100.0000% | 92.4057% | 0.0000% | 0.2629% | 453 | 2638 | 9.6211% | 0.0000% | 1075 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 50 | 0 | 0.6502% | [0.4063%, 0.8941%] | 47/6/5489 | 2.903e-09 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 47 | 0 | 0.6112% | [0.3480%, 0.8744%] | 53/15/5474 | 2.058e-06 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 347 | 0 | 4.5124% | [3.7139%, 5.3108%] | 192/26/5324 | 9.074e-33 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5542 | 10197 | 7690 | 2652 | 9 | 5.800% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5542 | 10197 | 7690 | 2638 | 9 | 5.759% | 20 |
| T0.95_TF0 | 2 | 5542 | 10197 | 7690 | 2652 | 9 | 5.892% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5542 | 10197 | 7690 | 2638 | 9 | 5.852% | 20 |
| T0.95_TF0 | 4 | 5542 | 10197 | 7690 | 2652 | 9 | 6.364% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5542 | 10197 | 7690 | 2638 | 9 | 6.035% | 20 |

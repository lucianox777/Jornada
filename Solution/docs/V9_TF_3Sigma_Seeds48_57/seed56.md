# Campanha 3σ — seed 56 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 56 |
| Run / job | [41950145096](https://github.com/lucianox777/Jornada/actions/runs/41950145096) / 126991894503 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:40:50Z / 2026-11-05T01:47:28Z |
| Duração | 6 min 38 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed56-frozen-transfer-41950145096-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `f3e53881ab2821ec29750c2fd798b99a53172c067bc1fceb4b8735f7dddbd2ac` |
| `gabarito.json` | `a820c0cf92e0e462a417573606070c13e81ae871b2a0be9fbf97cedd48945949` |
| `generation-manifest.json` | `56029020f533bf24ee2ed27744de668e97d78003e79722d72300f32b475d5731` |
| `transfer-results.json` | `3a5afa6d50809ce3a994f57051bc614686f138ffca99a9c84adfc049c82f8270` |
| `analysis.json` (artefato) | `ff58ac3a03d3e8a01c50254b13c68886529e23cf2a31acaa45f3c245f6b6ec28` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 56 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 56: 10590 observações, 5614 identidades-base. Corpus `f3e53881ab2821ec29750c2fd798b99a53172c067bc1fceb4b8735f7dddbd2ac`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7894 | 0 | 303 | 100.0000% | 96.3035% | 0.0000% | 0.2352% | 142 | 2554 | 6.0540% | 0.0000% | 1362 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7925 | 0 | 272 | 100.0000% | 96.6817% | 0.0000% | 0.2342% | 118 | 2547 | 5.3400% | 0.0000% | 1156 |
| T0.95_TF0 | 2 | 7797 | 0 | 400 | 100.0000% | 95.1202% | 0.0000% | 0.2383% | 239 | 2554 | 6.8302% | 0.0000% | 1024 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7831 | 0 | 366 | 100.0000% | 95.5350% | 0.0000% | 0.2372% | 212 | 2547 | 6.4266% | 0.0000% | 1231 |
| T0.95_TF0 | 4 | 7161 | 0 | 1036 | 100.0000% | 87.3612% | 0.0000% | 0.2608% | 875 | 2554 | 14.6538% | 0.0000% | 1021 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7593 | 0 | 604 | 100.0000% | 92.6315% | 0.0000% | 0.2451% | 450 | 2547 | 9.6243% | 0.0000% | 1189 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 31 | 0 | 0.3782% | [0.2111%, 0.5453%] | 30/4/5580 | 3.082e-06 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 34 | 0 | 0.4148% | [0.2122%, 0.6174%] | 33/9/5572 | 0.0001358 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 432 | 0 | 5.2702% | [4.4548%, 6.0856%] | 221/19/5374 | 4.099e-45 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5614 | 10590 | 8197 | 2554 | 8 | 5.437% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5614 | 10590 | 8197 | 2547 | 8 | 5.414% | 20 |
| T0.95_TF0 | 2 | 5614 | 10590 | 8197 | 2554 | 8 | 5.508% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5614 | 10590 | 8197 | 2547 | 8 | 5.483% | 20 |
| T0.95_TF0 | 4 | 5614 | 10590 | 8197 | 2554 | 8 | 6.027% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5614 | 10590 | 8197 | 2547 | 8 | 5.664% | 20 |

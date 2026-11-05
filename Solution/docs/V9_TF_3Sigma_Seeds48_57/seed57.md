# Campanha 3σ — seed 57 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 57 |
| Run / job | [41950150897](https://github.com/lucianox777/Jornada/actions/runs/41950150897) / 126991913803 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:40:53Z / 2026-11-05T01:47:58Z |
| Duração | 7 min 05 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed57-frozen-transfer-41950150897-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `5679d31cc8378381e901a853903e90a67922b9c9887e57c38f9c6f8ff61bad37` |
| `gabarito.json` | `e2e62ee4716a22cfe88f462a1ea52838a5ac2a834f30a43f640f67015a0c703d` |
| `generation-manifest.json` | `54efb7ded7b3e201aef538d7e424b6113c5f6b766924940f8a171e1cebfd7bbb` |
| `transfer-results.json` | `0bc534e1efaf3a8e5b65eeaa801891b11f318b35131c51becf98c34b416d4abf` |
| `analysis.json` (artefato) | `164ade4311636f87c63db5647d8b8f0a47fe7b164bed340a19d1bcbf9d388f4b` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 57 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 57: 10426 observações, 5598 identidades-base. Corpus `5679d31cc8378381e901a853903e90a67922b9c9887e57c38f9c6f8ff61bad37`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7602 | 0 | 323 | 100.0000% | 95.9243% | 0.0000% | 0.2447% | 154 | 2670 | 6.7485% | 0.0000% | 1371 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7624 | 0 | 301 | 100.0000% | 96.2019% | 0.0000% | 0.2440% | 137 | 2665 | 6.1027% | 0.0000% | 1232 |
| T0.95_TF0 | 2 | 7499 | 0 | 426 | 100.0000% | 94.6246% | 0.0000% | 0.2483% | 257 | 2670 | 7.2974% | 0.0000% | 1152 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7524 | 0 | 401 | 100.0000% | 94.9401% | 0.0000% | 0.2474% | 237 | 2665 | 7.0391% | 0.0000% | 1258 |
| T0.95_TF0 | 4 | 6890 | 0 | 1035 | 100.0000% | 86.9401% | 0.0000% | 0.2718% | 866 | 2670 | 15.0145% | 0.0000% | 1107 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7265 | 0 | 660 | 100.0000% | 91.6719% | 0.0000% | 0.2568% | 496 | 2665 | 10.5586% | 0.0000% | 1211 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 22 | 0 | 0.2776% | [0.1100%, 0.4452%] | 24/4/5570 | 9e-05 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 25 | 0 | 0.3155% | [0.1162%, 0.5147%] | 30/11/5557 | 0.002162 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 375 | 0 | 4.7319% | [3.9404%, 5.5233%] | 189/19/5390 | 1.05e-36 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5598 | 10426 | 7925 | 2670 | 8 | 5.649% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5598 | 10426 | 7925 | 2665 | 8 | 5.631% | 20 |
| T0.95_TF0 | 2 | 5598 | 10426 | 7925 | 2670 | 8 | 5.731% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5598 | 10426 | 7925 | 2665 | 8 | 5.711% | 20 |
| T0.95_TF0 | 4 | 5598 | 10426 | 7925 | 2670 | 8 | 6.269% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5598 | 10426 | 7925 | 2665 | 8 | 5.926% | 20 |

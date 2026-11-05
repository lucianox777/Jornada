# Campanha 3σ — seed 53 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 53 |
| Run / job | [41949355083](https://github.com/lucianox777/Jornada/actions/runs/41949355083) / 126989337810 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:33:46Z / 2026-11-05T01:40:25Z |
| Duração | 6 min 39 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed53-frozen-transfer-41949355083-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `2215507dc5f8dcca300675dbd9090c3f4a7ae287b7971d258fe951ecbd89deb5` |
| `gabarito.json` | `01d32644b369fe5b5002cd3cc394442c48612f8b687da306b0c2a02d410aea2e` |
| `generation-manifest.json` | `de844cdfc5e7459aa27af11d857998e89ac655fe11a9322f423101dac93d9f9e` |
| `transfer-results.json` | `09b8f71b7fc322d92ee49b03354e28169cf99d49ab8557190f5ec4d0c9bab155` |
| `analysis.json` (artefato) | `3afc11a9ec361af427a057378e624116e33638cdbf977c2922ff100462216e7c` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 53 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 53: 10348 observações, 5626 identidades-base. Corpus `2215507dc5f8dcca300675dbd9090c3f4a7ae287b7971d258fe951ecbd89deb5`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7432 | 0 | 350 | 100.0000% | 95.5024% | 0.0000% | 0.2507% | 180 | 2736 | 7.3856% | 0.0000% | 1269 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7465 | 0 | 317 | 100.0000% | 95.9265% | 0.0000% | 0.2496% | 157 | 2726 | 6.7647% | 0.0000% | 1104 |
| T0.95_TF0 | 2 | 7323 | 0 | 459 | 100.0000% | 94.1018% | 0.0000% | 0.2547% | 289 | 2736 | 8.2026% | 0.0000% | 992 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7376 | 0 | 406 | 100.0000% | 94.7828% | 0.0000% | 0.2528% | 246 | 2726 | 7.7124% | 0.0000% | 1149 |
| T0.95_TF0 | 4 | 6775 | 0 | 1007 | 100.0000% | 87.0599% | 0.0000% | 0.2768% | 837 | 2736 | 15.0980% | 0.0000% | 1003 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7157 | 0 | 625 | 100.0000% | 91.9686% | 0.0000% | 0.2610% | 465 | 2726 | 10.3268% | 0.0000% | 1083 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 33 | 0 | 0.4241% | [0.2244%, 0.6237%] | 36/9/5581 | 3.287e-05 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 53 | 0 | 0.6811% | [0.4236%, 0.9385%] | 51/12/5563 | 3.73e-07 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 382 | 0 | 4.9088% | [4.1332%, 5.6843%] | 192/12/5422 | 3.233e-43 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5626 | 10348 | 7782 | 2736 | 8 | 5.801% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5626 | 10348 | 7782 | 2726 | 8 | 5.773% | 20 |
| T0.95_TF0 | 2 | 5626 | 10348 | 7782 | 2736 | 8 | 5.892% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5626 | 10348 | 7782 | 2726 | 8 | 5.847% | 20 |
| T0.95_TF0 | 4 | 5626 | 10348 | 7782 | 2736 | 8 | 6.399% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5626 | 10348 | 7782 | 2726 | 8 | 6.037% | 20 |

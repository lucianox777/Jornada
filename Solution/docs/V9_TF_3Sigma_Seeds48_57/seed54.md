# Campanha 3σ — seed 54 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 54 |
| Run / job | [41949361357](https://github.com/lucianox777/Jornada/actions/runs/41949361357) / 126989361015 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:33:49Z / 2026-11-05T01:40:36Z |
| Duração | 6 min 47 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed54-frozen-transfer-41949361357-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `d2821213bcc9b30b48837f065e9e4dd85d862422fa9e3ceef85c1f580709c708` |
| `gabarito.json` | `7cf3aa73fc906829901a9a5820ac97ce9597c2f3770ae71817b35cbd4ff33c9c` |
| `generation-manifest.json` | `c5bd05bc933f3daef6cae464bf1a5b41a2e5b705a1b5b03f7875c8cd34219758` |
| `transfer-results.json` | `e4b7354a53b052825f2ba1bdedd917e6c4e76860e0284db08dbec28db04e3fce` |
| `analysis.json` (artefato) | `1319309eb3e6493d26bba09073c202b2d2dc3ec62f23094dc74de7ce4ed8cbcf` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 54 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 54: 10150 observações, 5489 identidades-base. Corpus `d2821213bcc9b30b48837f065e9e4dd85d862422fa9e3ceef85c1f580709c708`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7426 | 0 | 299 | 100.0000% | 96.1294% | 0.0000% | 0.2508% | 162 | 2562 | 6.0705% | 0.0000% | 1004 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7454 | 0 | 271 | 100.0000% | 96.4919% | 0.0000% | 0.2498% | 143 | 2553 | 5.2872% | 0.0000% | 871 |
| T0.95_TF0 | 2 | 7329 | 0 | 396 | 100.0000% | 94.8738% | 0.0000% | 0.2543% | 259 | 2562 | 7.0822% | 0.0000% | 782 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7370 | 0 | 355 | 100.0000% | 95.4045% | 0.0000% | 0.2528% | 227 | 2553 | 6.4295% | 0.0000% | 863 |
| T0.95_TF0 | 4 | 6782 | 0 | 943 | 100.0000% | 87.7929% | 0.0000% | 0.2762% | 806 | 2562 | 14.2298% | 0.0000% | 799 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7154 | 0 | 571 | 100.0000% | 92.6084% | 0.0000% | 0.2609% | 443 | 2553 | 9.5953% | 0.0000% | 888 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 28 | 0 | 0.3625% | [0.1534%, 0.5715%] | 31/7/5451 | 5.808e-05 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 41 | 0 | 0.5307% | [0.2787%, 0.7828%] | 46/15/5428 | 4.419e-05 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 372 | 0 | 4.8155% | [4.0064%, 5.6246%] | 195/21/5273 | 8.042e-37 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5489 | 10150 | 7725 | 2562 | 8 | 5.730% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5489 | 10150 | 7725 | 2553 | 8 | 5.708% | 20 |
| T0.95_TF0 | 2 | 5489 | 10150 | 7725 | 2562 | 8 | 5.811% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5489 | 10150 | 7725 | 2553 | 8 | 5.776% | 20 |
| T0.95_TF0 | 4 | 5489 | 10150 | 7725 | 2562 | 8 | 6.309% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5489 | 10150 | 7725 | 2553 | 8 | 5.961% | 20 |

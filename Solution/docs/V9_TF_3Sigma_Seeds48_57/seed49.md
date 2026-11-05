# Campanha 3σ — seed 49 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 49 |
| Run / job | [41948501134](https://github.com/lucianox777/Jornada/actions/runs/41948501134) / 126986538390 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:26:09Z / 2026-11-05T01:33:32Z |
| Duração | 7 min 23 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed49-frozen-transfer-41948501134-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `98dee38b04e74fe8130583876766cc8434011fde36c3dd5cd498b119319f2239` |
| `gabarito.json` | `f128990a0343e5390839714d44e3312166beceaca58c2793259c6c6f49c9fe7e` |
| `generation-manifest.json` | `fbcf67dd9d6e667fdc6a53de3829df4358f2f1863c2a10be28908b69dbbbcfa2` |
| `transfer-results.json` | `16892109f392af84eed356cc17dc5bc44bd4c36b435c7671a966d94e4471d0a8` |
| `analysis.json` (artefato) | `f4851454230c77594478053010fb7dc5f4ca443a2f669bb8c4153ccfaafdb58d` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 49 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 49: 10316 observações, 5580 identidades-base. Corpus `98dee38b04e74fe8130583876766cc8434011fde36c3dd5cd498b119319f2239`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7448 | 0 | 333 | 100.0000% | 95.7203% | 0.0000% | 0.2501% | 150 | 2718 | 7.0936% | 0.0000% | 1091 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7481 | 0 | 300 | 100.0000% | 96.1445% | 0.0000% | 0.2489% | 125 | 2710 | 6.3711% | 0.0000% | 1046 |
| T0.95_TF0 | 2 | 7366 | 0 | 415 | 100.0000% | 94.6665% | 0.0000% | 0.2531% | 232 | 2718 | 7.5862% | 0.0000% | 958 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7405 | 0 | 376 | 100.0000% | 95.1677% | 0.0000% | 0.2516% | 201 | 2710 | 7.1593% | 0.0000% | 1036 |
| T0.95_TF0 | 4 | 6829 | 0 | 952 | 100.0000% | 87.7651% | 0.0000% | 0.2743% | 769 | 2718 | 14.7455% | 0.0000% | 812 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7204 | 0 | 577 | 100.0000% | 92.5845% | 0.0000% | 0.2591% | 402 | 2710 | 9.9507% | 0.0000% | 941 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 33 | 0 | 0.4241% | [0.2213%, 0.6269%] | 32/6/5542 | 1.217e-05 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 39 | 0 | 0.5012% | [0.2613%, 0.7412%] | 42/13/5525 | 5.681e-05 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 375 | 0 | 4.8194% | [3.9893%, 5.6495%] | 193/26/5361 | 5.144e-33 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5580 | 10316 | 7781 | 2718 | 8 | 5.762% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5580 | 10316 | 7781 | 2710 | 8 | 5.735% | 20 |
| T0.95_TF0 | 2 | 5580 | 10316 | 7781 | 2718 | 8 | 5.830% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5580 | 10316 | 7781 | 2710 | 8 | 5.798% | 20 |
| T0.95_TF0 | 4 | 5580 | 10316 | 7781 | 2718 | 8 | 6.318% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5580 | 10316 | 7781 | 2710 | 8 | 5.969% | 20 |

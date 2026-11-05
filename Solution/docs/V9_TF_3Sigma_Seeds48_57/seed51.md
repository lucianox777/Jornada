# Campanha 3σ — seed 51 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 51 |
| Run / job | [41948513005](https://github.com/lucianox777/Jornada/actions/runs/41948513005) / 126986577917 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:26:16Z / 2026-11-05T01:33:14Z |
| Duração | 6 min 58 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed51-frozen-transfer-41948513005-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `624c05a8afd6ed246ecbf344353f192699ddc55322f8a9ac6b9ea81ceb9457f4` |
| `gabarito.json` | `85aa95d9faf201c3edf4fc58e04bf714fce9f382f93aa0ed41ba0260dfda6dcf` |
| `generation-manifest.json` | `0caa652704f9afff64f13e3cb7bcbff69645477297bfa28e022733d42a33d05c` |
| `transfer-results.json` | `3021c705ad92a04ad8bfa61af344962ec215da94a1b17f003098b3ab249a3955` |
| `analysis.json` (artefato) | `885eec5e66ffb72a3d255b2743c743a29881963b47f654d327a896605f68e874` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 51 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 51: 10097 observações, 5467 identidades-base. Corpus `624c05a8afd6ed246ecbf344353f192699ddc55322f8a9ac6b9ea81ceb9457f4`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7365 | 0 | 323 | 100.0000% | 95.7986% | 0.0000% | 0.2529% | 163 | 2569 | 6.2459% | 0.0000% | 1345 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7395 | 0 | 293 | 100.0000% | 96.1889% | 0.0000% | 0.2518% | 142 | 2560 | 5.7881% | 0.0000% | 1162 |
| T0.95_TF0 | 2 | 7281 | 0 | 407 | 100.0000% | 94.7060% | 0.0000% | 0.2560% | 247 | 2569 | 6.8345% | 0.0000% | 1016 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7315 | 0 | 373 | 100.0000% | 95.1483% | 0.0000% | 0.2548% | 222 | 2560 | 6.7364% | 0.0000% | 1232 |
| T0.95_TF0 | 4 | 6789 | 0 | 899 | 100.0000% | 88.3065% | 0.0000% | 0.2759% | 739 | 2569 | 13.4402% | 0.0000% | 1067 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 7110 | 0 | 578 | 100.0000% | 92.4818% | 0.0000% | 0.2626% | 427 | 2560 | 9.5487% | 0.0000% | 1176 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 30 | 0 | 0.3902% | [0.2031%, 0.5773%] | 26/4/5437 | 2.974e-05 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 34 | 0 | 0.4422% | [0.2034%, 0.6811%] | 33/13/5421 | 0.002267 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 321 | 0 | 4.1753% | [3.4104%, 4.9403%] | 166/20/5281 | 4.051e-30 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5467 | 10097 | 7688 | 2569 | 7 | 5.768% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5467 | 10097 | 7688 | 2560 | 7 | 5.744% | 20 |
| T0.95_TF0 | 2 | 5467 | 10097 | 7688 | 2569 | 7 | 5.839% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5467 | 10097 | 7688 | 2560 | 7 | 5.810% | 20 |
| T0.95_TF0 | 4 | 5467 | 10097 | 7688 | 2569 | 7 | 6.289% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5467 | 10097 | 7688 | 2560 | 7 | 5.988% | 20 |

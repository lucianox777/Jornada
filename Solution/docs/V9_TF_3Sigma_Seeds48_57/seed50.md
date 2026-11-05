# Campanha 3σ — seed 50 (relatório individual)

| Campo | Valor |
|---|---|
| Seed | 50 |
| Run / job | [41948507355](https://github.com/lucianox777/Jornada/actions/runs/41948507355) / 126986557942 |
| SHA executado | `31730624a6285a68acfb5b6c8334e40c38195c02` (PR #1276, sem merge) |
| LOCK | `Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, sha256 `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` |
| Início / fim (UTC) | 2026-11-05T01:26:12Z / 2026-11-05T01:32:52Z |
| Duração | 6 min 40 s |
| Repetições por braço | 20 (6 braços: TF0 e ONE_MF × margens 0,5/2/4) |
| Artefato | `v9-tf-seed50-frozen-transfer-41948507355-31730624a6285a68acfb5b6c8334e40c38195c02` (retenção 90 dias) |
| `observacoes.csv` | `fb7ca6a71e757cb1600c181fcb4fe02fb3684b842847aaf27ce0dea903e775bf` |
| `gabarito.json` | `96d88b347066ec38211ce498e349f3d945f0e6974aeb64afbdfe442cc0b8e550` |
| `generation-manifest.json` | `6e568dca0b992632b1d15359554f8d290e9bb516d7c8f982540a7c25c0b95394` |
| `transfer-results.json` | `7138a4bacfaaf6f2cbb068862e6f3957646ec5feb98895edeae36c3f83b7aa44` |
| `analysis.json` (artefato) | `5da8b72bf5c094b82e449b68608b4a0779ec7e9480f8746f772853d110449299` |
| Gates | LOCK, proveniência seed 42 + 6 snapshots, preflight, corpus congelado e reconferido (`OK` ×4): PASS |

Pendências = observações não resolvidas pelo Resolve (todas as causas). Fanout = P99 do grau de candidatos falsos por observação.

# Transferência congelada seed 42 → seed 50 (resultado físico)

Status: **NOT_CERTIFIED_IID_NOT_VALIDATED_SEED43_NOT_FRESH_HOLDOUT**. Sem refit, sem TEST, sem ativação.

VALIDATION seed 50: 9964 observações, 5421 identidades-base. Corpus `fb7ca6a71e757cb1600c181fcb4fe02fb3684b842847aaf27ce0dea903e775bf`.

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR pontual | FDR sup. CP (cond.) | Conflitos | Não resolvidos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 7249 | 0 | 299 | 100.0000% | 96.0387% | 0.0000% | 0.2572% | 143 | 2572 | 6.0899% | 0.0000% | 1307 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 7274 | 0 | 274 | 100.0000% | 96.3699% | 0.0000% | 0.2562% | 113 | 2577 | 5.5574% | 0.0000% | 1058 |
| T0.95_TF0 | 2 | 7158 | 0 | 390 | 100.0000% | 94.8331% | 0.0000% | 0.2606% | 234 | 2572 | 6.6223% | 0.0000% | 955 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 7192 | 0 | 356 | 100.0000% | 95.2835% | 0.0000% | 0.2593% | 195 | 2577 | 6.3228% | 0.0000% | 1183 |
| T0.95_TF0 | 4 | 6629 | 0 | 919 | 100.0000% | 87.8246% | 0.0000% | 0.2829% | 763 | 2572 | 13.7770% | 0.0000% | 998 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 6975 | 0 | 573 | 100.0000% | 92.4086% | 0.0000% | 0.2679% | 412 | 2577 | 9.4842% | 0.0000% | 1070 |

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% Δrecall (cluster) | melhor/pior/empate | sinal p unil. | Hoeffding pré-declarado |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0.5 | PRIMARY_PREDECLARED | 25 | 0 | 0.3312% | [0.1220%, 0.5404%] | 28/7/5386 | 0.0002541 | não rejeita H0 |
| 2 | SECONDARY_DESCRIPTIVE | 34 | 0 | 0.4505% | [0.2127%, 0.6882%] | 36/9/5376 | 3.287e-05 | não rejeita H0 |
| 4 | SECONDARY_DESCRIPTIVE | 346 | 0 | 4.5840% | [3.7736%, 5.3944%] | 178/16/5227 | 4.453e-36 | não rejeita H0 |

Critério descritivo (ganho > 0 sem aumento de FP, margem 0,5): **atendido**.
Ganho inferencial pelo teste pré-declarado: **não estabelecido**.

Os ICs tratam identidades-base como independentes; isso não foi validado (competição de candidatos).


| Config | Margem | Identidades | Observações | Elegíveis | Pendências | Fanout P99 | FDR sup. Hoeffding | Amostras P95 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| T0.95_TF0 | 0.5 | 5421 | 9964 | 7548 | 2572 | 7 | 5.840% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 0.5 | 5421 | 9964 | 7548 | 2577 | 7 | 5.819% | 20 |
| T0.95_TF0 | 2 | 5421 | 9964 | 7548 | 2572 | 7 | 5.919% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 2 | 5421 | 9964 | 7548 | 2577 | 7 | 5.889% | 20 |
| T0.95_TF0 | 4 | 5421 | 9964 | 7548 | 2572 | 7 | 6.421% | 20 |
| TF_ONE_MF_W1_M0_A0.5_T0.95 | 4 | 5421 | 9964 | 7548 | 2577 | 7 | 6.083% | 20 |

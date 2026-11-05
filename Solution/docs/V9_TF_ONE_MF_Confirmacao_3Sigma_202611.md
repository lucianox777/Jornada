# V9 — Confirmação 3σ da ONE_MF × TF0, seeds 48–57 (issue #1275)

## 1. Resumo executivo

O critério primário pré-registrado foi **atendido**. A ONE_MF (`TF_ONE_MF_W1_M0_A0.5_T0.95`) teve recall maior que o TF0 (`T0.95_TF0`) em **10 de 10** campanhas confirmatórias novas.

- **Teste:** sinal exato bilateral, com a campanha como unidade: p = 2/1024 = **0,001953**, menor que α = 0,0026998. Isso equivale a **3,10σ**.
- **Magnitude:** ganho médio de **+0,427 p.p.** (DP 0,110; IC t95% [+0,348; +0,506] p.p.).
- **Segurança:** 0 FP e 0 fusão em todos os braços e margens.
- **Fragmentação:** caiu de 6,60% para 5,88%.

Estes resultados mostram a **direção** do ganho na população sintética do gerador da Jornada. **Não** certificam FDR ≤ 1%, nem o Gold, nem superioridade sobre a V8, nem autorização operacional. Os modelos estavam congelados, sem refit; o TEST ficou selado.

## 2. Objetivo

Confirmar, com campanhas novas pré-registradas, que o ganho de recall observado nas seeds 42–47 se reproduz em 10 corpora independentes. A significância bilateral pré-fixada é 3σ (α = 0,0026998).

## 3. Metodologia

O protocolo é o mesmo das seeds 43–47:
- workflow `v9-tf-seed43-frozen-transfer.yml`, com o input `target_seed`;
- `v9-tf-prepare-pin.sh` gera, com o gerador C#, um corpus novo de 30.000 identidades-base por seed;
- preflight fail-closed;
- `V9FrozenTransferCli` reproduz os 6 snapshots seed 42 congelados em SQL Docker descartável, com 20 repetições por braço, usando só a VALIDATION;
- `v9-tf-seed43-frozen-transfer-physical.py analyze` faz a análise pareada por identidade;
- o subcomando `sign-test` faz o teste confirmatório entre campanhas.

**Teste primário pré-registrado no LOCK:**
- sinal exato bilateral, com unidade = campanha;
- positivo = Recall_ONE_MF > Recall_TF0 na margem 0,5; empate conta como não positivo;
- H0: P(positivo) = 0,5;
- atendido somente com 10 de 10 positivos (com 9 de 10, p = 0,0215).

## 4. Modelo de dados

- **Gerador:** `JORNADA_SYNTH_CORPUS_CSHARP_V1`, perfil `DEMOGRAPHIC_PRIMARY_V1`, erros `clean`, cap de 4, partição 60/20/20 por identidade.
- **Referências:** IBGE 2022 de nomes e nascimento SP.
- **Verificação:** o lock gate comparou, em cada run e antes de gerar qualquer dado, o digest dos 25 arquivos do gerador e as referências com o commit da seed 43 (`b98a0118`).

## 5. Manifesto

`Solution/docs/V9_TF_SEEDS_48_57_LOCK.json`, status `LOCKED`. Foi commitado e anunciado na #1275 (comentário 6646972861) **antes** do primeiro disparo.

Ele mantém os campos do LOCK 44–47 (`reference`, `generation`, `models`, `evaluation`, `execution`, `seed_exposure`) e acrescenta dois blocos:
- **`statistics`:** teste, unidade, H0/H1, α, 10 campanhas e o que não é afirmado;
- **`closure`:** sem parada antecipada, sem campanhas extras, sem troca de seed, regras de falha técnica e de mudança de código.

## 6. Digests e referências

| Item | Valor |
|---|---|
| LOCK 48–57 | `3c2a11192bf8b2877861a5f70774d77a1c4cbed37e27b56f5eb6ac145d060bad` (fixado no workflow como `SEEDS_48_57_LOCK_SHA256`) |
| LOCK 44–47 (histórico) | `822648cc3cca71368b77afad1fe816ae1ee9f87583311e1b25f18e19f01bfba3` |
| Gerador (digest dos 25 arquivos) | `924ad6455e15b73093f7d4834174966717de13be9b89b14b9640d11ffbea8deb` |
| Snapshot TF0, margem 0,5 | `f010349623b6bae32b4303f680a715bb1264271fec0e325730cce3f7bc0eb12e` |
| Snapshot ONE_MF, margem 0,5 | `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f` |
| Fonte seed 42 | run 41900357031 / `935f704e`; `study.json` `7499ee63…22cf`; `plan-tf.json` `b6f5f519…ee8` |
| IBGE publicado / TF materializado | `E3CC61BC…E885` / `89211544…9243` |
| Planos por seed | `Solution/data/reference/v9-tf-frozen-transfer-seed{48..57}/`, iguais à seed 43 exceto `target_seed`; os SHA estão no LOCK |

## 7. Seeds e exposição

| Grupo | Seeds | Exposição | Papel |
|---|---|---|---|
| Histórico | 42 | Seleção da ONE_MF (TF-001) | Exploratório, com viés de seleção |
| Histórico | 43, 44 | Expostas antes (CI multiseed, docs de 14/10, `V9FreshHoldoutProtocol`) | Descritivo |
| Histórico | 45–47 | Nenhuma exposição encontrada no repo | Réplicas descritivas, sem pré-registro confirmatório |
| **Confirmatório** | **48–57** | Nenhuma exposição encontrada no repo (código, docs, workflows e git log) antes do pré-registro. Não sei se houve uso fora do repositório. | **Família confirmatória pré-registrada** |

## 8. Commits

- **Execução:** as 10 campanhas rodaram no SHA **`31730624a6285a68acfb5b6c8334e40c38195c02`**, branch `feature/v9-tf-frozen-transfer-seeds44-47`, PR #1276 (**sem merge**). O `head_sha` de cada run foi conferido.
- **Seeds 44–47:** rodaram no SHA anterior da mesma PR (`34503812`). As mudanças entre os dois SHA não alteram o gerador, os snapshots nem as definições de métricas: ampliam a faixa de seeds, acrescentam o LOCK 48–57 e o subcomando `sign-test`. O lock gate confirmou digest do gerador e hashes dos planos idênticos.
- **Master:** os resultados **não** são da master (`bf17c779`).

## 9. Hashes (por campanha)

| Seed | `observacoes.csv` | `gabarito.json` | `generation-manifest.json` | `transfer-results.json` | `analysis.json` |
|---|---|---|---|---|---|
| 48 | `1eeb115b…0770` | `bfc81f7e…db94` | `770393c6…2397` | `4f215dd9…d21e` | `68018385…d0f1` |
| 49 | `98dee38b…2239` | `f128990a…fe7e` | `fbcf67dd…cfa2` | `16892109…d0a8` | `f4851454…b58d` |
| 50 | `fb7ca6a7…75bf` | `96d88b34…e550` | `6e568dca…5394` | `7138a4ba…aa44` | `5da8b72b…9299` |
| 51 | `624c05a8…57f4` | `85aa95d9…6dcf` | `0caa6527…d05c` | `3021c705…3955` | `885eec5e…e874` |
| 52 | `7b907f48…705f` | `a0ce5a05…367e` | `9a52b5fd…479d` | `e5996fbd…cbc81` | `86b581ab…1a5e` |
| 53 | `2215507d…deb5` | `01d32644…2e` | `de844cdf…f9e` | `09b8f71b…b155` | `3afc11a9…6e7c` |
| 54 | `d2821213…c708` | `7cf3aa73…c9c` | `c5bd05bc…9758` | `e4b7354a…3fce` | `1319309e…cbcf` |
| 55 | `ad30dd3b…26f2` | `4e959966…b9a5` | `d6284444…4815` | `0f846791…a4b7` | `c607510e…a097` |
| 56 | `f3e53881…bd2ac` | `a820c0cf…5949` | `56029020…5731` | `3a5afa6d…8270` | `ff58ac3a…ec28` |
| 57 | `5679d31c…ad37` | `e2e62ee4…703d` | `54efb7de…7bbb` | `0bc534e1…4abf` | `164ade43…388f4b` |

Os hashes completos estão em `Solution/docs/V9_TF_3Sigma_Seeds48_57/seed{N}.md`. Em todas as campanhas, `TargetObservationsSha256` (do resultado) é igual ao hash de `observacoes.csv` registrado antes da avaliação. O `sha256sum --check` pós-avaliação deu `OK` nos 4 arquivos de cada corpus.

## 10. Runs e tempos

| Seed | Run | Job | Início–fim (UTC, 05/11) | Duração | Artefato (bytes) |
|---|---|---|---|---|---:|
| 48 | [41948494857](https://github.com/lucianox777/Jornada/actions/runs/41948494857) | 126986516447 | 01:26:06–01:32:47 | 6 min 41 s | 168.347 |
| 49 | [41948501134](https://github.com/lucianox777/Jornada/actions/runs/41948501134) | 126986538390 | 01:26:09–01:33:32 | 7 min 23 s | 169.263 |
| 50 | [41948507355](https://github.com/lucianox777/Jornada/actions/runs/41948507355) | 126986557942 | 01:26:12–01:32:52 | 6 min 40 s | 164.963 |
| 51 | [41948513005](https://github.com/lucianox777/Jornada/actions/runs/41948513005) | 126986577917 | 01:26:16–01:33:14 | 6 min 58 s | 166.397 |
| 52 | [41949348953](https://github.com/lucianox777/Jornada/actions/runs/41949348953) | 126989318607 | 01:33:43–01:39:23 | 5 min 40 s | 172.500 |
| 53 | [41949355083](https://github.com/lucianox777/Jornada/actions/runs/41949355083) | 126989337810 | 01:33:46–01:40:25 | 6 min 39 s | 170.718 |
| 54 | [41949361357](https://github.com/lucianox777/Jornada/actions/runs/41949361357) | 126989361015 | 01:33:49–01:40:36 | 6 min 47 s | 166.774 |
| 55 | [41949367613](https://github.com/lucianox777/Jornada/actions/runs/41949367613) | 126989379452 | 01:33:53–01:39:21 | 5 min 28 s | 167.175 |
| 56 | [41950145096](https://github.com/lucianox777/Jornada/actions/runs/41950145096) | 126991894503 | 01:40:50–01:47:28 | 6 min 38 s | 170.185 |
| 57 | [41950150897](https://github.com/lucianox777/Jornada/actions/runs/41950150897) | 126991913803 | 01:40:53–01:47:58 | 7 min 05 s | 170.514 |

Todas as campanhas terminaram em **success** na primeira tentativa, sem falhas técnicas nem repetições. Os artefatos `v9-tf-seed{N}-frozen-transfer-{run}-31730624a6285a68acfb5b6c8334e40c38195c02` têm retenção de 90 dias.

## 11. Isolamento e paralelismo

As campanhas rodaram em 3 rodadas, com até 4 simultâneas (48–51, 52–55, 56–57). Cada uma teve:
- runner GitHub próprio;
- container SQL Server próprio, com senha mascarada aleatória;
- banco `JornadaHarness` novo;
- diretório `Solution/.local/v9-tf-seed{N}` e corpus próprios;
- artefato próprio.

Nenhum banco foi reutilizado entre seeds.

## 12. Resultados individuais (margem 0,5, VALIDATION)

As definições são idênticas às das seeds 43–47:
- **FN:** elegíveis − TP.
- **Fragmentação:** identidades com fonte elegível não vinculada ÷ identidades com fontes elegíveis.
- **Fusão:** identidades com falso aceite ÷ identidades.
- **FDR sup.:** Clopper-Pearson condicional por cluster, família de 2, sem independência validada.

| Seed | Config | Ident. | Obs. | Elegíveis | TP | FP | FN | Precisão | Recall | FDR obs. | FDR sup. | Fragm. | Fusão | Confl. | Pend. | Fanout P99 | P95 ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 48 | TF0 | 5.542 | 10.197 | 7.690 | 7.377 | 0 | 313 | 100% | 95,93% | 0 | 0,253% | 6,49% | 0% | 168 | 2.652 | 9 | 1.297 |
| 48 | ONE_MF | 5.542 | 10.197 | 7.690 | 7.427 | 0 | 263 | 100% | 96,58% | 0 | 0,251% | 5,44% | 0% | 132 | 2.638 | 9 | 1.146 |
| 49 | TF0 | 5.580 | 10.316 | 7.781 | 7.448 | 0 | 333 | 100% | 95,72% | 0 | 0,250% | 7,09% | 0% | 150 | 2.718 | 8 | 1.091 |
| 49 | ONE_MF | 5.580 | 10.316 | 7.781 | 7.481 | 0 | 300 | 100% | 96,14% | 0 | 0,249% | 6,37% | 0% | 125 | 2.710 | 8 | 1.046 |
| 50 | TF0 | 5.421 | 9.964 | 7.548 | 7.249 | 0 | 299 | 100% | 96,04% | 0 | 0,257% | 6,09% | 0% | 143 | 2.572 | 7 | 1.307 |
| 50 | ONE_MF | 5.421 | 9.964 | 7.548 | 7.274 | 0 | 274 | 100% | 96,37% | 0 | 0,256% | 5,56% | 0% | 113 | 2.577 | 7 | 1.058 |
| 51 | TF0 | 5.467 | 10.097 | 7.688 | 7.365 | 0 | 323 | 100% | 95,80% | 0 | 0,253% | 6,25% | 0% | 163 | 2.569 | 7 | 1.345 |
| 51 | ONE_MF | 5.467 | 10.097 | 7.688 | 7.395 | 0 | 293 | 100% | 96,19% | 0 | 0,252% | 5,79% | 0% | 142 | 2.560 | 7 | 1.162 |
| 52 | TF0 | 5.694 | 10.473 | 7.878 | 7.526 | 0 | 352 | 100% | 95,53% | 0 | 0,248% | 6,87% | 0% | 189 | 2.758 | 9 | 1.013 |
| 52 | ONE_MF | 5.694 | 10.473 | 7.878 | 7.563 | 0 | 315 | 100% | 96,00% | 0 | 0,246% | 6,13% | 0% | 156 | 2.754 | 9 | 745 |
| 53 | TF0 | 5.626 | 10.348 | 7.782 | 7.432 | 0 | 350 | 100% | 95,50% | 0 | 0,251% | 7,39% | 0% | 180 | 2.736 | 8 | 1.269 |
| 53 | ONE_MF | 5.626 | 10.348 | 7.782 | 7.465 | 0 | 317 | 100% | 95,93% | 0 | 0,250% | 6,76% | 0% | 157 | 2.726 | 8 | 1.104 |
| 54 | TF0 | 5.489 | 10.150 | 7.725 | 7.426 | 0 | 299 | 100% | 96,13% | 0 | 0,251% | 6,07% | 0% | 162 | 2.562 | 8 | 1.004 |
| 54 | ONE_MF | 5.489 | 10.150 | 7.725 | 7.454 | 0 | 271 | 100% | 96,49% | 0 | 0,250% | 5,29% | 0% | 143 | 2.553 | 8 | 871 |
| 55 | TF0 | 5.523 | 10.136 | 7.649 | 7.313 | 0 | 336 | 100% | 95,61% | 0 | 0,255% | 6,95% | 0% | 194 | 2.629 | 7 | 866 |
| 55 | ONE_MF | 5.523 | 10.136 | 7.649 | 7.356 | 0 | 293 | 100% | 96,17% | 0 | 0,253% | 6,06% | 0% | 158 | 2.622 | 7 | 616 |
| 56 | TF0 | 5.614 | 10.590 | 8.197 | 7.894 | 0 | 303 | 100% | 96,30% | 0 | 0,235% | 6,05% | 0% | 142 | 2.554 | 8 | 1.362 |
| 56 | ONE_MF | 5.614 | 10.590 | 8.197 | 7.925 | 0 | 272 | 100% | 96,68% | 0 | 0,234% | 5,34% | 0% | 118 | 2.547 | 8 | 1.156 |
| 57 | TF0 | 5.598 | 10.426 | 7.925 | 7.602 | 0 | 323 | 100% | 95,92% | 0 | 0,245% | 6,75% | 0% | 154 | 2.670 | 8 | 1.371 |
| 57 | ONE_MF | 5.598 | 10.426 | 7.925 | 7.624 | 0 | 301 | 100% | 96,20% | 0 | 0,244% | 6,10% | 0% | 137 | 2.665 | 8 | 1.232 |

**Análise pareada por identidade-base** (X = TP_ONE_MF − TP_TF0; o IC assume identidades independentes, o que não foi validado):

| Seed | ΔTP | ΔFP | Ganho de recall | IC95% cluster-robusto | Melhor / pior / empate | Sinal (p unil.) | Hoeffding histórico (α 0,025) |
|---|---:|---:|---:|---|---|---:|---|
| 48 | +50 | 0 | +0,650 | [+0,406; +0,894] | 47 / 6 / 5.489 | 2,9×10⁻⁹ | não rejeita |
| 49 | +33 | 0 | +0,424 | [+0,221; +0,627] | 32 / 6 / 5.542 | 1,2×10⁻⁵ | não rejeita |
| 50 | +25 | 0 | +0,331 | [+0,122; +0,540] | 28 / 7 / 5.386 | 2,5×10⁻⁴ | não rejeita |
| 51 | +30 | 0 | +0,390 | [+0,203; +0,577] | 26 / 4 / 5.437 | 3,0×10⁻⁵ | não rejeita |
| 52 | +37 | 0 | +0,470 | [+0,243; +0,696] | 37 / 7 / 5.650 | 2,6×10⁻⁶ | não rejeita |
| 53 | +33 | 0 | +0,424 | [+0,224; +0,624] | 36 / 9 / 5.581 | 3,3×10⁻⁵ | não rejeita |
| 54 | +28 | 0 | +0,362 | [+0,153; +0,571] | 31 / 7 / 5.451 | 5,8×10⁻⁵ | não rejeita |
| 55 | +43 | 0 | +0,562 | [+0,338; +0,787] | 41 / 6 / 5.476 | 8,9×10⁻⁸ | não rejeita |
| 56 | +31 | 0 | +0,378 | [+0,211; +0,545] | 30 / 4 / 5.580 | 3,1×10⁻⁶ | não rejeita |
| 57 | +22 | 0 | +0,278 | [+0,110; +0,445] | 24 / 4 / 5.570 | 9,0×10⁻⁵ | não rejeita |

As margens 2 e 4 (descritivas), o Hoeffding de FDR por identidade (5,41–5,82%) e as tabelas completas por braço estão em `V9_TF_3Sigma_Seeds48_57/seed{N}.md`. Nas margens 2 e 4, o ΔTP foi sempre positivo (+25 a +53 e +321 a +432) e houve 0 FP em todos os braços.

## 13. Resultados consolidados (grupo confirmatório, margem 0,5)

| Medida | Valor |
|---|---|
| Campanhas válidas | 10 de 10 |
| Campanhas positivas / não positivas | **10 / 0** |
| Valor-p bilateral exato | **0,001953125**, menor que α 0,0026998 |
| Significância equivalente (bilateral) | **3,10σ** |
| Ganho médio por campanha | +0,427 p.p. |
| Desvio-padrão / erro-padrão | 0,110 / 0,035 p.p. |
| IC t(9) 95% da magnitude | [+0,348; +0,506] p.p. |
| Ganho mínimo / máximo | +0,278 p.p. (seed 57) / +0,650 p.p. (seed 48) |
| Recall médio TF0 / ONE_MF | 95,85% / 96,28% |
| Agregado (77.863 elegíveis) | TF0 95,85%, ONE_MF 96,28%; ΔTP +332 (+0,426 p.p.), IC cluster [+0,362; +0,491]¹ |
| ΔFP total | 0 (TF0 0, ONE_MF 0, em todos os braços e margens) |
| FDR observada | 0 nos dois braços; limite CP condicional da ONE_MF 0,234–0,256% por campanha¹ |
| Fragmentação média | 6,60% → 5,88% (queda de 0,46 a 1,05 p.p. em 10 de 10) |
| Fusão | 0% nos dois braços |
| Conflitos | ONE_MF −17 a −36 por campanha |

¹ Pressupõe independência entre identidades, o que não foi validado. Não faz parte do critério primário.

## 14. Comparação histórica

| Grupo | Seeds | Positivas | Ganho médio | DP | Mín–máx | FP TF0 / ONE_MF |
|---|---|---|---:|---:|---|---|
| Histórico (exploratório/descritivo) | 42–47 | 6 de 6 | +0,392 | 0,071 | +0,287 a +0,486 | 2 / 0 |
| **Confirmatório** | **48–57** | **10 de 10** | **+0,427** | **0,110** | **+0,278 a +0,650** | **0 / 0** |
| Geral (descritivo, **não** usado no teste) | 42–57 | 16 de 16 | +0,414 | 0,096 | +0,278 a +0,650 | 2 / 0 |

**Tabela geral das 16 campanhas:**

| Seed | Grupo | Recall TF0 | Recall ONE_MF | Ganho | FP TF0 | FP ONE_MF |
|---|---|---:|---:|---:|---:|---:|
| 42 | histórico (seleção) | 95,71% | 96,07% | +0,36 | 0 | 0 |
| 43 | histórico | 96,00% | 96,46% | +0,45 | 0 | 0 |
| 44 | histórico (exposta) | 95,18% | 95,66% | +0,49 | 0 | 0 |
| 45 | histórico | 96,12% | 96,40% | +0,29 | 0 | 0 |
| 46 | histórico | 96,50% | 96,89% | +0,39 | 2 | 0 |
| 47 | histórico | 96,19% | 96,57% | +0,38 | 0 | 0 |
| 48 | confirmatório | 95,93% | 96,58% | +0,65 | 0 | 0 |
| 49 | confirmatório | 95,72% | 96,14% | +0,42 | 0 | 0 |
| 50 | confirmatório | 96,04% | 96,37% | +0,33 | 0 | 0 |
| 51 | confirmatório | 95,80% | 96,19% | +0,39 | 0 | 0 |
| 52 | confirmatório | 95,53% | 96,00% | +0,47 | 0 | 0 |
| 53 | confirmatório | 95,50% | 95,93% | +0,42 | 0 | 0 |
| 54 | confirmatório | 96,13% | 96,49% | +0,36 | 0 | 0 |
| 55 | confirmatório | 95,61% | 96,17% | +0,56 | 0 | 0 |
| 56 | confirmatório | 96,30% | 96,68% | +0,38 | 0 | 0 |
| 57 | confirmatório | 95,92% | 96,20% | +0,28 | 0 | 0 |

Os grupos **não** foram misturados no cálculo da significância confirmatória.

## 15. Reprodutibilidade

O ganho se reproduziu em todas as 16 campanhas, com magnitude estável: entre +0,28 e +0,65 p.p., coeficiente de variação de cerca de 26% no grupo confirmatório. A média confirmatória (+0,427) fica dentro da faixa histórica e do IC das seeds 42–47. Não há sinal de piora em FP, fusão ou conflitos.

## 16. Inferência estatística

- **Direção do ganho (critério primário):** sinal exato bilateral, p = 0,00195, ou 3,10σ. **Atendido.**
  - Pressuposto: as campanhas são réplicas independentes do gerador. Isso é sustentado no desenho: a única variável é a seed; cada campanha tem runner, SQL, corpus e artefato próprios; os modelos estão congelados; não houve seleção posterior nem campanhas extras.
  - O teste não supõe independência entre identidades.
- **Magnitude:** IC t(9) 95% de [+0,35; +0,51] p.p. por campanha. Ele pressupõe ganhos por campanha aproximadamente normais. O IC agregado cluster-robusto [+0,36; +0,49] supõe identidades independentes e é só descritivo.
- **Hoeffding histórico (α 0,025, por campanha):** não rejeita H0 em nenhuma das 10, como nas seeds 43–47. Ficou preservado sem alteração. Ele usa só a amplitude [−4, 4] e é conservador demais para um efeito em que cerca de 99% das identidades têm X = 0. Não é o critério desta etapa e não foi reescrito.
- **O que o 3σ não significa:**
  - não significa que 99,73% dos vínculos estejam corretos;
  - não é a probabilidade de o algoritmo estar certo;
  - não vale para dados reais;
  - diz respeito à direção do ganho na população sintética da Jornada.

## 17. FDR e falsos positivos

- **Ocorrências:** 0 FP nos 60 braços confirmatórios (10 seeds × 2 configurações × 3 margens). FP = 0 **não** significa FDR verdadeira igual a zero.
- **Limites:** o limite CP condicional da ONE_MF ficou em 0,23–0,26% por campanha, sob independência não validada. O limite conservador por identidade (Hoeffding) ficou em 5,4–5,8%, acima da meta de 1%.
- **Certificação de FDR ≤ 1%:** **não obtida**. Continua exigindo validação da independência e avaliação própria.

## 18. Fragmentação e fusão

- **Fragmentação:** caiu em 10 de 10 campanhas, de 0,46 a 1,05 p.p. (média 6,60% → 5,88%). Isso é coerente com o ganho de recall.
- **Fusão:** 0 nas duas configurações.

## 19. Latência

- **Faixa:** P95 de 0,58 a 1,37 s por braço, com 20 repetições, em runners compartilhados e até 4 campanhas em paralelo.
- **Diferença entre braços:** a ONE_MF teve P95 menor em 10 de 10 na margem 0,5. O TF0 roda primeiro e sofre aquecimento, então isso **não** é evidência de desempenho.
- **Uso:** não é SLA nem P95 operacional.

## 20. Limitações

1. Resultado restrito à população sintética do gerador (erro `clean`), sem generalização para dados reais.
2. Exposição fora do repositório desconhecida para as seeds 48–57.
3. Independência entre campanhas sustentada pelo desenho, não por teste empírico. Independência entre identidades não validada.
4. Executado em SHA de branch (PR #1276), não na master.
5. FDR ≤ 1%, Gold (falsas criações), P95 operacional e replay da V8 não foram avaliados.
6. Algumas mensagens de log ainda dizem "seed-43". São textos fixos e cosméticos; o `TargetSeed`, os planos e os hashes estão corretos.

## 21. Conclusão técnica

A superioridade do recall da ONE_MF sobre o TF0 na população sintética da Jornada está **confirmada em 3σ** pelo critério pré-registrado. Foram 10 de 10 campanhas novas, p bilateral = 0,00195 (3,10σ), com ganho médio de +0,43 p.p. (IC95% [+0,35; +0,51]), sem falsos positivos ou fusões adicionais e com menos fragmentação. FDR, Gold, compatibilidade com a V8 e autorização operacional continuam separados e não certificados.

## 22. Recomendações

1. Adotar a ONE_MF como configuração TF candidata da V9, mantendo o TF0 como controle.
2. Certificar a FDR separadamente: validar a independência entre identidades ou usar um método por cluster validado e pré-registrado.
3. Medir falsas criações no Gold e P95 operacional; fazer o replay comparável da V8.
4. **Merge da PR #1276 somente com autorização específica do PO.** Ativação somente pela DT-15.

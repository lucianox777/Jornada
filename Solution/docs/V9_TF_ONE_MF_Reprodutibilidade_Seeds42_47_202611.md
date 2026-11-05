# V9 — Reprodutibilidade da ONE_MF × TF0, seeds 42–47 (issue #1275)

## 1. Resumo executivo

A ONE_MF (`TF_ONE_MF_W1_M0_A0.5_T0.95`) teve recall maior que o TF0 (`T0.95_TF0`) **nas 6 seeds**. O ganho médio foi de **+0,39 p.p.**, com desvio-padrão entre seeds de 0,07 p.p. e faixa de +0,29 a +0,49 p.p. Os modelos estavam congelados e não houve refit.

- **Falsos positivos:** a ONE_MF teve **0 FP em todas as seeds**. O TF0 teve **2 FP na seed 46**. Nenhuma seed teve aumento de FP com a ONE_MF.
- **Fragmentação:** caiu em todas as 5 seeds medidas por identidade.
- **Repetição empírica:** forte.
- **Certificação estatística: não obtida.** O teste de Hoeffding pré-declarado não rejeita H0 em nenhuma seed. A independência entre identidades não foi validada. Nenhuma seed é holdout confirmatório novo, e a 44 está comprovadamente exposta.
- **Autorização operacional:** inexistente e separada (DT-15).

## 2. Objetivo

Verificar se o ganho de recall da ONE_MF sobre o TF0, observado nas seeds 42 e 43, se reproduz em 4 corpora novos (seeds 44–47). A verificação é feita sem recalibrar e mede também FP, FDR, fragmentação, fusão, conflitos, fanout e latência.

## 3. Metodologia

O procedimento é o mesmo da seed 43 (run 41925355648):
- workflow `v9-tf-seed43-frozen-transfer.yml`, agora parametrizado pelo input `target_seed`;
- o gerador C# gera um corpus novo de 30.000 identidades-base;
- `V9FrozenTransferCli` reproduz os 6 snapshots seed 42 congelados (`EvaluateFrozenHoldoutAsync`) em SQL Server Docker descartável;
- a medição usa **somente a VALIDATION**, e o TEST não é lido;
- são 20 repetições ponta a ponta por braço;
- a análise pareada por identidade-base é feita por `v9-tf-seed43-frozen-transfer-physical.py analyze`, com as mesmas definições da seed 43.

A margem 0,5 é a família primária pré-declarada. As margens 2 e 4 são descritivas.

**Diferença de implementação, documentada:** a seed 42 foi medida dentro do estudo adaptativo de 45 braços (run 41900357031), que **selecionou** a ONE_MF nessa própria VALIDATION. Por isso a seed 42 é amostra de seleção, sujeita a viés de seleção, e não tem resultados por identidade, fragmentação nem P95. As seeds 43–47 usam o mesmo replay congelado.

## 4. Modelo de dados reutilizado

- **Gerador:** `JORNADA_SYNTH_CORPUS_CSHARP_V1`, perfil `DEMOGRAPHIC_PRIMARY_V1`, erros `clean`, cap de 4 observações por identidade.
- **Partição:** 60/20/20 por identidade-base.
- **Referências:** as mesmas referências IBGE de nomes e de nascimento.
- **Verificação:** o gate (`v9-tf-frozen-transfer-lock-gate.py`) comparou o código do gerador (digest dos 25 arquivos) e as referências com o commit da seed 43 **antes** da geração. Não existe diff entre `b98a0118` e a branch nesses caminhos.

## 5–6. Manifesto congelado e digest

`Solution/docs/V9_TF_SEEDS_44_47_LOCK.json`, status `LOCKED`, SHA-256 **`822648cc3cca71368b77afad1fe816ae1ee9f87583311e1b25f18e19f01bfba3`**. O digest está fixado no workflow (`SEEDS_44_47_LOCK_SHA256`) e foi verificado em cada um dos 4 runs.

| Campo | Valor |
|---|---|
| Commit do gerador | `b98a01180737909edf7cbd0bee4ba2757d52fe0f` |
| Digest do gerador | `924ad645…a8deb` |
| Árvore git do gerador | `56cc0c10…` |
| IBGE publicado | `E3CC61BC…E885` |
| Materialização TF | `89211544…9243` |
| Partição (arquivo) | `83015950…48e4` |
| Snapshot TF0 | `f0103496…b12e` |
| Snapshot ONE_MF | `8bdf08af…d29f` |
| `study.json` seed 42 | `7499ee63…22cf` |
| `plan-tf.json` seed 42 | `b6f5f519…ee8` |

## 7. Seeds e exposição

| Seed | Exposição | Uso |
|---|---|---|
| 42 | Fonte do ajuste e da seleção (TF-001) | Descritiva, viés de seleção |
| 43 | Exposta antes (multiseed CI e estudos de 14/10); `V9FreshHoldoutProtocol` a rejeita | Descritiva |
| 44 | **Exposta**: CI multiseed (seeds 43 e 44), docs de 14/10, rejeitada pelo `V9FreshHoldoutProtocol` | Descritiva |
| 45, 46, 47 | Nenhuma exposição encontrada no repositório (código, docs, workflows, git log). **Não sei** se houve uso fora dele. | Réplicas descritivas novas, sem orçamento alfa confirmatório pré-registrado |

## 8–11. Commits, hashes, runs, tempos e paralelismo

As seeds 44–47 rodaram na branch `feature/v9-tf-frozen-transfer-seeds44-47`, SHA **`345038121a3612744fec369379e114e97ec5db93`** (PR #1276, **sem merge**). Os resultados **não são da master**.

**Paralelismo:** as 4 rodaram em paralelo. Cada uma teve runner, SQL Docker, banco `JornadaHarness`, diretório, corpus e artefato próprios. Os hashes do corpus foram congelados antes da avaliação, e o `sha256sum --check` passou depois dela.

| Seed | Run | Início–fim (UTC) | Duração | `observacoes.csv` | `gabarito.json` | `generation-manifest.json` | `transfer-results.json` |
|---|---|---|---|---|---|---|---|
| 42 | [41900357031](https://github.com/lucianox777/Jornada/actions/runs/41900357031) | 04/11 19:47–19:55 | ~8 min | `28bbb9f4…5d17` | — | `2f6ed064…e613` | (study.json `7499ee63…`) |
| 43 | [41925355648](https://github.com/lucianox777/Jornada/actions/runs/41925355648) | 04/11 22:26:55–22:32:17 | 5 min 22 s | `b72815d5…5d89` | — | `70f60288…58d7` | `02d7db47…1eb2` |
| 44 | [41942914164](https://github.com/lucianox777/Jornada/actions/runs/41942914164) | 05/11 00:39:11–00:46:26 | 7 min 15 s | `a33c096a…24e2` | `f2d9b087…7ef4` | `2ea44fbc…1c4f` | `6428dd4e…b411` |
| 45 | [41942917704](https://github.com/lucianox777/Jornada/actions/runs/41942917704) | 00:39:12–00:45:59 | 6 min 47 s | `eb887de9…4e7d` | `d8c80086…0c` | `c8790d4a…72cc` | `bf8e2a70…cbf0e` |
| 46 | [41942921106](https://github.com/lucianox777/Jornada/actions/runs/41942921106) | 00:39:14–00:45:52 | 6 min 38 s | `e2561c86…daf1` | `3909a35d…6d22` | `0d228941…12af` | `18271532…9a15` |
| 47 | [41942924301](https://github.com/lucianox777/Jornada/actions/runs/41942924301) | 00:39:16–00:45:44 | 6 min 28 s | `bfd732c1…dbc8` | `44fc6721…f16c` | `6720f631…9712` | `375a893d…771a7` |

Os hashes completos estão nos logs de cada run e nos artefatos `v9-tf-seed{N}-frozen-transfer-{run}-345038121a…`, com retenção de 90 dias. Os hashes dos planos por seed, do preflight, do pin e do bootstrap aparecem no log de cada run.

## 12. Resultados individuais (margem 0,5, VALIDATION)

Definições:
- **Elegíveis:** fontes elegíveis antes do blocking.
- **FN:** elegíveis menos TP, incluindo as perdas de blocking.
- **Fragmentação:** identidades com fonte elegível não vinculada, sobre as identidades com fontes elegíveis.
- **Fusão:** identidades com falso aceite, sobre todas as identidades.
- **FDR sup.:** limite Clopper-Pearson condicional por cluster, família de 2 modelos, sem independência validada.
- **Fanout:** P99 do grau de candidatos falsos por observação.

| Seed | Config | Ident. | Obs. | Elegíveis | TP | FP | FN | Precisão | Recall | FDR obs. | FDR sup. | Fragm. | Fusão | Confl. | Fanout P99 | P95 ms |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 42 | TF0 | 5.556 | 10.296 | 7.841 | 7.505 | 0 | 336 | 100% | 95,71% | 0 | 0,705%¹ | — | — | 176 | — | — |
| 42 | ONE_MF | 5.556 | 10.296 | 7.841 | 7.533 | 0 | 308 | 100% | 96,07% | 0 | 0,702%¹ | — | — | 143 | — | — |
| 43 | TF0 | 5.492 | 10.151 | 7.704 | 7.396 | 0 | 308 | 100% | 96,00% | 0 | 0,252% | 6,57% | 0% | 173 | 7 | 845 |
| 43 | ONE_MF | 5.492 | 10.151 | 7.704 | 7.431 | 0 | 273 | 100% | 96,46% | 0 | 0,251% | 5,68% | 0% | 147 | 7 | 656 |
| 44 | TF0 | 5.461 | 10.071 | 7.610 | 7.243 | 0 | 367 | 100% | 95,18% | 0 | 0,258% | 7,20% | 0% | 191 | 9 | 1.481 |
| 44 | ONE_MF | 5.461 | 10.071 | 7.610 | 7.280 | 0 | 330 | 100% | 95,66% | 0 | 0,256% | 6,47% | 0% | 161 | 9 | 1.255 |
| 45 | TF0 | 5.499 | 10.369 | 8.008 | 7.697 | 0 | 311 | 100% | 96,12% | 0 | 0,241% | 6,15% | 0% | 151 | 9 | 1.431 |
| 45 | ONE_MF | 5.499 | 10.369 | 8.008 | 7.720 | 0 | 288 | 100% | 96,40% | 0 | 0,241% | 5,51% | 0% | 131 | 9 | 1.217 |
| 46 | TF0 | 5.439 | 10.126 | 7.743 | 7.472 | **2** | 271 | 99,97% | 96,50% | 0,027% | 0,461% | 5,76% | **0,037%** | 159 | 7 | 964 |
| 46 | ONE_MF | 5.439 | 10.126 | 7.743 | 7.502 | 0 | 241 | 100% | 96,89% | 0 | 0,248% | 4,94% | 0% | 135 | 7 | 966 |
| 47 | TF0 | 5.517 | 10.079 | 7.619 | 7.329 | 0 | 290 | 100% | 96,19% | 0 | 0,254% | 6,05% | 0% | 134 | 9 | 1.278 |
| 47 | ONE_MF | 5.517 | 10.079 | 7.619 | 7.358 | 0 | 261 | 100% | 96,57% | 0 | 0,253% | 5,46% | 0% | 105 | 9 | 1.121 |

¹ Seed 42: a família tem 3.861 comparações do estudo adaptativo, e por isso esse limite não é comparável ao das demais seeds. O limite de Hoeffding por identidade ficou entre 5,5% e 5,9% em todas as seeds 43–47, acima da meta de 1%.

**Margens descritivas, Δ TP (ONE_MF − TF0):**

| Seed | Margem 2 | Margem 4 | FP na margem 4 (TF0 / ONE_MF) |
|---|---:|---:|---|
| 43 | +50 | +331 | 0 / 0 |
| 44 | +44 | +389 | 0 / 0 |
| 45 | +32 | +408 | 0 / 0 |
| 46 | +34 | +392 | 2 / 0 |
| 47 | +24 | +286 | 0 / 0 |

Na seed 46, o TF0 teve 2 FP também na margem 2. Os 2 FP do TF0 são os mesmos nas três margens.

## 13. Tabela consolidada (margem 0,5)

| Seed | Recall TF0 | Recall ONE_MF | Ganho ONE_MF | FP TF0 | FP ONE_MF |
|---|---:|---:|---:|---:|---:|
| 42 | 95,71% | 96,07% | +0,36 p.p. | 0 | 0 |
| 43 | 96,00% | 96,46% | +0,45 p.p. | 0 | 0 |
| 44 | 95,18% | 95,66% | +0,49 p.p. | 0 | 0 |
| 45 | 96,12% | 96,40% | +0,29 p.p. | 0 | 0 |
| 46 | 96,50% | 96,89% | +0,39 p.p. | 2 | 0 |
| 47 | 96,19% | 96,57% | +0,38 p.p. | 0 | 0 |

### Estatísticas entre seeds

| Medida | Valor |
|---|---|
| Recall médio por seed, TF0 | 95,95% |
| Recall médio por seed, ONE_MF | 96,34% |
| Recall agregado (ponderado pelas 46.525 elegíveis), TF0 | 95,95% |
| Recall agregado, ONE_MF | 96,34% |
| Ganho médio | +0,392 p.p. |
| Desvio-padrão dos ganhos | 0,071 p.p. |
| Ganho mínimo e máximo | +0,287 p.p. (seed 45) e +0,486 p.p. (seed 44) |
| Seeds com ganho positivo | 6 de 6 |
| Seeds com aumento de FP | 0 |
| FP totais na margem 0,5 | TF0 2, ONE_MF 0 |
| FP em todos os braços e margens | 6, todos do TF0 na seed 46 |
| Fragmentação média (seeds 43–47) | TF0 6,35%, ONE_MF 5,61% (−0,73 p.p.); queda em 5 de 5 seeds |
| Fusão | TF0 0,037% só na seed 46 (2 identidades); ONE_MF 0% em todas |

## 14–16. Comparação, reprodutibilidade e análise estatística

**A. Reprodutibilidade empírica.** O ganho é positivo e de magnitude parecida nas 6 seeds, com coeficiente de variação de cerca de 18%. O resultado das seeds 42 e 43 se reproduz.

**C. Inferência estatística.** Há três leituras, com pressupostos diferentes:

1. **Entre seeds:** cada seed é um corpus gerado de forma independente; esta é a unidade mais defensável.
   - Teste do sinal: 6 de 6 ganhos positivos, p unilateral = 1/64 ≈ 0,016. Excluindo a seed 42, que serviu à seleção: 5 de 5, p = 1/32 ≈ 0,031.
   - t de Student com 5 g.l. para o ganho médio: IC95% [+0,32; +0,47] p.p.
   - Esta leitura **não depende** da independência entre identidades de um mesmo corpus. Depende de as seeds serem réplicas do mesmo gerador, e por isso não generaliza para dados reais.
2. **Pareada por identidade, dentro de cada seed:** todos os IC cluster-robustos excluem zero (tabela abaixo). Combinando as seeds 43–47, que vêm de corpora independentes: ΔTP = +154 em 38.684 elegíveis, ou **+0,40 p.p.**, IC95% [+0,31; +0,49]. Somando, 163 identidades ficaram melhores com a ONE_MF e 31 piores. Esta leitura **pressupõe** independência entre identidades, o que não foi validado: a competição entre candidatos no blocking e no agrupamento pode violar isso.
3. **Hoeffding unilateral pré-declarado** (α 0,025, X ∈ [−4, 4]): **não rejeita H0 em nenhuma seed**. O limite inferior da média por identidade ficou em cerca de −0,14. Avaliação crítica:
   - O Hoeffding usa só a amplitude [−4, 4] e ignora a variância. Na prática, 99% das identidades têm X = 0, o que torna o teste extremamente conservador.
   - Pelo desenho da #1268, ele exigiria cerca de 14,9 milhões de identidades.
   - Um teste sensível à variância (Bernstein ou Bernstein empírico) ou um teste por seed seria mais adequado. **Não troquei o critério retroativamente**: isso exige pré-registro novo.

| Seed | ΔTP | ΔFP | Ganho de recall | IC95% cluster-robusto | Melhor / pior / empate | Sinal (p unil.) | Hoeffding |
|---|---:|---:|---:|---|---|---:|---|
| 43 | +35 | 0 | +0,454 | [+0,243; +0,665] | 36 / 6 / 5.450 | 1,4×10⁻⁶ | não rejeita |
| 44 | +37 | 0 | +0,486 | [+0,270; +0,703] | 37 / 7 / 5.417 | 2,7×10⁻⁶ | não rejeita |
| 45 | +23 | 0 | +0,287 | [+0,087; +0,487] | 31 / 9 / 5.459 | 3,4×10⁻⁴ | não rejeita |
| 46 | +30 | −2 | +0,387 | [+0,205; +0,570] | 30 / 4 / 5.405 | 3,1×10⁻⁶ | não rejeita |
| 47 | +29 | 0 | +0,381 | [+0,190; +0,571] | 29 / 5 / 5.483 | 1,9×10⁻⁵ | não rejeita |

Seed 42: ΔTP +28 e ΔFP 0, só em agregado (sem dados por identidade).

**D. Certificação.** Os critérios aprovados exigem as três condições abaixo. Nenhuma foi satisfeita, e os critérios **não foram modificados**.
- Limite de FDR certificado ≤ 1% com independência validada (`ClusterIidIndependentlyValidated=false`).
- Holdout confirmatório novo.
- Falsas criações e P95 operacional medidos.

## 17. FDR e falsos positivos

- **ONE_MF:** 0 FP observados em 46.525 fontes elegíveis (6 seeds) e em todas as margens.
- **TF0:** 2 FP na seed 46.
- FP = 0 **não** significa FDR verdadeira igual a zero. O limite condicional da ONE_MF ficou em cerca de 0,24% a 0,26% por seed, sob independência não validada. O limite conservador por identidade (Hoeffding) ficou em cerca de 5,5% a 5,8%.
- Nos dados observados, a ONE_MF não piora a segurança. Também não está demonstrado que a melhore: são só 2 eventos, numa única seed.

## 18. Fragmentação e fusão

- **Fragmentação:** a ONE_MF reduziu entre 0,59 e 0,89 p.p. por seed. O efeito é coerente com o ganho de recall, porque menos fontes da mesma pessoa ficam sem vínculo.
- **Fusão:** só o TF0 teve fusão, na seed 46, com 2 identidades.
- **Conflitos:** a ONE_MF teve menos conflitos em todas as seeds, entre −20 e −30 por seed.

## 19. Latência

- **Seeds 44–47, em paralelo:** P95 entre 0,73 e 1,48 s.
- **Seed 43, sozinha:** entre 0,58 e 0,85 s.
- O paralelismo e a variação entre runners do GitHub **afetam a latência**. O primeiro braço de cada run (TF0, margem 0,5) também tem o maior P95, o que é compatível com aquecimento.
- As métricas de qualidade não dependem de tempo nem de ordem: o replay de cada braço é verificado como idêntico nas 20 repetições.
- A latência **não** é comparável entre runs, nem é SLA de produção.

## 20. Limitações

1. Os corpora são sintéticos, todos do mesmo gerador e com erro `clean`. Não há generalização para dados reais.
2. As seeds 42, 43 e 44 estão expostas, e a 42 é a amostra de seleção. Nenhuma seed é holdout confirmatório.
3. A independência entre identidades não foi validada.
4. P95 medido em runner compartilhado e em paralelo.
5. Falsas criações (Gold) não foram medidas.
6. **Não há comparação com a V8 completa:** nenhuma superioridade sobre a V8 é afirmada.
7. As seeds 44–47 rodaram num SHA de branch sem merge (#1276).
8. Algumas mensagens de log ainda dizem "seed-43". São textos fixos, cosméticos; o `TargetSeed` dos resultados, os planos e os hashes estão corretos.

## 21. Conclusão técnica

A melhoria da ONE_MF sobre o TF0 **se reproduz empiricamente** em 6 corpora sintéticos independentes:
- ganho de recall entre +0,29 e +0,49 p.p.;
- menos fragmentação e menos conflitos;
- nenhum FP adicional (o TF0 teve 2 FP e a ONE_MF nenhum).

A inferência entre seeds é favorável. Os testes por identidade também são favoráveis, mas sob um pressuposto não validado. O teste conservador pré-declarado não é atingido, e **não há certificação estatística nem autorização operacional**.

## 22. Recomendação

1. Manter a ONE_MF como **candidata principal de TF na V9**, com o TF0 como controle.
2. Para certificar, pré-registrar antes de olhar dados novos:
   - um holdout confirmatório com seeds nunca usadas e orçamento alfa próprio;
   - um teste com unidade entre seeds, ou um limite sensível à variância.
3. Validar a independência entre identidades e medir falsas criações e P95 operacional.
4. Fazer um replay comparável da V8 antes de qualquer afirmação contra ela.
5. Ativação somente pela DT-15, com autoridade separada.

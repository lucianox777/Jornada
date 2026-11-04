# V9-TF-002 — transferência congelada seed 42 → seed 43: resultado físico

**Status:** validação física descritiva **concluída**. Certificação estatística **não obtida**. Autorização operacional **não concedida** (etapa separada, DT-15). Nenhum modelo foi ativado.

## Evidência e hashes

| Item | Valor |
|---|---|
| Run físico seed 43 | [41925355648](https://github.com/lucianox777/Jornada/actions/runs/41925355648), master `b98a01180737909edf7cbd0bee4ba2757d52fe0f` |
| Run anterior com falha | [41920808713](https://github.com/lucianox777/Jornada/actions/runs/41920808713), master `be97319d`. Falhou por caminho relativo antes de ler a VALIDATION (corrigido na #1271); nenhuma medição |
| Fonte congelada | run 41900357031, SHA `935f704edd2bceb6a125ecfe532ad5b71f9c89b9` |
| `study.json` seed 42 | `7499ee635ecb4999106dbee22a5993f9f16b88d75c725f99490f2fccd39cba2b` |
| `plan-tf.json` seed 42 | `b6f5f5197074f7ecedf004745f763df3b66a0496f9355589242062f9f1d33ee8` |
| Snapshot TF0, margem 0,5 | `f010349623b6bae32b4303f680a715bb1264271fec0e325730cce3f7bc0eb12e` |
| Snapshot ONE_MF, margem 0,5 | `8bdf08affbbcd88550cb055e42a063c1bfff1ffb968b55a3707be6179475d29f` |
| Plano pré-registrado (`executor.json`) | `4a8c6a2fcbca00b02ea870e3f2d3b03832555d42f75c76d4c9cd9f5989cc401f` |
| Plano do preflight | `841af20a9db7cf7a78637f7c39504c16808f52dc0f4aa0ffc1f5dd84885837ca` |
| `observacoes.csv` seed 43 | `b72815d5b0d26ac2ffdec1ad2708e45ba08636d98c65a76f6862f027b02f5d89` |
| `generation-manifest.json` seed 43 | `70f60288721a290a0b3c7394a1c4a08f1a452decece81073b4b2ae38b1b158d7` |
| Pin IBGE seed 43 | `6f1b8b47cb1c33f90e741133327c792a50bd878a4b166e6662155c7762df419c` |
| IBGE publicado / TF materializado | `E3CC61BC…E885` / `89211544…9243` (iguais ao seed 42, versão 1) |
| `transfer-results.json` | `02d7db4757ee9816a3bf531d7da1b68379aea0232b67760c33a761740cf41eb2` |
| `analysis.json` | `1ee490a6a4c8611a0ef60c8557ad8b2298f102594353698c26c5988fc8cb3227` |
| `preflight.json` | `c71b07c5a3459b8a09d78a8ea0c16ea1fe6366a735cc10bae8a5b17780d9bc96` |

O plano e os 6 snapshots foram commitados (PR #1270) **antes** de existir o corpus seed 43. O run verificou os SHA do artefato de origem antes de gerar qualquer dado.

## Desenho executado

- **Sem refit:** os 6 snapshots seed 42 (TF0 e `TF_ONE_MF_W1_M0_A0.5_T0.95` × margens 0,5/2/4) foram reproduzidos sem alteração por `EvaluateFrozenHoldoutAsync`. Não houve reajuste de m/u, TF, limiar ou blocking.
- **Ambiente:** SQL físico no JornadaHarness descartável. Corpus seed 43 novo, com 30.000 identidades-base e o mesmo produtor C# (clean, `DEMOGRAPHIC_PRIMARY_V1`, cap 4).
- **Partições:** somente a VALIDATION, com 10.151 observações e 5.492 identidades-base. O TEST não foi lido.
- **Família primária pré-declarada:** margem 0,5, com 2 modelos. As margens 2 e 4 são descritivas.
- **P95:** 20 repetições ponta a ponta por braço.

## Resultados (VALIDATION seed 43)

| Opção | Margem | TP | FP | FN | Precisão | Recall | FDR sup. CP cond. | Conflitos | Fragmentação | Fusão | P95 ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| TF0 | 0,5 | 7.396 | 0 | 308 | 100% | 96,00% | 0,252% | 173 | 6,57% | 0% | 845* |
| ONE_MF | 0,5 | 7.431 | 0 | 273 | 100% | 96,46% | 0,251% | 147 | 5,68% | 0% | 656 |
| TF0 | 2 | 7.283 | 0 | 421 | 100% | 94,54% | 0,256% | 286 | 7,13% | 0% | 581 |
| ONE_MF | 2 | 7.333 | 0 | 371 | 100% | 95,18% | 0,254% | 245 | 6,83% | 0% | 625 |
| TF0 | 4 | 6.812 | 0 | 892 | 100% | 88,42% | 0,275% | 757 | 12,71% | 0% | 576 |
| ONE_MF | 4 | 7.143 | 0 | 561 | 100% | 92,72% | 0,261% | 435 | 9,06% | 0% | 653 |

Definições usadas na tabela:
- **Base:** 7.704 fontes elegíveis antes do blocking. O recall de blocking é de 98,83%.
- **FN:** fontes elegíveis não aceitas, inclusive as perdidas no blocking.
- **Fragmentação:** identidades com ao menos uma fonte elegível não vinculada, dividido pelas 3.045 identidades com fontes elegíveis.
- **Fusão:** identidades com algum falso aceite.
- **FDR sup. CP condicional:** limite superior Clopper-Pearson condicional por cluster, com família de 2 modelos. Pressupõe independência entre identidades, o que **não foi validado**.
- **Hoeffding por identidade:** cerca de 5,7% a 6,3%, acima da meta de 1%.
- **(*) P95:** o primeiro braço executado foi TF0 na margem 0,5, e o seu P95 maior é compatível com aquecimento. O P95 é latência do quadro de VALIDATION em runner do GitHub, **não** é SLA de produção.

### Diferença pareada por identidade-base (X = TP_ONE_MF − TP_TF0)

| Margem | Família | ΔTP | ΔFP | Δrecall | IC95% cluster-robusto | melhor/pior/empate | Sinal (unil.) | Hoeffding pré-declarado (α 0,025) |
|---:|---|---:|---:|---:|---|---|---:|---|
| 0,5 | primária | +35 | 0 | +0,45 p.p. | [+0,24; +0,67] p.p. | 36 / 6 / 5.450 | 1,4×10⁻⁶ | não rejeita H0 |
| 2 | descritiva | +50 | 0 | +0,65 p.p. | [+0,38; +0,92] p.p. | 45 / 12 / 5.435 | 6,6×10⁻⁶ | não rejeita H0 |
| 4 | descritiva | +331 | 0 | +4,30 p.p. | [+3,50; +5,09] p.p. | 178 / 24 / 5.290 | 1,5×10⁻³⁰ | não rejeita H0 |

### Comparação com o seed 42 (margem 0,5, mesmos snapshots)

| Seed | TF0 recall | ONE_MF recall | ΔTP | ΔFP |
|---|---:|---:|---:|---:|
| 42 (run 41900357031) | 95,71% | 96,07% | +28 | 0 |
| 43 (run 41925355648) | 96,00% | 96,46% | +35 | 0 |

## Leitura

1. **Critério descritivo pré-declarado** (ganho > 0 sem aumento de FP): **atendido**. O ganho do prenome da mãe se repetiu, com sentido e tamanho semelhantes, num corpus novo e sem reajuste.
2. **Critério inferencial pré-declarado** (Hoeffding unilateral, α 0,025): **não atendido**. Ele exigiria cerca de 14,9 milhões de identidades, conforme V9_TF_002_Statistical_Sample_Design. O IC cluster-robusto e o teste de sinal são favoráveis, mas pressupõem identidades independentes. A competição entre candidatos pode violar esse pressuposto, e isso não foi validado.
3. **FDR:** zero FP observado nas 6 medições, e o limite CP condicional ficou em cerca de 0,25%. **Não é certificação**, por três motivos:
   - `ClusterIidIndependentlyValidated=false`;
   - o seed 43 já foi exposto em estudos anteriores e não é holdout confirmatório novo (`V9FreshHoldoutProtocol` o rejeita);
   - a família de 2 modelos só vale para este ensaio pré-registrado e não apaga a multiplicidade histórica de 3.861.
4. **Falsas criações e P95 operacional** continuam sem medição própria. A métrica de "fusão" aqui é por identidade-base no quadro de VALIDATION, e não substitui a medição Gold.

## Auditoria das decisões V8/V9 (DC-V9-TF-20261031, decisões 0–19)

- **Atendidas neste ensaio:**
  - 3: TF do prenome da mãe;
  - 4: pesos por lado;
  - 5: TF0 preservado como controle;
  - 13: 81 células preservadas;
  - 14: blocking sem modificação;
  - 15: Calibrador C# reutilizado, sem segundo motor;
  - 18: TRAIN e VALIDATION separados, com snapshots rastreáveis.
- **Fora do escopo deste ensaio:** 6 a 12 (simétrico/assimétrico, sobrenome e compartilhado) e 19 (U_ref). Foram medidas na TF-001 (seed 42) e não fazem parte da transferência.
- **Critério de aceite (b):** reprodução dos 97,4754% nas condições originais. **Não verificado neste mandato.** O TF0 desta grade usa blocking fixo de 3 passes e não é o mesmo ensaio.
- **Compatibilidade com V8:** a V8 usava TF de prenome da pessoa e da mãe, braço `TF_TWO_PF_MF_W1_M0` na TF-001. Por decisão do PO não há braço V8 nesta transferência. A comparação com a V8 permanece a da TF-001 (seed 42: 96,49%, 1 FP). A opção transferida (só a mãe) é um subconjunto da regra V8 e usa os mesmos ajustes `peso × ln(u_ref / max(f, piso))`.

## Impedimentos à certificação

1. Validar a independência entre identidades, incluindo a competição de candidatos.
2. Um holdout confirmatório **novo**, com protocolo e orçamento alfa próprios. O seed 43 não serve.
3. Uma amostra compatível com o teste pareado pré-declarado, ou um método pareado validado e pré-registrado.
4. Falsas criações (Gold) e P95 operacional medidos.
5. A ativação depende da DT-15 e de autoridade separada.

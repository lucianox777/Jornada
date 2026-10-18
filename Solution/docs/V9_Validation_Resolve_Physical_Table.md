# V9 — VALIDATION pós-Resolve: formatador histórico e probe físico atual

> **Situação superveniente em 18/10/2026 — números físicos existentes:** este
> documento iniciou como **formatador diagnóstico**, quando o probe ainda
> não era acionado por workflow. **Isto foi superado** pelo
> `v9-physical-seed42-probe.yml` e pela CLI
> `--v9-physical-probe-seed42` do Worker. O
> [run clean #39147073026](https://github.com/lucianox777/Jornada/actions/runs/39147073026)
> executou SQL Server descartável, FS TRAIN e VALIDATION pós-Resolve
> por observação; criou 27 linhas de TF=0, auditoria de qualidade
> TRAIN/VALIDATION e artefato #12142909368. A grade 3×2 de TF,
> Gold real, seleção final/TEST independente e homologação **continuam
> pendentes**. `correlated` é histórico. A narrativa V1 abaixo
> permanece para proveniência do formatador antigo, mas não representa
> a ausência atual de execução. Ver [auditoria documental e runners](V9_Reconciliacao_Decisoes_Evidencias_Runners_20261018.md).
>

CLI: `python3 Solution/scripts/v9-physical-validation-resolve-table.py --evidence <runner-measured.json> --out <new.csv>`.

**Não é executor do probe físico.** Este incremento valida e formata **apenas** um JSON previamente exportado por um runner físico com decisões finais Rank→Resolve e verdade independente. Não fornece autenticidade criptográfica do JSON, não executa SQL, não implementa o seletor, não calcula Hoeffding nem cria números. Os campos `source_kind=PHYSICAL_SQL_RUNNER_RESOLVE`, `final_rank_resolve_evaluated=true` e `independent_truth_verified=true` são alegações de entrada, **não comprovantes auditados**. O resultado não certifica V9 e não autoriza ativação.

Entrada obrigatória: `seed=42`, `split=VALIDATION`, `test_used=false`, `runs` com as 12 combinações completas dos perfis `clean`/`correlated`, 3 thresholds e 2 pesos TF. Cada run deve conter `profile`, `threshold`, `tf_weight`, `eligible_true` (antes do blocking), `sql_true`, `tp`, `fp`, `conflict`, `unresolved`, `sql_p95_ms` (repetições físicas comparáveis), `hoeffding_upper_by_identity`, `final_rank_resolve_evaluated` e `independent_truth_verified`.

O formatador rejeita grade incompleta, denominadores impossíveis, resultados de pares que não passaram pelo Resolve e uso do TEST. `fdr_after_resolve` fica indefinida quando TP+FP=0; CONFLITO e NAO_RESOLVIDO permanecem separados. `sql_p95_ms` **não** é latência ponta a ponta. Não confundir asserções sintéticas dos testes do formatador com resultados medidos do corpus de 30 mil.

**Bloqueio histórico da fase V1 (já superado parcialmente):** criar/exportar os 12 resultados do `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` integrado ao Resolve, registrar a verdade independente por identidade e o UCB agrupado, ligar a CLI/workflow ao ambiente descartável `JornadaHarness`, executar os perfis 30k seed 42 e publicar CSV + SHA das fontes. Naquela data não havia tabela de desempenho real. Desde então há a medição física TF=0 de clean por fonte com 27 opções (run 39147073026), **mas não a grade TF positiva, a seleção adaptativa completa nem certificação V9**. Não usar JornadaLocal, IBGE original, HML/PROD ou TEST para ajuste.


## Execução SQL física (seed 42, 30 mil identidades): correção metodológica

O [workflow físico](../../.github/workflows/v9-physical-seed42-probe.yml) chama de fato o
`V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` em `JornadaHarness`
descartável. Foram publicados os primeiros artefatos medidos no run
[39138689675](https://github.com/lucianox777/Jornada/actions/runs/39138689675):
`clean` e `correlated`, 20 repetições SQL cada. O SQL materializa
**pares de observações** cross-gestor, enquanto a operação resolve
**uma observação fonte → uma identidade de destino**. Assim, as primeiras
tabelas de `GlobalRecall = TP_dirigido/(2 × M_elegível_pares)`
eram diagnóstico de pares dirigidos e **não mediam corretamente recall
de decisões únicas por observação**.

A rodada V2 mede em paralelo:

1. **Fonte elegível pré-blocking**: cada observação VALIDATION
   sem CPF pertencente a identidade com pelo menos outra observação
   de *gestor diferente*. Inclui fontes com zero candidatos no SQL.
   `GlobalSourceRecall = TP_fonte / ObservacoesFontesElegiveis`;
   `BlockingSourceRecall = FontesElegiveisComAoMenosUmM_SQL /
   ObservacoesFontesElegiveis`. Não usar `2 × M_pares`.
2. **RAW_SQL_OBSERVATION_CANDIDATES**: cada observação recuperada
   é candidato independente. Serve para quantificar conflitos
   introduzidos por duplicação da mesma pessoa.
3. **ORACLE_SYNTHETIC_GOLD_GROUPING_NOT_OPERATIONAL**: usa somente
   os rótulos *sintéticos de verdade* para juntar candidatos
   pertencentes a uma mesma pessoa, conserva o maior LLR por
   identidade, e chama o MESMO núcleo `ResolveRankedCore`.
   **É um limite exploratório otimista**, com formação de grupos
   perfeita fornecida pelo gabarito; não demonstra desempenho
   de agrupamento Gold real e não pode entrar no seletor como
   evidência operacional. A verdade só escolhe o grupo em que
   os candidatos são consolidados; não é usada para estimar M/U
   ou para alterar o LLR observado.
4. **Grade TF=0** com thresholds `0.99 / 0.999 / 0.9999` e
   margens em log-odds `0 / 0.5 / 2` nos dois modos, 18 células
   por perfil. O novo CSV expõe TP/FP por fonte, conflitos,
   não resolvidos, motivos do Resolve e P95 físico, sem
   alegar que é a grade TF=0/0.5/1.
5. **UCB Hoeffding** continua descritivo e condicionado às premissas
   de independência entre identidades e teto pré-declarado. O
   bootstrap por identidade pode complementar sensibilidade,
   **não substitui por si só um limite superior com cobertura
   frequentista garantida**; sobretudo com FP raro/zero, percentil
   bootstrap pode dar zero indevidamente. Ausência de UCB
   sustentado significa não certificação.

**Cuidados**: a implementação do scorer FS do TRAIN contém
probabilidades, prior e decisão, mas flags de guard da
política operacional precisam ser verificadas explicitamente,
assim como a origem da TF. No código atual, `TF` positiva aplica
`peso × ln(U_TRAIN_SQL(prenome | U,ExactFirst,blocking) /
max(f_IBGE_populacional(prenome),piso))`.
Uma marginal populacional e uma condicional pós-blocking
**não são o mesmo condicionamento estatístico**. A segunda
coluna TF positiva da pesquisa exige demonstração de
compatibilidade e snapshot publicado/pinado; não se
inventam linhas da grade.

Passar de 95% de recall **somente no braço com agrupamento
oracle de gabarito** não torna a V9 pronta para TEST nem
certificada: verificar a formação real do Gold, FDR-UCB por
identidade com suporte válido, catálogos/guards, seletor
pós-Resolve, congelamento TRAIN/VALIDATION e a
proveniência do TEST novo e lacrado antes de avaliar o TEST.


### Terceiro braço — agrupamento sem gabarito

Para não confundir um Gold *oracle* com agrupamento que existe na prática,
o experimento adiciona `BLIND_EXACT_PERSON_MOTHER_DOB_GROUPING`.
O agrupador usa **somente** os valores observados normalizados
`(nome da pessoa, nome da mãe, nascimento)`: agrupa apenas
compostos completos idênticos, mantém isoladas observações com campos
ausentes, não consulta `BasePersonId` para decidir membros, e
mantém o maior score FS da identidade candidata antes de invocar
o mesmo núcleo Resolve. O gabarito somente avalia
**posteriormente** a pureza dos grupos e se a ligação aceita está
certa. Se um grupo blind mistura identidades verdadeiras,
a decisão para ele é contada como **FP** (não como TP fácil).
O CSV expõe `impure_blind_groups`.

É uma experiência de sensibilidade **sem oracle**, não um algoritmo de
Gold aprovado: não reconhece variações de grafia que pertencem
à mesma pessoa e pode fundir homônimos com tripla idêntica.
A tabela contém três comparações da unidade candidata —
`RAW`, `BLIND_EXACT` e `ORACLE_GOLD` —
para cada threshold/margem, todos com TF=0.
O número de linhas passa a **27 por perfil**.
Não ativar em Gold operacional.

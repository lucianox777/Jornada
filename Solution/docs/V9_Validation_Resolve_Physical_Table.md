# V9 — tabela de VALIDATION pós-Resolve (incremento parcial)

CLI: `python3 Solution/scripts/v9-physical-validation-resolve-table.py --evidence <runner-measured.json> --out <new.csv>`.

**Não é executor do probe físico.** Este incremento valida e formata **apenas** um JSON previamente exportado por um runner físico com decisões finais Rank→Resolve e verdade independente. Não fornece autenticidade criptográfica do JSON, não executa SQL, não implementa o seletor, não calcula Hoeffding nem cria números. Os campos `source_kind=PHYSICAL_SQL_RUNNER_RESOLVE`, `final_rank_resolve_evaluated=true` e `independent_truth_verified=true` são alegações de entrada, **não comprovantes auditados**. O resultado não certifica V9 e não autoriza ativação.

Entrada obrigatória: `seed=42`, `split=VALIDATION`, `test_used=false`, `runs` com as 12 combinações completas dos perfis `clean`/`correlated`, 3 thresholds e 2 pesos TF. Cada run deve conter `profile`, `threshold`, `tf_weight`, `eligible_true` (antes do blocking), `sql_true`, `tp`, `fp`, `conflict`, `unresolved`, `sql_p95_ms` (repetições físicas comparáveis), `hoeffding_upper_by_identity`, `final_rank_resolve_evaluated` e `independent_truth_verified`.

O formatador rejeita grade incompleta, denominadores impossíveis, resultados de pares que não passaram pelo Resolve e uso do TEST. `fdr_after_resolve` fica indefinida quando TP+FP=0; CONFLITO e NAO_RESOLVIDO permanecem separados. `sql_p95_ms` **não** é latência ponta a ponta. Não confundir asserções sintéticas dos testes do formatador com resultados medidos do corpus de 30 mil.

**Próximo bloqueio real:** criar/exportar os 12 resultados do `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` integrado ao Resolve, registrar a verdade independente por identidade e o UCB agrupado, ligar a CLI/workflow ao ambiente descartável `JornadaHarness`, executar os perfis 30k seed 42 e publicar CSV + SHA das fontes. Até lá não há tabela de desempenho real nem seleção V9 válida. Não usar JornadaLocal, IBGE original, HML/PROD ou TEST para ajuste.

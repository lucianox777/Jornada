# V9 — orçamento de custo observacional sem autorização de promoção

A seleção por menor U em TRAIN com alvo de 95% não equivale a comprovar
latência física, FDR ou viabilidade municipal. O censo de chaves da #1078
encontra concentração de colisões, e o bootstrap da #1076 revelou limite
inferior de 95% de recall **abaixo** da meta, ainda que a taxa pontual
ultrapasse ligeiramente 95%. A decisão de promoção precisa permanecer
independente desses ensaios.

## Comando opt-in

`python3 scripts/v9-evaluate-blocking-cost-budget.py --report RELATORIO_AGREGADO.json --budget POLITICA_DEV_VERSIONADA.json --out NOVO_RESULTADO.json`

A política deve ser um JSON **explícito** com `version:
V9_OBSERVED_COST_BUDGET_DIAGNOSTIC_V1`, `source_sha256` igual ao
`canonical_observations_sha256` do relatório e três limites obrigatórios
`max_u_degree_mean`, `max_u_degree_p95` e `max_u_degree_p99`.
O campo **opcional** `minimum_global_M_recall`, quando definido,
deve estar entre 0 e 1 e determina o piso específico do estudo. Não
existe piso global de 95% obrigatório. Se faltar política, limite
obrigatório, versão ou SHA, abortar; não preencher ausências com
limiares implícitos. A política deve ser definida antes de avaliar
quaisquer planos e seu SHA deve corresponder ao relatório.

Para os **dois planos treinados exclusivamente em TRAIN**
(`greedy_pruned`, `exact_targeted`), o avaliador verifica
separadamente TRAIN e VALIDATION, sem abrir TEST:

1. O **recall global medido** usa **todos** os pares M sem CPF,
   incluindo os inelegíveis por ausência de nome; usar só
   `name_eligible_truth_M` no denominador não autoriza sucesso.
   O antigo alvo de 95% do estimador de planos permanece registrado
   como **diagnóstico histórico**, não como limite universal do orçamento.
2. A incerteza do recall usa limite inferior unilateral de confiança de
   95% produzido por bootstrap de **identidades-base**, não pares
   dependentes. A evidência bootstrap, as contagens e os indicadores
   históricos continuam sendo validados. Se o orçamento declarar
   `minimum_global_M_recall`, o recall pontual e o limite inferior
   por identidades devem atingir esse valor; se não declarar, os
   números são informados sem reprovação por um piso inexistente.
3. O grau médio de candidatos U de cada observação conta as duas
   extremidades: `2 × U / observacoes_elegiveis`. P95/P99 incluem
   observações com grau zero, e o gate reconcilia grau médio com U.
4. Os limites de U por observação são **observados no universo sintético**
   específico, não multiplicados cegamente por um fator 380 e não
   constituem orçamento aprovado para município de 11,45 milhões.
   Somente um confronto físico SQL Server e frequência conjunta/gestor
   adequada permite modelar o custo de escala.

A saída inclui `optional_recall_floor` (nulo quando ausente), além
dos limites de custo. Uma aprovação diagnóstica de **custo** sem piso
opcional de recall **não demonstra recall aceitável** nem autoriza
seleção, congelamento ou ativação de um modelo.
A saída contém somente totais, percentis e status por partição;
nenhum CPF, nome, chave nominal ou ID é serializado. Mesmo que todos
os limites sejam satisfeitos, `may_promote_policy=false`,
`city_scale_certified=false`, `fdr_measured=false` e
`sql_server_latency_measured=false` são invariáveis. A ausência de
custo mensurado SQL Server e de FDR de vínculos **aceitos** impede
ativação V9/DT-15. Nenhuma alteração em FS/TF/m/u, V8, Gold,
JornadaLocal, NODE/NAS, HML/PROD ou IBGE original.

Os testes são herméticos; números de política usados em fixture
demonstram apenas validação de lógica, não representam recomendações.

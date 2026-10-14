# DT-V9 — Comparação de passes de blocking sobre 30 mil pessoas

**Prioridade P0 — issue #999.** O corpus sintético de 30.000 pessoas por seed **já existe**; não recriar um corpus paralelo. Consultar `Ensaio_30mil_Suporte_Nominal_V9_20261013.md` e `V9_Veredito_Funcional_30mil_20261013.md` antes de executar.

## Fontes existentes e distinções
- Gerador C# `Jornada.Linkage.SyntheticCorpus`: seed 42, perfil `DEMOGRAPHIC_PRIMARY_V1`, observações com `BasePersonId` e partições TRAIN/VALIDATION/TEST; seeds 43/44 para transferência sem refit.
- `Jornada.Linkage.Evaluation --v9-physical-sql-blocking`: frame físico SQL no `JornadaHarness`, com união de passes ATIVOS + C1, sem CPF e com data, limite explícito de 1 milhão de candidatos. Já há métricas agregadas de `M_eligible`, `M_sql`, `U_sql` e FS. Não confundir essa união com as seis configurações da #1035.
- `v9-batch-memory-configs.py`: executor Python de passes configuráveis sobre JSONL pré-projetado; ainda não está ligado à saída canônica C# de 30 mil. Não afirmar que comparar 4 linhas testa a massa de 30 mil.

## Trabalho necessário para executar a comparação real
1. Exportar **apenas dados sintéticos** do gerador C# para JSONL versionado, com ID da observação, `BasePersonId` (mapeado para `truth_id`), split, gestor, indicador CPF presente/ausente e projeções canônicas de campos de blocking (nome, mãe, data, sobrenomes, fonética). Preservar os hashes do corpus/projetor; não publicar dados reais.
2. Definir o **mesmo frame elegível** do ensaio físico para todas as estratégias e um segundo frame com missingness para testar robustez. Pares M entre gestores com mesmo `BasePersonId`; U são candidatos com IDs-base distintos, **não** pares negativos amostrados por inicial.
3. Executar as seis configurações da #1035 e variações de cap, passes e ordem. Computar recall `M_found/M_eligible`, `U_found`, `U/M_found`, contribuição marginal de cada passe, percentis de fanout, células suprimidas, tempo e memória. Uma configuração com 0 pares M elegíveis deve falhar, não receber recall artificial de 100%.
4. Segregar por pessoa-base entre TRAIN/VALIDATION/TEST, selecionar na VALIDATION e só então rodar TEST. Nunca escolher por TEST. Executar seeds 42/43/44 sem reajustar o modelo FS/TF/LLR.
5. Publicar um relatório comparativo com denominadores e fingerprints; selecionar fronteira de Pareto recall × custo, incluindo estratos de nomes comuns e ausência de campos. Confrontar finalistas com SQL físico e histórico `VERSIONED_ALIAS` antes de qualquer recomendação operacional.

## Gates de correção
- Não contar `person_first` e `person_last` como duas evidências independentes de identidade.
- O frame físico existente exige data presente: resultados dele **não** validam o cenário com nascimento ausente.
- O script Python atual usa projeções de entrada, não projeta automaticamente os nomes pelo C#; equivalência de chaves é pré-requisito, não hipótese.
- Não inferir FDR de vínculo a partir de U/M de candidatos. Não alterar TF, LLR, V8, ruleset ATIVO ou Gold.
- Não publicar uma estratégia vencedora até haver artefatos executados no corpus de 30 mil e verificação dos denominadores.

## Paralelismo e entregas
- **Frente A:** integrar exportador sintético C# → JSONL e oráculo M elegível por split.
- **Frente B:** robustecer executor/fixture e otimizar cálculo de pares verdadeiros (#1033, #1037).
- **Frente C:** configurar passes (#1035), executar lote e ordenar fronteira de Pareto.
- **Frente D:** confrontar finalistas com SQL físico, sem tocar produção.
- **Frente E (posterior):** otimização genérica de CI, subordinada aos experimentos; atividade técnica separada.

**Estado deste documento:** escopo de implementação e critérios; **não** é resultado de execução de 30 mil.

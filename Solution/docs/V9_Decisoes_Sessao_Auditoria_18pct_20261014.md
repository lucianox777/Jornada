# V9 — decisões e auditoria da alegação de 18% (2026-10-14)

## Decisões da sessão
- Normalização é obrigatória nos nomes da pessoa e da mãe. Projeções fonéticas PT-BR são obrigatoriamente **geradas, persistidas e indexáveis**, mas sua utilização nos passes é uma dimensão experimental **ligada/desligada** para medir ganho marginal; desligar o uso fonético em um braço não dispensa gerar as projeções.
- Quatro campos derivados: nome_normalizado, nome_fonetico, nome_mae_normalizado, nome_mae_fonetico. Preservar os originais apenas para proveniência, sem usá-los diretamente nos novos passes.
- Projetar exclusivamente com BlockingProjectionKeyProjector.Project em C#, usando IdentityComparison.NormalizeText e MetaphoneBr.Encode. Exigir paridade SQL.
- Criar índices compatíveis com os passes físicos, medindo planos, fanout e custo.
- **Não existe estratégia F separada.** Comparar oito braços: A sem fonética, A com fonética, B sem fonética, B com fonética, C sem fonética, C com fonética, D sem fonética, D com fonética. Mesmos corpus, seeds, split e denominador.
- Duas representações do mesmo nome não são evidências independentes.
- Congelar corpus, partições, identidades, versões, hashes, projeções, frequências individuais e conjuntas TRAIN, parâmetros e resultados. Snapshots são imutáveis; novos algoritmos geram novas versões.
- Isolar a dimensão fonética: manter iguais os demais parâmetros de cada par de braços; declarar quando um baseline histórico não permitir desligar a fonética sem alterar sua semântica. Não chamar essas variantes de comparáveis antes de comprovar paridade do restante.\n- Selecionar em TRAIN/VALIDATION; TEST lacrado. Medir M/U elegíveis, recall, tempo, memória, FDR e abstenção. Não promover V9 só com sintético.

## Origem da alegação de deterioração de 18%
**Não comprovada.** A busca textual no repositório encontrou 18% como dúvida, não como cálculo:
- Solution/docs/V9_Memory_First_Experiment_And_CI_Plan.md pergunta qual é o denominador e o conjunto de pares.
- Solution/docs/DT_V9_Experimento_Memoria_Lote_CI_20261014.md solicita investigar o denominador.

Evidência diferente: Solution/docs/V9_Suporte_81_Celulas_MU_SQL_TRAIN_20261013.md informa 7.786/18.036 M elegíveis em TEST, aproximadamente 43,17% de recall absoluto, não queda de 18%. Outros relatórios têm cerca de 79–80% em TRAIN/VALIDATION e universo/política diferentes; **não** subtrair números não comparáveis.

## Investigação necessária
1. Localizar os dois relatórios originais supostamente comparados: run IDs, commits, seeds, hashes de corpus/modelo, splits, filtros e política.
2. Registrar numerador e denominador de M em ambos; distinguir 18 pontos percentuais de redução relativa de 18%.
3. Reexecutar baseline e variante no mesmo universo; decompor M perdidos por ausência de dados, regra, índice, fanout e partição.
4. Se faltar evidência, classificar NAO_COMPROVADO. Não publicar 18% como deterioração medida.

PR #1045: planejador; PR #1047: snapshots; PR #1044: testes integrados. Comparação completa dos oito braços A/B/C/D com/sem uso fonético ainda pendente.

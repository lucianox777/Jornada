# GOV-V9-MARGIN-REUSE-20261111 — reutilização de frame SQL/fit/ranking entre margens

**Autoridade:** aprovação expressa do Product Owner, 11/11/2026, no contexto da campanha V9 100k/324 em execução: “Acho melhor para a execução agora e fazer esta alteração. Considere o tempo que já foi gasto na execução atual e qual o ganho teriamos no código com esta alteração”. **Estado:** implementação proposta em PR, governança GOV-DOC-01; resultado físico e benefício ainda NÃO medidos.

## Contexto e precedência

O estudo previamente autorizado [GOV-V9-V3-TF4-324-20261111](Decisao_GOV_V9_V3_TF4_324_100k_20261111.md) possui 81 configurações nominais, M/U V3 mistura U λ em {0,5, 1}, margens {0,5, 2} e três passes de blocking, **324 hipóteses físicas selecionáveis**, α=0,5, threshold=0,95, treino sintético conhecido 100k/seed42, um único lote. A campanha manual [#43069538326](https://github.com/lucianox777/Jornada/actions/runs/43069538326) iniciou em 11/11/2026 às **14h34 BRT**, SHA `2a5fb3189015116c2a7749b8364dd9ba6686572e`; às 15h27 BRT (53 min) ainda executava o fitting C#, sem artefato final, checkpoints reutilizáveis ou medição parcial por grupo. Interrompê-la e reiniciar perderia tempo já executado.

O `V9NestedCalibrationCoordinator` anterior agrupava requests por blocking+margin+TF positivo/política de TF, construindo cerca de oito grupos SQL (TF0 e TF positivo separados; positivos em lotes de 64 opções). No entanto, o `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` e `V9FellegiSunterAdaptiveCalibrationAdapter.EvaluateSourceUnitResolveValidation` **já aceitam `finalConflictMargins`** e calculam ranking congelado uma vez por opção para várias margens. `V9AdaptiveCalibrationCoordinator.EvaluateRulesetAsync` já utiliza este caminho em produção de estudos, com lotes máximos de 64 modelos × 8 margens.

## Decisão técnica implementável

- Reutilizar **somente os dados físicos SQL, treinamento/fitting V3 e scores/ranking congelados** entre as margens declaradas para o **mesmo frame blocking e conjunto TRAIN**. Agrupar pelo mesmo frame/passes, mesma condição TF positiva e mesma política de termo não publicado. Preservar TF0 como grupo separado de TF positivo e nunca misturar política de abstenção/rejeição.
- `V9MarginFrameWorkPartition` cria grupos canônicos de até **64 opções distintas** por até **8 margens**, com assinatura de conjunto de margens por opção; cobre grades esparsas sem gerar hipóteses novas ou criar produto cartesiano não declarado. Recusa duplicatas, opções inválidas e limites indevidos. Mantém todos os requests originais e seus hashes SHA, independentemente do agrupamento.
- `V9NestedCalibrationCoordinator.EvaluateAsync` envia cada opção **uma vez** ao probe com a coleção `finalConflictMargins`. Recupera resultado e snapshot pela **chave composta de OptionId+ConflictMargin**, exigindo `BLIND_FS_COMPLETE_LINK_CANDIDATES`. Rejeita frames incompletos antes de SQL.
- **Não reutilizar nem suprimir P95:** cada hipótese elegível continua realizando seu próprio `EvaluateFrozenHoldoutAsync` físico de ponta a ponta e reconciliando TP/FP/conflitos/unresolved. A margem fica congelada no snapshot de cada hipótese. Em caso de ausência de suporte ou fanout, preservar rejeições individuais, não imputar resultados.
- Preservar **as 324 hipóteses**, `PredeclaredCandidateCpMultiplicity=324`, FDR observado e upper 1%, P95 60s, fanout P99=100, cap4 por identidade, modo dos TFs, λ, bloqueio de TEST/Gold, versões/sha do corpus, IBGE e algoritmo de seleção. **Não** ampliar a grade, não alterar o threshold, não adicionar validação independente nem ativar modelos.
- Não executar campanhas em PR, não cancelar a execução #43069538326 antes de saber se a versão nova é correta e tem benefício real. O código mergeado em commit futuro **não altera um workflow já rodando em SHA fixado**; novo benefício exige nova execução voluntária no SHA correspondente após gates.

## Custo e ganho esperado — hipótese, não medição

Com 324 hipóteses e margens {0,5,2}, o particionamento do lote TF positivo mais os TF0 deve passar de **8 para 4 grupos físicos de fitting/SQL**. Isso pode reduzir reprocessamento de SQL, ajuste M/U e rankings. **Não implica 50% de economia de tempo**: os até 324 replays P95, Resolve por margem, retenção de evidências e agregação continuam. A duração anterior de 24 hipóteses (20m50s) extrapolada linearmente fornece apenas referência (~4h41m), não previsibilidade do custo da grade 324. Aos 53 min transcorridos na campanha atual, cancelamento/reinício só compensaria no modelo linear se a otimização **economizasse acima de 19% do total**, antes de acrescentar os minutos necessários à CI, build e setup. **Não existe benchmark que suporte essa economia nem checkpoint físico reutilizável.**

Pode ocorrer **pico de memória maior** por métricas multi-margin no mesmo probe. O governor físico de RAM/SQL e os tetos originais permanecem; exigir primeira prova controlada e limites de recursos. A integração só é elegível após testes de equivalência, sem exigir cancelamento do run antigo.

## Verificações e critérios de aceitação

1. Teste `V9MarginFrameWorkPartitionTests`: 324 requests completos => quatro grupos, cobertura exata 324/324, TF0 isolado, nenhuma margem inventada; casos adaptativos esparsos e mais de oito margens com fatias limitadas, condições FAIL-CLOSED.
2. `V9FsAdaptiveFirstNameTfTrainValidationTests`: no mesmo fit e mesmo conjunto de pares SQL, resultados conjuntos em margens {0,5,2} devem coincidir com duas avaliações separadas em TP, FP, conflitos, unresolved, recall, FDR, multiplicidade 324 e contribuições por identidade.
3. Gates CI obrigatórios do SHA exato e ensaios físicos / auditor oficial; comparar `CandidatePairsSha256`, `RequestSha256`, snapshots, TP/FP/recall/FDR, P95 real e finalistas. O conjunto científico deve ser idêntico; medidas de latência podem variar entre runners, nunca copiadas.
4. Para alegar redução de tempo, medir a duração de ambas as estratégias **na mesma classe de recursos e plano**, incluindo pico RSS, CPU, disco, tempo SQL e número de grupos. Nenhuma economia declarada como fato antes desse teste.

**Precedência:** adição operacional restrita à decisão GOV-V9-V3-TF4-324; não substitui a campanha anterior nem seus resultados. A escolha de particionamento/checkpoints/restart automático é assunto separado, ainda **NÃO aprovado**; nenhum TEST/Gold ou LOCK é modificado.

**Rastreabilidade:** [issue #1349](https://github.com/lucianox777/Jornada/issues/1349), [run #43069538326](https://github.com/lucianox777/Jornada/actions/runs/43069538326), [índice de decisões](Indice_Decisoes_Vigentes.md), [índice documental](Indice_Acervo_Documental.md).

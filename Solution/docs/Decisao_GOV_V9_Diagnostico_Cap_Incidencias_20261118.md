# GOV-V9-DIAGNOSTICO-CAP-INCIDENCIAS-20261118 — diagnóstico da falha física

**Estado:** instrumentação técnica em revisão, sem aprovação de alteração dos limites científicos. **GOV-DOC-01.**

## Evidência e contexto
A execução manual diagnóstica #44312200910 (commit `8c7e4c480cba1e1a2f744ee1feecddea0cd0dac0`) recuperou e verificou o artefato histórico da VALIDATION #44235715333, compilou, preparou SQL descartável, converteu o vencedor e gerou uma nova coorte de 100 mil identidades. A execução física interrompeu em `V9PhysicalSqlFrozenHoldout.cs:158`: `Holdout excede recursos pré-declarados; não truncar candidatos.`

O teto de `HardPairIncidenceCap` é 1.000.000 **incidências** entre gestores, não necessariamente pares distintos. A contagem considera múltiplas regras e é censurada em teto+1. A evidência atual não demonstra o total exato, nem falha do resolvedor congelado, nem defeito do grafo da PR #1654.

## Intervenção e escopo
Instrumentar somente o ramo de rejeição por excesso de incidências para emitir, nos logs da execução, contagens agregadas censuradas por `PassId`, assinaturas projetadas, número de observações e tetos. Não registrar nomes, assinaturas, identidades, pares, conteúdo do vencedor ou rótulos de verdade. Não alterar o algoritmo de projeção, regras de bloqueio, limites, TEST científico, métricas, seed, SQL ou seleção do vencedor. A exceção original permanece fail-closed e nenhuma materialização SQL de candidatos ocorre após rejeição.

## Limitações e próximos passos
Contagens por regra e contagem global são **censuradas**, não um censo exato. A soma das regras pode duplicar pares. O diagnóstico não autoriza aumentar o teto nem ajustar passes com base no TEST; essas decisões exigem avaliação metodológica independente e aprovação do PO. Uma nova execução manual pontual requer SHA da master atual e autorização explícita. O TEST científico permanece não certificado.

## Rastreabilidade e precedência
Execução: https://github.com/lucianox777/Jornada/actions/runs/44312200910 . PR #1689 integrou o workflow pontual. PR #1654 permanece separada. Esta instrumentação não revoga GOV-V9-TESTE-PONTUAL-WORKFLOW-20261118, não substitui GOV-DOC-01 e não declara a campanha liberada.

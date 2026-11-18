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

## Resultado físico observado — execução #44314407095 (18/11/2026)

A instrumentação da PR #1690 (merge `efe0a4b80c74fad0aae9ac460bbab129de5a9a80`) foi exercitada no workflow manual #44314407095. Recuperação do vencedor, conversão SQL, geração do corpus e validação da seed passaram. A avaliação TEST física abortou corretamente antes de materializar pares. Recibo agregado nos logs:

| Regra | Assinaturas | Incidências entre gestores |
| --- | ---: | ---: |
| P_FIRST_BIRTH_YEAR | 169.191 | 902.410 |
| P_LAST_M_PHON | 162.499 | 103.612 |
| P_PHON_M_LAST | 162.499 | 84.770 |
| **Total das três regras** | **494.189** | **1.090.792** |

Coorte: 100.000 identidades-base, 170.941 observações. Teto declarado no TEST: 1.000.000 incidências; excesso: 90.792 (9,0792%). Nenhuma contagem **por regra** atingiu o teto individual de censura; a contagem global do mecanismo foi censurada em 1.000.001. A soma exata das contagens por regra resulta em 1.090.792 **incidências**, não em pares SQL distintos, que não foram materializados nem contados. P_FIRST_BIRTH_YEAR responde por aproximadamente 82,73% das incidências contadas.

### Divergência de parâmetros que exige decisão explícita

`V9IndependentTestCli` constrói `V9PhysicalAdaptiveExecution` sem informar `MaximumCandidatePairIncidences` e `MaximumProjectedSignatures`, herdando ambos os defaults de 1.000.000 definidos por `HardPairIncidenceCap`. Entretanto, o documento histórico [GOV-V9-100K-DIAGNOSTICO-PRESQL-20261109](Decisao_V9_100k_Diagnostico_Offline_Limites_PreSQL_20261109.md) registra **20.000.000 incidências e 5.000.000 assinaturas** no plano oficial anterior de 24 configurações TRAIN. Esses valores **não são automaticamente o orçamento aprovado do TEST independente**, cuja fonte de autoridade deve ser verificada. O plano TRAIN não deve ser importado implicitamente pelo TEST.

**Sem decisão de alteração:** não elevar o cap para 1.090.792, 20 milhões ou qualquer outro valor com base no TEST. Primeiro estabelecer o orçamento operacional TEST pré-declarado e sua proveniência, independentemente dos outcomes TEST; depois avaliar recursos, critérios de aprovação e eventual nova campanha. Não modificar o vencedor, as três regras, a seed, o protocolo de 20 repetições ou a separação científica. TEST segue não certificado. Referências: [run #44314407095](https://github.com/lucianox777/Jornada/actions/runs/44314407095), PR #1690.

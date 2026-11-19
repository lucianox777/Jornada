# GOV-V9 — Alinhamento de testes TF com política aprovada de termos não publicados (19/11/2026)

**Natureza:** aplicação de decisão já aprovada pelo PO, sem decisão estatística nova. **Status:** proposta de correção em PR, condicionada a CI/revisão. **Política:** GOV-DOC-01.

## Contexto e evidência
Na CI master #44564338404, o job unit-runtime reprovou sete testes: cinco casos de V9TfDirectionDecision19Tests e dois de FrozenIbgeNominalTfV9Tests. Todos esperavam exceção para termo nominal não publicado no IBGE em chamadas de U_ref.ResolveTerm ou FrozenIbgeNominalTfV9.CreatePreview; o código corrente implementa a política de imputação aprovada em 18/11/2026.

## Precedência e justificativa
A seção “Decisão posterior do Product Owner — 18/11/2026” de Decisao_GOV_V9_TF_Suporte_REF_TEST_Diagnostico_20261118.md substituiu a exigência universal de publicação IBGE; as seções subsequentes aprovaram f_inedito = 20/N_contexto, com padrão N_contexto=11.911.337, preservando U_ref TRAIN. A rejeição explícita RejectOperation e a abstenção AbstainCandidate continuam válidas nos respectivos caminhos operacionais: esta alteração não as modifica. A expectativa antiga de exceção nos métodos diretos, sem política explícita, não representa o contrato posterior aprovado.

## Alterações propostas e limites
Ajustar apenas as sete assertivas antigas: cinco verificam U_ref invariável para termo publicado/não publicado e frequência imputada 20/11911337; duas verificam preview de termo não publicado e frequência imputada. Preservar testes de vantagem do nome raro, peso zero neutro, argumentos inválidos, referências congeladas, proveniência e políticas explícitas de rejeição/abstenção em suas suítes. Nenhuma alteração no algoritmo, frequência publicada, peso, parâmetros, treino, gate ou execução científica.

## Validação e rastreabilidade
Branch test/v9-tf-unpublished-approved-contract-20261119; commits 2e21383 e d9989e7; CI original #44564338404. Exigir CI verde e revisão antes de integrar; não declarar sete testes corrigidos antes da execução. A correção operacional SCALE segue isolada na PR #1738.

## Evidência CI da PR #1739 — 19/11/2026
No commit `77f31be99eea4b76fe7fc0afefa7acdec26e59a4`, o workflow `jornada-pr-required-fast` #44569839649 concluiu **success** nos três jobs (`unit-runtime`, `deterministic-build`, `security-guards`). O workflow `V9 full-schema real RunAsync smoke (nonpublishing, no scientific TEST)` #44569839584 também concluiu **success**. Isso valida as assertivas alteradas no escopo da CI rápida e o smoke não publicador; **não substitui** a CI completa da master, não comprova publicação e não autoriza TRAIN científico. PR ainda draft até integração e verificações de governança.

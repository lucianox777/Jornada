# GOV-V9-TESTE-PONTUAL-WORKFLOW-20261118 — autorização de implementação

**Estado:** APROVADA para implementação pelo PO em 18/11/2026 (“Pode continuar”), após discussão expressa de workflow separado e temporário para liberar a próxima campanha. **Execução física:** ainda não comprovada. **Referência:** GOV-DOC-01.

## Contexto e decisão

A campanha #44235715333 falhou no TEST físico após VALIDATION; a correção da projeção congelada foi integrada em #1686 e #1687. O PO revogou a criação de diagnóstico permanente (PR #1688, merge `8edc81d5d7e4df15da3d9cf7eaceef9a321fa94c`). Para evitar uma terceira campanha integral sem evidência, implementar **workflow manual independente e pontual**, sem alterar o workflow científico principal, que recupere o vencedor operacional histórico SHA-pinned da execução #44235715333 e percorra o caminho físico corrigido com 100 mil identidades sintéticas novas e 20 repetições.

## Escopo e controles

- Workflow: `.github/workflows/v9-one-time-physical-release-preflight.yml`, somente `workflow_dispatch` por proprietário, SHA exato da `master`, tentativa única, confirmação de preflight; sem CI recorrente, agendamento ou promoção.
- Fonte: artefato `v9-tf4-validated-operational-model-d9b751212ce4af0502c6827ae783067142a4b8f7-44235715333`, recuperado por `run-id` explícito, nome fixo e hash de conteúdo verificado. Não inferir “último vencedor”.
- SQL efêmero `JornadaHarness`, esquema versionado, conversão operacional byte-exata, carregamento pelo mesmo runtime, resolvedor de projeção congelada, avaliação física completa, sem TRAIN/AUDIT/VALIDATION.
- Coorte nova de 100 mil identidades, seed aleatória **exclusiva do diagnóstico**, sem consumir ou registrar seed científica no ledger global. Não reutilizar coorte TEST anterior. Protocolo físico de 20 repetições inalterado.
- Resultado **DIAGNÓSTICO DE REGRESSÃO NÃO CIENTÍFICO**: não certifica vencedor, não equivale a TEST independente de uma nova campanha, não ativa nem publica modelo. Não usar esse resultado para substituir o TEST científico da próxima execução.
- Artefato de saída agregado, logs, hashes e estado da CI preservados; descarte de SQL e corpus após execução. A autorização de execução e a liberação do workflow científico dependem de evidências verificadas.

## Justificativa, impactos e precedência

Reutilizar o mesmo caminho de execução física reduz divergência de implementação e permite detectar a falha da projeção antes de refazer TRAIN, mas usar vencedor histórico não verifica a geração de vencedores pelo código atual. Portanto, este é um teste **pontual de regressão** e não um mecanismo permanente ou certificação científica. Esta decisão não revoga a separação TRAIN/VALIDATION/TEST, nem a exigência de seed inédita e coorte de 100 mil do TEST oficial. Prevalece sobre qualquer sugestão anterior de implementar um diagnóstico físico permanente; não altera o registro histórico de sua revogação.

## Rastreabilidade

Aprovação do PO: “Pode continuar”, em resposta à proposta explícita de workflow temporário separado com TEST físico completo. PR e execução: a registrar após CI e disparo manual. Não declarar sucesso físico antes da execução observada.

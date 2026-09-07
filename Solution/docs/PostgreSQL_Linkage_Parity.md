# PostgreSQL — Linkage: inventário e primeira fatia

Base: master `73cc7a4075a5075c9b977cef47b010f976dfc83a`. A implementação PostgreSQL é paralela; SQL Server e Fabric mantêm seus papéis canônicos. Este documento não declara homologação institucional ou de produção.

## Inventário de paridade

| Componente | SQL Server existente | PostgreSQL após esta fatia | Próximo gate |
|---|---|---|---|
| Contratos e normalização de identidade | Compartilhados em Jornada.Contracts | Reutilizados, sem alteração | Validação de novas versões de normalização somente mediante decisão aprovada |
| Fellegi–Sunter, prior condicionado, limiar e margem | FellegiSunterScoring e SqlProbabilisticIdentityLinkage | Mesmo cálculo e mesma política de decisão, extraídos para uso comum | Evidência de calibração e avaliação em corpus representativo |
| Modelo e parâmetros m/u | modelo_linkage, parametro_linkage, frequencia_linkage, estatistica_linkage | Catálogo PostgreSQL, leitura versionada e imutabilidade de modelos finalizados | Portar calibrador, evidências de validade e ativação governada |
| Candidate generation | V1 data exata; V2 cinco passes, sem truncamento silencioso | Mesmos passes e limites, com SQL PostgreSQL e índice de nascimento | Avaliar plano, recall e volume realista |
| Runner e universo congelado | ProbabilisticLinkageBatchRunner, linkage_run e linkage_run_item | Não portados | Runner PostgreSQL com coordenação exclusiva, checkpoints e recuperação |
| Resultado e publicação | linkage_resultado, publicação lógica atômica e vínculo corrente | Não portados | Publicação fail-closed, precedência determinística, recomposição Gold/Serving |
| Calibração | Jornada.Linkage.Parameters.Worker | Não portada | Amostragem independente, m/u, evidência, aprovação e versão imutável |
| Correção governada e replay | Fluxos SQL Server existentes | Não portados | Separação/fusão histórica, replay e auditoria sem perder fatos |

## Fronteira desta entrega

O novo scorer é somente leitura e não é registrado no executável do runner. Ele não cria UUID, não grava vínculo, não ativa modelo, não escreve em Gold/Serving e não autoriza uma decisão probabilística a substituir CPF determinístico. O modelo é fixado por UUID, e versões em rascunho são recusadas. O normalizador aceito é `IDENTITY_NORMALIZATION_V1`; não se reinterpretam modelos de outra versão com regras novas.

O catálogo é aditivo e mantém a precisão numérica e os metadados de calibração do modelo SQL Server. O índice parcial permite somente um modelo ATIVO. Parâmetros e metadados de modelos validados/publicados são imutáveis; uma versão nova exige novo modelo. A futura ativação deverá validar o conjunto de parâmetros e a evidência de avaliação antes de transicionar o estado, sem editar modelos anteriores.

O scorer preserva os cinco passes V2, o modo V1 de data exata, o limite explícito de candidatos, o desempate por UUID e os motivos de decisão. Nenhum Soundex, IA generativa, evidência nova ou threshold foi introduzido. O cálculo compartilhado continua usando o prior condicionado ao tamanho real do bloco e os parâmetros m/u já calibrados. A ausência de candidato e a insuficiência de margem não constituem identidade.

## Próxima entrega

Portar o calibrador e o ciclo de validação/ativação de modelos; depois o runner com coordenação PostgreSQL, universo congelado e publicação logicamente atômica. Somente após testes de reprocessamento, perda de lease, concorrência, precedência determinística e recomposição será possível habilitar o fluxo operacional. Escala e homologação devem ter evidências próprias, não apenas smoke tests.

# Decisões contingentes e alocação C# × T-SQL

Este documento separa **princípios/invariantes** de escolhas que podem mudar sem violar a arquitetura.

## Escolhas correntes que não são princípios

| Escolha | Estado corrente | Pode mudar quando |
|---|---|---|
| Complete-link conservador em composição/transitividade | Escolha de segurança corrente | experimento e contrato versionado demonstrarem alternativa superior sem fusão transitiva indevida. |
| Processamento em lote, sem CDC como requisito | Adequado à Fase 1 | fonte/volumetria/SLA real justificar CDC e houver ownership operacional. |
| Organização Medallion (Bronze/Silver/Gold) | Estrutura corrente | mudança preservar proveniência, reprocessamento e separação fato×identidade. |
| Separação lógica no mesmo SQL Server | Contrato corrente | evidência operacional/governança exigir separação física, sem duplicar fonte de verdade. |
| SQL Server 2022 como baseline operacional | Contrato da candidata | decisão arquitetural formal de nova linha alterar o runtime; compatibilidade Fabric isolada não muda isso. |

Essas escolhas não devem ser defendidas como leis universais do produto.

## Regra-mãe de alocação

**C# decide/orquestra quando a regra precisa de composição de domínio, algoritmos, parsing, integração externa ou teste unitário independente. T-SQL protege quando a propriedade precisa permanecer verdadeira sob qualquer writer concorrente dentro da mesma transação/banco.**

Uma regra não deve existir em C# e T-SQL com duas implementações semânticas independentes. Quando há defesa em profundidade, uma camada calcula e a outra valida um contrato simples.

## Preferir C#

- parsing de JSON/ZIP e contratos;
- normalização/comparadores reutilizados pelo Linkage;
- Fellegi–Sunter, ranking e políticas versionadas;
- seleção/otimização de rulesets;
- chamadas HTTP/IBGE/storage;
- orquestração de workflows e workers;
- construção de decisões explícitas;
- validação rica que não precisa proteger writers externos ao processo.

## Preferir T-SQL

- unicidade, FK, CHECK e append-only;
- locks/serialização e idempotência de escrita;
- reserva atômica de UUID/âncora;
- transições de estado que precisam ser impossíveis mesmo com outro writer;
- ledger na mesma transação da mutação;
- recomposição set-based próxima aos dados quando a semântica já foi decidida;
- guards de promoção que precisam falhar fechado no banco.

## Inventário de regras críticas em procedures

| Objeto | Papel |
|---|---|
| `identidade.sp_assegurar_origem_progressiva` | assegura continuidade de origem sob lock/transação; não decide similaridade. |
| `identidade.sp_obter_cpf_ancora` / rotinas de âncora | reserva/recupera autoridade CPF permanente. |
| `identidade.sp_publicar_referencia_progressiva_deterministica` | publica referência determinística sob contrato explícito. |
| `identidade.sp_publicar_resolucao_progressiva_linkage` | aplica decisão produzida pelo Linkage usando resultado persistido, sem receber score arbitrário do chamador. |
| `identidade.sp_recompor_gold_pessoa` | recompõe projeção Gold após mudança governada de atribuição. |
| `identidade.sp_reservar_uuid_composicao` | reserva UUID de composição de modo idempotente. |
| `auditoria.sp_registrar_decisao_identidade` | registra ato/evidência na mesma transação da mutação. |
| `qualidade.sp_registrar_conflitos_linkage_publicados` | materializa divergências oriundas de conflitos publicados. |
| `qualidade.sp_registrar_desfecho_divergencia` | encerra fila e registra desfecho; #401 complementará identidade causal para não renascer caso idêntico. |
| `auditoria.sp_registrar_conferencia_linkage` | persiste evidência agregada de conferência sem declarar validação estatística. |

## Inventário de guards críticos em triggers

| Objeto | Papel |
|---|---|
| `identidade.tr_progressiva_origem_guard` | protege invariantes de origem progressiva contra update/delete inválido. |
| `identidade.tr_cpf_ancora_imutavel` | impede mutação/remoção da âncora CPF. |
| `identidade.tr_vinculo_fonte_progressiva` | assegura consistência progressiva no ponto de publicação do vínculo. |
| `gold.tr_pessoa_nome_publicacao` | mantém projeção de chaves nominais a partir da evidência canônica; não implementa segundo normalizador. |
| `identidade.tr_modelo_linkage_promotion_contract` | valida contrato mínimo de modelo na promoção. |
| `identidade.tr_modelo_linkage_llr_monotonicity` | protege monotonicidade/tolerância governada de LLR. |
| `identidade.tr_linkage_run_congela_frequencia_nome` | congela proveniência nominal do run. |
| `identidade.tr_linkage_resultado_publicacao_imutavel` | impede reescrita indevida do resultado publicado. |
| `auditoria.tr_decisao_identidade_evento_append_only` | torna ledger de atos append-only. |
| `auditoria.tr_modelo_linkage_estado_evento_append_only` | torna ledger de promoção append-only. |
| `auditoria.tr_linkage_conferencia_evidencia_append_only` | torna evidência de conferência append-only. |
| `identidade.tr_composicao_*_append_only` | protege reservas/aplicações/publicações de composição contra reescrita histórica. |

## Regra de revisão

Ao introduzir procedure/trigger com regra nova, registrar aqui somente quando ela carregar **semântica de domínio ou guard arquitetural**, não para todo objeto SQL mecânico. A revisão deve responder: “por que isto precisa estar no banco?” e “qual camada calcula a decisão?”. Se as respostas forem a mesma implementação duplicada, refatorar.

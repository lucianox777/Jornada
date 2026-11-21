# Decisão GOV-V9-RESPONSABILIDADES-CALIBRADOR-CONTRATO-RUNNER-20261121

**Data:** 21/11/2026. **Autoridade:** Product Owner. **Status:** aprovada; integração documental em PR pendente.

## Contexto e justificativa
A auditoria das issues #1089, #1488, #1584, #1575 e #1478 identificou sobreposição de critérios históricos, embora o núcleo Rank→Resolve já tenha sido compartilhado (#1147), a fronteira por artefato imutável tenha sido implementada (#1655) e a execução científica isolada tenha avançado (#1627, #1820 e #1822). Manter critérios duplicados obscurece o progresso e pode exigir implementação redundante ou acoplamento indevido.

## Decisão vinculante: responsabilidade única
1. **#1089 — Calibrador C# único:** engenharia reversa, catálogo, TRAIN/VALIDATION, seleção, exportação imutável, REF e bootstrap sem recalibração automática. Não é responsável pela implementação produtiva do Runner.
2. **#1488 — contrato e configuração administrativa:** arquivo operacional completo, versionado e íntegro, compatibilidade sem perdas e fail-closed; operador MASTER seleciona manualmente ou por interface o artefato a configurar no Runner. Não há sincronização, importação ou ativação automática. Evoluções do Calibrador e Runner podem ser assíncronas; o Runner somente aceita versões suportadas.
3. **#1584 — execução do contrato no Runner:** loader, blocking, TF4/81, Rank→Resolve, agrupamento, regressão legada e provas E2E/SQL; não repetir núcleo ou fronteira já integrados. Certificação/ativação produtiva permanecem sujeitas à governança MASTER/DT-15.
4. **#1478 — TEST científico:** autorização, LOCK, corpus independente, uso único, segurança, métricas, gates e closeout. TEST isolado não executa no ambiente produtivo.
5. **#1575 — histórico a reconciliar:** critérios que exigiam execução do TEST científico pela pipeline produtiva não prevalecem quando conflitantes com a decisão de isolamento de 17/11. Preservar rastreabilidade até conferir cobertura e encerrar eventual duplicação, sem dispensar gates válidos.

## Entregue versus pendente
**Entregue/integrado:** #1147 (núcleo Rank→Resolve compartilhado, sem certificar V9 produtivo), #1655 (fronteira imutável, Runner produtivo V9 ainda bloqueado), #1627 (separação TEST/executor), #1820/#1822 (TEST científico isolado com fixtures). **Não demonstrado integralmente:** fluxo administrativo de seleção/importação/ativação do artefato pelo MASTER e execução produtiva E2E do Runner V9, evidência física final e certificação científica. O estado de cada gate deve ser confirmado no SHA pertinente; PR merged não equivale a certificação.

## Precedência, impactos e rastreabilidade
Esta decisão complementa #1089 e a política GOV-DOC-01; esclarece a separação de responsabilidade sem revogar os gates de segurança/estatística. A decisão `Decisao_GOV_V9_TEST_Isolamento_Retrocompat_Liberacao_20261117.md` prevalece sobre critérios antigos que exigiam TEST no ambiente produtivo. Nenhuma campanha científica, alteração operacional ou DT-15 é autorizada por esta reconciliação.

Corpos das issues #1089, #1488, #1584, #1575 e #1478 atualizados em 21/11/2026 com delimitação de responsabilidade, entregas existentes, exclusões e referências cruzadas. A #999 foi encerrada como superada, preservando requisitos residuais rastreáveis nas issues pertinentes.

# GOV-V9-RESSALVAS-SANEAMENTO-20261117 — limites aprovados pelo PO

**Autoridade:** Product Owner, 17/11/2026. **Status:** diretriz aprovada para análise, sem autorização de implementação técnica. **Política:** GOV-DOC-01.

## Contexto
O saneamento de PRs identificou propostas antigas de ETA, auditoria TF0, paridade TEST e gerador sintético. O PO esclareceu que a instrumentalização do calibrador já foi simplificada por excesso de sobrecarga, com redução observada. As PRs não devem reintroduzir custo nem alterar o protocolo sem justificativa.

## Decisões, justificativas e impactos
1. **#1458 — ETA:** é observabilidade/apresentação do andamento, não parte da matemática ou da seleção da calibração. Preservar a simplificação operacional. Não integrar novos cronômetros, callbacks ou I/O no caminho crítico sem demonstrar necessidade, custo mensurado e compatibilidade com o fluxo atual. Manter PR em avaliação.
2. **#1429 — TF0:** é braço de controle sem abstenção na auditoria pareada TF0 versus TF_ABSTAIN. A PR acrescenta checagem `abstained == 0` e `abstainedPositive == 0` no script de auditoria e um teste de regressão; **não altera o calibrador oficial**. O PO não ratificou essa regra como nova decisão científica; explicar pertinência ao protocolo atual antes de qualquer integração.
3. **#1576 — paridade TEST:** avaliar seletivamente código, testes e contratos úteis ao Seletor/Runner. Não integrar em bloco: há desenho antigo `prepare-test-100k` e pré-requisitos superados pela arquitetura de TEST no próprio job após VALIDATION (#1634). Replay de versões já existe, mas não equivale a certificação de paridade física e numérica. Nenhuma autorização de TEST one-use.
4. **#1567 — gerador sintético:** preservar o trabalho relevante, incluindo degradação familiar, evidências, handoff e testes. A branch é DRAFT e deve ser reconciliada com a master e o protocolo atual; não descartar nem mesclar automaticamente, não executar TEST.

## Precedência, rastreabilidade e próximos gates
Esta diretriz complementa [GOV-V9-SANEAMENTO-BACKLOG-20261117](Decisao_GOV_V9_Saneamento_Backlog_20261117.md) e prevalece sobre sugestões de merge automático das PRs #1458/#1429/#1576/#1567. A arquitetura TEST da #1634 prevalece sobre contratos antigos. Nenhuma alteração de código científico, parâmetros, workflow ou seed é aprovada aqui. Próximo passo: revisão seletiva e evidência de custo/compatibilidade antes de propor PR técnica; manter CI obrigatória.

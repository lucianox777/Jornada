# GOV-OPS-SUSPENSAO-AUTOMACOES-JORNADA-20261122 — desativação de rotinas, vigílias e agendamentos anteriores

**ID:** GOV-OPS-SUSPENSAO-AUTOMACOES-JORNADA-20261122  
**Data:** 22/11/2026 (horário de Brasília).  
**Autoridade:** Product Owner — determinação expressa: "Desativar todas as rotinas, vigílias e agendamentos anteriores sobre a Jornada".  
**Estado:** **DECIDIDO** pelo PO; **IMPLEMENTADO** no serviço de automações do ChatGPT para as tarefas ativas expostas na consulta; integração documental na `master` depende dos gates da PR.  
**Política:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Contexto e justificativa

O projeto acumulou acompanhamentos recorrentes e vigílias de CI, PRs e campanhas V9, criados em diferentes conversas. O PO determinou explicitamente a **desativação de todas as rotinas, vigílias e agendamentos anteriores referentes à Jornada**, sem condicionar a desativação ao desfecho das campanhas ou dos trabalhos monitorados. O objetivo operacional imediato é encerrar a execução agendada dessas tarefas; a determinação não pede descarte de histórico ou de evidências.

## Decisão e âmbito

1. **Pausar**, sem apagar o histórico, todas as automações anteriores relacionadas à Jornada no serviço ChatGPT Automations, independentemente da conversa em que tenham sido configuradas.
2. Não manter vigília, acompanhamento horário, execução autônoma periódica, alerta condicional, resumo recorrente ou outra rotina anterior relacionada à Jornada por força de mandatos mais antigos. Qualquer reativação ou novo agendamento precisa de uma determinação posterior do PO.
3. Preservar o trabalho **sob demanda**, em interações atuais e futuras, e todos os critérios de segurança, governança, documentação GOV-DOC-01 e gates de integração.
4. **Delimitação técnica:** a desativação confirmada nesta operação abrange as automações do ChatGPT. Ela **não** cancela execuções de GitHub Actions já iniciadas, não desabilita workflows/cron do próprio repositório, nem altera CI, rulesets, runners, serviços, bases de dados ou campanhas científicas. Não inferir desativação de agendamentos em plataformas externas sem verificação e ato específicos.

## Evidência operacional da implementação

Em 22/11/2026, a consulta às automações do ChatGPT apresentou **25 registros na listagem consultada**, dos quais **1 estava habilitado**, intitulado `V9 — CI e correções`, identificador `Automation_9dcd6fe9ca508191a5b4365a223300bd`. Foi executada atualização `is_enabled=false`, com resposta de sucesso e retorno do registro como desabilitado. Nova consulta retornou **nenhuma automação habilitada na listagem**; contadores globais informaram **143 pausadas** e **2 concluídas**. Os contadores globais não identificam individualmente a que projetos pertencem as tarefas e não são usados para afirmar cobertura nominal das demais pausadas.

**Limite da evidência:** o serviço confirmou a pausa do único item ativo encontrado na consulta e ausência de ativos nessa listagem. A validação não comprova cancelamento de agendadores externos, nem altera a situação de workflows GitHub.

## Impacto, precedência e rastreabilidade

- **Precedência:** esta determinação do PO, posterior às ordens de vigília e mandatos periódicos anteriores da Jornada, **prevalece especificamente sobre a execução agendada das automações ChatGPT**. Mandatos documentados de continuidade de desenvolvimento passam a ser executados apenas quando demandados; permanecem históricos seus checkpoints e autorizações específicas que não dependam de agendamento.
- **Sem mudança científica ou funcional:** não altera parâmetros V9, TRAIN/VALIDATION/TEST, referências LOCKED, políticas de CI, regras de merge ou a obrigação de documentar decisões.
- **Rastreabilidade primária:** determinação expressa do PO em conversa do projeto, em 22/11/2026; inventário e atualizações confirmadas pelo serviço ChatGPT Automations, com identificador acima. Este documento e o [Índice de Decisões Vigentes](Indice_Decisoes_Vigentes.md) são o registro versionado exigido por GOV-DOC-01.
- **Pendência documental:** conferir checks obrigatórios e integrar a PR por fluxo protegido; até o merge, não declarar a decisão documentalmente incorporada à `master`.

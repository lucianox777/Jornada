# Ledger canônico de decisões de identidade

**Estado:** implementado na candidata v5.00 / SolutionSchema 3.70.

## Finalidade

`controle.api_evento` continua sendo telemetria e auditoria do pipeline HTTP. Ele não é a fonte de verdade de autoria de uma decisão governada de identidade porque uma correlação de requisição não identifica o decisor institucional e o header opcional de CPF do agente é declarativo.

A fonte canônica para esses atos é `auditoria.decisao_identidade_evento`.

## Autoria

Cada evento preserva:

- `operacao_id` UUID gerado pelo SQL Server;
- `credencial_id` autenticada e existente em `controle.credencial_api`;
- snapshot do tipo/código público da credencial e do Gestor responsável;
- referência explícita à correção, caso governado ou divergência que produziu o ato;
- ato/justificativa derivados do próprio registro governado, quando aplicável;
- `correlation_id` somente como contexto observacional;
- instante atribuído pelo banco.

Atos de identidade desta superfície exigem credencial `GESTOR`. O CPF opcional declarado em `X-Jornada-Agente-CPF` não é usado como autoria do ledger.

## Atomicidade

A API abre a transação de cada mutação governada, executa a procedure de domínio e chama `auditoria.sp_registrar_decisao_identidade` antes do commit.

A procedure do ledger exige uma transação ativa e valida que a credencial autenticada pertence ao mesmo Gestor do objeto alterado. Falha de persistência do evento invalida a transação inteira: não existe decisão aplicada sem o respectivo evento canônico.

O contrato é exercitado por `IdentityDecisionLedgerTests`, incluindo falha injetada no INSERT do ledger e prova de rollback do caso/vínculo.

## Imutabilidade e cardinalidade

`auditoria.tr_decisao_identidade_evento_append_only` rejeita UPDATE e DELETE. Índices filtrados impedem duplicação de:

- correção CPF aplicada;
- abertura/aplicação do mesmo caso por tipo de evento;
- desfecho da mesma divergência.

O ledger não duplica score, candidatos ou evidência probabilística. Esses dados continuam em `identidade.linkage_resultado`/runs/modelos. Da mesma forma, detalhes do agrupamento permanecem nas tabelas de correção/caso às quais o evento referencia.

## Eventos cobertos

- `CORRECAO_CPF_APLICADA`;
- `CASO_CONFLITO_ABERTO`;
- `CASO_CONFLITO_APLICADO`;
- `DIVERGENCIA_DESFECHO`.

Decisões automáticas de CPF/Linkage continuam auditadas por suas estruturas próprias (`identity_map_estado_evento`, `linkage_resultado`, composição e publicação). Este ledger fecha especificamente a autoria institucional dos atos governados.

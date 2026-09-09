# Composição reversível — fronteira governada de pré-aplicação V1

Estado: desenho operacional da fatia posterior ao ledger de preparação. Complementa `ADR_Identidade_Composicao_Reversivel.md`, `Identidade_Composicao_Ledger.md` e a issue #36. A issue #31 continua independente e bloqueia ativação probabilística real.

## Objetivo

A existência de um plano `PREPARADA` não autoriza sua aplicação. Antes de qualquer alteração de identidade, a Jornada deve reconstruir uma leitura autoritativa dentro da mesma transação que, futuramente, publicará os efeitos. Essa fronteira deve provar novamente as condições que o planejador puro não consegue provar sozinho: estado corrente, versões, fechamento do componente, autoridade CPF, propriedade das reservas e identidade exata do recibo preparado.

Esta fatia não introduz endpoint, worker, agendamento, ativação automática nem aplicação em produção. Ela define e implementará um adapter interno fail-closed, exercitado apenas por testes/harnesses em bancos descartáveis até que a unidade de publicação completa esteja provada.

## Entrada governada

A operação recebe somente identificadores e conteúdo já persistido ou passível de conferência:

- `decision_id` estável;
- referência opaca do solicitante e correlação;
- decisão canônica e plano preparado esperados;
- conjunto de `initial_uuid` declarado pela decisão;
- reservas pertencentes à decisão.

A lista enviada pelo chamador nunca é tratada como prova de completude. CPF em claro, nome, telefone, e-mail ou outros atributos pessoais não fazem parte do contrato do adapter.

## Leitura autoritativa obrigatória

Sob transação e locks determinísticos, o adapter deve:

1. carregar o recibo `identidade.composicao_plano` e exigir estado `PREPARADA`;
2. conferir os hashes do request, plano e reservas contra o conteúdo canônico recebido;
3. carregar as reservas em `identidade.composicao_uuid_reserva` e exigir igualdade exata do conjunto e da propriedade por `decision_id`;
4. localizar cada `initial_uuid` em `identidade.pessoa_origem_progressiva`, exigir unicidade e carregar `canonical_uuid`, `estado` e `versao` correntes;
5. expandir todas as referências correntes e destinos existentes envolvidos até obter o componente fechado; nenhum membro de um agregado afetado pode ficar de fora;
6. carregar a autoridade `identidade.cpf_ancora` apenas como relação UUID de âncora já admitida. O adapter não altera, transfere ou recria essa âncora;
7. carregar histórico de composição efetivado quando esse armazenamento existir; histórico proposto no plano não substitui histórico aplicado;
8. reconstruir `IdentityCompositionReadSet` e executar novamente `IdentityCompositionPlanner.Prepare` com a decisão original;
9. exigir igualdade semântica e canônica entre o plano refeito e o plano `PREPARADA` persistido.

Qualquer ausência, duplicidade, divergência, versão obsoleta ou expansão que não possa ser provada falha fechada. Timeout, truncamento, erro de consulta ou lock não equivalem a conjunto vazio.

## Ordem de locks

Para reduzir deadlocks sem enfraquecer consistência, a ordem deve ser determinística:

1. `decision_id`;
2. UUIDs de reserva em ordem binária/lexicográfica estável;
3. `initial_uuid` em ordem estável;
4. referências canônicas afetadas em ordem estável;
5. âncoras CPF por UUID, sem ordenar ou registrar CPF em telemetria.

SQL Server e PostgreSQL podem usar mecanismos diferentes (`sp_getapplock`/row locks e advisory/row locks), mas o contrato observável deve ser equivalente.

## Separação entre identidade, atribuição e fatos

O adapter não pode inferir que uma referência progressiva altera automaticamente todos os `vinculo_fonte`. Uma composição futura deve reaproveitar a correção governada existente e preservar proveniência por observação. Da mesma forma, Gold/Serving não pode ser atualizado antes de a nova atribuição factual e a referência progressiva pertencerem à mesma unidade de consistência ou a uma publicação versionada explicitamente bloqueada para leitores.

CPF→UUID permanente é autoridade distinta da atribuição factual. Uma observação pode estar incorretamente associada e ser corrigida sem transferir a âncora CPF. Duas âncoras admitidas diferentes tornam uma fusão inadmissível; o mecanismo não escolhe uma delas.

## Replay e obsolescência

Replay do mesmo `decision_id` com o mesmo conteúdo deve ser distinguido de uma nova tentativa de aplicação. Enquanto só existe `PREPARADA`, o adapter pode retornar a validação do recibo existente, mas não deve avançar versões. Quando existir recibo `APLICADA`, o replay idêntico deverá retornar esse recibo sem reexecutar writers. Reutilização do ID com conteúdo diferente sempre falha.

Uma decisão preparada pode se tornar obsoleta por qualquer alteração de versão, referência corrente, âncora, reserva ou fechamento do componente. Nesse caso a aplicação futura é recusada; não se "corrige" silenciosamente o plano persistido.

## Fronteira da próxima publicação

Somente após a pré-aplicação ser provada em SQL Server e PostgreSQL será adicionada a unidade de publicação que, numa única transação ou protocolo versionado equivalente:

- registra recibo de aplicação append-only;
- grava histórico efetivado;
- avança eventos/versões progressivas;
- reutiliza a correção governada de `vinculo_fonte` quando houver mudança factual;
- invalida/recompõe Gold/Serving de forma consistente;
- preserva aliases e resolução histórica sem sucessor arbitrário após separação.

Nenhuma dessas ações é autorizada por este documento isoladamente.

## Evidência mínima desta fatia

Antes de sair de draft, a implementação de pré-aplicação deve provar em bancos descartáveis, nos dois providers:

- plano preparado válido é reconstruído de leitura autoritativa;
- versão alterada após preparação é recusada;
- membro omitido de agregado afetado é detectado;
- reserva ausente, extra, trocada ou de outra decisão é recusada;
- duas âncoras CPF distintas são recusadas sem modificar a âncora;
- replay idêntico não escreve nem avança versão;
- rollback não deixa locks lógicos, recibos novos ou alterações de Pessoa;
- falha/timeout/truncamento não vira ausência de candidatos ou componente vazio;
- nenhuma tabela Gold/Serving, `vinculo_fonte`, `cpf_ancora` ou estado progressivo é modificada pela pré-aplicação.

CI verde demonstra somente a correção técnica dessa fronteira. Não constitui homologação estatística, autorização institucional ou ativação operacional.

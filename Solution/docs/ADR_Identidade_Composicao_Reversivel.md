# Jornada — composição reversível de identidades

Estado: contrato de planejamento V1 em implementação; persistência e execução ainda não habilitadas. Complementa a ADR_Identidade_Progressiva.md e a issue #36. A homologação estatística da issue #31 é independente.

## Fronteira

O decisor determina a atribuição admissível. O mecanismo de composição aplica uma decisão explícita e versionada. Ele não calcula similaridade, procura candidatos, escolhe automaticamente um sobrevivente, corrige CPF, nem decide a validade dos fatos. Uma decisão governada pode usar o mecanismo sem depender de Linkage probabilístico; isso não exige uma fila de operadores para o fluxo normal.

A primeira fatia implementa somente `IdentityCompositionPlanner`, sem registro no host, API, worker, migração, job ou chamada ao Processor. O planejador recebe uma leitura de componente fechado e produz alterações propostas, recibo identificável e snapshots históricos. Não existe ainda um executor persistente, nem autorização para executar fusões em dados reais. O nome de um método, um booleano de completude ou um hash não comprova autorização, qualidade da evidência ou fechamento do universo.

## Identidades e sobrevivência

A unidade estável de proveniência é a identidade de origem, identificada por `initial_uuid`. O UUID inicial nunca é alterado, transferido, reciclado ou derivado de PII. `canonical_uuid` é a referência corrente e pode mudar apenas por decisão de composição explícita. `PROVISORIA`, `REFERENCIA` e `INDEFINIDA` continuam os únicos estados públicos progressivos; `RESOLVIDO` continua sendo um estado de atribuição de observação.

O destino é declarado pela política e pela decisão, não escolhido pelo mecanismo por ordem, UUID mínimo, tamanho do agregado, score ou conveniência. Numa fusão com âncora CPF admitida, o destino deve ser o UUID dessa âncora. Duas âncoras distintas não podem ser fundidas automaticamente. Sem âncora, a política pode selecionar um UUID corrente apropriado ou uma nova referência previamente reservada, desde que preserve a proveniência. Uma separação pode devolver uma origem ao próprio UUID inicial, usar um destino existente admissível, usar uma reserva nova ou deixá-la sem referência canônica quando a atribuição for indefinida.

A âncora CPF→UUID permanece permanente, inclusive sem fatos ou mapas ativos. Corrigir uma observação associada incorretamente não transfere a âncora: corrige a atribuição e sua proveniência. O armazenamento deve consultar a autoridade CPF e verificar a cardinalidade de, no máximo, um CPF corrente por UUID de âncora. `CpfAnchorUuid` no planejador representa uma vinculação já admitida e verificada, não substitui essa consulta nem autoriza alterar o cadastro CPF. Uma contradição não é resolvida escolhendo um destino.

## Leitura fechada, versões e idempotência

A futura operação persistente deve receber um `decision_id` estável, tipo, evidência opaca, versão de política, instante UTC e partição completa dos membros envolvidos. Cada atribuição informa a versão esperada da origem. A camada de persistência deve verificar a autorização e a origem da decisão, carregar o estado autoritativo, reservar os UUIDs novos, travar as referências e seus membros em ordem determinística e provar que o conjunto afetado está fechado, inclusive os agregados de destino. A lista enviada pelo chamador não é prova de completude.

A aplicação deve verificar versões sob os locks, rejeitar conflitos e decisões obsoletas e registrar o hash canônico do conteúdo com unicidade de `decision_id`. Repetir a mesma decisão retorna o recibo persistido sem novas versões ou escritas; reutilizar o ID com conteúdo diferente falha. O hash é identificação de conteúdo, não assinatura, autenticação ou prova de verdade. A primeira fatia calcula esse hash, mas a garantia de replay concorrente depende do ledger transacional futuro.

Nenhum destino novo pode ser aceito apenas porque um UUID não aparece no conjunto carregado. A reserva deve ser comprovada no registro persistente de identidades e no histórico global de referências, sob transação, para impedir reutilização. O planejador também rejeita destinos que não são correntes, iniciais próprios ou reservas declaradas, e rejeita apropriação do UUID inicial de outra origem.

## Histórico e resolução de referências antigas

Uma referência histórica é identificada pelo UUID e pelo identificador da composição que capturou seu estado. O registro preserva o conjunto de `initial_uuid` dos membros daquele agregado, não apenas um ponteiro para outro UUID. A resolução histórica consulta a atribuição corrente de cada membro desse conjunto. Um único destino e nenhum membro indefinido permitem resposta unívoca. Mais de um destino produz `AMBIGUA`, sem UUID único; membro sem referência suficiente produz `INDEFINIDA`, salvo quando já existem múltiplos destinos, caso em que a ambiguidade permanece explícita.

Isso distingue a pergunta "qual é hoje a referência desta origem?" da pergunta "o que aconteceu com este agregado histórico?". A primeira usa a identidade de origem. A segunda usa o snapshot histórico. Uma fusão posterior não apaga a antiga ambiguidade nem autoriza redirecionar todos os fatos de um agregado dividido a um sucessor arbitrário. Histórico e referências antigas nunca são excluídos para simplificar uma recomposição.

## Escrita e recomposição futuras

A implementação persistente deverá reutilizar os procedimentos e invariantes existentes de correção governada, `vinculo_fonte` e recomposição Gold, sem criar um writer paralelo que ignore lease/fencing, autorização, CPF ou proveniência. O plano é entrada para validação transacional, nunca uma ordem SQL confiável por si só. Cada alteração deve preservar a origem e a versão da observação, seus fatos e seus vínculos anteriores. Atribuição factual é distinta de referência progressiva; um fato válido não desaparece por conflito, separação ou identidade indefinida.

A mesma unidade de publicação deve registrar a decisão, membros antes/depois, eventos progressivos, versões, vínculos afetados, histórico de composição e invalidar/recompor Gold/Serving. Se a recomposição for assíncrona, uma versão de publicação ou barreira equivalente deve impedir que leitores observem uma mistura de versões como se fosse um estado consistente. Não publicar sucesso antes de concluir a unidade de consistência. Falhas devem deixar rollback integral ou pendência recuperável sem referência parcial visível. A contagem municipal deve usar referência canônica admissível e distinta, não somar UUIDs iniciais de um agregado composto. Dados de origem e fatos continuam preservados.

## Evidência necessária antes de habilitar

A próxima fatia deverá implementar ledger append-only e guardas SQL Server/PostgreSQL com instalação repetida, atualização de base povoada, concorrência, replay e rollback. Depois, integrar uma operação governada com as rotinas existentes e provar fusão, separação, reagrupamento, retorno explícito ao UUID inicial, conflitos de âncora, referências históricas ambíguas, conservação de fatos e recomposição Gold/Serving. Testes devem usar bancos descartáveis e decisões sintéticas. Só após essas provas, revisão e aprovação operacional poderá ser habilitada a execução governada no ambiente apropriado. A ativação probabilística permanece bloqueada pela issue #31.

A primeira fatia não altera contratos HTTP existentes nem acrescenta autorização para consultar dados de outros Gestores. A consulta histórica futura deverá aplicar os mesmos controles de finalidade, propriedade e minimização, e não expor automaticamente o conjunto de membros de uma referência a qualquer consumidor.

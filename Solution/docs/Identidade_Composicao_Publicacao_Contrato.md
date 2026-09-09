# Contrato de publicação e continuidade histórica da identidade

Estado: proposta normativa para a issue #36. Esta fatia não depende do Processor, do Gerador de Parâmetros ou da homologação estatística da issue #31. Não ativa writers, endpoints ou publicação operacional.

## Decisões independentes

A identidade progressiva estrutural, a atribuição factual e as projeções Gold/Serving são estados distintos. A existência de APLICADA não implica que os derivados estejam publicados. Uma decisão de composição pode ter origem determinística ou governada; o mecanismo de publicação não interpreta escores nem escolhe identidades.

O UUID inicial nunca é reciclado. A associação CPF→UUID permanente permanece estável, salvo procedimento excepcional de correção auditada. Uma fusão não transfere âncoras CPF nem autoriza agregar duas âncoras distintas. A separação não escolhe um sucessor arbitrário.

## Continuidade histórica

A resolução recebe referência, versão temporal opcional e política de consulta. Deve distinguir referência corrente, referência histórica unívoca, ambígua e indefinida. O histórico aplicado é factual; um plano PREPARADA não é histórico efetivado. Se uma referência antiga possui múltiplos descendentes correntes, a consulta retorna AMBIGUA com destinos autorizados e não redireciona silenciosamente. A ausência de prova não é tratada como identidade inexistente.

Consultas históricas devem preservar o estado observado na versão solicitada. Consultas correntes devem indicar explicitamente a versão da identidade e da projeção. A implementação deve reutilizar o planejador e os snapshots aplicados existentes, sem criar um segundo algoritmo de composição.

## Publicação consistente

Antes de expor uma nova composição em Gold/Serving, a implementação deve provar o conjunto de fatos afetados e suas atribuições. Uma mudança estrutural não autoriza alterar vinculo_fonte ou identity_map por inferência. Correções factuais devem utilizar o mecanismo governado existente e preservar proveniência, observação original e histórico.

A recomposição deve ser idempotente por decision_id e versão de publicação. Deve conservar fatos, evitar duplicação por fusão, retirar atribuições obsoletas das projeções e reconstruir somente o escopo afetado quando esse escopo puder ser provado. Falha de fechamento, versão divergente ou dependência indisponível bloqueia a publicação; não produz resultado parcial visível.

A fronteira pode ser uma transação única ou protocolo versionado com staging e troca atômica de versão visível. A escolha depende dos writers e leitores reais de Gold/Serving e deve ser comprovada antes da implementação. Não se presume que tabelas existentes aceitem troca de versão nem que todos os consumidores usem a mesma projeção.

## Ordem de implementação

1. Inventariar os writers, leitores, chaves e dependências reais de Gold/Serving e os contratos de correção factual.
2. Definir e testar o resolvedor histórico puro, incluindo fusão, separação, ambiguidade, indefinição e replay.
3. Definir o plano determinístico de invalidação/recomposição e sua conservação de fatos, sem executar writes.
4. Implementar a fronteira de publicação no provider escolhido, com rollback, concorrência, replay e isolamento de leitores.
5. Integrar APIs/BI versionadas e validar compatibilidade; somente depois conectar um executor automático governado.

## Gates

Testes devem cobrir SQL Server e PostgreSQL quando houver persistência equivalente, dados existentes, reinstalação, concorrência, rollback, falha intermediária, replay, conservação de fatos e ausência de publicação parcial. Não utilizar CPF em claro em contratos ou telemetria. Não alterar modelo, parâmetros ou decisão probabilística nesta fatia. A aprovação técnica não constitui autorização de produção.

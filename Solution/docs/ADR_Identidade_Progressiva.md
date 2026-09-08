# ADR — Identidade progressiva e resolução automática

Estado: decisão conceitual aprovada em 2026-09-07; implantação operacional por etapas. Esta ADR complementa a especificação existente e substitui, para a evolução futura, a premissa de que a ausência de CPF impede a criação de identidade. Não declara que o novo comportamento já esteja implantado.

## Decisão

A Jornada atribuirá um UUID inicial a cada identidade de origem admitida, inclusive sem CPF, de modo idempotente e independente do resultado probabilístico. Esse UUID representa uma hipótese de pessoa, não uma prova de unicidade municipal. A mesma chave estável `(sistema_origem_id, codigo_pessoa_origem)` deve recuperar a mesma referência inicial em retransmissões e versões posteriores. O UUID não será derivado de CPF, nome, nascimento ou outros atributos pessoais. Criação e vinculação à origem devem ocorrer em transação, com unicidade no banco e proteção contra concorrência. O UUID de uma observação versionada não substitui a identidade de origem.

A identidade terá somente três estados públicos de resolução:

| Estado | Significado |
|---|---|
| `PROVISORIA` | UUID inicial criado; a resolução ainda não executou para essa identidade. |
| `RESOLVIDA` | Uma execução concluiu uma associação sustentada ou manteve uma identidade nova após busca completa, dentro do universo e da política declarados. |
| `INDEFINIDA` | A resolução executou, mas não pôde estabelecer uma associação suficientemente segura. Nenhum destino é escolhido por conveniência. |

Esses estados não são níveis de qualidade do CPF nem garantias de verdade definitiva. `RESOLVIDA` é uma conclusão relativa às evidências e versões disponíveis. `INDEFINIDA` não é erro de processamento, conflito documental obrigatório ou fila de aprovação humana. `PROVISORIA` não é usada para indicar que uma identidade já resolvida está sendo reavaliada. Durante reavaliação, a última conclusão permanece identificável até a publicação atômica do novo resultado. Erros, retries e leases pertencem ao processamento técnico, sem criar uma quarta classificação de Pessoa.

CPF válido e confiável mantém a rota determinística prioritária e suas travas de consistência. CPF ausente, inválido ou não confiável não impede o UUID inicial. Não se aceita CPF inválido como prova determinística, nem se converte automaticamente uma contradição documental em autorização probabilística. O CPF pode participar da evidência probabilística conforme política validada, mas não é um campo comum cujo peso substitua proveniência e qualidade. Rótulos de treinamento não podem ser definidos circularmente pelo próprio atributo que se pretende calibrar.

## Resultados e completude

Após execução completa, a resolução pode manter uma nova identidade, associar a uma existente ou permanecer indefinida. Nenhum candidato suficientemente compatível após busca completa permite manter o UUID inicial como `RESOLVIDA`, sem afirmar inexistência absoluta de duplicatas fora do universo observado. Evidência insuficiente, ambiguidade ou contradição não resolvida produz `INDEFINIDA`, preservando a referência e os registros originais para novas avaliações. Esses resultados são eventos, não classificações adicionais.

Não se permite transformar limite excedido, consulta incompleta, falha de banco ou corpus truncado em conjunto vazio. Uma execução incompleta não pode publicar conclusão de resolução: deve seguir o mecanismo técnico de retry/falha. A completude institucional e a validade da política devem ser atestadas externamente; um campo booleano ou fingerprint não comprova isso sozinho. A ausência de candidato é relativa ao universo pesquisado e não presume recall perfeito do blocking.

## UUIDs, composição e reversibilidade

O UUID inicial e a atribuição canônica corrente são conceitos distintos. O primeiro nunca é reciclado nem reassociado silenciosamente a outra pessoa. A atribuição pode evoluir mediante evidência, com referências históricas versionadas. Fusões não apagam UUIDs nem registros originais. Devem registrar participantes, composição anterior, destino, evidências, versão da política/modelo e evento de decisão. A aplicação precisa atualizar vínculos e projeções afetados atomicamente, recompor Gold/Serving e invalidar derivados dependentes.

Separações devem identificar o pertencimento de cada observação/registro, preservar referências antigas e produzir sucessores rastreáveis. Um UUID histórico dividido em dois sucessores não pode ser resolvido escolhendo arbitrariamente um deles. Sem sucessor unívoco, a API de resolução histórica deve informar ambiguidade, não devolver um UUID incorreto. A política de sobrevivência e aliases será fechada e testada antes de habilitar fusões automáticas.

A automação não autoriza toda fusão. Somente políticas e modelos aprovados, com evidência suficiente e capacidade de recomposição, podem alterar composição. Decisões devem ser idempotentes, versionadas e protegidas contra concorrência e escrita obsoleta. Reavaliação não apaga decisões anteriores nem converte implicitamente fusão em separação. Uma decisão indefinida pode suspender uma atribuição canônica insegura, mas não deve destruir histórico nem executar separação incompleta. Retorno ao UUID inicial após associação externa exige evento explícito de recomposição, ainda que automático.

## Fatos e consultas

A validade de atendimento ou benefício independe da identidade consolidada. A Gold factual continua preservando fatos válidos mesmo com resolução incompleta. O vínculo com UUID inicial não significa identidade municipal confirmada. O estado de resolução da identidade e o estado de atribuição de cada fato são dimensões distintas e não serão fundidos em um enum. A proveniência e as versões do fato permanecem preservadas.

Consultas devem informar a semântica da referência e seu estado sem ampliar automaticamente o compartilhamento de dados sensíveis. Identidades provisórias ou indefinidas não autorizam agregar registros de outras origens somente por semelhança. GETs existentes exigem compatibilidade e versionamento; não se altera silenciosamente a semântica de `pessoa_uuid`. Referências históricas divididas não devem redirecionar dados para uma pessoa arbitrária. BI deve distinguir identidades iniciais de identidades canônicas resolvidas, evitando dupla contagem de aliases; agregados factuais não podem perder ocorrências por causa da resolução.

## Migração e limites

Esta decisão não revoga imediatamente os estados e constraints atuais de `identidade.vinculo_fonte`, `identidade.pessoa`, Gold ou Serving. A migração será aditiva e versionada, com backfill baseado em evidências, sem fabricar execução de Linkage ou transformar todos os vínculos pendentes em resolvidos. Estados históricos de CPF, vínculo e fato permanecem separados. Não se renomeiam enums por substituição textual global.

A implantação será dividida em contrato/ADR/inventário; persistência do UUID inicial e estado versionado em ambos os providers; integração transacional do Processor; executor de resolução com modelo congelado aprovado; continuidade de UUID, fusão/separação reversível, APIs e BI; e homologação representativa antes da ativação real. Cada etapa manterá compatibilidade até aprovação dos respectivos testes.

Os PRs #33 e #34 permanecem úteis como infraestrutura diagnóstica de amostragem e estimação ponderada. Não autorizam novos thresholds, fusões ou modelos reais. A issue #31 permanece aberta para rótulos independentes, representatividade, dependências multievidência, variância, avaliação e aprovação estatística. Não se cria módulo de Regularização Cadastral nem fila obrigatória de operadores. Correções dos dados de origem e conferência documental permanecem responsabilidade das finalísticas; novas evidências voltam pela integração. SQL Server permanece baseline canônico, PostgreSQL provider operacional paralelo e Fabric camada analítica.

## Primeira implementação

`ProgressiveIdentityLifecycle` implementa somente o contrato puro de criação e conclusão, com versionamento, recibo idempotente e proteção contra separação implícita. Não persiste, não executa score, não cria aliases, não altera fatos, não realiza fusões/separações e não está registrado no Processor. A associação produzida pelo núcleo ainda precisa ser aplicada pelo futuro executor transacional. O código não comprova autenticidade de evidências, completude institucional ou homologação estatística. A integração deste contrato não ativa o novo comportamento operacional.

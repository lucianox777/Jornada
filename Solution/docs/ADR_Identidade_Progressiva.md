# Identidade progressiva — decisão arquitetural e semântica V1

Estado: decisão conceitual aprovada e implementação estrutural em andamento. Este documento é a referência normativa da identidade progressiva da Jornada. Como a solução ainda não foi publicada, o vocabulário abaixo é nativo da V1; não existe versão pública anterior a ser preservada.

## Decisão

A Jornada atribui um UUID inicial a cada identidade de origem admitida, inclusive sem CPF, de modo idempotente e independente de resultado probabilístico. Esse UUID representa uma referência técnica estável da origem, não prova de unicidade municipal. A mesma chave `(sistema_origem_id, codigo_pessoa_origem)` recupera a mesma referência inicial em retransmissões e versões posteriores. O UUID não é derivado de CPF, nome, nascimento ou outros atributos pessoais.

Criação e vinculação à origem ocorrem em transação, com unicidade no banco e proteção contra concorrência. O UUID inicial nunca é reciclado ou transferido. Uma observação versionada não cria uma nova identidade de origem.

A identidade progressiva possui exatamente três estados públicos:

| Estado | Significado |
|---|---|
| `PROVISORIA` | UUID inicial criado; ainda não existe referência canônica publicada para a origem. |
| `REFERENCIA` | Existe uma referência canônica estabelecida segundo a política e as evidências declaradas. Não significa certeza absoluta de identidade civil, titularidade de todos os registros ou autorização irrestrita de compartilhamento. |
| `INDEFINIDA` | Uma execução completa não pôde estabelecer referência canônica suficientemente segura. Nenhum destino é escolhido por conveniência. |

`REFERENCIA` substitui conceitualmente o termo “resolvida” para o **estado da identidade progressiva**. A palavra “resolução” continua válida para o processo técnico que avalia evidências e produz uma decisão. Da mesma forma, `RESOLVIDO` continua válido em `identidade.vinculo_fonte`, onde significa que uma observação possui atribuição corrente. Esses conceitos não devem ser unificados por substituição textual global.

Os estados progressivos não são níveis de qualidade do CPF, nem estados de fatos, vínculos ou `identidade.pessoa`. `PROVISORIA` não é reutilizada durante reavaliação: a última referência publicada permanece identificável até a publicação atômica de um novo resultado. Erros, retries, leases e falhas de processamento não criam uma quarta classificação.

## CPF e referência permanente

CPF válido, confiável e admitido mantém a rota determinística prioritária. A âncora CPF→UUID é permanente: não é transferida, reciclada ou substituída por decisão probabilística. A existência de uma âncora não prova que todos os registros associados pertençam ao titular. Uma atribuição incorreta é corrigida nos vínculos e fatos, preservando histórico e a âncora.

CPF ausente, inválido ou não confiável não impede a criação do UUID inicial. CPF inválido não é usado como prova determinística nem transforma automaticamente uma contradição documental em autorização probabilística. O Linkage pode, quando futuramente homologado, associar uma origem sem CPF a uma referência existente, mas não pode fundir automaticamente âncoras de CPFs distintos.

## Resultados e completude

Uma execução completa de resolução pode produzir `NOVA_IDENTIDADE`, `ASSOCIACAO_EXISTENTE` ou `INDEFINIDA`. Esses são resultados de execução, não estados adicionais da Pessoa.

Quando uma busca completa não encontra candidato suficientemente compatível, a referência canônica pode permanecer no UUID inicial e o estado passa a `REFERENCIA`. Isso não afirma inexistência absoluta de duplicata fora do universo observado. Evidência insuficiente, ambiguidade ou contradição não resolvida produz `INDEFINIDA`.

Limite excedido, consulta incompleta, falha de banco ou corpus truncado não podem ser tratados como conjunto vazio. Execução incompleta não publica referência: segue o mecanismo técnico de retry/falha. A completude do universo e a validade da política precisam ser comprovadas externamente; um booleano ou fingerprint não é prova por si só.

## UUIDs, composição e reversibilidade

`initial_uuid` e `canonical_uuid` são conceitos distintos. O primeiro é imutável. O segundo representa a referência canônica corrente e pode evoluir mediante decisão versionada, evidência e recomposição explícita.

Fusões não apagam UUIDs nem registros originais. Separações preservam referências antigas e produzem sucessores rastreáveis. Um UUID histórico dividido em dois sucessores não pode ser redirecionado arbitrariamente para um deles. Sem sucessor unívoco, a resolução histórica deve informar ambiguidade.

Uma associação externa anterior não pode ser desfeita implicitamente por ausência de candidatos. Retorno ao UUID inicial exige recomposição explícita. Decisões são idempotentes, versionadas e protegidas contra escrita obsoleta e concorrência.

## Fatos e atribuição

A validade de atendimento ou benefício independe da consolidação da identidade. Fatos válidos permanecem disponíveis mesmo com identidade `PROVISORIA` ou `INDEFINIDA`. O estado da identidade e o estado de atribuição de cada fato são dimensões diferentes.

Os estados factuais `ATRIBUIDA`, `PENDENTE_IDENTIDADE` e `CONFLITO_IDENTIDADE` continuam próprios dos fatos. Uma Pessoa pode possuir `REFERENCIA` e ainda assim determinado registro estar em conflito ou sem atribuição. O vínculo com UUID inicial não significa identidade civil confirmada e não amplia automaticamente acesso entre origens.

## Persistência e operação V1

`identidade.pessoa_origem_progressiva` mantém uma linha por `silver.pessoa_origem`, com `initial_uuid` único, `canonical_uuid` opcional, referência legada, estado, versão e timestamps. `identidade.pessoa_origem_progressiva_evento` é append-only e exige recibo correspondente para avanço de versão.

A instalação V1 de SQL Server e PostgreSQL usa diretamente `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`. Não existe migração pública `RESOLVIDA→REFERENCIA`, alias de enum ou compatibilidade de leitura para uma versão nunca publicada.

O backfill de origens é paginado, reentrante e separado da instalação. O cutover operacional falha fechado enquanto houver origem histórica sem `initial_uuid`. Depois do backlog zero, um novo `vinculo_fonte` assegura a referência inicial na mesma transação do Processor. Rollback do lote não pode deixar Pessoa ou referência progressiva órfã.

A criação do UUID inicial já está integrada ao caminho transacional do Processor. Isso não publica decisão probabilística nem altera Gold/Serving por si só. A próxima integração funcional relevante é usar a âncora CPF permanente de forma universal nos escritores e nas correções governadas.

## Limites de ativação

A infraestrutura de amostragem, calibração, scoring e diagnóstico continua sem autorização para ativação probabilística real. A issue #31 permanece o gate independente para corpus representativo, rótulos independentes, recall, calibração, falsos vínculos, subgrupos, variância e aprovação estatística.

Não se cria módulo de Regularização Cadastral nem fila obrigatória de operadores. Correções dos dados de origem e conferência documental continuam responsabilidade das áreas finalísticas; novas evidências retornam pela integração. SQL Server permanece baseline canônico, PostgreSQL provider operacional paralelo e Fabric camada analítica.

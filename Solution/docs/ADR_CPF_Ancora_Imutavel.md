# CPF como âncora permanente de UUID

Estado: decisão aprovada e armazenamento implementado; integração universal aos writers e correções ainda em andamento. Este documento complementa `ADR_Identidade_Progressiva.md` e restringe a política de composição da identidade.

## Invariante

Um CPF válido, confiável e admitido pela Jornada possui exatamente uma âncora UUID permanente. Uma vez constituída, a associação CPF→UUID não é transferida, apagada, reciclada ou substituída por decisão probabilística. Cada UUID de âncora admite no máximo um CPF.

Não se exige CPF para criar identidade: pessoas sem CPF recebem UUID inicial próprio e seguem os estados progressivos `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`.

A permanência independe da existência corrente de observações, fatos, vínculos Silver/Gold/Serving ou mapas ativos. Se todos os registros associados desaparecerem ou deixarem de ser materializados, a Pessoa e sua âncora CPF→UUID permanecem armazenadas. O UUID pode ficar sem registros ativos e continua reservado exclusivamente ao mesmo CPF. Se o CPF reaparecer em qualquer origem ou versão futura, a Jornada deve recuperar o UUID já reservado, nunca criar outro por ausência de dados correntes.

O UUID do CPF é uma referência permanente, não garantia de que todos os registros hoje associados pertençam ao titular. Uma atribuição incorreta é corrigida movendo os registros e preservando histórico, nunca transferindo a âncora. A existência da âncora não autoriza compartilhar dados de composição suspeita. Consulta da referência estável e liberação de dados canônicos são decisões distintas; conflito pode impedir a segunda sem criar outro UUID para o CPF.

O Linkage pode futuramente associar identidades sem CPF a uma âncora existente mediante política validada, ou manter identidade separada. Não pode criar segundo CPF para a mesma âncora, alterar uma âncora existente nem fundir automaticamente duas âncoras de CPFs distintos. Correção do CPF declarado de um fato não modifica seu histórico nem transfere a âncora.

## Armazenamento V1

`database/migrations/20260907_Cpf_Ancora.sql` e `database/postgresql/Jornada_Cpf_Ancora.sql` criam `identidade.cpf_ancora`, com CPF como chave primária, UUID único e FK para Pessoa. A tabela é append-only; update e delete são rejeitados. O CPF é validado estruturalmente, incluindo dígitos verificadores, sem que isso por si só comprove titularidade.

`sp_obter_cpf_ancora`/`fn_obter_cpf_ancora` consultam a referência. `sp_reservar_cpf_ancora`/`fn_reservar_cpf_ancora` reservam somente um UUID existente de forma transacional e idempotente. Reservar outro UUID para CPF já ancorado ou outro CPF para UUID já ancorado falha sem escolha automática de destino.

A FK impede remover fisicamente uma Pessoa enquanto a âncora existir. Limpeza de fatos, observações ou projeções derivadas não é limpeza da identidade técnica. Política de retenção deve preservar essa referência mínima ou mecanismo normativamente equivalente que mantenha a mesma relação CPF→UUID.

O backfill examina mapas CPF históricos, inclusive encerrados. CPF com UUIDs distintos, UUID com CPFs distintos, CPF inválido ou divergência com âncora existente faz a operação falhar sem escolher titular e sem modificar os mapas. O backfill não transforma observações pendentes em `REFERENCIA`, não altera estado de `vinculo_fonte` e não inventa execução de Linkage. Instalação repetida preserva âncoras e instantes de criação.

## Relação com a identidade progressiva

A identidade de origem tem `initial_uuid` imutável por `(sistema_origem_id,codigo_pessoa_origem)`. A referência canônica corrente pode evoluir e, quando estabelecida, o estado progressivo é `REFERENCIA`. Isso não muda o estado `RESOLVIDO` de um `vinculo_fonte`, que descreve atribuição de observação.

Uma origem que posteriormente informa CPF confiável deverá ser associada à âncora já existente, ou reservar nova âncora quando o CPF ainda não possuir referência permanente. Ela nunca poderá gerar segundo UUID permanente para CPF conhecido. Fusões e separações preservam âncoras e identidades históricas e recompõem Gold/Serving sem transferir CPF.

## Continuidade

A criação transacional de `initial_uuid` por origem e o cutover do Processor já foram implementados nos dois providers. A próxima etapa é integrar a reserva CPF como fonte universal dos writers SQL Server/PostgreSQL e dos procedimentos de correção governada, eliminando caminhos que ainda dependem apenas do `identity_map` corrente.

Depois virão composição reversível, APIs e BI. A issue #31 continua obrigatória antes de ativar decisões probabilísticas reais. Nenhuma aprovação de CI substitui validação estatística. Não há fila humana obrigatória nem módulo de Regularização Cadastral.

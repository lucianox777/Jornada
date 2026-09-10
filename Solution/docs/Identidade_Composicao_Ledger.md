# Composição reversível — ledger de preparação V1

Estado: primeira fatia de armazenamento, sem execução de composição. Detalha a composição reversível definida em `Arquitetura_Identidade_Linkage.md` e a issue #36. A PR #49 entregou o planejador puro; este componente armazena suas propostas e reserva referências novas. A issue #31 continua independente.

## Responsabilidade

O ledger não decide identidade, não executa score, não aplica fusões, não modifica CPF, não publica referência progressiva, não altera vínculos e não recompõe Gold/Serving. `PREPARADA` é o único estado persistido nesta versão; não significa `APLICADA`, aprovação institucional ou conclusão de resolução. Um plano preparado pode ficar obsoleto e deve ser revalidado integralmente no momento da aplicação. O histórico em `HistoryToAppend` é proposto, não um histórico de composição já efetivada.

São duas tabelas aditivas: `identidade.composicao_uuid_reserva`, registro permanente dos UUIDs gerados pelo banco, e `identidade.composicao_plano`, registro imutável do conteúdo da decisão, alterações propostas, snapshots históricos propostos, reservas e proveniência. Não se cria um segundo writer de identidade nem uma fila obrigatória de operadores.

## Instalação e contrato

SQL Server: aplicar `database/migrations/20260908_Identidade_Composicao_Ledger.sql` após a base normativa e `Jornada_Identidade_Progressiva.sql`. PostgreSQL: aplicar `database/postgresql/Jornada_Identidade_Composicao_Ledger.sql` após os cores operacionais e a persistência progressiva. As instalações são repetíveis e não executam backfill nem varredura da população. Nenhum serviço, endpoint ou job é registrado nesta fatia.

`sp_reservar_uuid_composicao` / `reservar_uuid_composicao` recebe `decision_id` e `reserva_id` estáveis. O banco gera o UUID, insere `identidade.pessoa` e a reserva na mesma transação. A mesma chave retorna o mesmo UUID; outro proprietário é recusado. Uma reserva nova não pode ser criada depois do registro do plano. Reservas não utilizadas não são apagadas nem recicladas. O registro permanente e a FK de Pessoa protegem o namespace reservado. O mecanismo não aceita um UUID arbitrário como nova reserva.

`sp_registrar_plano_composicao` / `registrar_plano_composicao` recebe o identificador, JSON canônico da decisão, JSON do plano, lista de reservas da decisão, referência opaca do solicitante e correlação. O hash SHA-256 do request é calculado no banco sobre UTF-8 exato, em paridade com o planejador .NET. O banco também calcula hashes do plano e da lista de reservas, verifica os identificadores, formato, campos obrigatórios, hash do plano e propriedade/conjunto completo das reservas. O mesmo `decision_id` com os mesmos hashes retorna o recibo anterior; conteúdo divergente falha. A serialização deve ser canônica para que reordenações equivalentes não produzam hashes diferentes.

Os registros são append-only, com PKs, FKs, unicidade e triggers que recusam UPDATE/DELETE. O bloqueio transacional por decisão serializa reserva e registro em ambos os providers. As procedures participam da transação do chamador; SQL Server abre uma transação própria somente quando não há uma externa. Em caso de erro numa transação externa, o chamador deve efetuar rollback. Não há commit antecipado de uma operação maior.

A referência do solicitante e da evidência deve ser opaca, sem CPF ou atributos pessoais em claro. O hash identifica conteúdo, não autentica o solicitante nem prova a qualidade das evidências. As rotinas são infraestrutura interna para execução governada futura. Não se concedem novos acessos de aplicação neste pacote; os grants do futuro papel executor deverão ser explícitos e mínimos. Os testes usam proprietário de banco descartável.

## O que o ledger ainda não garante

O registro de preparação não prova que o universo de membros está fechado, que o destino é admissível, que o CPF está correto ou que o plano permanece atual. Essas garantias pertencem ao adapter transacional de aplicação, que deverá carregar o estado autoritativo, conferir a autoridade CPF e as reservas, executar o planejador, bloquear os componentes envolvidos, revalidar versões e publicar todos os efeitos com consistência. A lista fornecida pelo chamador e um hash não substituem essas provas. Nenhum futuro executor pode tratar uma linha `PREPARADA` como autorização para executar SQL de composição sem essa revalidação.

A próxima fatia acrescentará o recibo de aplicação e histórico efetivado, integrando os procedimentos existentes de correção governada, `vinculo_fonte`, eventos progressivos e recomposição Gold/Serving. Os recibos de preparação não serão convertidos retroativamente em eventos aplicados. Referências históricas divididas continuarão exigindo resolução explícita `AMBIGUA` ou `INDEFINIDA`, sem sucessor arbitrário.

## Validação

O workflow dedicado instala os schemas duas vezes em SQL Server 2022 e PostgreSQL 17 sobre bases descartáveis, executa 13 regressões do planejador e um harness real nos dois providers. O harness verifica concorrência de reserva e registro, replay idêntico, rejeição de conteúdo divergente, propriedade das reservas, rollback sem Pessoa órfã, imutabilidade e conservação de CPF, vínculos, Gold e referências progressivas. Reinstala o ledger sobre registros povoados e exige evidência de preservação. CI verde valida esta fatia de armazenamento, não autoriza fusões reais ou Linkage probabilístico.
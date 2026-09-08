# Identidade progressiva — persistência V1

Estado: implementação de armazenamento isolado, em validação. Complementa ADR_Identidade_Progressiva.md e issue #36. Não altera a semântica operacional de Pessoa, CPF, Gold ou Serving e não ativa resolução/fusão probabilística.

## Modelo e semântica

`identidade.pessoa_origem_progressiva` possui uma linha por `silver.pessoa_origem.pessoa_origem_id`, cuja chave estável é `(sistema_origem_id,codigo_pessoa_origem)`. O banco garante a unicidade da origem e do UUID inicial, as FKs e a imutabilidade da origem, UUID inicial, instante de criação e referência legada. O UUID é aleatório, não derivado de CPF ou outros dados pessoais, e é reservado em `identidade.pessoa` na mesma transação. A identidade inicial ainda não é publicada em Gold nem utilizada pelo scorer ou pelas APIs.

O estado começa em `PROVISORIA`, versão zero, sem atribuição canônica. `RESOLVIDA` e `INDEFINIDA` permanecem os únicos outros estados públicos. Estado técnico de `identidade.pessoa`, situação do CPF e atribuição de cada fato continuam separados. A tabela de eventos reserva os campos de resultado, versão esperada, política, evidência, modelo e universo para o futuro executor. O histórico é append-only, e o avanço de versão exige um recibo correspondente. Esta fatia não disponibiliza escrita de decisões de Linkage, nem comprova autenticidade ou aprovação de um recibo externo.

`legacy_pessoa_uuid` preserva, se houver, a atribuição corrente da última observação da origem no momento da criação. É uma referência de migração, não uma resolução nova. Não se reutiliza esse UUID como UUID inicial, pois diversas origens podem compartilhar a mesma identidade canônica. O valor legado é imutável, não é atualizado automaticamente após mudanças de CPF e não redireciona fatos. O relacionamento atual permanece nas tabelas existentes. A conciliação posterior terá de consultar a atribuição vigente e o histórico, sem tratar a fotografia legada como verdade atual.

## Instalação e backfill

SQL Server: aplicar `database/Jornada_Identidade_Progressiva.sql` após `database/Jornada_Fase1.sql`. PostgreSQL: aplicar `database/postgresql/Jornada_Identidade_Progressiva.sql` após Resultado, Ingestion Processor e Processor Persistence cores. As migrações são aditivas e reentrantes, mas ainda não fazem parte do instalador operacional principal. A versão de schema existente não é elevada nesta fatia. Não executar migrações não homologadas em bancos institucionais ou de produção.

`ProgressiveIdentityOriginStore.EnsureInitialAsync(sourceId)` exige a origem já persistida. O overload com conexão/transação permite ao Processor futuro criar a referência dentro da mesma transação da Silver. O lock da linha de origem e a unicidade do banco serializam criações concorrentes. O overload independente usa Read Committed com lock explícito; quem fornecer transação serializável deve reiniciar a transação inteira quando houver falha de serialização. Nenhuma falha é convertida em UUID novo fora da transação.

`BackfillPageAsync(maxSources)` aceita 1 a 1000, identifica apenas origens ainda não registradas e assegura cada uma em transação própria. Pode ser repetido após interrupções, sem criar novas versões nem duplicar eventos. O retorno é o número de origens tentadas na página; concorrência pode fazer com que outra execução as tenha criado antes. A instalação não dispara o backfill e não há agendamento automático. Em banco povoado, executar primeiro inventário, backup e ensaio de restore, depois lotes limitados e monitorados. Não há promessa de duração, throughput ou janela institucional sem medição.

## Validação e limites

O workflow dedicado utiliza bancos descartáveis `JornadaProgressiveTest` em SQL Server e PostgreSQL. Aplica o baseline e a migração duas vezes, executa regressões de domínio e um harness real com concorrência, retransmissão, rollback, origem inexistente, preservação de referência legada, backfill reentrante e imutabilidade. O harness rejeita qualquer outro nome de banco. Evidências são publicadas inclusive em falha e o resumo exige todos os estágios declarados.

A validação desta fatia não cobre ainda criação automática no Processor real, versões cadastrais sucessivas, publicação de decisões, fusão/separação, aliases, recomposição Gold/Serving, APIs, BI, escala ou migração institucional povoada. Esses itens permanecem na issue #36. A issue #31 continua responsável pela calibração representativa e homologação estatística. A próxima etapa integrará a criação do UUID no processamento transacional de ambas as bases, preservando CPF determinístico e fatos independentes da identidade. Nenhum modelo ou threshold é promovido por este PR.

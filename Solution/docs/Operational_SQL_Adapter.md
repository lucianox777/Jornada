# Adapter do Banco Operacional

## Objetivo

A Jornada mantém acesso explícito ao banco operacional, sem Entity Framework e sem tentar esconder diferenças reais entre os SGBDs.

A arquitetura tem duas fronteiras complementares:

- `IOperationalSqlAdapter`: fronteira legada fortemente tipada em `SqlConnection`, preservada para todo o código SQL Server/Fabric já homologado;
- `IOperationalDatabaseAdapter`: fronteira ADO.NET neutra baseada em `DbConnection`, usada por componentes que já possuem implementação multi-provider.

A migração é incremental. Nenhum componente existente é obrigado a trocar de provider antes de ter DDL, SQL, concorrência e testes equivalentes no PostgreSQL.

## Providers

```text
                         Jornada
                            |
                IOperationalDatabaseAdapter
                     /                \
                    /                  \
       OperationalSqlAdapter    PostgreSqlOperationalAdapter
              |                         |
 Microsoft.Data.SqlClient             Npgsql
              |                         |
 SQL Server / Fabric SQL           PostgreSQL
```

`OperationalDatabaseAdapterFactory` reconhece `SqlServer` e `PostgreSql`. A ausência de `Database:Provider` mantém `SqlServer` como padrão, preservando retrocompatibilidade.

## SQL Server / Fabric

`OperationalSqlAdapter` continua implementando `IOperationalSqlAdapter` e também implementa `IOperationalDatabaseAdapter`.

O comportamento existente não mudou:

- connection string Microsoft SQL;
- conexões normais com pooling conforme configuração;
- sessões dedicadas com `Pooling=false` e `Enlist=false`;
- `sp_getapplock` e demais construções T-SQL continuam no caminho SQL Server;
- SQL Database in Microsoft Fabric continua pertencendo à família Microsoft SQL enquanto o protocolo e o T-SQL usados pela Jornada forem compatíveis.

A aplicação não introduz `if (fabric)` na lógica funcional. Não existe comportamento específico de Fabric no adapter Microsoft SQL enquanto nenhuma diferença concreta for demonstrada por teste.

## PostgreSQL

`PostgreSqlOperationalAdapter` usa Npgsql e implementa `IOperationalDatabaseAdapter`.

Ele fornece:

- conexão normal PostgreSQL;
- sessão dedicada com `Pooling=false` e `Enlist=false`;
- ciclo de vida assíncrono via `DbConnection`;
- seleção explícita por `Database:Provider=PostgreSql` nos componentes já portados.

Não existe Entity Framework na implementação PostgreSQL.

## Coordenação do pipeline

A coordenação concorrente é uma diferença de plataforma deliberadamente explícita.

### Microsoft SQL

`SqlPipelineCoordinator` usa `sp_getapplock` com locks vinculados à sessão.

### PostgreSQL

`PostgreSqlPipelineCoordinator` usa advisory locks vinculados à sessão:

- `pg_try_advisory_lock_shared` para a intenção compartilhada do Processor;
- `pg_try_advisory_lock` para locks exclusivos;
- `pg_advisory_unlock_shared` / `pg_advisory_unlock` na liberação;
- conexão física dedicada sem pooling como proteção final de ciclo de vida.

Os recursos lógicos permanecem os mesmos:

- `Jornada.Pipeline.ExclusiveRequest`;
- `Jornada.Pipeline.Corpus`.

O CI PostgreSQL prova que um job exclusivo impede a aquisição do corpus pelo Processor e que, após a liberação, o Processor volta a adquirir o lock.

## Fatias funcionais multi-provider já implementadas

### Resultado de processamento

`Jornada.Resultado.Api` é o primeiro serviço operacional executado nos dois providers.

Ele usa `IOperationalDatabaseAdapter` e mantém diferenças pequenas de dialeto em `ResultadoDatabaseDialect`, por exemplo:

- SQL Server: `TOP(1)` e `COUNT_BIG`;
- PostgreSQL: `LIMIT 1` e `COUNT(*)`.

Consultas estruturalmente comuns usam `DbConnection`, `DbCommand`, `DbParameter` e `DbDataReader`.

### Metadados de ingestão

`PostgreSqlIngestionMetadataStore` implementa no PostgreSQL a parte relacional do recebimento:

- resolve Gestor, `codigoSistemaOrigem`, schema de Pessoa e Tipo/versão;
- registra atomicamente `ingestao.entrega`, referência Bronze e lote inicial;
- usa `ON CONFLICT(gestor_id,idempotency_key)` para idempotência;
- retransmissão do mesmo conteúdo retorna a Entrega original;
- reutilização da chave para hash/tamanho diferentes é rejeitada.

O objeto Bronze continua sendo persistido pelo `IBronzeObjectStore` antes do registro relacional. A `Jornada.Api` principal ainda não é declarada PostgreSQL porque outros serviços da API continuam SQL Server específicos.

### Reserva do Processor

`PostgreSqlProcessorLeaseStore` implementa o ciclo de lease da fila operacional:

- `FOR UPDATE SKIP LOCKED` substitui semanticamente `UPDLOCK + READPAST` na reserva concorrente;
- fencing por `lease_id + lease_owner`;
- heartbeat e renovação do lease;
- recuperação de lease expirado;
- retry exponencial / poison;
- finalização em rejeição ou quarentena;
- recomposição do estado da Entrega por `ingestao.recalcular_entrega`.

Essa fatia porta **reserva e ciclo de vida do lote**, mas ainda não a materialização Silver/Gold realizada pelo `SqlProcessorRepository`.

## SEHAB no PostgreSQL

O fixture de integração preserva as decisões de contrato da SEHAB:

- Gestor: `SEHAB`;
- `codigoSistemaOrigem`: `HabitaSampa`;
- schema de Pessoa: v1;
- `AA01`: Auxílio Aluguel, Tipo v1;
- `AE01`: Auxílio Emergencial, Tipo v1;
- ambos são benefícios distintos e não compartilham regra factual por conveniência.

O CSV real da SEHAB é somente fonte para conversão/teste externo. A fronteira da Jornada continua sendo o ZIP JSON canônico.

## DDL PostgreSQL atual

O diretório `database/postgresql/` contém:

- `Jornada_Resultado_Core.sql`;
- `Jornada_Ingestion_Processor_Core.sql`;
- `Jornada_Resultado_Core_Smoke.sql`.

Os scripts são reaplicados no CI para provar idempotência. Eles representam somente as fatias já portadas e **não são ainda substituto integral** de `database/Jornada_Fase1.sql`.

Ainda precisam ser portados e testados antes de PostgreSQL poder ser declarado backend completo da Jornada:

- integração da `Jornada.Api` principal com o provider PostgreSQL, incluindo coordenação Bronze;
- persistência Silver de Pessoa e fatos;
- Gold;
- identidade e correções governadas;
- linkage completo;
- manutenção e retenção;
- views/Serving/BI dependentes do banco;
- auditoria completa;
- DDL integral e seeds;
- testes E2E HTTP → Bronze → Silver → Gold → Serving.

## Regra de arquitetura

Não criar SQL supostamente universal quando os bancos têm mecanismos diferentes.

A regra é:

1. operações ADO.NET comuns podem usar `IOperationalDatabaseAdapter`;
2. diferenças reais de dialeto ficam em componentes/dialetos pequenos e explícitos;
3. locking, bulk, DDL e operações específicas recebem implementação própria por provider;
4. cada nova fatia PostgreSQL deve ter teste contra PostgreSQL real antes de substituir o caminho SQL Server;
5. SQL Server/Fabric permanece funcional até a suíte PostgreSQL atingir paridade.

## Configuração

SQL Server/Fabric, padrão retrocompatível:

```text
Database__Provider=SqlServer
ConnectionStrings__Jornada=<connection string Microsoft SQL>
```

PostgreSQL, somente para componentes/fatias já portados:

```text
Database__Provider=PostgreSql
ConnectionStrings__Jornada=<connection string PostgreSQL>
```

Não configurar a Solution inteira com `PostgreSql` enquanto a matriz de paridade não estiver completa.

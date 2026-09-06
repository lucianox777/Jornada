# Adapter do Banco Operacional

## Objetivo

A Jornada mantém acesso explícito ao banco operacional, sem Entity Framework e sem tentar esconder diferenças reais entre os SGBDs.

A arquitetura passa a ter duas fronteiras complementares:

- `IOperationalSqlAdapter`: fronteira legada fortemente tipada em `SqlConnection`, preservada para todo o código SQL Server/Fabric já homologado;
- `IOperationalDatabaseAdapter`: nova fronteira ADO.NET neutra baseada em `DbConnection`, usada por componentes que já possuem implementação multi-provider.

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

`OperationalSqlAdapter` continua implementando `IOperationalSqlAdapter` e agora também implementa `IOperationalDatabaseAdapter`.

O comportamento existente não mudou:

- connection string Microsoft SQL;
- conexões normais com pooling conforme configuração;
- sessões dedicadas com `Pooling=false` e `Enlist=false`;
- `sp_getapplock` e demais construções T-SQL continuam no caminho SQL Server;
- SQL Database in Microsoft Fabric continua pertencendo à família Microsoft SQL enquanto o protocolo e o T-SQL usados pela Jornada forem compatíveis.

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

## Primeira fatia funcional multi-provider

`Jornada.Resultado.Api` é o primeiro serviço operacional executado nos dois providers.

Ele usa `IOperationalDatabaseAdapter` e mantém diferenças pequenas de dialeto em `ResultadoDatabaseDialect`, por exemplo:

- SQL Server: `TOP(1)` e `COUNT_BIG`;
- PostgreSQL: `LIMIT 1` e `COUNT(*)`.

Consultas estruturalmente comuns usam `DbConnection`, `DbCommand`, `DbParameter` e `DbDataReader`.

O workflow `jornada-postgresql-adapter` executa PostgreSQL real em container, aplica o DDL duas vezes para provar idempotência, testa advisory locks e chama o endpoint HTTP de resultado até a leitura das tabelas PostgreSQL.

## DDL PostgreSQL atual

O diretório `database/postgresql/` contém neste momento:

- `Jornada_Resultado_Core.sql`;
- `Jornada_Resultado_Core_Smoke.sql`.

Esse DDL representa **somente a primeira fatia operacional necessária à Resultado API e aos testes do adapter**. Ele não é ainda um substituto integral de `database/Jornada_Fase1.sql`.

Ainda precisam ser portados e testados antes de PostgreSQL poder ser declarado backend completo da Jornada:

- ingestão transacional completa;
- Processor e reserva concorrente de lotes;
- Silver;
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

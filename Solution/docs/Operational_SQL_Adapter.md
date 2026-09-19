# Adapter operacional Microsoft SQL — candidata v5.00

## Decisão corrente

A Jornada candidata v5.00 possui **um único runtime relacional suportado: Microsoft SQL Server**.

`OperationalSqlAdapter` / `Microsoft.Data.SqlClient` são a fronteira operacional corrente. `IOperationalDatabaseAdapter` permanece como abstração de ciclo de vida de conexão para reduzir acoplamento interno, mas **não representa promessa de múltiplos providers**.

`OperationalDatabaseAdapterFactory` aceita `SqlServer` e o alias `MicrosoftSql`. Qualquer outro valor falha fechado.

PostgreSQL/Npgsql, seus DDLs, stores, coordenação, calibrador, runner e gates de paridade foram extraídos da candidata. A implementação anterior permanece recuperável no histórico Git e pode ser portada para projeto independente sem impor paridade ao produto corrente.

## Fronteiras

- `IOperationalDatabaseAdapter`: criação/abertura de conexões operacionais.
- `IOperationalSqlAdapter`: operações que dependem explicitamente de `SqlConnection` e `SqlTransaction`.
- `OperationalSqlAdapter`: implementação Microsoft SQL usada pelo runtime.
- `SqlPipelineCoordinator`: locking/coordenação transacional do pipeline.
- repositórios `Sql*`: persistência operacional do Processor, identidade e Linkage.

A abstração não tenta esconder semântica de locking, DDL ou SQL específico. O código deve ser explícito quando depender de T-SQL ou de primitivas do SQL Server.

## Microsoft Fabric

SQL Database in Microsoft Fabric não é provider alternativo nem alvo operacional da candidata v5.00. Evidências anteriores permanecem apenas como histórico de compatibilidade técnica.

Não existe `FabricSqlAdapter`, alias `Database:Provider=Fabric` ou branch funcional Fabric no runtime corrente. Lakehouse e SQL Analytics Endpoint permanecem na camada analítica/compatibilidade.

## Configuração

Configuração canônica:

```text
Database__Provider=SqlServer
ConnectionStrings__Jornada=<connection string Microsoft SQL>
```

A omissão de `Database:Provider` mantém `SqlServer` como padrão.

Valores `PostgreSql`, `Postgres` ou `Fabric` não são aceitos pela factory da candidata.

## Regra de evolução

Uma futura segunda tecnologia relacional exige nova decisão arquitetural explícita e projeto de paridade próprio. Ela não deve ser introduzida gradualmente por condicionais de provider dentro dos stores correntes.

Ramificações históricas de dialeto ainda presentes em componentes compartilhados durante a purga não constituem suporte: nenhum entrypoint/factory consegue selecionar PostgreSQL. Elas devem ser removidas incrementalmente para reduzir código morto, sem reabrir a decisão de runtime.

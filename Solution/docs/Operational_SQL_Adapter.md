# Adapter do Banco Operacional

## Objetivo

A Jornada mantém acesso explícito ao banco operacional, sem Entity Framework e sem tentar esconder diferenças reais entre os SGBDs.

A arquitetura tem duas fronteiras complementares:

- `IOperationalSqlAdapter`: fronteira legada fortemente tipada em `SqlConnection`, preservada para o código SQL Server/Fabric já homologado;
- `IOperationalDatabaseAdapter`: fronteira ADO.NET neutra baseada em `DbConnection`, usada por componentes que possuem implementação multi-provider.

A migração é incremental. Nenhum componente troca de provider antes de ter DDL, SQL, concorrência e testes equivalentes no PostgreSQL.

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

`OperationalSqlAdapter` continua implementando `IOperationalSqlAdapter` e também `IOperationalDatabaseAdapter`.

O comportamento existente não mudou:

- connection string Microsoft SQL;
- conexões normais com pooling conforme configuração;
- sessões dedicadas com `Pooling=false` e `Enlist=false`;
- `sp_getapplock` e demais construções T-SQL continuam no caminho SQL Server;
- SQL Database in Microsoft Fabric permanece na família Microsoft SQL enquanto o protocolo e o T-SQL usados pela Jornada forem compatíveis.

A aplicação não introduz `if (fabric)` na lógica funcional. Não existe `FabricSqlAdapter`: Fabric reutiliza `OperationalSqlAdapter` e `Microsoft.Data.SqlClient`.

## PostgreSQL

`PostgreSqlOperationalAdapter` usa Npgsql e implementa `IOperationalDatabaseAdapter`.

Ele fornece:

- conexão normal PostgreSQL;
- sessão dedicada com `Pooling=false` e `Enlist=false`;
- ciclo de vida assíncrono via `DbConnection`;
- seleção explícita por `Database:Provider=PostgreSql` nos componentes portados.

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

## Fatias funcionais multi-provider implementadas

### Resultado de processamento

`Jornada.Resultado.Api` executa nos dois providers.

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

### Processor PostgreSQL

O Processor possui agora fronteiras neutras para repositório e coordenação, com implementações específicas por provider.

`PostgreSqlProcessorLeaseStore` e seu adapter implementam o ciclo operacional da fila:

- `FOR UPDATE SKIP LOCKED` substitui semanticamente `UPDLOCK + READPAST` na reserva concorrente;
- fencing por `lease_id + lease_owner`;
- heartbeat e renovação do lease;
- recuperação de lease expirado;
- retry exponencial / poison;
- finalização em rejeição ou quarentena;
- recomposição do estado da Entrega por `ingestao.recalcular_entrega`.

`PostgreSqlProcessorRepository` implementa a persistência transacional do pacote validado:

- versionamento e retransmissão idempotente de Pessoa;
- resolução determinística por CPF, conflito de identificador e pendência quando o CPF não está disponível;
- observações Silver de Pessoa, verificação documental e atributos transversais;
- referência territorial e geografia declarada pela origem;
- Gold Pessoa com baseline de fonte única/corroborado/divergente;
- versionamento de fatos de benefício e serviço;
- QC factual;
- Gold de benefício/serviço e `serving.registro_integrado`;
- publicação de `entrega_completa` somente após a Entrega atingir `PROCESSADA`;
- rollback transacional e fencing do lease antes da publicação final.

A implementação PostgreSQL **não declara paridade do subsistema analítico de Linkage Fellegi–Sunter**. Observações sem CPF permanecem pendentes para o fluxo probabilístico explícito; a portabilidade do Runner/calibração/modelos de Linkage é uma etapa própria.

## SEHAB no PostgreSQL

O código técnico do sistema de origem segue o mesmo contrato canônico do envelope e do SQL Server:

- Gestor: `SEHAB`;
- `codigoSistemaOrigem`: `SEHAB`;
- nome de exibição do sistema: `HabitaSampa`;
- `codigoSistemaOrigem` aceita somente `A-Z`, `0-9`, `_` e `-`;
- schema de Pessoa usado no runtime E2E: v2;
- `AA01`: Auxílio Aluguel, Tipo v1.

Os smokes legados de metadados também mantêm AA01/AE01 como Tipos distintos. O CSV real da SEHAB é somente fonte para conversão/teste externo; a fronteira da Jornada continua sendo o ZIP JSON canônico.

## DDL PostgreSQL atual

O diretório `database/postgresql/` contém, entre outros:

- `Jornada_Resultado_Core.sql`;
- `Jornada_Ingestion_Processor_Core.sql`;
- `Jornada_Processor_Persistence_Core.sql`;
- `Jornada_Resultado_Core_Smoke.sql`;
- `Jornada_Processor_Persistence_Smoke.sql`.

Os cores são reaplicados no CI para provar idempotência. O workflow PostgreSQL também executa o Worker real com um ZIP canônico na Bronze e exige o caminho:

```text
Bronze -> Processor Worker -> Silver -> Identidade -> Gold -> Serving
```

Esse teste usa os schemas reais SEHAB Pessoa v2 e AA01 v1 e valida seus SHA-256 antes da persistência.

Os scripts ainda não são substituto integral de `database/Jornada_Fase1.sql`. Permanecem fora da paridade PostgreSQL completa, principalmente:

- integração da `Jornada.Api` principal com o provider PostgreSQL, incluindo toda a borda de recebimento/autorização;
- correções governadas completas de identidade;
- Runner, calibração, modelos e replay do Linkage Fellegi–Sunter;
- manutenção e retenção integrais;
- views/BI ainda dependentes de objetos Microsoft SQL não portados;
- auditoria completa;
- DDL/seeds integrais de toda a Solution;
- E2E HTTP da API principal até Gold/Serving no PostgreSQL.

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

PostgreSQL, somente para componentes/fatias portados:

```text
Database__Provider=PostgreSql
ConnectionStrings__Jornada=<connection string PostgreSQL>
```

Não configurar a Solution inteira com `PostgreSql` enquanto a matriz de paridade não estiver completa.

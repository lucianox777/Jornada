# Adapter do Banco Operacional — v4.03

## Objetivo

`Jornada.Operational.Sql` mantém uma fronteira única entre os componentes operacionais da Jornada e a família Microsoft SQL, sem esconder T-SQL, transações, constraints ou recursos SQL que fazem parte da solução.

A release v4.03 preserva a prova de portabilidade do mesmo núcleo relacional entre **SQL Server 2022** e **SQL Database in Microsoft Fabric**. Não existe `FabricSqlAdapter`, DDL alternativo ou regra funcional duplicada por hospedagem.

## Fronteira

```text
Jornada.Api / Processor / Linkage / Maintenance
                    |
                    v
          IOperationalSqlAdapter
                    |
                    v
           OperationalSqlAdapter
                    |
                    v
          Microsoft.Data.SqlClient
                    |
          +---------+----------+
          |                    |
 SQL Server 2022     SQL Database in Fabric
 DEV/CI/testes       HML/Produção preferencial
```

## Baseline de desenvolvimento

O desenvolvimento diário, o CI e a validação ordinária de release permanecem em **SQL Server 2022 local/Testcontainers**. Nenhuma conta, capacidade, workspace, usuário ou connection string Fabric é requisito para compilar ou testar a Jornada.

SQL Database in Microsoft Fabric é o ambiente operacional preferencial de HML/Produção. A suíte de compatibilidade Fabric continua sendo executada somente quando houver ambiente institucional específico disponível; a connection string é configuração externa e não integra o pacote de release.

## Evidência de compatibilidade

Em 03/09/2026 a suíte Integration executou 58/58 testes com sucesso em SQL Database in Microsoft Fabric, sem skips. O cenário concorrente de oito contendores também passou. A evidência confirma a compatibilidade funcional do Adapter único e do DDL atual; não constitui benchmark de performance nem escolha automática de produção.

## Sessões normais e dedicadas

A fronteira oferece dois perfis de conexão:

- `OpenAsync` / `CreateConnection`: preservam as propriedades normais da connection string do ambiente;
- `OpenDedicatedSessionAsync` / `CreateDedicatedSessionConnection`: forçam `Pooling=false` e `Enlist=false`.

Sessões dedicadas são obrigatórias para mecanismos com `sp_getapplock` e `LockOwner='Session'`, porque a vida do lock deve coincidir com a sessão física. Locks com `LockOwner='Transaction'` continuam usando conexões normais e são liberados pelo ciclo transacional.

## Regra de arquitetura

A lógica funcional não deve conter `if (fabric)` / `if (sqlServer)`. Diferenças de hospedagem devem ser classificadas e demonstradas por teste antes de qualquer especialização.

O ambiente operacional preferencial de HML/Produção é SQL Database in Microsoft Fabric. Ensaios com carga representativa continuam necessários para dimensionamento, capacidade, disponibilidade, segurança, custo e operação, sem introduzir bifurcação funcional do Adapter.

## O que não muda na v4.03

- schema persistido Base 3.62 / SolutionSchema v3.68;
- `database/Jornada_Fase1.sql` e seed;
- contratos JSON/OpenAPI;
- regras de identidade, UUID, linkage, Gold/Serving, autorização e auditoria;
- dependências NuGet;
- Bronze física fora do banco funcional.

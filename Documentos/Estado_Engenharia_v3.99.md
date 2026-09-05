# Estado de Engenharia — v3.99

**Base Normativa:** v3.62
**SolutionSchema:** v3.68
**Solution Engenharia:** v3.99
**Natureza:** refatoração de infraestrutura — Adapter do banco operacional

## Fechamento desta versão

A v3.99 introduz `Jornada.Operational.Sql` como fronteira única de criação/abertura de conexões Microsoft SQL. O objetivo é preparar uma comparação empírica entre SQL Server e SQL Database in Microsoft Fabric sem contaminar regras funcionais com decisões de hospedagem.

A implementação permanece deliberadamente estreita:

- `IOperationalSqlAdapter` define conexão normal e sessão dedicada;
- `OperationalSqlAdapter` é a única implementação;
- API, Processor, Linkage e Workers consomem a fronteira;
- nenhuma lógica de negócio possui `if (fabric)` ou equivalente;
- sessões `LockOwner='Session'` usam `Pooling=false` e `Enlist=false` centralmente.

O DDL, contratos, Base Normativa, SolutionSchema e a família RN/RF/RNF/RT da v3.98 não mudam.

## Validação

O ambiente de empacotamento desta execução não possui `dotnet`, Docker ou PowerShell. Foram executados somente gates e auditorias estáticas disponíveis. Como a inclusão do novo ProjectReference alterou `packages.lock.json`, a v3.99 fica explicitamente **pendente de restore locked e runtime**.

A evidência externa v3.95 (build 0/0, Unit 153/153 e Integration 58/58) permanece histórica e não é atribuída à v3.99.

## Próximo experimento

Depois da revalidação real da v3.99 em SQL Server, a mesma Solution deve ser executada contra SQL Database in Microsoft Fabric: DDL sem adaptação inicial, seed, Integration e testes de concorrência/locks. Somente incompatibilidades demonstradas justificam um Adapter especializado adicional.

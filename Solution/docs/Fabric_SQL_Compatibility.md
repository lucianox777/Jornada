# Compatibilidade SQL Database in Microsoft Fabric — v4.03

## Decisão arquitetural

A Jornada mantém **SQL Server 2022 local/Testcontainers como baseline obrigatória de desenvolvimento, CI e validação ordinária de release**. Microsoft Fabric não é requisito para build, testes automatizados ou desenvolvimento diário.

O mesmo núcleo relacional é compatível com **SQL Database in Microsoft Fabric**, sem `FabricSqlAdapter`, DDL alternativo ou duplicação de regra funcional. Em alinhamento com a Especificação Técnica v3.63 e com a arquitetura proposta pela PRODAM, SQL Database in Microsoft Fabric passa a ser o ambiente relacional operacional preferencial de HML/Produção, enquanto SQL Server 2022 Developer permanece a referência obrigatória de desenvolvimento, CI e testes independentes do Fabric.

## Evidência obtida

Em 03/09/2026 a suíte Integration foi executada contra um SQL Database in Microsoft Fabric real, usando a mesma Solution/DDL e o mesmo `OperationalSqlAdapter`:

- total: 58;
- executados: 58;
- aprovados: 58;
- falhas: 0;
- skips/notExecuted: 0;
- duração reportada pelo operador: 4 min 48 s;
- o cenário concorrente `Eight_simultaneous_processor_contenders_have_exactly_one_winner_and_gate_recovers` também passou.

Resultado consolidado: **58/58 Integration PASS, 0 falhas e 0 skips**.

A evidência demonstra **compatibilidade funcional da implementação atual**. Ela não substitui ensaios não funcionais de capacidade, desempenho, custo e operação necessários ao dimensionamento do ambiente de produção.

## Desenvolvimento e release ordinária

O gate canônico continua sendo:

```powershell
./scripts/local-validate-release.ps1
```

Ele força `JORNADA_TEST_SQL_TARGET=SQL_SERVER_2022`, usa SQL Server 2022 descartável/Testcontainers e não exige conta, workspace, capacidade, usuário ou connection string Fabric.

## Homologação Fabric

O gate Fabric é **adicional e condicional**. Só é executado quando houver banco institucional Test/Dev/Local disponibilizado para essa finalidade.
Internamente, o harness seleciona `JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE`; esse alvo não é ativado pelo gate local de desenvolvimento.

PowerShell, passando a conexão apenas para a execução:

```powershell
./scripts/fabric-sql-compatibility.ps1 -ConnectionString '<connection string Entra do banco Fabric Test/Dev/Local>'
```

Alternativamente, a conexão pode ser injetada por `JORNADA_FABRIC_SQL_CONNECTION` ou gravada localmente em:

```text
.local/fabric-sql-compatibility/connection.txt
```

`.local/` é ignorado pelo Git e **nenhuma connection string Fabric é distribuída no pacote**.

O script PowerShell usa Microsoft Entra Device Code Flow, retransmite o código de autenticação ao console, reseta o banco externo de teste antes da suíte e exige 100% dos testes executados e aprovados. O reset é protegido: somente bancos cujo nome contenha `Test`, `Dev` ou `Local` são aceitos.

## Regra de arquitetura

A aplicação continua com uma única fronteira:

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

A lógica funcional não deve conter ramificações por hospedagem. Especialização só é aceitável diante de incompatibilidade reproduzível que não possa ser resolvida no contrato comum Microsoft SQL.

## Validação não funcional

A escolha arquitetural preferencial de produção é SQL Database in Microsoft Fabric. Antes da entrada em produção, devem ser executados ensaios de capacidade, desempenho, segurança, disponibilidade e custo com carga representativa para dimensionamento e homologação operacional; esses ensaios não reabrem a bifurcação funcional do núcleo Microsoft SQL.

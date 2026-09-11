# Compatibilidade SQL Database in Microsoft Fabric — estado corrente (candidato v5.00)

## Decisão arquitetural

A Jornada mantém **SQL Server 2022 local/Testcontainers como baseline obrigatória de desenvolvimento, CI e validação ordinária de release**. O DDL canônico, o contrato de prontidão e as regressões independentes de ambiente continuam sendo exercitados nesse baseline; Microsoft Fabric não é requisito para build, testes automatizados ou desenvolvimento diário.

O mesmo núcleo relacional é compatível com **SQL Database in Microsoft Fabric**, sem `FabricSqlAdapter`, DDL alternativo ou duplicação de regra funcional. Quando houver ambiente institucional disponível e homologado para a release exata, **SQL Database in Microsoft Fabric é o destino relacional operacional preferencial de HML/Produção**, enquanto SQL Server 2022 Developer permanece a referência obrigatória de desenvolvimento, CI e validação independente do Fabric.

A expressão “Microsoft Fabric” não deve ser usada como se todos os seus recursos tivessem o mesmo papel arquitetural. Para a Jornada:

- **SQL Database in Microsoft Fabric** pode exercer o papel de banco relacional operacional de HML/Produção, condicionado à homologação da release exata;
- **Lakehouse e SQL Analytics Endpoint** permanecem no escopo analítico e não substituem implicitamente o banco relacional operacional;
- a lógica funcional permanece independente da hospedagem e continua usando o mesmo contrato Microsoft SQL.

Esta decisão de engenharia consolida a arquitetura proposta para a Jornada sem promover, por inferência, uma versão normativa ausente do repositório. O baseline histórico dos requisitos permanece rastreado nos documentos canônicos vigentes até rebaseline formal.

## Evidência histórica disponível

Em 03/09/2026 a suíte Integration foi executada contra um SQL Database in Microsoft Fabric real, usando a mesma Solution/DDL e o mesmo `OperationalSqlAdapter` da linha então vigente:

- total: 58;
- executados: 58;
- aprovados: 58;
- falhas: 0;
- skips/notExecuted: 0;
- duração reportada pelo operador: 4 min 48 s;
- o cenário concorrente `Eight_simultaneous_processor_contenders_have_exactly_one_winner_and_gate_recovers` também passou.

Resultado consolidado daquela execução: **58/58 Integration PASS, 0 falhas e 0 skips**.

Essa execução é **evidência histórica de compatibilidade da linha v4.00**. Ela não constitui, por si só, evidência de homologação do `master` atual, do SolutionSchema v3.70 ou do candidato Solution Engenharia v5.00.

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

## Gate específico antes do corte v5.00

Antes de cortar a Solution Engenharia v5.00, o harness Fabric deve ser reexecutado contra **o HEAD exato candidato à release e o SolutionSchema v3.70**. A evidência versionada deve registrar, no mínimo:

- SHA do commit exercitado;
- versão do SolutionSchema;
- data/hora da execução;
- alvo `FABRIC_SQL_DATABASE`;
- quantidade total, executada, aprovada, falhada e ignorada de testes;
- confirmação de que não houve skips silenciosos;
- identificação do arquivo TRX ou artefato equivalente preservado.

Sem essa execução corrente, a compatibilidade histórica permanece válida como antecedente, mas **a homologação Fabric da v5.00 permanece pendente**.

## Validação não funcional

A escolha arquitetural preferencial de produção é SQL Database in Microsoft Fabric. Antes da entrada em produção, devem ser executados ensaios de capacidade, desempenho, segurança, disponibilidade e custo com carga representativa para dimensionamento e homologação operacional; esses ensaios não reabrem a bifurcação funcional do núcleo Microsoft SQL.

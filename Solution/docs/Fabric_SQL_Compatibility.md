# Compatibilidade SQL Database in Microsoft Fabric — estado corrente (candidato v5.00)

## Decisão arquitetural corrente

A Jornada adota **Microsoft SQL Server como tecnologia relacional normativa e banco relacional operacional de Produção**. SQL Server 2022 Developer/Testcontainers permanece o baseline obrigatório de desenvolvimento, CI e validação ordinária de release; a edição efetiva de Produção será definida pela implantação institucional, sem transformar a edição Developer em requisito de Produção.

**SQL Database in Microsoft Fabric não é alvo operacional de Produção nem gate de release da candidata v5.00.** O harness Fabric e as evidências já produzidas permanecem úteis exclusivamente como prova de compatibilidade técnica e como histórico de engenharia. Eles não devem ser interpretados como requisito de homologação para o corte v5.00 enquanto Fabric não fizer parte do alvo operacional da release.

A expressão “Microsoft Fabric” também não deve ser usada como se todos os seus recursos tivessem o mesmo papel arquitetural. Para a Jornada:

- **Microsoft SQL Server** exerce o papel de banco relacional operacional de HML/Produção;
- **SQL Database in Microsoft Fabric** é apenas alvo opcional de compatibilidade técnica, sem autoridade operacional de Produção no desenho corrente;
- **Lakehouse e SQL Analytics Endpoint** permanecem no escopo analítico/compatibilidade e não substituem implicitamente o banco relacional operacional;
- a lógica funcional permanece independente da hospedagem e continua usando o contrato Microsoft SQL já adotado.

Esta decisão não apaga nem reinterpreta retroativamente documentos ou releases anteriores; ela define o estado arquitetural corrente da candidata v5.00.

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

Essa execução permanece como **evidência histórica de compatibilidade da linha v4.00**. Ela não constitui requisito para promoção, homologação ou release da candidata v5.00.

## Desenvolvimento e release ordinária

O gate canônico continua sendo:

```powershell
./scripts/local-validate-release.ps1
```

Ele força `JORNADA_TEST_SQL_TARGET=SQL_SERVER_2022`, usa SQL Server 2022 descartável/Testcontainers e não exige conta, workspace, capacidade, usuário ou connection string Fabric.

## Harness Fabric

O harness Fabric permanece disponível apenas para ensaios opcionais de compatibilidade técnica. Internamente, ele seleciona `JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE`; esse alvo não é ativado pelo gate local de desenvolvimento nem pelos critérios normais de corte da release.

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
                    v
          Microsoft SQL Server
             HML/Produção
```

A lógica funcional não deve conter ramificações por hospedagem. O suporte de compatibilidade Fabric não cria um segundo runtime operacional nem uma política paralela de domínio.

## Gate de corte v5.00

Não existe mais gate `FABRIC_SQL_DATABASE_EXACT_HEAD_HOMOLOGATION` para a candidata v5.00. O corte deve validar o HEAD final contra os gates correntes de SQL Server, documentação, segurança, integração, volumetria e decisões institucionais aplicáveis.

Se no futuro SQL Database in Microsoft Fabric voltar a ser proposto como alvo operacional, essa decisão deverá ser formalizada novamente e acompanhada de homologação específica no HEAD correspondente. Evidência histórica não será promovida automaticamente a evidência corrente.

## Validação não funcional

Antes da entrada em Produção em Microsoft SQL Server, devem ser executados **ensaios não funcionais** de capacidade, desempenho, segurança, disponibilidade e operação com carga representativa para dimensionamento e homologação operacional. Esses ensaios pertencem ao ambiente SQL Server efetivamente adotado para HML/Produção.

# Estado de Engenharia — v4.01

**Base Normativa:** v3.62
**SolutionSchema:** v3.68
**Solution Engenharia:** v4.01
**Natureza:** portabilidade Microsoft SQL — baseline local e homologação Fabric condicional

## Resultado consolidado

A implementação mantém um único `OperationalSqlAdapter` para SQL Server 2022 e SQL Database in Microsoft Fabric.

Em 03/09/2026 a suíte Integration foi executada externamente em SQL Database in Microsoft Fabric com **58/58 testes aprovados, 0 falhas e 0 skips**. O teste concorrente de oito contendores também passou.

## Modelo de desenvolvimento

SQL Server 2022 local/Testcontainers permanece o alvo obrigatório para desenvolvimento, CI e validação ordinária de release. O script `scripts/local-validate-release.ps1` força `SQL_SERVER_2022` e não depende de configuração Fabric.

Fabric é um alvo adicional de homologação. Sua ausência não bloqueia desenvolvimento, build ou testes ordinários. Quando houver ambiente institucional Test/Dev/Local, `scripts/fabric-sql-compatibility.ps1` ou `.sh` executa a suíte contra esse ambiente.

Nenhuma connection string Fabric é distribuída. Configuração local opcional fica sob `.local/`, ignorado pelo Git.

## Correções incorporadas

- autenticação Fabric por Microsoft Entra Device Code Flow;
- relay do Device Code do testhost para o processo PowerShell pai;
- restore/build explícito do projeto Integration antes de `dotnet test --no-build`;
- reset seguro do banco externo Test/Dev/Local antes da suíte;
- teste concorrente corrigido para manter o vencedor enquanto todos os contendores concluem a tentativa;
- validação do TRX exige 100% executado/aprovado e zero skips;
- workaround para `ExitCode` nulo em Windows PowerShell após `Start-Process`;
- gate Fabric sem Python.

## Decisão de produção

A compatibilidade funcional está comprovada, mas a plataforma de produção não é congelada nesta release. A escolha entre SQL Server e SQL Database in Microsoft Fabric deverá ser fundamentada em avaliação técnica comparativa com carga representativa, considerando desempenho, escalabilidade, disponibilidade, custo, segurança e operação.

## Limite da evidência

A execução Fabric recebida não é benchmark de performance/custo. O ambiente de empacotamento da v4.01 também não possui .NET/Docker/PowerShell, portanto a release consolidada ainda requer reexecução local do validador canônico para fechar sua evidência byte-a-byte.

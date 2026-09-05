# Estado de Engenharia - v4.02

**Base Normativa:** v3.63  
**Schema persistido:** Base 3.62 / SolutionSchema v3.68  
**Solution Engenharia:** v4.02  
**Data:** 04/09/2026  
**Natureza:** alinhamento normativo/arquitetural com Microsoft Fabric, sem mudanca funcional de runtime

## Resultado consolidado

A v4.02 incorpora a Especificacao Tecnica v3.63, atualizada para maior aderencia a arquitetura proposta pela PRODAM para utilizacao do Microsoft Fabric.

A arquitetura passa a registrar SQL Database in Microsoft Fabric como ambiente relacional operacional preferencial de HML/Producao, mantendo SQL Analytics Endpoint/OneLake no caminho analitico e SQL Server 2022 Developer como referencia de desenvolvimento, CI e testes independentes do Fabric.

## Compatibilidade e implementacao

A implementacao continua com um unico `OperationalSqlAdapter`, o mesmo DDL, procedures, constraints, transacoes e codigo `Microsoft.Data.SqlClient` para SQL Server 2022 e SQL Database in Microsoft Fabric. Nao ha branch funcional por hospedagem.

A mudanca v3.63 nao altera o DDL nem o SolutionSchema. Por isso, o marcador persistido permanece Base 3.62 / SolutionSchema 3.68.

## Evidencia preservada

A linha predecessora confirmou 58/58 testes Integration em SQL Database in Microsoft Fabric, 0 falhas e 0 skips, inclusive o cenario concorrente de oito contendores. A validacao local SQL Server predecessora tambem permanece como evidencia historica.

## Limite da evidencia

O ambiente de empacotamento da v4.02 nao possui .NET, Docker ou PowerShell. A release requer reexecucao local de `scripts/local-validate-release.ps1` para fechar a evidencia runtime byte-a-byte da nova distribuicao.

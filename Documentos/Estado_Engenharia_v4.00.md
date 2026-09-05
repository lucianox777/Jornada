# Estado de Engenharia — v4.00

**Base Normativa:** v3.62
**SolutionSchema:** v3.68
**Solution Engenharia:** v4.00
**Natureza:** harness de compatibilidade — SQL Database in Microsoft Fabric

## Fechamento do predecessor

A v3.99 foi revalidada externamente em 03/09/2026 pelo script canônico `2026.09.03-v3.99`. Restore locked da Solution/Unit/Integration, builds, Unit, Docker, imagem SQL/digest, SQL engine + CHECKDB e Integration foram reportados como **OK**.

Isso fecha a pendência de runtime declarada na v3.99 e permite tratar seus `packages.lock.json` como grafo predecessor externamente verificado. A síntese recebida não informa contagens de testes; a v4.00 não inventa esse detalhe.

## Implementação desta versão

A v4.00 prepara o experimento Fabric sem bifurcar a aplicação:

- `JORNADA_TEST_SQL_TARGET=SQL_SERVER_2022` permanece padrão;
- `JORNADA_TEST_SQL_TARGET=FABRIC_SQL_DATABASE` seleciona o experimento;
- Fabric exige `JORNADA_TEST_SQL_USE_EXISTING_DATABASE=true` e connection string para um banco dedicado Test/Dev/Local;
- a fixture desabilita pooling também no banco externo;
- não há CREATE/DROP do item Fabric no harness;
- a verificação de major version 16 é exclusiva da baseline SQL Server 2022;
- DDL, seed e demais testes Integration permanecem os mesmos;
- scripts PowerShell e POSIX geram TRX/summary e proíbem skips.

## Fronteira de arquitetura

`OperationalSqlAdapter` continua sendo a única implementação. A v4.00 não adiciona `FabricSqlAdapter` nem branches funcionais por hospedagem. O objetivo é descobrir primeiro se existe incompatibilidade real.

## Validação

O ambiente de empacotamento não possui .NET, Docker ou PowerShell. Foram executadas apenas verificações estáticas disponíveis.

A promoção técnica da v4.00 pede duas evidências separadas:

1. baseline SQL Server: `scripts/local-validate-release.ps1`;
2. compatibilidade Fabric: `scripts/fabric-sql-compatibility.ps1` ou `.sh` contra banco Fabric dedicado.

Somente falha reproduzível no segundo gate pode justificar especialização do Adapter.

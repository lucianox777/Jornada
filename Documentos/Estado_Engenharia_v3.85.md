# Estado da Engenharia — v3.85

**Base Normativa:** v3.62  
**Solution Engenharia:** v3.85  
**Data:** 02/09/2026

## Escopo desta release

Correção de engenharia derivada da primeira execução real da v3.84. Não altera Base Normativa, SolutionSchema, DDL, contratos públicos nem dependências NuGet.

- Corrige 54 erros CS0122 causados pelo novo assembly `Jornada.Integration.Tests` não estar autorizado a acessar internals usados pela suíte.
- Mantém os tipos internos; adiciona `InternalsVisibleTo("Jornada.Integration.Tests")` somente em Api, Processor.Worker e Bronze.Maintenance.Worker.
- Corrige perda de argumentos em helpers PowerShell que usavam `$Args`, variável automática case-insensitive do PowerShell.
- `local-db.ps1` passa a validar Docker Engine, executar `docker compose up -d sqlserver` com argumentos preservados e diagnosticar ausência/saída prematura do container.
- O mesmo padrão é corrigido em `local-backup-restore-drill.ps1` e `local-scale.ps1`.
- `technical-closure-gate.py` passa a falhar se os friend assemblies necessários desaparecerem ou se scripts locais voltarem a declarar `[string[]]$Args`.

## Evidência recebida da v3.84

- Compilação no Visual Studio 2022 alcançou `Jornada.Integration.Tests` e apresentou 54 CS0122 em internals de produção.
- Execução de `local-db.ps1 -Action up` mostrou `docker compose` sem subcomando e, depois, `No such object: jornada-sqlserver-local`, compatível com perda do array de argumentos.

Esses resultados são evidência de falha da v3.84, não de sucesso da v3.85.

## Estado de validação v3.85

- Gates estáticos: executáveis no ambiente de empacotamento.
- .NET/Docker runtime: não disponíveis no ambiente de empacotamento.
- NuGet locks: byte-a-byte inalterados frente à v3.84; dois permanecem `PENDING_TRUSTED_DOTNET_RESTORE`.

## Critério de fechamento

Executar externamente, nesta ordem: `dotnet restore Jornada.sln --locked-mode`, build Release com warnings como erro, Unit, Integration, FaultInjection e os scripts locais relevantes com Docker SQL Server 2022. A release só se torna runtime-validada após essas etapas passarem sem skips/falhas indevidas.

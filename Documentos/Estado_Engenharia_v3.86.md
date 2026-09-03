# Estado da Engenharia — v3.86

**Base Normativa:** v3.62  
**Solution Engenharia:** v3.86  
**Data:** 02/09/2026

## Escopo desta release

Correção de engenharia derivada da execução real da v3.85. Não altera Base Normativa, SolutionSchema, DDL, contratos públicos, projetos C# nem dependências NuGet.

- Mantém as correções v3.85 de friend assembly e encaminhamento de argumentos PowerShell.
- Corrige `Wait-Healthy` em `local-db.ps1`: `docker compose ps` usa `--format json`, interpretado por `ConvertFrom-Json`.
- Remove dependência do template Go customizado que falhou na versão de Docker Compose observada no Windows.
- Adiciona diagnóstico fail-closed para JSON inválido, container ausente, `unhealthy`, saída prematura e timeout.
- `technical-closure-gate.py` protege o uso do caminho JSON e rejeita regressão para o template incompatível.

## Evidência recebida da v3.85

- Docker/Compose baixou SQL Server 2022, criou rede e volume e iniciou `jornada-sqlserver-local`.
- O acompanhamento de health falhou com `format value "{{.Name}}|{{.State}}|{{.Health}}" could not be parsed`.

Isso comprova que a criação do container funcionou e isola a falha no parser de formato do comando `docker compose ps`; não constitui ainda validação completa de bootstrap/testes da v3.85.

## Estado de validação v3.86

- Gates estáticos: executáveis no ambiente de empacotamento.
- .NET/Docker/PowerShell runtime: não disponíveis no ambiente de empacotamento.
- NuGet locks: byte-a-byte inalterados frente à v3.85; dois permanecem `PENDING_TRUSTED_DOTNET_RESTORE`.

## Critério de fechamento

Reexecutar externamente `local-db.ps1 -Action up`; depois `dotnet restore Jornada.sln --locked-mode`, build Release com warnings como erro, Unit, Integration, FaultInjection e demais ensaios técnicos pertinentes.

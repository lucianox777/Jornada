# Evidência de execução externa — v3.87 — 02/09/2026

Registro resumido de execução fornecida pelo usuário durante a validação da release.

- `dotnet restore Jornada.sln --locked-mode`: todos os projetos reportados como atualizados.
- `dotnet build Jornada.sln --configuration Release --no-restore -warnaserror`: **Build succeeded**, 0 warnings, 0 errors.
- `Jornada.Tests`: **153/153 PASS**, 0 failed, 0 skipped.
- `Jornada.Integration.Tests`: **0/58 executados com sucesso; 58 falhas de OneTimeSetUp**. A causa repetida foi Testcontainers/Docker API: cliente 1.44 contra servidor máximo 1.41.
- O bootstrap SQL local da v3.87 havia concluído e exibido apenas warning de chave máxima do índice `IX_bronze_entrega_arquivo_objeto_chave` (2048 bytes potenciais > 1700).

Esta evidência não declara os testes Integration como funcionalmente falhos: os casos não chegaram a iniciar por falha de infraestrutura no setup. A v3.88 corrige o preflight da API Docker e o índice físico Bronze.

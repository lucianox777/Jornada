# Estado de Engenharia — v3.90

- Base Normativa: **v3.62**
- SolutionSchema: **v3.68**
- Solution Engenharia: **v3.90**
- Predecessora: **v3.89**

## Mudança

Hardening de `scripts/local-clean.ps1`: `.vs` é cache não normativo e agora é removido em best-effort. Arquivo bloqueado por Visual Studio/Copilot/ServiceHub não aborta a limpeza. `bin/obj` e os demais artefatos relevantes permanecem estritos.

`local-validate-release.ps1` continua sendo o único validador canônico. Não existe `local-validate-release-r4.ps1` na release.

## Runtime

A v3.89 revelou o lock real em `CopilotIndices/CodeChunks.db`. A v3.90 ainda requer nova execução externa de limpeza + validação antes de qualquer declaração de fechamento runtime.

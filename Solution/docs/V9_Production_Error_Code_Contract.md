# V9 — contrato de códigos de erro de produção

Em Development, a exceção completa (incluindo exceções internas e stack trace) é emitida no canal de diagnóstico configurado, além do código estável. Em HML, a exceção completa segue as mesmas variáveis de ambiente já existentes (`DOTNET_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT`, `JORNADA_RUNTIME_MODE`), sem variável adicional de opt-in. Em Production, apenas códigos sanitizados são emitidos.

Formato do código: `V9_CALIBRATOR_FAILED:<FAMILIA>:<CATEGORIA>[:<MOTIVO>]`.

- `FAMILIA`: `INPUT_NOT_FOUND`, `INVALID_DATA`, `IO_ERROR`, `INVALID_ARGUMENTS`, `UNEXPECTED`.
- `CATEGORIA`: resultado da lista finita `V9CalibradorSafeFailureCategory.Classify`, ou `UNCLASSIFIED`.
- `MOTIVO`: apenas o motivo permitido por `PhysicalSqlProbeReason`, quando disponível; valores não classificados indicam a última subfase conhecida, **não** uma causa comprovada.

A saída padrão de erro e o `calibrador.log` reservado (10 KiB) recebem o identificador. `interrupted.json` conserva os campos de estágio e categoria já existentes. Nenhum deles deve conter `Exception.Message`, `Exception.ToString()`, stack trace, SQL, credenciais ou dados pessoais em produção.

**Correlação de execução:** usar o identificador externo da campanha (`GITHUB_RUN_ID`/`GITHUB_RUN_ATTEMPT`) ou o identificador da execução do serviço que já acompanha o registro; não introduzir dados arbitrários no código estável. O contrato não prova que a falha 100K anterior foi corrigida.

**Segurança operacional:** habilitar DEV/HML com diagnóstico integral apenas em destinos de logs com acesso restrito. A presença de `GITHUB_ACTIONS` não altera a classificação de ambiente; tampouco torna públicos logs integrais automaticamente seguros.

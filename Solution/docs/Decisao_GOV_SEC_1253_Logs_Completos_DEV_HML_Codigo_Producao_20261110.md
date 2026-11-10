# GOV-SEC-1253-LOGS-DEV-HML-PROD-20261110 — diagnóstico integral restrito por ambiente

**Autoridade:** Product Owner, 10/11/2026. **Política:** GOV-DOC-01. **Rastreabilidade:** incidente [#1253](https://github.com/lucianox777/Jornada/issues/1253), implementação da política V9, e índices oficiais de decisões e acervo.

## Decisão do PO

- **Desenvolvimento:** disponibilizar informação diagnóstica completa (tipo e texto da exceção, inner exceptions e stack trace), exclusivamente em terminal/arquivo local privado e sob controle do operador. Não publicar indiscriminadamente em CI, GitHub Actions ou anexos públicos.
- **Homologação:** disponibilizar o mesmo diagnóstico completo **a operadores autorizados**, em destino de logs privado, com controle de acesso, retenção e segregação operacional. A configuração explícita `JORNADA_PRIVATE_DIAGNOSTICS=1` habilita esse comportamento apenas se `DOTNET_ENVIRONMENT` identifica `Staging`, `Homologation`, `Homologacao` ou `HML`; `ASPNETCORE_ENVIRONMENT` e `JORNADA_RUNTIME_MODE` não podem contradizer o ambiente. Não definir essa flag antes de proteger o destino e sua retenção.
- **Produção:** devolver ao consumidor/operador somente **código de erro estável e identificável** (ex.: `V9_CALIBRATOR_FAILED:INVALID_DATA`, `INPUT_NOT_FOUND`, `IO_ERROR`, `INVALID_ARGUMENTS`, `UNEXPECTED`). Nunca revelar stack trace, texto de exceção, SQL, conexão, segredo ou dados pessoais na resposta pública. Falha de logging não pode substituir erro original.
- **GitHub Actions e CI, independentemente de ambiente alegado:** sempre desabilitar a impressão integral de exceções. O arquivo `calibrador.log` de 10 KiB pré-reservado conserva somente o código seguro em **todos os ambientes** para evitar exposição por `upload-artifact` e operação sob disco cheio.

## Contexto, justificativa e precedência

A regra anterior [GOV-V9-100K-SQL-PY-OPTOUT-20261109](Decisao_V9_100K_OptOut_Orcamento_SQL_Python_20261109.md) limitava a impressão integral de exceção ao desenvolvimento local privado. **Esta decisão superveniente do PO amplia a cobertura à homologação privada**, sem revogar os controles de sigilo nem a política de CI/produção. O incidente #1253 registra exposição histórica de senha em logs públicos: não confundir log detalhado para diagnóstico autorizado com publicação sem proteção. Logs integrais podem conter credenciais e dados sensíveis; limitar acesso, transporte, retenção e compartilhamento, usar dados sintéticos nos testes.

## Implementação e verificação

`V9WorkerDevelopmentDiagnostics.Enabled` resolve os ambientes por configuração explícita, bloqueia valores `Production/Prod` contraditórios e os indicadores `CI/GITHUB_ACTIONS`, habilita desenvolvimento local compatível com a regra anterior e exige opt-in de destino privado para homologação. `V9NestedCalibrationCli.TryWriteLastError` mantém os códigos públicos `SafeErrorCode` e invoca a saída integral somente após esse gate. Os testes cobrem DEV, HML explícita, HML sem autorização, contradições, CI e produção, além da proteção do log reservado.

**Limitação e governança:** este commit não comprova que a infraestrutura de homologação tem armazenamento/acesso/retention apropriados; a flag só deve ser habilitada pelo operador após essa confirmação. O incidente #1253 permanece **ABERTO** por logs históricos e eventual reutilização de credenciais; esta política não autoriza apagar logs, dar `security_preflight_approved=true` nem executar 24×100k. Não altera ciência, TF, M/U, FDR, SQL, Docker, TEST, Gold ou resultados.

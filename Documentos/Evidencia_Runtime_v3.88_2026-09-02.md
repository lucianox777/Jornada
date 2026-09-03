# Evidência de Runtime — v3.88 — 2026-09-02

Fonte: execução real em Windows/.NET 8.0.424/Docker reportada pelo operador.

## Validação de build e Unit

- `dotnet restore Jornada.sln --locked-mode`: OK.
- restore explícito `Jornada.Tests`: OK.
- restore explícito `Jornada.Integration.Tests`: OK.
- build Release da Solution: 0 warnings / 0 errors.
- build explícito `Jornada.Tests`: 0 warnings / 0 errors.
- Unit: 153/153 PASS.
- Docker Engine: acessível.
- build explícito `Jornada.Integration.Tests`: 0 warnings / 0 errors.

## Integration

A suíte foi iniciada e todos os 58 casos foram descobertos/executados pelo runner:

- PASS: 5
- FAIL: 53
- SKIP: 0

As 53 falhas observadas têm a mesma família de mensagem:

- `Banco de integração deve conter Test, Dev ou Local.`
- `Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.`
- `Banco de integração deve ser Test/Dev/Local.`

Diagnóstico: a fixture v3.88 criou banco isolado com prefixo `JornadaIntegration_<guid>`. Esse nome não contém os tokens exigidos pelos guards fail-closed dos próprios testes. A infraestrutura Docker/Testcontainers já havia avançado além do problema de API 1.44/1.41.

## Consequência para v3.89

A correção deve alterar a origem do nome para `JornadaIntegrationTest_<guid>`, sem relaxar os guards. A aprovação funcional da Integration permanece pendente até nova execução real da v3.89.

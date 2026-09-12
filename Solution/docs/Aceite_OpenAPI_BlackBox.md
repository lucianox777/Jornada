# Aceite funcional OpenAPI em caixa-preta

## Objetivo

O aceite funcional portátil da API não deve depender da implementação interna da Jornada, do `Program.cs`, do banco SQL ou do processo usado pelo fornecedor para hospedar a aplicação. O alvo do ensaio é somente uma URL HTTP acessível e o contrato publicado `openapi/jornada-v1.openapi.json`.

`OpenApiRuntimeConformanceTests` possui dois modos:

- **interno / antirregressão**: sem `JORNADA_ACCEPTANCE_BASE_URL`, o teste sobe a `Jornada.Api` com `WebApplicationFactory`, como já ocorria no CI;
- **externo / caixa-preta**: com `JORNADA_ACCEPTANCE_BASE_URL`, o teste não cria `WebApplicationFactory` e envia os mesmos probes HTTP para a implementação indicada.

O catálogo cobre exatamente uma vez todas as 19 operações publicadas. Para cada operação, a resposta observada precisa usar um status declarado no OpenAPI; respostas com corpo precisam declarar `Content-Type`, ser JSON bem-formado e usar mídia compatível com a resposta declarada quando o contrato publica `content`.

## Execução contra implementação externa

PowerShell:

```powershell
$env:JORNADA_ACCEPTANCE_BASE_URL = "https://host-hml-exemplo"
dotnet test .\tests\Jornada.Tests\Jornada.Tests.csproj `
  --configuration Release `
  --filter "TestCategory=OpenApiRuntime" `
  --logger "trx;LogFileName=openapi-blackbox.trx"
```

POSIX:

```bash
JORNADA_ACCEPTANCE_BASE_URL="https://host-hml-exemplo" \
  dotnet test ./tests/Jornada.Tests/Jornada.Tests.csproj \
  --configuration Release \
  --filter 'TestCategory=OpenApiRuntime' \
  --logger 'trx;LogFileName=openapi-blackbox.trx'
```

A URL deve apontar para um ambiente preparado para aceite. O teste usa valores sintéticos e UUIDs sentinela, mas algumas operações são mutáveis; portanto o ensaio não deve ser disparado contra Produção sem procedimento operacional específico.

## Evidência e interpretação

O arquivo TRX é a evidência reproduzível do ensaio. Aprovação exige zero falhas e zero operações ausentes do catálogo. Um status HTTP não declarado no contrato é falha, mesmo que a implementação o considere tecnicamente razoável.

Este gate mede **conformidade da superfície HTTP observável**. Ele não substitui os ensaios E2E de persistência, DDL, segurança/autorização corporativa, volumetria HML, Fabric ou readiness de Produção.

## Separação em relação aos gates textuais

`openapi-contract-gate.py` continua válido como proteção de regressão da implementação própria: ele compara o OpenAPI com as rotas registradas no código da Jornada. Esse gate não deve ser usado como critério de aceite de uma implementação de terceiro, porque inspeciona estrutura interna.

Para fornecedor/terceiro, o critério portátil é o modo externo de `OpenApiRuntimeConformanceTests`, exercendo somente HTTP contra o contrato publicado.

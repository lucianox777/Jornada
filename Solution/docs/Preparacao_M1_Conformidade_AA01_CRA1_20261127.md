# Preparação M1 — conformidade factual AA01/CRA1 — 27/11/2026

**Natureza:** evidência técnica auxiliar da preparação autorizada; NÃO é nova decisão do PO, novo gate ou certificação do produto.
**Escopo:** [GOV-M1-ESCOPO-AA01-CRA1-20261127](Decisao_GOV_M1_Escopo_AA01_CRA1_20261127.md), somente AA01 v1 e CRA1 v1. GESTOR, schemas, modelos, permissões e prioridade da Fase A preservados.
**Base consultada:** `250aedc5f71aec2fd54f7af80b404450d800ce08`.

## Trabalho executado

Reaberto o kit rev07, SHA-256 `14b085a9416218a5010459a180a4439e9e766f53b07f1b86640841a7e3bb2f47`, com CRC íntegro. Preparado corpus separado com 141 casos de conformidade JSON Schema: 84 AA01 e 57 CRA1, sendo 44 aceitações e 97 rejeições esperadas. Cada negativa deve reprovar pela regra indicada, não apenas produzir um erro qualquer. Nenhum ZIP original, schema ou código do produto foi modificado.

Cobertura: propriedades obrigatórias, tipos, enum de operações, limites de comprimento, propriedades adicionais, datas, valores anuláveis, condicionais de vigência/motivo de AA01 e dataHoraServico/opcionais de CRA1. As expectativas foram declaradas a partir dos schemas, não escolhidas após observar o resultado.

**Medição realizada:** os 141 casos concordaram com as expectativas no validador Python jsonschema 4.26.0, Draft 2020-12, com FormatChecker e sem resolução remota. Reexecução com Python -O produziu o mesmo recibo. Foram aprovados 20 autotestes do avaliador, os mesmos 20 sob -O, e reexecutados 56 autotestes em memória do probe AA01/CRA1 anterior. Não somar reexecuções como cobertura adicional. Os quatro registros originais dos dois exemplos passaram novamente na verificação offline já existente.

## Pins

- AA01: `Solution/config/contracts/registros/AA01/v1/registro.schema.json`, SHA-256 `8fa7723753cb083a8be6dc1bf083f24dac41dddd55e731be00f113b4313e616d`.
- CRA1: `Solution/config/contracts/registros/CRA1/v1/registro.schema.json`, SHA-256 `c6342a5fc5486d1e180b2dd8bd9e5b2e02fc1c8562ed6c84968e1da872612154`.
- Corpus auxiliar: `fatos-aa01-cra1.json`, SHA-256 `8f28fecffba30ab539ad5abfbac7e4e306a7c12b858fcaebc1af2cc98a7a28b1`.
- Validador do produto consultado: `Solution/src/Jornada.Ingestion/JsonSchemaSubsetValidator.cs`, blob `700922fee756589cebf216b02429ba1895619869`.
- Projeto de testes consultado: `Solution/tests/Jornada.Tests/Jornada.Tests.csproj`, blob `32fbe35b37d16a1e7e98092f7005bcc32fbe1037`.

## Não confundir schema com regra de negócio

Quatro caracterizações separadas demonstraram somente que os schemas examinados não impõem: mínimo para valorConcedido; ordem entre dataFim/dataInicio; enum para situacao de CRA1; existência de idPessoaEntrega em outra linha. A aceitação desses valores pelo schema isolado NÃO autoriza publicação, remove validação do Processor ou comprova aceitação de negócio. Não foram propostas mudanças contratuais por esses resultados.

O validador .NET usa DateTimeOffset.TryParse para date-time. Foram preparados casos CRA1 sem hora/sem fuso para confronto com a verificação de formato do validador de referência. Não foi executado .NET; portanto nenhuma divergência no runtime foi demonstrada. Caso apareça, registrar a evidência e reconciliar o contrato antes de corrigir, sem mudar expectativas apenas para obter verde.

## Testes de produto preparados, não executados

Patch candidato no kit com dois arquivos novos: teste NUnit `M1Aa01Cra1FactualContractConformanceTests.cs` e corpus em `Solution/tests/fixtures/m1-aa01-cra1/`. Usa JsonSchemaValidatorCache/JsonSchemaSubsetValidator reais e os schemas existentes, sem reimplementar a política. Não modifica csproj, dependências, workflow, proteção ou produto. Aplicação conferida somente em Git temporário vazio, não no checkout real.

.NET e Docker não estão disponíveis no ambiente desta preparação; tentativa de consultar metadados do SDK para instalação local isolada foi impedida por resolução de rede. Nenhum SDK foi instalado. Não houve acesso ao WSL do PO. O SDK fixado pelo repositório permanece 10.0.113, sem substituição.

**Estados:** preparação auxiliar e avaliação offline REALIZADAS; teste NUnit NÃO COMPILADO/NÃO EXECUTADO; HTTP/SQL/catálogo/Gold/instalação/autorização por recurso NÃO VERIFICADOS nesta revisão. Nenhum API_INTEGRACAO_DEV_PASS ou E2E PASS.

## Incorporação e rastreabilidade

Artefatos de teste distribuídos no kit do chat, não incorporados à branch do produto. Este documento factual está em branch documental separada. Sincronização dos índices e integração protegida permanecem pendentes, conforme GOV-DOC-01; não declarar conclusão da incorporação documental. Não interromper a Fase A ou publicar testes concorrentes na #1940 por este registro.

Referências: [#1194](https://github.com/lucianox777/Jornada/issues/1194), [#1853](https://github.com/lucianox777/Jornada/issues/1853), [#1855](https://github.com/lucianox777/Jornada/issues/1855), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md). Continuidade de preparação solicitada pelo PO no chat, preservando o escopo factual decidido.

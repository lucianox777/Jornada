# Preparação M1 — conformidade factual AA01/CRA1 — 27/11/2026

**Natureza:** evidência técnica auxiliar da preparação autorizada; NÃO é nova decisão do PO, novo gate ou certificação do produto.
**Escopo:** [GOV-M1-ESCOPO-AA01-CRA1-20211127](Decisao_GOV_M1_Escopo_AA01_CRA1_20261127.md), somente AA01 v1 e CRA1 v1. GESTOR, schemas, modelos, permissões e prioridade da Fase A preservados.
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

Quatro caracterizações separadas demonstraram somente que os schemas examinados não impõem: mínimo para valorConcedido; ordem entre dataFim/dataInicio; enum textual para situacao de CRA1; existência de idPessoaEntrega em outra linha. A aceitação desses valores pelo schema isolado NÃO autoriza publicação, remove validação do Processor ou comprova aceitação de negócio. Não foram propostas mudanças contratuais por esses resultados.

O validador .NET usa DateTimeOffset.TryParse para date-time. Foram preparados casos CRA1 sem hora/sem fuso para confronto com a verificação de formato do validador de referência. Não foi executado .NET; portanto nenhuma divergência no runtime foi demonstrada. Caso apareça, registrar a evidência e reconciliar o contrato antes de corrigir, sem mudar expectativas apenas para obter verde.

## Testes de produto preparados, não executados

Patch candidato no kit com dois arquivos novos: teste NUnit `M1Aa01Cra1FactualContractConformanceTests.cs` e corpus em `Solution/tests/fixtures/m1-aa01-cra1/`. Usa JsonSchemaValidatorCache/JsonSchemaSubsetValidator reais e os schemas existentes, sem reimplementar a política. Não modifica csproj, dependências, workflow, proteção ou produto. Aplicação conferida somente em Git temporário vazio, não no checkout real.

.NET e Docker não estão disponíveis no ambiente desta preparação; tentativa de consultar metadados do SDK para instalação local isolada foi impedida por resolução de rede. Nenhum SDK foi instalado. Não houve acesso ao WSL do PO. O SDK fixado pelo repositório permanece 10.0.113, sem substituição.

**Estados:** preparação auxiliar e avaliação offline REALIZADAS; teste NUnit NÃO COMPILADO/NÃO EXECUTADO; HTTP/SQL/catálogo/Gold/instalação/autorização por recurso NÃO VERIFICADOS nesta revisão. Nenhum API_INTEGRACAO_DEV_PASS ou E2E PASS.

## Incorporação e rastreabilidade

Artefatos de teste distribuídos no kit do chat, não incorporados à branch do produto. Este documento factual está em branch documental separada. Os apontadores em ambos os índices foram incluídos na PR documental #1946; revisão, checks aplicáveis e integração protegida permanecem pendentes, conforme GOV-DOC-01; não declarar conclusão da incorporação documental. Não interromper a Fase A ou publicar testes concorrentes na #1940 por este registro.

Referências: [#1194](https://github.com/lucianox777/Jornada/issues/1194), [#1853](https://github.com/lucianox777/Jornada/issues/1853), [#1855](https://github.com/lucianox777/Jornada/issues/1855), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md). Continuidade de preparação solicitada pelo PO no chat, preservando o escopo factual decidido.

## Repasse ao executor — seleção dos materiais auxiliares

A revisão 0.8 do kit entregue no chat (SHA-256 `2075c5fda096908a8d498c8f3321e5e0370301bdde2341d33c0d06822795b269`) contém múltiplas revisões históricas. Para evitar aplicação cumulativa ou uso do probe de quatro recursos, selecionar os artefatos vigentes: `conformidade_registros/conformidade-registros-aa01-cra1.patch` (corpus e NUnit factual), `escopo_aa01_cra1/` (probe de 30 casos e configuração desabilitada) com a dependência original `testes_1194/patch_files/Solution/scripts/m1-resource-authorization-probe.py`, e a classe NUnit de autorização em `testes_1194/patch_files/Solution/tests/Jornada.Tests/Unit/IngestionTypeAuthorization1194Tests.cs`. O probe amplo e seu exemplo de configuração não constituem o modo ativo. Não aplicar o patch amplo indiscriminadamente. A classe de autorização tem positivos esperados RED na base anterior à #1194; revisar e executar junto ao ciclo autorizado de implementação, não mesclar isoladamente.

Reconciliar a master e o mandato no início do M1. Aplicar patches somente após revisão do diff e proveniência. Primeiro compilar e medir o validador real (sem ajustar contrato para tornar verde), depois proteger autorização/envio/replay/status/resultado e provar os controles GESTOR. Preparar ambiente isolado, reconciliando as guardas de banco e contexto de build já identificadas; não executar no `jornada-local`. Usar apenas AA01 e CRA1 como fluxos factuais, sem classificar como PASS os negativos que não podem ser isolados nesses dois recursos. Preservar a prova HTTP/SQL até Gold de fatos, sem substituir por testes offline. Esta orientação de aproveitamento não altera a ordem do mandato nem constitui aprovação de nova política.

## Preparação posterior — contexto Git sem copiar a worktree

**Continuidade de 27/11/2026, sem nova decisão.** Reaberto e conferido o pacote consolidado `Jornada_M1_Repasse_AA01_CRA1_20261127.zip`, SHA-256 `8e67580e2461585f9a19d2bc37a888523513eb8ebd29a1663c3509089707f8f7`. A master consultada permanece na base acima. O Dockerfile integrado contém `COPY . .` e o `.dockerignore` consultado não exclui `.env`; isso motiva a preparação de uma exportação limpa, sem afirmar que houve vazamento.

Preparado utilitário auxiliar offline que lê somente os objetos Git de `Solution/` no commit completo informado. Não copia a worktree nem o índice, não executa checkout/reset/archive/filtros, Docker, SQL ou comandos do projeto. Gera diretório novo, sem sobrescrita, e recibo externo ao contexto. Verifica bytes, modos, conjunto de arquivos e hashes novamente contra o commit local; um manifesto adulterado junto com o arquivo não substitui a referência Git. Recusa links, submódulos, ponteiros LFS, caminhos anômalos, classes de arquivos sensíveis e resíduos versionados. O exemplo público `.env.example` só é admitido pelo blob fixado; nenhum `.env` privado é autorizado. Não há opção de remover guardas ou ignorar uma recusa.

**Executado nesta preparação:** 53 testes com repositórios Git temporários sintéticos, aprovados em Python normal e os mesmos 53 sob `-O`, sem contagem duplicada. Foram exercitados checkout sujo/staged sem alteração da origem, HEAD diferente do SHA solicitado, `.env` não rastreado, filtros inertes, objetos replace ignorados, symlinks/submódulos/LFS, sobrescrita recusada e adulterações de arquivos/recibo. A primeira execução expôs somente um rótulo genérico indevido na rejeição de chave JSON duplicada; o tratamento foi corrigido e toda a bateria repetida. Revalidados o manifesto de 56 arquivos do pacote anterior, os quatro registros originais e os 141 casos factuais offline.

**Limites:** não exportada a árvore integral real do Jornada, não construída imagem, não executados .NET/HTTP/SQL. O utilitário bloqueia classes de arquivos pelo nome, mas NÃO é scanner integral de segredos: segredo já versionado em nome comum exige revisão independente. Proveniência remota, bind mounts e isolamento do runtime não são certificados. Os recibos mantêm `executionAuthorized=false`, `safeForBuild=false`, `runtimeIsolation=NOT_VERIFIED` e `apiIntegracaoDevPass=false`. Preservados o conflito JornadaE2E/codex-, a pendência #1194 e o HTTPS do Apoio. A árvore de fontes preserva os componentes do produto; os ensaios continuam restritos a AA01/CRA1.

Fontes conferidas: Dockerfile blob `384952244972c43ee9be53fee300b7e958b0afa7`, `.dockerignore` blob `85825cc41cf51125c85233442827c6e81b1d5ffa`, `.env.example` blob `51818d5982610dab8824e886558080c21b863a0b`. Código auxiliar e resultados entregues no pacote do chat, não adicionados à PR documental. Esta atualização preserva os apontadores já sincronizados; integração protegida da #1946 permanece pendente.

## Preparação posterior — conferência factual por Entrega

**Continuidade de 27/11/2026, sem nova decisão ou gate.** Reaberto o pacote com contexto Git, SHA-256 `5fb64a93a529c9514a23f27a78cee2033027b3004e7998d2f48e33a70864770d`; CRC conferido. A base continua `250aedc5f71aec2fd54f7af80b404450d800ce08`.

Preparadas duas consultas SQL parametrizadas somente leitura, uma por fixture AA01/CRA1, e um conferidor offline das capturas. Reaproveitam os relacionamentos do `local-e2e.sh`, do `v9-e2e-1k-factual.py`, da carga de lotes do Processor e da Resultado.Api. Não executam nem exigem Runner V9, não alteram Produto, GESTOR, schemas, permissões, modelos ou prioridade da Fase A.

O recorte é a primeira Entrega dos dois ZIPs originais em banco descartável sem os fatos preexistentes, após PROCESSADA e antes de nova Entrega lógica, retenção ou mutação concorrente. Cada consulta retorna 17 contagens de presença e correspondência: envelope/recibo, referência Bronze, hashes cadastral/factual, lote processado, Pessoa e fato Silver, fato Gold, ausência de fato na outra natureza e registro integrado em Serving. A correlação usa entrega_id, lote_id e registro_observacao_id, não apenas o código do fato. Confere os campos de negócio declarados, proveniência e referências nullable; não inventa codigoPessoaOrigem no exemplo AA01. Destino esperado, autorização externa e janela de recebimento são parâmetros obrigatórios. Sem SQL dinâmico, DML, reset, NOLOCK ou nova infraestrutura.

**Medição realizada nesta preparação:** 30 testes offline do conferidor com capturas artificiais aprovados, e os mesmos 30 sob Python -O. Incluem alteração de cada contador, Entrega/tentativa divergentes, hash/bytes/query divergentes, dados antigos, campos ausentes, JSON duplicado, valores não inteiros e HTTP 403 em todos os positivos. O template incompleto é recusado antes de qualquer rede; nenhum código desta ferramenta abre conexão. A primeira bateria revelou somente uma asserção estática que procurava alias inexistente na CTE; o teste foi corrigido para conferir o filtro real, sem alterar a consulta ou enfraquecer o isolamento. Reexecutados 56 autotestes em memória do probe anterior, os 141 casos factuais e quatro registros originais offline.

**Limites:** consultas NÃO EXECUTADAS nem compiladas pelo SQL Server; revisão lexical de instruções não comprova comportamento SQL. Não há prova nova HTTP/SQL/Gold/.NET/instalação. Conferência da referência Bronze não é releitura dos bytes do objeto; prova cadastral integral, autorização por recurso, concorrência, efeitos SQL das negativas, replay completo, cliente Apoio e identidade V9 continuam pendentes. O conferidor valida consistência do material fornecido, mas não autentica sua origem ou o SHA do runtime: retorna `INPUT_CONSISTENT_NOT_EXECUTION_CERTIFICATION`, com `sourceExecutionIndependentlyVerified=false`, `runtimeShaIndependentlyVerified=false`, `authorization1194Verified=false` e `apiIntegracaoDevPass=false`. Capturas artificiais dos testes não são evidência física.

Fontes reabertas no SHA-base: `local-e2e.sh` blob `ea378f85594d04e5a1a1948e4ffa823449e1b076`; `v9-e2e-1k-factual.py` blob `4a5cdb65f6f517db94913466ee6f0653d7a2be4e`; `SqlProcessorRepository.Reservation.cs` blob `a945a2990ee0b872bf3b7f844a5b1fb287251cd7`; `Jornada.Resultado.Api/Program.cs` blob `3cb074c389131c85623a41e8fefe572ec45fe4a2`. Código candidato no pacote do chat, não na PR documental; integrar ao harness existente somente no ciclo autorizado e sem retirar guardas. Índices preservados; integração protegida da #1946 permanece pendente.

## Preparação posterior — exclusão de arquivos de ambiente do build

**Continuidade de 27/11/2026, sem nova decisão ou gate.** Base reconfirmada `250aedc5f71aec2fd54f7af80b404450d800ce08`; #1940 em `bcbc91464cae4e804d95a4370dc4e0c8c861155f`, sem alteração por esta preparação. Reaberto o pacote de conferência factual, SHA-256 `89b90b70de17403b6ceadf59d385e9b2c7ae98fba0734392af366f4b220ea4de`, CRC íntegro.

**Correção candidata, não aplicada ao produto:** patch de dois arquivos: `Solution/.dockerignore` acrescenta exclusões recursivas de `.env` e `.env.*`, mantendo somente o arquivo público `.env.example` na raiz e excluindo conteúdos de eventual diretório com esse nome; `Solution/tests/scripts/test_m1_dockerignore.py` acrescenta regressão estrutural da guarda e de um eventual ignore específico do Dockerfile. Preservadas todas as exclusões anteriores. Não altera bytes de exemplos, credenciais locais, Dockerfile, compose, API, contratos, GESTOR, banco, workflows ou prioridades. A inspeção do diretório `install/container-test` confirmou ausência de `Dockerfile.dockerignore` na base. A proteção se aplica ao contexto `Solution/`, não a qualquer build possível.

**Reprodução e medição:** biblioteca upstream Moby `patternmatcher` v0.6.1, com `ignorefile.ReadAll` e `MatchesOrParentMatches`, compilada em Go 1.23.2/Linux sem rede durante os testes. Fontes intactas conferidas pelos blobs `216dea671eb4fac6883346a1e0d1350edd43e43e` e `94ea5a0ef55025ff7b4bac5abe7a681c35c377e0`. Dos 63 caminhos sintéticos, 25 violavam a exclusão esperada no arquivo original; no candidato, 63/63 passaram (25 exclusões novas, 16 antigas preservadas, 22 inclusões esperadas). Nove testes Python: um erro na base e nove aprovações com a correção; reexecução sob `-O` dos mesmos casos. Patch aplicado e conferido em Git temporário contendo os bytes exatos do `.dockerignore` original, não em checkout integral. Extração limpa do pacote: manifesto e reexecução do matcher aprovados.

**Limites e repasse:** não executados Docker/build de imagem, .NET, HTTP ou SQL. Filtragem por nome não é scanner de segredos/PII, certificação de imagem, saneamento de caches antigos ou prova de isolamento. Nomes fora dos padrões e segredo colocado no exemplo público continuam exigindo revisão. Nenhum indício de vazamento ocorrido foi demonstrado por estes testes. O módulo Go é auxiliar, não dependência adicionada ao Jornada. Conflito de nome do banco, #1194 e HTTPS continuam pendentes; nenhum M1/E2E PASS.

Pacote no chat: `Jornada_M1_Protecao_Build_Env_20261127.zip`, 31.415 bytes, SHA-256 `ef1fd427e06e5764aa66c4671b3c8410665abd77afedd45c2f437c7e55d6c4f6`. Patch SHA-256 `5451a3dd80f4e7c4583b474f00fd7c5a6bf40f3ab7a6992cf6572f701d1d442c`. É um delta para revisão/incorporação no ciclo autorizado, não nova distribuição do produto nem autorização de instalação. O pacote anterior permanece intacto. Fontes: [Docker build context](https://docs.docker.com/build/concepts/context/#dockerignore-files) e [Moby v0.6.1](https://github.com/moby/patternmatcher/tree/v0.6.1). Índices existentes continuam apontando para este documento; integração protegida da #1946 pendente. GOV-DOC-01; #1194/#405.

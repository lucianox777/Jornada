# Linkage — frequência externa de nomes IBGE

Estado: contrato preparatório da issue #31. Não habilita nem modifica Linkage probabilístico operacional.

## Fonte e finalidade

A fonte prevista é **IBGE — Nomes no Brasil**, tratada exclusivamente como referência estatística externa agregada. O catálogo não é verdade individual, não depende de CPF e não substitui frequências observadas no corpus da Jornada.

A primeira versão preserva a decisão registrada na issue #31: não aplicar normalização fonética, colapso de letras duplicadas ou equivalências probabilísticas. São permitidas apenas adaptações técnicas de consulta, como `Trim` e uniformização de caixa. A grafia/frequência publicada pela fonte continua sendo a unidade estatística de referência.

## Contrato legado de snapshot

`ExternalNameFrequencyCatalog` permanece disponível para snapshots locais simples já usados pelos testes e ferramentas preparatórias. Ele mantém identificador fixo da fonte, versão explícita, pares nome/ocorrências e fingerprint SHA-256 determinístico.

Esse formato não é suficiente para alimentar o otimizador porque não distingue semanticamente frequência de **prenome** e frequência de **sobrenome**, nem identifica o recorte territorial. Por isso ele não deve ser usado para atribuir frequência externa a um atributo do blocking.

## Snapshot tipado para uso downstream

`IbgeTypedNameFrequencyCatalog` introduz o contrato necessário para utilização segura pelo Calibrador/otimizador. Cada entrada registra:

- `IbgeNameStatisticKind.FirstName` ou `IbgeNameStatisticKind.Surname`;
- grafia da entrada com apenas adaptação técnica de consulta;
- número de ocorrências;
- versão explícita da fonte;
- escopo territorial `Brazil`, `State` ou `Municipality`;
- código territorial obrigatório para UF/Município e ausente para Brasil;
- fingerprint SHA-256 incluindo tipo estatístico, escopo, código e conteúdo.

Prenome e sobrenome com a mesma grafia são entradas distintas e podem possuir frequências diferentes. O fingerprint também muda quando muda o recorte territorial, evitando que uma estatística municipal seja confundida com Brasil/UF.

`IbgeCalibrationAttributeCatalog.TryGetOccurrences` faz a ponte semântica entre atributo da Jornada e snapshot tipado. Apenas atributos explicitamente mapeados podem consultar a frequência externa. `name_first`/`mother_name_first` usam estatística de prenome; `name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` usam estatística de sobrenome. Nome completo e atributos sem correspondência IBGE permanecem sem enriquecimento externo, em vez de receber aproximação artificial.

## Entrada local versionada

`ExternalNameFrequencySnapshotReader` continua aceitando o formato local legado para validação de fonte/fingerprint. A evolução seguinte deverá fornecer leitor/materializador equivalente para o snapshot tipado, preservando fail-closed, proveniência e detecção de mudança antes de qualquer automatização de download.

A entrada local separa responsabilidades: obtenção/licenciamento/atestado da publicação externa ocorre fora do runtime de Linkage; o código da Jornada valida, tipa e identifica de forma reproduzível o snapshot recebido. O arquivo não é promovido automaticamente a parâmetro de modelo.

## Limites

Esta fatia ainda não injeta frequência IBGE no critério de escolha do `BlockingRuleSetSearch`, não altera scorer, m/u, prior, thresholds, precedência do CPF ou decisão de identidade. Também não cria/funde UUID, não altera fatos, Gold ou Serving.

O próximo passo implementável é materializar o snapshot tipado a partir da publicação oficial e permitir ao otimizador comparar, no corpus de calibração, alternativas **com e sem** contribuição externa, registrando a proveniência do snapshot escolhido. A promoção continua condicionada à avaliação independente e aos gates estatísticos da issue #31.

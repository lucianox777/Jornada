# Linkage — frequência externa de nomes IBGE

Estado: contrato preparatório da issue #31. Não habilita nem modifica Linkage probabilístico operacional.

## Fonte e finalidade

A fonte prevista é **IBGE — Nomes no Brasil**, tratada exclusivamente como referência estatística externa agregada. O catálogo não é verdade individual, não depende de CPF e não substitui frequências observadas no corpus da Jornada.

A primeira versão preserva a decisão registrada na issue #31: não aplicar normalização fonética, colapso de letras duplicadas ou equivalências probabilísticas. São permitidas apenas adaptações técnicas de consulta, como `Trim` e uniformização de caixa. A grafia/frequência publicada pela fonte continua sendo a unidade estatística de referência.

## Contrato legado de snapshot

`ExternalNameFrequencyCatalog` permanece disponível para snapshots locais simples já usados pelos testes e ferramentas preparatórias. Ele mantém identificador fixo da fonte, versão explícita, pares nome/ocorrências e fingerprint SHA-256 determinístico.

Esse formato não é suficiente para alimentar o otimizador porque não distingue semanticamente frequência de **primeiro nome** e frequência de **sobrenome**, nem identifica o recorte territorial. Por isso ele não deve ser usado para atribuir frequência externa a um atributo do blocking.

## Snapshot tipado para uso downstream

`IbgeTypedNameFrequencyCatalog` preserva as classes estatísticas publicadas. Cada entrada registra:

- `IbgeNameStatisticKind.FirstName` ou `IbgeNameStatisticKind.Surname`;
- grafia da entrada com apenas adaptação técnica de consulta;
- número de ocorrências;
- versão explícita da fonte;
- escopo territorial `Brazil`, `State` ou `Municipality`;
- código territorial obrigatório para UF/Município e ausente para Brasil;
- fingerprint SHA-256 incluindo tipo estatístico, escopo, código e conteúdo.

Primeiro nome e sobrenome com a mesma grafia são entradas distintas e podem possuir frequências diferentes. O fingerprint também muda quando muda o recorte territorial, evitando que uma estatística municipal seja confundida com Brasil/UF.

O snapshot tipado preservar estatística oficial de `Surname` **não significa** que qualquer token interno da Jornada seja semanticamente um sobrenome do IBGE. A publicação oficial parte de campos separados de nome e sobrenome; `nome_completo` da Jornada não preserva essa fronteira original.

## Mapeamento seguro para o Calibrador

`IbgeCalibrationAttributeCatalog` faz a ponte semântica entre atributo da Jornada e snapshot tipado. Na versão corrente, somente:

- `name_first` usa estatística oficial de primeiro nome;
- `mother_name_first` usa estatística oficial de primeiro nome.

As features internas `name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` continuam disponíveis para diagnóstico/calibração com evidência da própria Jornada, mas são derivadas por tokenização do nome completo normalizado. Elas **não recebem frequência oficial de sobrenome do IBGE**, porque isso confundiria uma heurística interna de blocking com a semântica publicada da fonte.

A estatística tipada de `Surname` permanece no modelo de referência para uso futuro quando existir atributo de origem com fronteira estruturada confiável e semântica compatível. Nome completo e atributos sem correspondência explícita permanecem sem enriquecimento externo em vez de receber aproximação artificial.

## Entrada local versionada

`ExternalNameFrequencySnapshotReader` continua aceitando o formato local legado para validação de fonte/fingerprint. A evolução seguinte deverá preservar fail-closed, proveniência e detecção de mudança antes de qualquer automatização de obtenção da fonte.

A entrada local separa responsabilidades: obtenção/licenciamento/atestado da publicação externa ocorre fora do runtime de Linkage; o código da Jornada valida, tipa e identifica de forma reproduzível o snapshot recebido. O arquivo não é promovido automaticamente a parâmetro de modelo.

## Limites

Esta fatia não transforma frequência IBGE em peso, raridade, prior ou decisão de identidade por convenção. Também não cria/funde UUID, não altera fatos, Gold ou Serving.

Qualquer uso estatístico da frequência no Calibrador deve ser explicitamente versionado e comparado no corpus de calibração, com avaliação independente. Para sobrenomes, existe um gate adicional: somente uma fonte/atributo com semântica estruturada compatível pode receber a estatística oficial de `Surname`; tokens derivados de `nome_completo` não satisfazem esse gate.

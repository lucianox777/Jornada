# Linkage — frequência externa de nomes IBGE

Estado: contrato preparatório da issue #31. Não habilita nem modifica Linkage probabilístico operacional.

## Fonte e finalidade

A fonte prevista é **IBGE — Nomes no Brasil**, tratada exclusivamente como referência estatística externa agregada. O catálogo não é verdade individual, não depende de CPF e não substitui frequências observadas no corpus da Jornada.

A primeira versão preserva a decisão registrada na issue #31: não aplicar normalização fonética, colapso de letras duplicadas ou equivalências probabilísticas. São permitidas apenas adaptações técnicas de consulta, como `Trim` e uniformização de caixa. A grafia/frequência publicada pela fonte continua sendo a unidade estatística de referência.

## Contrato de snapshot

`ExternalNameFrequencyCatalog` produz um snapshot somente leitura contendo:

- identificador fixo da fonte `IBGE_NOMES_NO_BRASIL`;
- versão explícita da fonte, informada pelo processo de ingestão;
- pares nome/ocorrências, ordenados canonicamente;
- fingerprint SHA-256 determinístico do identificador, versão e conteúdo canônico.

Entradas vazias, contagens negativas e duplicidades após a adaptação técnica são rejeitadas. O fingerprint permite demonstrar exatamente qual publicação agregada foi usada em uma análise sem persistir dados pessoais da Jornada.

## Limites

Esta fatia não conecta automaticamente a API/site do IBGE, não altera `FrequencyCalculator`, não injeta frequência externa no scorer, não muda m/u, prior, thresholds, blocking, precedência do CPF ou decisão de identidade. Também não cria/funde UUID, não altera fatos, Gold ou Serving.

Uso no estimador/scorer exige uma fatia posterior com corpus representativo, separação calibração/avaliação, avaliação independente de recall/precisão/calibração/falsos vínculos e aprovação institucional conforme a issue #31.

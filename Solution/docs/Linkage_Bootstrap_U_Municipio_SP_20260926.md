# Bootstrap nominal de u — escopo por atributo (decisão de 26/09/2026)

**Status:** `nome_mae` nacional V1 é decisão técnica vigente e infraestrutura implementada; `nome` civil e `sobrenome` do município de São Paulo são candidatos V2 **não implementados nem aprovados para HML**. Fonte de verdade: [decisões centrais](Decisoes_Linkage_Calibracao_IBGE_20260926.md) e [Plano](Plano_Desenvolvimento.md).

## Escolha ex ante por atributo — não existe fallback geográfico

| Evidência | Fonte escolhida | Status | Lacuna e reação |
|---|---|---|---|
| Prenome de `nome_mae` | `BRASIL/NOME/FEMININO/TODOS` no período | Cache nominal V1 `FEMININO` implementado (#498); validade populacional não demonstrada | Registrar nome não publicado/ausente e falhar fechado quando não houver suporte; **não procurar município ou UF** |
| Sobrenome de `nome_mae` | `BRASIL/SOBRENOME/TODOS` | Parte do mesmo bootstrap nacional V1 independente de prenome | Não supor distribuição real conjunta dos sobrenomes familiares |
| Prenome `nome` civil | `MUNICIPIO/3550308/NOME/TODOS` | Candidato V2, condicionado a leitura/medições integrais | Ausência/insuficiência local impede evidência/derivado SP; **nunca UF/Brasil como preenchimento** |
| `sobrenome` civil | `MUNICIPIO/3550308/SOBRENOME/TODOS` | Candidato V2, condicionado à marginal publicada | Mesmo fail-closed municipal |

Uma pessoa atendida em SP pode ter nascido em outro município, bem como sua mãe em outra UF e coorte. A escolha nacional da mãe considera diferenças geracionais/migratórias sem presumir que os nomes maternos reproduzam a distribuição atual da capital. O Seade conta **8,6 milhões entre 44,4 milhões de residentes do estado de São Paulo em 2022** nascidos em outras UFs, e **22,7% das mulheres que foram mães no estado em 2021** nascidas fora de SP (27,1% em 2010). São universos/períodos diferentes e **não** medidas de mães vinculadas aos cadastros municipais. Nenhum dos números prova qual fonte minimiza FP/FN; isso exige validação externa por estratos. [Seade sobre o Censo 2022](https://www.agenciasp.sp.gov.br/mais-de-8-milhoes-de-brasileiros-escolheram-sao-paulo-para-viver-mostra-estudo-do-seade/); [Seade, naturalidade das mães 2021](https://informa.seade.gov.br/wp-content/uploads/sites/8/2022/10/seade-informa-naturalidade-maes-2021.pdf).

## Implementação V2 candidata da pessoa

Usar snapshot imutável `CENSO2022_NOMES_BRASIL_V1`, `projection/frequencia-municipio.ndjson.gz`, filtrar `UF=35/municipio_codigo=3550308`; manifesto contém 4.088.822 linhas **de todos os municípios**, não da capital. Provar SHA de origem, totais e tipos `NOME/TODOS` e `SOBRENOME/TODOS`. `NOME/FEMININO` municipal pode entrar em diagnósticos de disponibilidade, mas **não** é gate da V2 de pessoa, nem origem operacional da mãe.

Preservar todas as contagens publicadas positivas na amostragem, inclusive `k<25`. `k_min=25` é limite de confiança **por term frequency** proposto/versionado, não filtro do gerador. `RSE≈1/sqrt(k)` é heurística Poisson preditiva, não incerteza amostral do Censo. Omissão/supressão permanece desconhecida; OOV só com denominador independente e teste contra `u=0` e LLR infinito. Falta de evidência municipal implica `MARGINAL_INSUFICIENTE` e bloqueia V2. Não usar frequência de UF ou Brasil nem pseudo-contagens em lacunas municipais.

Persistir V2 imutável com chave distinta por fonte/hash/escopo/município/método/contrato/marginais/seed/PairCount/política de cauda; V1 da mãe permanece imutável. ENSURE apenas explícito antes da primeira Entrega e leitura sem recálculo implícito em `GENERATE_DRAFT`. Testar SQL, cache hit/concurrency/rollback/replay e proveniência por campo.

## Evidências necessárias e critérios de ativação

1. Inspecionar **100%** das linhas do município e reconciliar manifesto: nomes únicos, contagens publicadas, `k>=25`, `0<k<25`, supressão/massa desconhecida e denominadores compatíveis.
2. TVD sobre união **integral** de chaves para `NOME/TODOS` SP × Brasil e `SOBRENOME/TODOS` SP × Brasil, com massa ausente explicitada; Jensen–Shannon opcional.
3. Cobertura de **100% do corpus** por campo: nome/sobrenome da pessoa no candidato SP; mãe no nacional `FEMININO` + sobrenome `TODOS`; por coorte, fonte, CPF ausente e demais estratos disponíveis. Não retirar casos sem cobertura dos denominadores.
4. Comparar métodos V1 nacional integral e V2 híbrido (pessoa SP, mãe nacional) com partições e seeds congelados, reportando diferenças atribuíveis à pessoa e FN, FP por classe, PPV/recall, blocking, conflitos, abstenção e custo. TEST só audita modelo congelado; massa sintética não substitui fonte externa (#31).

**Fora do escopo:** `m` exige pares reais rotulados; `u` condicionado ao blocking permanece por modelo e separado da referência nominal não condicionada; `MaxCandidatePairs=10.000.000` limita o Avaliador, não o bootstrap. Sem autorização HML/Produção por esses resultados.

**Rastreio:** [#500](https://github.com/lucianox777/Jornada/issues/500), [#497](https://github.com/lucianox777/Jornada/issues/497), [#31](https://github.com/lucianox777/Jornada/issues/31), [ADR-002](../../Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md), [ADR-003](../../Documentos/ADR/ADR-003-corpus-sintetico-nomes-frequencia-ibge.md) e [PR #498](https://github.com/lucianox777/Jornada/pull/498).

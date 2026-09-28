# Bootstrap nominal de u — escopo por atributo (decisão de 26/09/2026)

**Status:** `nome_mae` nacional V1 é decisão técnica vigente e infraestrutura implementada; `nome` civil e `sobrenome` do município de São Paulo são candidatos V2 **não implementados nem aprovados para HML**. Fonte de verdade: [decisões centrais](Decisoes_Linkage_Calibracao_IBGE_20260926.md) e [Plano](Plano_Desenvolvimento.md).

## Escolha ex ante por atributo — não existe fallback geográfico

| Evidência | Fonte escolhida | Status | Lacuna e reação |
|---|---|---|---|
| Prenome de `nome_mae` | `BRASIL/NOME/FEMININO/TODOS` no período | Cache nominal V1 `FEMININO` implementado (#498); validade populacional não demonstrada | Registrar nome não publicado/ausente e falhar fechado quando não houver suporte; **não procurar município ou UF** |
| Sobrenome de `nome_mae` | `BRASIL/SOBRENOME/TODOS` | Parte do mesmo bootstrap nacional V1 independente de prenome | Não supor distribuição real conjunta dos sobrenomes familiares |
| Prenome `nome` civil | `MUNICIPIO/3550308/NOME/TODOS` | Candidato V2, condicionado a leitura/medições integrais | Marginal inteira insuficiente bloqueia V2; nome individual não publicado com marginal íntegra implica **LLR neutro só nesse componente**; nunca preencher por UF/Brasil |
| `sobrenome` civil | `MUNICIPIO/3550308/SOBRENOME/TODOS` | Candidato V2, condicionado à marginal publicada | Mesma regra: marginal inteira insuficiente bloqueia V2; sobrenome individual ausente neutraliza somente seu componente |

Uma pessoa atendida em SP pode ter nascido em outro município, bem como sua mãe em outra UF e coorte. A escolha nacional da mãe considera diferenças geracionais/migratórias sem presumir que os nomes maternos reproduzam a distribuição atual da capital. O Seade conta **8,6 milhões entre 44,4 milhões de residentes do estado de São Paulo em 2022** nascidos em outras UFs, e **22,7% das mulheres que foram mães no estado em 2021** nascidas fora de SP (27,1% em 2010). São universos/períodos diferentes e **não** medidas de mães vinculadas aos cadastros municipais. Nenhum dos números prova qual fonte minimiza FP/FN; isso exige validação externa por estratos. [Seade sobre o Censo 2022](https://www.agenciasp.sp.gov.br/mais-de-8-milhoes-de-brasileiros-escolheram-sao-paulo-para-viver-mostra-estudo-do-seade/); [Seade, naturalidade das mães 2021](https://informa.seade.gov.br/wp-content/uploads/sites/8/2022/10/seade-informa-naturalidade-maes-2021.pdf).

## Implementação V2 candidata da pessoa

Usar snapshot imutável `CENSO2022_NOMES_BRASIL_V1`, `projection/frequencia-municipio.ndjson.gz`, filtrar `UF=35/municipio_codigo=3550308`; manifesto contém 4.088.822 linhas **de todos os municípios**, não da capital. Provar SHA de origem, totais e tipos `NOME/TODOS` e `SOBRENOME/TODOS`. `NOME/FEMININO` municipal pode entrar em diagnósticos de disponibilidade, mas **não** é gate da V2 de pessoa, nem origem operacional da mãe.

Preservar **todas** as contagens municipais publicadas positivas na amostragem e no bootstrap, inclusive 10–24, **sem limiar adicional de contagem nem teto artificial de LR**. Pisos da divulgação *Nomes no Brasil*: **município 10, UF 15, Brasil 20**; [Nota Técnica IBGE 01/2025](https://biblioteca.ibge.gov.br/visualizacao/livros/liv102228.pdf) e [explicação oficial do IBGE sobre a mesma regra em 2010](https://agenciadenoticias.ibge.gov.br/agencia-sala-de-imprensa/2013-agencia-de-noticias/releases/9552-um-brasil-de-marias-e-joses-ibge-apresenta-banco-de-nomes-com-base-no-censo-2010). Cada nome **publicado** usa `u_nominal=k_SP/N_SP`; essa marginal não substitui o `u` operacional condicionado ao blocking. `RSE≈1/sqrt(k)` é heurística Poisson, não erro amostral do Censo; medir a influência dos raros em FP/FN e validar externamente (#31).

**Ausência individual com marginais municipais íntegras:** somente o componente correspondente do score V2 recebe `LLR=0` (neutro). Não usar `u=0`, não completar com UF/Brasil e não neutralizar a mãe, o nascimento ou outros componentes disponíveis; as guardas de conflito continuam valendo. Registrar `NOME_NAO_OBSERVAVEL` **por componente**, razão e proveniência para replay. **Marginal inteira faltante/incompleta/incompatível:** `MARGINAL_INSUFICIENTE`, V2 não ativa. Regra **a implementar e medir**, não funcionalidade já comprovada.

Persistir V2 imutável com chave distinta por fonte/hash/escopo/município/método/contrato/marginais/seed/PairCount/política de cauda; V1 da mãe permanece imutável. ENSURE apenas explícito antes da primeira Entrega e leitura sem recálculo implícito em `GENERATE_DRAFT`. Testar SQL, cache hit/concurrency/rollback/replay e proveniência por campo.

## Evidências necessárias e critérios de ativação

1. Inspecionar **100%** das linhas do município e reconciliar manifesto: nomes únicos, contagens publicadas, faixas publicadas **10–24, 25–99, 100–999, ≥1000** (diagnóstico somente), supressão/massa desconhecida e denominadores compatíveis.
2. TVD sobre união **integral** de chaves para `NOME/TODOS` SP × Brasil e `SOBRENOME/TODOS` SP × Brasil, com massa ausente explicitada; Jensen–Shannon opcional.
3. Cobertura de **100% do corpus** por campo: nome/sobrenome da pessoa no candidato SP; mãe no nacional `FEMININO` + sobrenome `TODOS`; por coorte, fonte, CPF ausente e demais estratos disponíveis. Medir a fração de pares com LLR neutro por componente, seu efeito em FP/FN e decisão final; não retirar casos sem cobertura dos denominadores.
4. Comparar métodos V1 nacional integral e V2 híbrido (pessoa SP, mãe nacional) com partições e seeds congelados, reportando diferenças atribuíveis à pessoa e FN, FP por classe, PPV/recall, blocking, conflitos, abstenção e custo. TEST só audita modelo congelado; massa sintética não substitui fonte externa (#31).

**Fora do escopo:** `m` exige pares reais rotulados; `u` condicionado ao blocking permanece por modelo e separado da referência nominal não condicionada; `MaxCandidatePairs=10.000.000` limita o Avaliador, não o bootstrap. Sem autorização HML/Produção por esses resultados.

**Rastreio:** [#500](https://github.com/lucianox777/Jornada/issues/500), [#497](https://github.com/lucianox777/Jornada/issues/497), [#31](https://github.com/lucianox777/Jornada/issues/31), [ADR-002](../../Documentos/ADR/ADR-002-calibrador-fs-u-condicionado.md), [ADR-003](../../Documentos/ADR/ADR-003-corpus-sintetico-nomes-frequencia-ibge.md) e [PR #498](https://github.com/lucianox777/Jornada/pull/498).

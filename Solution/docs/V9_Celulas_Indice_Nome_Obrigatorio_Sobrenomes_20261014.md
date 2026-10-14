# V9 — ordem de blocking, índices normalizados e células de sobrenome (experimento)

**Prioridade estatística: 95% de recall de candidatos verdadeiros sem aumentar indevidamente U/erro de vínculo.** Este documento registra um experimento independente de `TRAIN/VALIDATION`, em SQL Server `JornadaHarness` efêmero, não uma alteração operacional.

## Contexto medido (PR #1024)

No relatório físico de 14 passes, o cenário **nome da pessoa obrigatório + (mãe OU nascimento)** recuperou, no recorte sem CPF, em três seeds com 30 mil pessoas-base cada:

| Seed | TRAIN recall entre M elegíveis | VALIDATION recall entre M elegíveis | M elegíveis sem nascimento físico, TRAIN/VALIDATION |
| --- | ---: | ---: | ---: |
| 42 | 79,18% | 80,47% | 1.775 / 543 |
| 43 | 80,11% | 80,28% | 1.532 / 555 |
| 44 | 80,08% | 79,61% | 1.627 / 548 |

**Não** interpretar os antigos ~87% da união com nascimento presente como recall do novo contrato. Não afirmar 95% por excluir pessoas. A cobertura da política sobre **todos os M sem CPF**, os M excluídos, o universo M elegível, os abstidos e os novos U devem permanecer visíveis simultaneamente. No TEST congelado não fazemos seleção.

## Quatro células SQL físicas exploratórias, ordenadas por seletividade presumida

| Ordem diagnóstica | Predicado de igualdade (AND) | Campos |
| --- | --- | --- |
| 1 | `MetaphoneBR(nome pessoa)` + **último sobrenome significativo da mãe** | P + M; aceita ausência de DOB |
| 2 | `MetaphoneBR(nome pessoa)` + ano de nascimento válido | P + D; aceita ausência de mãe |
| 3 | **Último sobrenome significativo da pessoa** + `MetaphoneBR(nome mãe)` | P + M; aceita ausência de DOB |
| 4 | `MetaphoneBR(nome pessoa)` + **qualquer sobrenome derivado da mãe** (índice invertido por presença) | P + M; cobertura auxiliar de posição intermediária |

**Nome P é obrigatório nos dois registros** antes de executar qualquer passe; `MD` isolado nunca ingressa nas novas células. Uma grafia normalizada e MetaphoneBR são representações versionadas complementares; **Jaro–Winkler não é chave de igualdade B-tree**: somente se aplica a pares já recuperados pelo SQL para medir `JaroWinklerHighM/U` com corte diagnóstico 0,92, sem selecionar/promover limiar. Os nomes integrais originais permanecem.

A sequência é hipótese de **custo e seletividade**, não autorização de `early stop`: cada passe elegível roda até o fim (ou falha explicitamente no cap), a união é deduplicada por par, e `ExclusiveNewM` identifica M adicionais exclusivos. Um primeiro passe ser preciso **não** autoriza suprimir os demais. A aferição definitiva da ordem exige medir tempo, CPU/I/O, grau/P95/P99, memória, blocos e orçamento de custo no mesmo corpus; ordem por si só não altera recall da união. `JaroWinklerHigh` não significa vínculo aceito.

## Paridade índice SQL ⇄ projeção canônica

O vocabulário já tem `name_full`, `name_phonetic_ptbr`, `mother_name_full`, `mother_name_phonetic_ptbr`, `name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last`. O índice operacional genérico `identidade.blocking_chave.IX_blocking_chave_lookup` usa `normalizacao_versao, atributo, valor_normalizado, vigencia_fim, pessoa_uuid`. **Ele não cria automaticamente a nova projeção sem que a feature seja materializada e aprovada**. As chaves históricas de `*_last`/ `*_surnames` incluem heurísticas de tokenização, inclusive partículas, e não garantem a mesma semântica de último sobrenome significativo do contrato `BrazilianNameComponents`.

Neste experimento são recriadas apenas tabelas e **índices temporários** `#v9cellobs` e `#v9cellmother`, com SQL BIN2 e chaves produzidas em C# usando `BlockingProjectionKeyProjector`/`IdentityComparison.NormalizeText` e `MetaphoneBr.Encode`. Antes dos passes físicos o ensaio compara, por `obs_id`, a **normalização integral da pessoa e mãe** e as **duas chaves fonéticas** com as colunas efetivamente materializadas em `#v9obs`. Qualquer divergência faz a CI falhar, e nenhum `COLLATE` tolerante transforma chaves diferentes em match silencioso. Os novos índices foram projetados em `(part,pfon,mlast)`, `(part,pfon,birthyear)`, `(part,plast,mfon)`, `(part,pfon)` e `(part,surname,obs_id)` do índice invertido da mãe. Não há alteração de SQL Server operacional ou DDL persistente.

## Sobrenome IBGE — qualquer posição; último significativo primeiro

A Nota Técnica IBGE 01/2025 estabelece coleta preferencial dos sobrenomes disponíveis; quando necessário, o último; o indicador `SOBRENOME` **não é frequência observada especificamente da posição final**. Uma busca por presença deve aceitar qualquer posição, **priorizando a chave do último sobrenome significativo conhecido**, sem apagar os demais nem criar correspondência determinística. A priorização de ordem nas chaves não elimina candidatos; deve ser medida em fanout/recall. A inferência de qual token é sobrenome em nomes completos não estruturados é uma **heurística**, não uma observação oficial de sobrenome estruturado. Partículas `DA/DE/DO...` e agnomes terminais reconhecidos são excluídos da projeção de presença quando aplicável, mas preservados no nome integral.

As células da pessoa (`PERSON_LAST_MOTHER_PHONETIC`) e da mãe (`PERSON_PHONETIC_MOTHER_LAST` e `PERSON_PHONETIC_MOTHER_ANY`) permanecem **separadas** em M/U, sem duplicar evidências do mesmo grupo no FS. A marginal IBGE de `SOBRENOME` em qualquer posição pode servir como **proxy inicial declarado** do último sobrenome significativo, jamais como taxa empírica da posição final, TF conjunta ou peso FS pronto. Referência vigente: `Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md`, `Decisoes_Canonicas_Identidade_Linkage_20260929.md`, `Justificativa_Retificacao_Proxy_IBGE_Ultimo_Sobrenome_V9_20261012.md`. Pessoa (município de São Paulo) e mãe (Brasil) conservam os respectivos recortes de referência, sem inventar marginais conjuntas.

## Gates e velocidade de desenvolvimento

Esta PR executa diagnóstico **uma vez na mesma CI já existente**: 3 sementes, SQL físico real e validação Python dos quatro passes, ganho M/U marginal contra os 14 passes, ganho coletivo, M exclusivo, recall sobre nome-elegíveis e déficit 95%. CI verde prova integridade da instrumentação, **não** meta atingida nem FDR/calibração. Qualquer cap acima de um milhão aborta, sem `TOP`/truncamento, e não se usa TEST para ajustar regras.

Para reduzir o ciclo: fazer várias edições **na mesma branch antes de abrir PR**, testar unidades/contratos primeiro, depois **uma** CI completa sobre o HEAD final; não disparar ou aguardar builds sucessivos em HEADs obsoletos. Reutilizar caches NuGet e artefatos quando hashes permitirem, diagnosticar a etapa SQL isolada pelas evidências, mas **sem separar/omitir CI obrigatória**, sem alargar os gates ou liberar merge sem `SUCCESS`. Qualquer refatoração de pipeline deve comprovar equivalência de gates.

Nada muda em `JornadaLocal`, Gold, NODE/NAS, HML/PROD, IBGE original, histórico V8, V9/DT-15 operacional, `Rank/Resolve`, limiar/TF/m/u ou regras ativas. A massa sintética não certifica o desempenho real em 11 milhões; se o corpus de 30 mil não fornecer suporte efetivo de M/U e rede U, ampliar TRAIN de maneira pré-registrada e manter TEST cego.

## Acompanhamento: ablação de recuperação com chaves da própria pessoa

A CI física anterior (PR #1025) mediu, em VALIDATION, recall **84,82%, 84,49% e 83,98%** nas seeds 42, 43 e 44. Houve incremento real de M com exposição incremental relativamente pequena de U, porém **faltam 674, 702 e 725 M**, respectivamente, para o patamar de 95% por split.

Nesta próxima rodada a matriz tem **seis** células (as quatro originais preservadas mais duas novas, no fim): `PERSON_PHONETIC_ONLY` testa a *cobertura-limite exploratória* da chave de **nome integral MetaphoneBR**, e `PERSON_FIRST_PLUS_PERSON_ANY_SURNAME` explora o **prenome normalizado + qualquer sobrenome significativo da própria pessoa** em índice invertido (o último é priorizado, mas não exclusivo). Ambas usam índices SQL temporários; nenhuma seleção de par prescinde da **elegibilidade prévia** de P + (M ou D) em **cada observação**. O passe fonético isolado **não exige concordância entre mães ou nascimentos**: só é diagnóstico do teto recuperável da identidade nominal, não pode ser promovido automaticamente, porque pode aumentar muito candidatos U e erros FS. Não retirar nem desviar os dois grupos mínimos exigidos pelo contrato.

Para ambas medir SQL físico completo, cap, novo M, novo U, casos exclusivos, recall incremental, Jaro–Winkler alto entre hits, fanout e comparação do custo de última posição vs presença em qualquer posição. Uma união ≥95% não é aprovação de FS/TF nem demonstra risco aceitável em 11 milhões.


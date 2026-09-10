# Matriz de Rastreabilidade - Aditivos RF/RNF v1.1

**Data:** 10/09/2026  
**Status:** VIGENTE - COMPLEMENTO DA MATRIZ v1.0

Esta matriz complementa `05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.md` sem alterar as cadeias históricas existentes. A consolidação v5.00 estabelece como numeração funcional canônica **RF-051 a RF-056** e como aditivos não funcionais `RNF34-A` a `RNF34-D`. O antigo Adendo 06 é histórico e não concorre com esta matriz.

Referências aos RNFs históricos usam a forma canônica `RNF-001` a `RNF-033`.

## Novos requisitos funcionais

| RF | RN de origem | RNF associados | Realização/evidência principal |
|---|---|---|---|
| **RF-051** Paralelismo no calibrador/avaliador quando vantajoso | RN-023, RN-024, RN-028, RN-035 | RNF-015, RNF-020, RNF-021, RNF34-A, RNF34-B | Benchmark comparativo serial×paralelo; grau de paralelismo configurável; regressão de equivalência determinística; CI |
| **RF-052** Uso governado de frequências agregadas oficiais do IBGE no blocking | RN-005, RN-014, RN-023, RN-024, RN-029 | RNF-013, RNF-016, RNF-021, RNF-027, RNF34-A, RNF34-B | `IbgeTypedNameFrequencyCatalog`, snapshot/fingerprint IBGE, testes de proveniência e avaliação de ganho |
| **RF-053** Nome completo, prenome, sobrenome, último nome e dia/mês/ano no blocking otimizado | RN-005, RN-006, RN-014, RN-023, RN-024 | RNF-016, RNF-021, RNF-025, RNF-027, RNF34-A, RNF34-B | `BirthBlockingPlan`, `BlockingFeatureDiagnostic`, `BlockingCombinationDiagnostic`, frequências IBGE quando aplicáveis, política dinâmica versionada e avaliação independente |
| **RF-054** Preservação da semântica oficial dos nomes do IBGE | RN-005, RN-014, RN-024, RN-029, RN-030 | RNF-013, RNF-016, RNF-021, RNF-030, RNF34-A, RNF34-B | grafia/frequência oficial preservada; snapshot tipado por prenome/sobrenome e território; representação técnica versionada; regressões contra colapsos indevidos |
| **RF-055** Mesma política dinâmica versionada no calibrador e avaliador | RN-003, RN-014, RN-024, RN-029 | RNF-013, RNF-016, RNF-021, RNF34-A, RNF34-B | `DynamicBlockingPolicy`/ruleset persistido, fingerprint da política, relatório de avaliação e testes de equivalência entre superfícies/providers |
| **RF-056** Evitar snapshots redundantes do IBGE | RN-014, RN-023, RN-024, RN-029 | RNF-013, RNF-020, RNF-021, RNF34-A, RNF34-B | `IbgeSnapshotChangeDetector`; Content-Length/ETag/Last-Modified como pré-verificação; SHA-256 para metadados inconclusivos; testes de mesmo tamanho com conteúdo distinto |

## Aditivos não funcionais

| RNF | Incidência | Evidência mínima |
|---|---|---|
| **RNF12 (complemento)** Regressão unitária + integração | Toda mudança funcional/estatística/contratual aplicável | Teste unitário e teste integrado do caminho alterado; exceção somente quando explicitamente justificada |
| **RNF34-A** Documentação sincronizada | Todo change-set material | Código + requisitos + README/runbook/especificação/arquitetura afetados no mesmo PR; ADR somente quando a política de histórico decisório o exigir |
| **RNF34-B** CI obrigatório | Todo PR com mudança verificável | Gates obrigatórios do HEAD exato concluídos com sucesso antes de Ready/merge |
| **RNF34-C** Diagramas UML | Toda visão técnica/arquitetural normativa | DOCX/PDF com diagramas UML incorporados; diagrama de classes para estrutura e de atividade para resolução de identidade; DER/DRE não classificado como UML; leitura sem software específico de modelagem |
| **RNF34-D** Ambiente tecnológico reprodutível | Desenvolvimento, teste, integração, bancos e BI | C#/.NET, Git, Docker e Power BI Desktop conforme aplicável; **Microsoft SQL Server como tecnologia relacional normativa**; PostgreSQL somente como provider paralelo em escopos explícitos; versões controladas/documentadas; CI reproduzível |

## Relação com a issue #31

Nenhum dos itens acima, isoladamente, encerra a homologação estatística. A ativação probabilística continua condicionada a corpus representativo/atestado, separação de calibração e avaliação, recall/precisão/calibração/falsos vínculos, análise de dependências e subgrupos e aprovação institucional explícita.

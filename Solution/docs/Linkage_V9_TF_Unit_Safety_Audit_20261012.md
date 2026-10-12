# V9 — segurança estatística da unidade de Term Frequency (TF)

**12/10/2026 — etapa 5 da migração V9 (issue #946).**

## Proteção de unidade

A nova biblioteca `CompositeNominalTermFrequencyContractV9` associa cada estado nominal composto V9 a **uma única unidade de frequência elegível**. Nenhum componente é adicionado ao nome integral como outra evidência independente:

| Nível de comparação | Unidade candidata de TF |
|---|---|
| `ExactFull` | `FullName` — nome integral, incluindo nomes intermediários e agnomes |
| `ExactFirstLast` | `FirstLastJoint` — **par observado** primeiro nome + último sobrenome |
| `ExactFirst` | `FirstName` |
| `ExactLast` | `LastContentSurname` |
| `Missing`, `Other`, `HighFull`, `HighFirstLast`, `ReversedFirstLast` | **Nenhuma**: TF fica neutra até contrato específico validado |

`RequireMatchingReference` falha fechado para unidade incompatível, fonte desconhecida, ausência de fingerprint de frame/seleção, suporte abaixo do mínimo e ausência de atestado de validação independente do universo. Em particular, proíbe usar a marginal publicada de primeiro nome para corrigir o `ExactFull`, ou criar a frequência `FirstLastJoint` a partir de marginais IBGE. **Em conformidade com a norma canônica [DC-LK-TF, linha 95](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026) e com [Calibrador FS, §10](Calibrador_FS_Specification.md#10-nome-completo-e-componentes-nominais-decisão-vigente), a marginal IBGE de sobrenomes em qualquer posição é PERMITIDA como proxy inicial para `LastContentSurname`**, exclusivamente com a fonte explícita `NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy`. Essa referência **não é** a frequência empiricamente medida do último sobrenome e não pode ser rotulada como `IndependentlyObservedPopulation`, nem usada para `FullName` ou `FirstLastJoint`. A marginal sem marcação de proxy (`IbgePublishedMarginal`) continua elegível para `FirstName`, mas não para `LastContentSurname` até receber proveniência de proxy explícita. O gate mantém requisitos de suporte, fingerprints e atestado, não comprova amostragem nem estima peso TF; nenhum scorer ou modelo ATIVO é alterado por essa correção. Os metadados passados pelo chamador **não são prova** de representatividade; o gate não estima `f(value)` nem autentica um atestado externo.

## Evidência agregada no TRAIN

O `CompositeNominalEndpointFrequencyAuditBuilderV9` percorre em memória a amostra U já retornada pelo `BlockingConditionedUnmatchedPairReader`. Para pessoa e mãe, registra apenas:

- quantidade de pares e extremidades observadas/ausentes;
- cardinalidade de valores nominais normalizados por unidade;
- maior contagem de aparições de uma unidade;
- quantidade de pares em que a unidade em ambas extremidades concorda.

As chaves de nome integral e de par primeiro + último existem apenas em dicionários locais temporários; o resultado e os logs exibem **somente agregados**, nunca nomes, componentes nominais, tokens, identificadores de cidadãos, pares ou fingerprints de valores nominais. O algoritmo usa o extrator `BrazilianNameComponents` e mantém partículas e agnomes no nome integral. Testes cobrem a diferença entre `Maria Aparecida Silva`, `Maria Cecilia Silva` e `Maria Silva`, nomes de mães ausentes, homônimos e agnomes.

**Advertência estatística determinante:** contagens de aparições em *extremidades da amostra de pares candidatos U* NÃO são frequências populacionais `f(value)`. Pessoas em blocos grandes podem aparecer mais vezes; a amostra pode não ter probabilidades de inclusão conhecidas. Portanto a saída recebe o status `DIAGNOSTIC_ONLY_ENDPOINT_APPEARANCES_NOT_POPULATION_TF`, não é um snapshot TF certificado, não pode ser aplicada diretamente ao scorer e não substitui estimação governada da frequência conjunta em universo apropriado.

## Próxima etapa obrigatória

1. Construir um frame populacional com indivíduos e pesos de seleção conhecidos no **mesmo universo de candidatos/blocking**, com tokenização e versões congeladas, e estimar `f(value)` com suporte/limites e prova de população-alvo.
2. Condicionar probabilidades `m/u` e dependências entre pessoa/mãe ao frame real; segregar TRAIN/VALIDATION/TEST, impedir vazamento por grupos de identidade.
3. Calibrar peso TF de forma independente, com `w_TF=0` permitido quando dados insuficientes; verificar monotonicidade EXACT/HIGH no pior cenário e paridade de cálculo C# × Splink.
4. Habilitar `AlgorithmVersion=V9` apenas após gates de FP/FDR/recall e decisão DT-15; **sem alteração silenciosa do modelo ativo**.

## Compatibilidade

A entrega contém biblioteca/diagnóstico e logs read-only no `GENERATE_DRAFT` histórico. **Não muda** `Rank`, `Resolve`, `VALIDATE`, `ACTIVATE`, `TermFrequencyScoring` V8, banco, contrato persistido, pesos ou snapshots históricos. V8 permanece apenas para replay; novos conceitos são V9.

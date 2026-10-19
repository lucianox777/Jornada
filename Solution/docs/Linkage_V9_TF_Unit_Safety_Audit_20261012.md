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

`RequireMatchingReference` falha fechado para unidade incompatível, fonte desconhecida ou ausência de fingerprints de frame/seleção. Na fonte real observada, também exige suporte mínimo e atestado de validação independente; na origem IBGE sintética, em vez de inventar observações reais, exige proveniência `SINTETICO_IBGE`, 30.000 pessoas e metadados rastreáveis de gerador, seed, corpus e hipóteses. Em particular, proíbe usar a marginal publicada de primeiro nome para corrigir o `ExactFull`, ou criar a frequência `FirstLastJoint` a partir de marginais IBGE. **Em conformidade com a norma canônica [DC-LK-TF, linha 95](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026) e com [Calibrador FS, §10](Calibrador_FS_Specification.md#10-nome-completo-e-componentes-nominais-decisão-vigente), a marginal IBGE de sobrenomes em qualquer posição é PERMITIDA como proxy inicial para `LastContentSurname`**, exclusivamente com a fonte explícita `NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy`. Essa referência **não é** a frequência empiricamente medida do último sobrenome e não pode ser rotulada como `IndependentlyObservedPopulation`, nem usada para `FullName` ou `FirstLastJoint`. A marginal sem marcação de proxy (`IbgePublishedMarginal`) continua elegível para `FirstName`, mas não para `LastContentSurname` até receber proveniência de proxy explícita. No bootstrap, a referência IBGE é aprovada pelo corpus sintético de 30.000 pessoas (`SINTETICO_IBGE`), sem exigir validação populacional nem suporte observado; essas exigências valem para fontes observadas reais, que substituem o IBGE progressivamente. O gate não comprova amostragem nem estima peso TF; nenhum scorer ou modelo ATIVO é alterado por essa correção. Os metadados passados pelo chamador **não são prova** de representatividade; o gate não estima `f(value)` nem autentica um atestado externo.

## Complemento de conformidade — bootstrap `SINTETICO_IBGE` da DC-LK-09/10

A [consolidação canônica de 09/10/2026](Decisoes_Canonicas_Identidade_Linkage_20260929.md) (§ «Finalidade do bootstrap», «Proveniência sem veto» e «Substituição progressiva») autoriza um corpus **sintético de 30.000 pessoas** como referência inicial antes de observações municipais representativas. O contrato diagnóstico foi versionado como `V9_NOMINAL_TF_UNIT_GUARD_V3`: fontes IBGE exigem `NominalSyntheticIbgeBootstrapV9` com origem `SINTETICO_IBGE`, quantidade 30.000, versão/seed do gerador e fingerprints de corpus/hipóteses, além dos fingerprints de frame/blocking; **não** se fazem passar por suporte de observações reais ou validação populacional. Fontes reais `IndependentlyObservedPopulation` mantêm suporte independente e validação e não podem declarar origem sintética. Isso **não** comprova a autenticidade do corpus gerado nem autoriza a publicação/ativação do scorer V9; a transição substitui o IBGE por evidências históricas reais sob suficiência e governança. O guard decide somente elegibilidade de contrato, não estima peso TF nem `f(value)`.

## Justificativa e rastreabilidade da retificação da #955

A razão normativa e estatística para aceitar **somente como proxy inicial declarado** a marginal IBGE de sobrenomes em qualquer posição — e não tratá-la como frequência observada da posição final — está registrada em [Justificativa da retificação do proxy IBGE para último sobrenome na V9](Justificativa_Retificacao_Proxy_IBGE_Ultimo_Sobrenome_V9_20261012.md). Esse registro documenta a divergência introduzida na #951, a correção da #955, as alternativas rejeitadas, os testes e os limites de ativação, sem alterar a DC-LK-TF.

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

## Revisão de consistência de TF — 19/10/2026

O preview da calibração `V9TrainSqlFirstNameUCalibration` confere os estados
informados com a classificação dos nomes presentes no próprio par antes de decidir
se TF é aplicável. Um estado desatualizado não pode transformar silenciosamente
um `ExactFirst` com peso positivo em correção ausente. Configurações inválidas
(lado desconhecido, piso fora de [0,1] ou suporte não positivo) também são
rejeitadas nos estados aos quais a TF de prenome não se aplica.

As regressões distinguem explicitamente frequência 1 (ajuste `ln(U)` com peso 1),
fator de Bayes 1 (frequência igual ao baseline U), peso TF 0 (ajuste desativado) e
frequência 0 (entrada inválida, nunca substituída por BF=1). Não se impõe ordem
artificial entre EXACT e HIGH: a TF deve respeitar a unidade do estado, e a razão
M/U conjunta estimada continua determinando sua evidência. HIGH permanece sem
ajuste TF até existir contrato específico; isto não autoriza promoção operacional
nem demonstra precisão, recall ou FPR em TEST independente.

`ExportFrozenSnapshot` exporta o mesmo fit TRAIN e, quando o peso TF é positivo,
inclui a referência nominal fixada, suas frequências e o baseline U condicional
por prenome estimado exclusivamente em TRAIN. Não consulta novamente a versão
ATIVA nem estima parâmetros ao carregar o artefato. A exportação exige os passes
de blocking e a identidade da projeção informados no frame original; parâmetros
de exportação divergentes são rejeitados. O fingerprint do fit também vincula
esses metadados. O frame com metadados explícitos rejeita uma referência TF de
outro blocking. Fixtures antigas somente com pares continuam aptas a ensaios,
mas não podem gerar um artefato exportável com proveniência inventada depois do
ajuste. O artefato com TF contém dicionários de termos nominais e deve receber os
mesmos controles de acesso da referência; logs e relatórios permanecem agregados.

## Compatibilidade

A entrega contém biblioteca/diagnóstico e logs read-only no `GENERATE_DRAFT` histórico. **Não muda** `Rank`, `Resolve`, `VALIDATE`, `ACTIVATE`, `TermFrequencyScoring` V8, banco, contrato persistido, pesos ou snapshots históricos. V8 permanece apenas para replay; novos conceitos são V9.

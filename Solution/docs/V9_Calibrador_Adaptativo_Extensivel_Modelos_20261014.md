# Calibrador adaptativo V9 — contrato extensível para diferentes famílias de modelos

## Decisão
Não transformar a classificação A/B/C/D em parâmetros nominais fixos do
calibrador. O motor deve **descobrir** configurações e parâmetros a partir
de TRAIN e compará-los em VALIDATION, mas adaptar-se tanto ao modelo
nominal Fellegi–Sunter V9 quanto a futuras famílias, com versões e
proveniência separadas. O modelo escolhido deve ser governado e
reproduzível. O diagnóstico automático de suficiência DT-23 **não
dispara nova calibração**. A execução de calibração continua **sob
solicitação manual** e a ativação/promoção é outra decisão manual.

## Implementado nesta PR: coordenador de estudos, não produtor operacional
`AdaptiveCalibrationModelSearch` aceita uma coleção de implementações
`IAdaptiveCalibrationFamilyAdapter` identificadas por `ModelFamily` e
`AdapterVersion`, com `AdaptiveCalibrationModelOption`
(`OptionId`, fingerprint SHA-256 da configuração).

Contrato de execução:
1. Requer solicitação manual, duas fontes distintas marcadas
   `TRAIN` e `VALIDATION`, fingerprints e evidência de isolamento
   de pessoas-base. Qualquer `UsesTestLabels=true` ou partição
   repetida é proibida.
2. Para cada família/configuração, invoca **somente**
   `FitTrain(option, train)`; um adaptador não pode apresentar
   parâmetros treinados em outro SHA. O resultado é um rascunho,
   sem modelo persistido/credencial/ativação.
3. Em seguida chama `EvaluateValidation(fit, validation)` do
   **mesmo** adaptador e examina contagens globais `TP`,
   `FP` aceitos, `M` global sem CPF, custo de blocking U e
   fanout P99, abstinência e grupos rotulados independentes.
   **FDR observada = FP/(TP+FP)** e não `U/(M+U)`.
4. Se não existe orçamento **explícito, versionado, vinculado
   ao SHA** (recall ≥95%, FDR pontual ≤1%, teto de fanout
   e `n_min` por identidade independente), apresenta as
   configurações sem sugerir uma opção. Com orçamento,
   ordena reprodutivelmente por recall, FDR, P99 e
   desempate de família/ID, podendo sugerir outra família.
   Se nenhuma passa, o resultado é `NO_MODEL_MEETS_DESCRIPTIVE_BUDGET`.
   Família/versão ausente, snapshot inválido, parâmetro de outra
   fonte, amostra insuficiente ou TEST => **falha fechada**.
5. Todos os resultados retornam
   `ConfidenceBoundForFdrMeasured=false`,
   `OperationalModelCreated=false`,
   `AutomaticRecalibrationOrActivationAllowed=false`.
   O máximo é **sugestão experimental**: limite superior
   estatístico de FDR, abstenção, paridade física e validação
   independente permanecem pendentes.

Esse código **não registra automaticamente adaptador FS em produção**
nem substitui o Calibrador/Runner em execução. Adicionar uma
nova família exige um adaptador com estimador/validador próprio,
versões explícitas, testes e seleção governada; a fronteira da
interface evita acoplar o orquestrador a um algoritmo particular.
Os testes unitários exercitam duas famílias hipotéticas, isolam
TRAIN/VALIDATION, rejeitam TEST, provam desempate, orçamento e
ausência de ativação. Os resultados de adapters fictícios
**não constituem desempenho de nenhum modelo real**.

## Fases ainda abertas
- Implementar o adaptador **real** FS V9 (conjuntos m/u
  nominais 9x9, TF na unidade correta, seleção de thresholds
  pela métrica de FDR e abstenção), estimado e observado
  no mesmo conjunto de pares SQL induzidos pelo blocking.
- Implementar conectores de famílias alternativas
  caso façam sentido estatístico, com catálogos de
  capacidades, versões, checkpoints, cancelamento e auditoria.
- Incluir UCB unilateral de FDR e limites por estrato,
  bootstrap por **identidade-base**, ESS e suporte por célula.
  O orçamento descritivo P99/FDR pontual **não é
  certificação**, nem permite escolha operacional.
- Integrar `CalibrationReplayManifest`, snapshots de
  parâmetros e um pipeline manual de confirmação e
  ativação com DT-15; sem criar bypass de controles.
- Comparar honestamente A/B/C/D e adaptativo real contra
  as composições (PR experimental paralela #1087);
  somente depois de Rank/Resolve calibrados medir FDR
  dos vínculos aceitos. Proxies D0/D1 antigos **não são
  estratégia adaptativa**, e célula mãe+DOB sem nome
  compartilhado não pode ganhar status produtivo.
- Separar erro sintético `correlated` (18% hipotéticos)
  e `clean` de erros empíricos municipais por gestor;
  TEST não pode ajudar o sistema a selecionar vencedor.

**Nenhuma modificação** em V8, V9/DT-15 ativo, Gold,
Calibrador atualmente ativo, FS/TF/m/u/prior operacionais,
IBGE original, HML/PROD, JornadaLocal ou NODE/NAS.


## Integração FS V9 real no coordenador — 15/10/2026

A PR #1093 acrescenta `V9FellegiSunterAdaptiveCalibrationAdapter`, primeira família concreta do coordenador que atravessa o scorer C# real em vez de usar adaptadores fictícios. O contrato recebe um frame candidato do ramo probabilístico sem CPF, já materializado pelo blocking, com partições TRAIN/VALIDATION disjuntas por identidade-base e fingerprints SHA-256.

Em TRAIN, para cada opção versionada de `alpha` e threshold, o adaptador:

1. exige o catálogo/esquema V9 corrigido da #1092;
2. recomputa os níveis de pessoa e mãe via `CompositeNominalComparisonV9`;
3. estima as 81 células conjuntas m/u no universo candidato recebido;
4. estima o prior no mesmo frame;
5. estima a distribuição binária de nascimento no mesmo TRAIN;
6. congela células/parâmetros por SHA-256 antes de qualquer leitura de VALIDATION.

Em VALIDATION, o mesmo fit é avaliado sem refit por `FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown`, produzindo TP, FP, recall aceito, FDR pontual, abstenções e fanout P99 para o `AdaptiveCalibrationModelSearch`. TEST é rejeitado pelo contrato e a saída continua não operacional: não persiste snapshot, não ativa V9/DT-15 e não autoriza recalibração automática.

### Limites explícitos desta etapa

- A V1 ainda **não aplica TF positivo**. Isso é deliberado: não inventar frequência ou tratar ausência de referência como TF neutra. A integração da política TF da #1092 deve ser um passo separado, com referência/unidade compatíveis e parâmetros versionados.
- O adaptador aceita um frame candidato materializado, mas #1093 ainda **não é o leitor do SQL Server físico**. O próximo incremento deve produzir esse frame a partir do censo SQL congelado, preservando exatamente o ruleset/fingerprint e sem `TOP N`.
- FDR nesta etapa é pontual em VALIDATION; não é UCB de certificação.
- O TEST histórico das seeds 42/43/44 permanece consumido; a certificação futura usa novo TEST não observado.


## FDR: triagem nominal conservadora — 17/10/2026

O coordenador diagnóstico `ADAPTIVE_CALIBRATION_MULTI_MODEL_TRAIN_VAL_DIAGNOSTIC_V2`
adiciona `NominalPairwiseFdrUpper95` (Wilson unilateral de 95%, com
`z=1,6448536269514722`) e `MeetsNecessaryNominalFdrScreen`.
O **ponto** `FP/(TP+FP)` continua registrado, mas não basta para sugerir
um modelo: a triagem nominal também precisa ter limite superior <= ao
orçamento predefinido. Zero FP em apenas dois vínculos aceitos não equivale
a risco baixo: o limite nominal aproxima-se de **57,5%**. Sem suporte,
a busca retorna `NO_MODEL_MEETS_PRELIMINARY_FDR_SCREEN`.

**Limitação estatística fundamental:** pares SQL compartilham pessoas e
candidatos. O Wilson por par supõe ensaios Bernoulli independentes e,
portanto, é apenas uma **barreira necessária de triagem**, NÃO um limite
de confiança válido do erro de linkage. `ConfidenceBoundForFdrMeasured`
continua `false` porque **o limite válido por conglomerado de
identidade-base não foi estimado**. Mesmo quando a triagem nominal passa,
a sugestão se chama `SUGGESTION_ONLY_UNCERTIFIED_GROUP_FDR_AND_PHYSICAL_SQL`
e continua **não operacional**. A certificação exige estimação por
conglomerados/splits independentes, estratos e tamanho efetivo, mesma
decisão final Rank/Resolve no SQL físico, TEST novo lacrado e DT-15.
O orçamento `ADAPTIVE_CALIBRATION_PREVIEW_BUDGET_V1` não foi
alargado; apenas acrescentou-se uma restrição conservadora à prévia.


## Diagnóstico de recall global no SQL físico — 18/10/2026

O probe SQL do calibrador possui **dois universos verdadeiros diferentes**:
`M_eligible` (todos os pares da mesma identidade-base entre gestores diferentes,
contados antes do blocking) e `M_sql` (subconjunto de pares recuperados
pelos passes SQL deduplicados). O adaptador FS continua reportando métricas
de VALIDATION *condicionadas aos pares SQL*, que **não** são recall global
quando `M_sql < M_eligible`.

`V9PhysicalSqlOptionGlobalCoverage`, produzido pelo mesmo probe físico
por opção FS (sem outro scorer), explicita três taxas com denominadores
distintos:
- `BlockingTruePairRecall = M_sql / M_eligible`;
- `ConditionalAcceptedPairRecall = TP_threshold / M_sql`;
- `GlobalAcceptedPairRecall = TP_threshold / M_eligible`.

Também reporta `ThresholdAcceptedPairFdr = FP_threshold /
(TP_threshold + FP_threshold)`, **indefinido** quando não há aceitos.
O método reconcilia contagens do scorer com os pares M/U SQL físicos;
contagens incompatíveis abortam. `MeetsGlobalRecallBudget` é somente
um **indicador descritivo** relativo à política recebida, nunca autorização
de seleção operacional.

**Limite essencial:** `TP_threshold` e `FP_threshold` são decisões
individuais de limiar do FS, **não vínculos finais após Rank/Resolve e
controle de conflitos de UUID**. Assim, nenhuma dessas taxas certifica
o resultado final. Também faltam TF positiva compatível, intervalo
válido de erro por grupo de identidade, latência ponta a ponta com
repetições e TEST novo lacrado. O output declara explicitamente
`FinalRankResolveEvaluated=false`, e a V9 segue inativa.


### Bloqueio de sugestões que só atingem recall condicional (18/10/2026)

O relatório genérico de `AdaptiveCalibrationModelSearch` calcula recall
no frame de candidatos recebido. No censo físico V9, isso é **condicional**
aos pares M que passaram pelo blocking. Portanto, não basta publicar o
recall global separadamente: o probe aplica também
`EnforcePhysicalGlobalRecallForSuggestion` a seu relatório, reconciliado
com `ValidationGlobalCoverage`. Se a sugestão condicional não atende ao
mínimo explícito de recall **TP_threshold / M_eligible**, o resultado
físico deixa de sugerir modelo e retorna
`NO_MODEL_MEETS_PHYSICAL_GLOBAL_RECALL_BUDGET`. Opções ausentes,
duplicadas ou uma sugestão sem correspondência no frame SQL causam
falha fechada. O coordenador genérico permanece puramente diagnóstico.

Esta é somente uma **condição necessária da prévia física**: o
`TP_threshold` do FS não representa vínculos finais após Rank/Resolve.
Não usar esse relatório para congelar/ativar o modelo nem para afirmar
certificação por identidade-base. TEST segue não utilizado.

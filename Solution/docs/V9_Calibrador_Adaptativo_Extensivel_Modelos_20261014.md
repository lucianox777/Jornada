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


### Cobertura integral das identidades-base, incluindo ausência de candidatos

Além do denominador `M_eligible` de pares verdadeiros, o relatório do
probe SQL físico retém, por split, o **universo completo de identidades**
das observações recebidas, antes de aplicar as regras: `BaseIdentitiesBeforeBlocking`,
`BaseIdentitiesRepresentedBySql` e `BaseIdentitiesWithoutSqlCandidates`.
A razão `BaseIdentityCandidateCoverage` é **cobertura do cadastro de
identidades com ao menos um candidato**, não recall de vínculo e muito
menos taxa de falsos vínculos.

`SummarizeIdentityCohort` recusa ids de base ausentes, duplicados,
candidatos externos à partição e tentativas de incluir TEST. Zero
candidatos não transforma uma identidade em evidência negativa: esse
grupo permanece no denominador. Isso prepara futura análise agrupada
da incerteza sem o viés de descartar as identidades com grau zero.

**Atenção:** `TrainDistinctBasePeople`/`ValidationDistinctBasePeople`
mantêm sua semântica histórica de **identidades com pelo menos um par SQL**;
não devem ser citados como tamanho integral do corpus. A presença de
um grupo no censo por si só **não comprova amostragem iid**, cap por
identidade, decisão final Rank/Resolve ou certificação.


### Identidades completas no próprio adaptador FS — correção de suporte (18/10/2026)

A métrica da #1137 já identificava o censo de pessoas **antes** do
blocking, inclusive identidades que não geraram pares SQL; contudo,
o probe ainda informava ao coordenador `AdaptiveCalibrationSlice`
apenas o subconjunto de identidades encontradas em pares candidatos.
Isso podia superestimar a evidência de suporte sobre uma população
selecionada pelo blocking e apagar identidades com grau zero.

A ponte SQL agora fornece `TrainPreBlockingBaseIdentityIds` e
`ValidationPreBlockingBaseIdentityIds` ao
`V9FsAdaptiveCandidateFrame`. O adaptador toma cópia imutável,
confere que todos os grupos encontrados nos pares SQL fazem parte
dos respectivos censos, rejeita identidades duplicadas, vazias e
vazamento entre TRAIN e VALIDATION. `AdaptiveCalibrationSlice`
e `IndependentlyLabeledIdentityGroups` passam a usar o **número
integral de grupos de entrada**, com ou sem candidatos. A antiga
interface sem censo permanece para fixtures unitárias legadas, mas
o fluxo físico canônico sempre envia ambas as partições completas.

**Importante:** contar uma identidade ausente do SQL não demonstra
rotulagem completa, amostragem iid nem suporte independente
validado. Os valores ainda são diagnósticos e o mecanismo V9 não
passa a ativar ou recalibrar automaticamente.


### Alvo de recall definido pela política, sem piso universal (18/10/2026)

`AdaptiveCalibrationPreviewBudget.MinimumGlobalAcceptedRecall`
é um limite **explícito de estudo** em `[0,1]`, vinculado à mesma
identidade de estudo e à versão do orçamento. Não há piso universal
de 95% embutido no coordenador de modelos. Por exemplo, um ensaio
pode declarar `0.90` sem receber uma reprovação por um `0.95`
implícito; mas declarar `0.90` **não afirma que 90% seja aceitável
para produção**. Orçamentos ausentes, números NaN/infinitos ou fora
de `[0,1]` falham fechado.

A fonte da métrica permanece declarada: o coordenador genérico vê
o recall **condicional ao seu frame**; o probe SQL físico (#1135/#1136)
corrige o denominador global `TP_threshold/M_eligible` antes de
admitir qualquer sugestão. O limite estatístico por identidade,
decisões finais Rank/Resolve e TEST novo lacrado seguem exigências
independentes. Nada nesta mudança altera o limite de risco existente,
desabilita gates de CI ou autoriza ativação automática V9.


## V9 — TF positiva de prenome no MESMO adaptador FS C# (18/10/2026)

O adaptador FS V9 evolui de `V1` para `V2` **somente no fluxo
diagnóstico TRAIN/VALIDATION**. O mesmo `FitTrain` congela as 81
células conjuntas e, para cada opção com `TfWeight > 0`, calcula
em **TRAIN U do mesmo SQL físico** a distribuição da **unidade de
prenome** entre pares `ExactFirst`. A probabilidade de referência
`UnitReferenceU` é `(contagem_U(termo) + alpha) /
(total_U_ExactFirst + alpha * (K + 1))`, onde K é o número de termos
U distintos observados. Esse baseline é **condicional à seleção SQL
e ao estado ExactFirst**, não frequência marginal na população:
deve ser interpretado apenas como estimativa preliminar de treinamento.

Na mesma avaliação `EvaluateValidation`, o scorer
`FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown`
recebe `CompositeNominalTfPreviewV9` da fonte
`FrozenIbgeNominalTfV9`, para o **mesmo termo** e com peso explícito
na opção (`0 <= TfWeight <= 1`). Somente `ExactFirst` é elegível
a essa **marginal IBGE de prenome**. Níveis integrais, sobrenomes,
reversões e comparações não exatas permanecem sem TF, **por decisão
explícita de elegibilidade da unidade**, não por fingir frequência
desconhecida igual a zero/um. Com peso zero, a contribuição TF é
desabilitada intencionalmente (BF=1). Se peso positivo e termo
`ExactFirst` desconhecido no U TRAIN, suprimido no snapshot ou
sem baseline de suporte, **a avaliação falha fechado**; não há
fallback para TF neutra.

O `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` aceita a
referência IBGE fixada por parâmetro opcional, e verifica o fingerprint
do ruleset de blocking **antes da execução SQL** quando existir
opção com TF positiva. Não escolhe outra versão publicada nem faz
lookup oportunista da referência `ATIVA`. A versão da fonte publicada,
digest da materialização, fingerprint do SQL e distribuição U TRAIN
compõem a identidade do fit; pesos e lados entram no fingerprint da
configuração. Nenhuma propriedade da referência IBGE substitui
estimativa U ou seleciona peso; `V9FsAdaptiveOptionConfiguration`
fornece a grade explícita de pesos e o coordenador avalia a mesma
VALIDATION, sem acessar TEST.

**Limitações não resolvidas:** este baseline baseado em pares U do
blocking não demonstra independência entre identidades, nem valida
uma massa-base sintética integral de 30 mil para a TF. O teste de
corpus sintético, compatibilidade m/u por unidade e estratos,
seleção adaptativa sob risco FDR por grupos, decisão final
Rank/Resolve, P95/P99 e TEST novo lacrado são exigências distintas.
A versão V2 continua `PREVIEW` e não altera `GENERATE_DRAFT`,
modelos publicados ou a V8/Gold.


## Materialização de frequências por identidade-base sintética (18/10/2026)

O censo `V9SyntheticBaseFrequencyAudit` passa a oferecer
`MaterializeCanonicalTrainUnit` para recuperar **valores normalizados
com frequências** da massa-base sintética completa de 30.000 identidades,
sem duplicar pessoas pela quantidade de registros ou de candidatos.
A entrada exige uma identidade única por pessoa, declaração fixada
`SINTETICO_IBGE` (corpus, gerador, seed e hipóteses), 30 mil
identidades e partições distintas TRAIN/VALIDATION/TEST.

**Somente nomes de pessoas TRAIN** alimentam a contagem; os demais
splits participam exclusivamente da verificação estrutural do universo.
As quatro unidades `FullName`, `FirstLastJoint`, `FirstName` e
`LastContentSurname` são contadas diretamente, sem multiplicar
marginais nem usar o nome de mãe como se fosse o nome da pessoa.
O lado `MOTHER` é medido em distribuição separada. `f(value)`
usa como denominador o número de bases TRAIN **com essa unidade
efetivamente observada**, expondo separadamente os casos ausentes;
valores abaixo do suporte mínimo podem ser suprimidos, **sem
renormalizar** as frequências restantes ou inferir frequência zero.
A materialização conserva um digest estável que incorpora o mapa
TRAIN de identidade-base → valor normalizado, inclusive termos
suprimidos, além de versão, parâmetros e fonte declarada.

Essa é uma **distribuição empírica do bootstrap sintético em TRAIN**,
não a estimativa `U` condicionada ao SQL nem a TF do IBGE real:
o scorer usa a fonte TF fixada e o `U` do mesmo universo comparativo
como contratos diferentes. O digest é prova de consistência interna,
**não autenticação externa** do corpus ou da amostragem; declarar
30 mil pessoas não demonstra por si só que o tamanho é suficiente
para nomes raros, caudas, FDR ou produção. O método não altera o
Rank/Resolve, não lê rótulos TEST e não ativa a V9.


## Evidência de TF no probe SQL físico de 30 mil — integração (18/10/2026)

O probe `V9PhysicalSqlBlockingProbe` existente já autentica os bytes
`pessoas_verdade.csv` e `observacoes.csv` contra os hashes
de `generation-manifest.json` antes do processamento SQL em
`JornadaHarness`. Agora ele aproveita essa mesma fonte verificada
para executar `MaterializePhysicalEvidence` em **oito** combinações
de lado (`PERSON`/`MOTHER`) e unidade nominal
(`FullName`, `FirstLastJoint`, `FirstName`,
`LastContentSurname`), comparando todos os agregados com
`TrainBaseFrequencyAudit`.

O relatório `TrainBaseNominalFrequencySnapshots` contém exclusivamente
**digest SHA-256 por unidade e contagens agregadas**, não nomes,
frequências de um indivíduo, CPF ou identificadores de pessoa.
A identidade de bootstrap usa o fingerprint e hash da massa-base,
`generator_version`, `seed` e hash do manifesto verificado como
identificação declarativa das hipóteses. Esta camada **não demonstra
assinatura/autenticidade externa** do gerador, apenas coerência e
rastreabilidade dentro do artefato físico lido.

Essa integração **não substitui U condicionado ao SQL**, não conecta
automaticamente oito ajustes TF ao scorer, não prova suficiência
de 30.000 para caudas, nem altera TEST ou a decisão Rank/Resolve.
Nenhum fingerprint de auditoria autoriza promoção operacional.

## SQL físico: P95/P99 repetidos no próprio probe adaptativo (18/10/2026)

O `V9PhysicalSqlAdaptiveCalibrationProbe.RunAsync` pode agora
receber `repeatedSqlMaterializationRuns` explicitamente: **0
(desabilitado por padrão) ou 20..100 execuções reais**. A coleta
ocorre somente depois da primeira materialização SQL e da
verificação da cardinalidade; chama o método existente
`RepeatPhysicalMaterializationAsync` usando **a mesma conexão,
o mesmo ruleset e a mesma sessão efêmera JornadaHarness**.
Cada execução recria a união física, recompõe o índice e confere
o número de pares distintos. Divergência de cardinalidade,
amostras não finitas, domínio inválido ou tentativa de usar TEST
provocam falha fechada.

Quando requisitado, o resultado contém
`RepeatedSqlMaterializationQuantiles` calculado com
`V9PhysicalSqlCostQuantiles.FromMeasuredMilliseconds`:
contagem de execuções, P95 e P99 por nearest-rank e máximo.
O P95/P99 de **materializar candidatos SQL** não é latência
de `Rank/Resolve`, de consulta de usuário, de pipeline completo,
nem garantia sob carga operacional. A medição inicial
`PhysicalSqlTimings` de cinco **fases distintas** continua separada,
pois cinco fases não são cinco repetições independentes. Essa
opção não cria orçamento operacional, não liga ativação e não
modifica as probabilidades ou a busca FS V9.


## 18/10/2026 — escolha do perfil do bootstrap sintético

A configuração inicial do único calibrador C# V9 deve ser inferida por
`FitTrain` usando apenas a nova massa **clean** sintética e a fonte de referência
IBGE pinada, com seleção preliminar no VALIDATION correspondente. Os valores
são **bootstrap provisório** e não parâmetros finais. A antiga sensibilidade
`correlated` fica somente como histórico, com hashes, artefatos e nomes de
perfil preservados. Não executar automaticamente novos estudos correlated.
A literatura **não** valida o clean como representativo do município;
por isso o experimento registra agora divergências por campo de pares M
TRAIN/VALIDATION e separa taxas por observação de erros entre pares.
Calibração empírica real por gestor/sistema, FDR pós-Resolve com suporte
independente, TEST inédito lacrado e decisão manual continuam obrigatórios
para eventual ativação. Ver
`V9_Bootstrap_Clean_Provisorio_20261018.md`.

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
- **Já entregue em #1093:** adaptador FS V9 com m/u nominais 9x9, prior,
  nascimento e avaliação do mesmo fit em VALIDATION. **Pendente:** TF positiva
  na unidade por pessoa-base (peso [0,1] sem dupla contagem), escolha governada
  de thresholds com FDR UCB por identidade-base e abstenção explícita.
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
- **Atualização #1094 (integrada):** `V9PhysicalSqlAdaptiveCalibrationProbe` já
  materializa o frame SQL Server físico em `JornadaHarness` com `DISTINCT`, sem
  `TOP N`, preflight de incidências, fanout e segregação TRAIN/VALIDATION;
  rejeita TEST. Ainda falta vincular o exportador canônico e o ruleset/pass list
  do snapshot selecionado pelo Calibrador e medir custo físico P95/P99.
- FDR nesta etapa é pontual em VALIDATION; não é UCB de certificação.
- O TEST histórico das seeds 42/43/44 permanece consumido; a certificação futura usa novo TEST não observado.

## Estado de integração e gates — 16/10/2026

- **#1094 está integrada:** a existência de frame SQL físico não é mais lacuna.
  A lacuna restante é sua vinculação ao exportador canônico, regras versionadas
  do calibrador e evidência de custo/latência SQL; CI verde não comprova FDR.
- **#1109 está aberta (não integrada nesta data):** propõe snapshot da lista
  de pares e verificação da proveniência do fit (fingerprints TRAIN e
  configuração). Não atribuir essas garantias à `master` antes do merge.
- **TF positiva permanece desabilitada no adaptador FS V9 V1** (`TF=DISABLED_V1`).
  A integração deve usar frequências por identidade-base no TRAIN, o mesmo
  universo candidato para U, peso versionado no intervalo [0,1], sem somar TF
  duas vezes e sem tratar ausência de referência como neutralidade automática.
- **FDR permanece pontual** no coordenador: `ConfidenceBoundForFdrMeasured=false`.
  Não certificar usando variância ingênua de pares correlacionados; requer UCB
  unilateral com reamostragem por identidade-base, suporte/ESS, estratos e
  abstenção. O status literal legado
  `SUGGESTION_ONLY_UNCERTIFIED_FDR_CONFIDENCE_AND_PHYSICAL_SQL` ainda contém
  `PHYSICAL_SQL`, embora o probe físico já exista; interpretá-lo como
  **não certificado**, não como prova de ausência do SQL.
- TRAIN estima, VALIDATION compara e um **novo TEST lacrado** será necessário
  para certificação independente. O TEST histórico das seeds 42/43/44 está
  consumido; não pode ser reutilizado para escolher parâmetros. Nenhuma
  sugestão descritiva autoriza ativação de V9/DT-15, recalibração automática,
  promoção para HML/PROD ou alteração de V8/Gold, IBGE original,
  `JornadaLocal` e NODE/NAS.

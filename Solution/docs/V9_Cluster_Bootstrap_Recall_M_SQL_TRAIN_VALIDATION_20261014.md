# V9 — incerteza exploratória do recall físico SQL por bootstrap de identidades-base

## Questão e origem (P0 #999)

O diagnóstico das PRs #1014–#1016 já identificou o teto **das onze famílias SQL concretamente testadas**, em TRAIN/VALIDATION dos corpora sintéticos de 30 mil identidades por seed. O recall de M elegíveis pré-blocking ficou na faixa de **70,78% a 72,11%**, e as seis famílias antigas não adicionaram qualquer par à união das cinco novas, em nenhuma das seis avaliações. A medida observada é `M_SQL_recuperados / M_elegíveis`, **não** recall de uma amostra condicionada aos pares que o blocking já encontrou. A sobreposição entre observações da mesma identidade-base invalida tratar pares M como Bernoulli independentes.

### Cuidado crucial com o alegado “95% do Calibrador”

No código atual, `BlockingRuleSetSearchOptions.MinimumTrueMatchRecall=0.95` é **uma restrição mínima CONFIGURADA** para o `BlockingRuleSetSearch.SearchBest` do Worker, calculada sobre as amostras independentes que o próprio Worker lê da Gold em `ReadDeterministicMatchedPairsAsync` e da amostra U em `ReadGoldUnmatchedPairsAsync`. `BlockingFeatureObservationFactory` projeta essas amostras em features lógicas e o `BlockingRuleSetOptimizer` só seleciona um conjunto que cumpra aquela restrição naquela amostra. **O valor 95% não é, por si, uma observação de recall físico SQL neste corpus, tampouco prova de que o Worker tenha obtido exatamente 95% em alguma execução real.** Não afirmar “95% medidos contra 71%” sem artefato de Calibrador que demonstre o recall efetivamente medido e um par comparável de fingerprints, população-base, regras e gabaritos. A demonstração válida de paridade exige executar os mesmos passes/projeções sobre **o mesmo universo de M elegíveis** em SQL, como diagnosticado separadamente pela auditoria `V9SqlBlockingFrameParityAudit` do baseline ATIVO+C1. Não houve edição no Worker nesta PR.

## Instrumentação implementada

`V9SqlClusteredMRecallBootstrapAudit` é uma avaliação **descritiva em desenvolvimento**, acoplada ao `V9SqlElevenPassResidualAudit` sem recalcular M ou U. Recebe apenas as identidades-base únicas de TRAIN e VALIDATION, com quantidades de pares M elegíveis, M recuperados pelo SQL baseline e M recuperados pela união das onze famílias. O relatório nunca contém o identificador da base, apenas agregados. O código verifica que cada par M elegível pertença **à mesma base em ambas as pontas**, que o denominador M pre-SQL e ambos numeradores recuperados conciliem **exatamente** com o relatório #1016, e que cada identidade-base só pertença a uma partição.

A cada split são executadas **1.000 reamostragens determinísticas de identidades-base com reposição**, cada uma contendo tantas identidades quantos grupos-base distintos contribuem com ao menos um par M elegível. O sorteio é ordenado por base e usa fonte `SourceSeed` e manifesto/fingerprint do corpus de entrada; dentro da reamostragem somam-se os pares M de todas as observações da identidade sorteada, em vez de reamostrar arestas como se fossem independentes. Publica:
- `ContributingBasePeople`, número de bases com ao menos um par M elegível (não é a totalidade das 30 mil nem ESS);
- `EligibleM`, `BaselineRecoveredM`, `ElevenRecoveredM` e recalls observados (reconciliados com SQL);
- percentis empíricos 2,5% e 97,5% para cada recall, por reamostragem clusterizada, inclusive quando cada base sustenta vários pares M;
- fração de reamostragens cujo recall SQL onze-pass alcança a restrição 95%, designada explicitamente `ProbabilityElevenMeets95InBootstrapNotGate`: **não é chance calibrada de liberação nem gate**.

Os dados são **restritos a TRAIN/VALIDATION**. TEST fica absolutamente fora da instrumentação; não se fazem thresholds, seleção de passes nem publicações operacionais com esse bootstrap. A mesma pessoa-base de TRAIN não pode ser reutilizada em VALIDATION; a entrada falha fechada em conflito, contagens impossíveis, duplicatas, ausência de fingerprint ou fontes diferentes.

## Limites estatísticos e decisão sobre massa 30 mil

Este percentil **não é um intervalo frequentista garantido para os 11 milhões de cidadãos**: é uma reamostragem condicional a um **gerador sintético** e a um ruleset SQL específico; depende de hipóteses de intercambialidade de pessoas-base e de que as identidades do corpus representem o alvo. Nem constitui ajuste por dependência de U: cada aresta U toca **duas bases**, produz uma rede e requer análise de reamostragem/variância adequadas a essa estrutura para medir falsos candidatos, FDR e `u`. Não significa ESS independente, intervalo por célula `m/u`, limite para estados raros ou resposta definitiva sobre 30 mil. Por isso os flags `IsIndependentPairConfidenceInterval=false`, `ModelsNetworkDependenceAmongU=false` e `MayPromoteModelOrBlockingPolicy=false` são fixos.

O cálculo **não usa IBGE para criar uma distribuição conjunta fictícia**, nem transforma TF desconhecido em neutralidade (Bayes=1), TF=0 ou TF=1. Na próxima calibração, frequências conjuntas `f(value)` devem vir de uma pessoa-base **única** em TRAIN da massa sintética; as marginais originais IBGE permanecem congeladas e só são usadas na unidade estatística compatível. A decisão sobre campos básicos obrigatórios em `NOVO` decorre do contrato da fonte, não da hipótese de “todos os campos obrigatórios”. Dados esparsos continuam **inconclusivos**, não imputados.

A CI Python verifica para cada seed 42/43/44 e cada split a consistência dos mesmos numeradores/denominadores SQL, os 1.000 sorteios declarados, percentis e flags, além dos testes NUnit de reprodutibilidade após reordenar bases, contagens M ponderadas por base, falha fechada TEST/duplicata e exportação sem IDs/PII. **CI verde aprova a correção técnica; não altera o veto à operação V9/DT15.**

Nenhuma mudança em `Rank/Resolve`, V8, V9 decisória, SQL do ruleset publicado, IBGE original, `JornadaLocal`, NODE/NAS, Gold, HML/PROD. Execução exclusiva em `JornadaHarness` e `DOTNET_ENVIRONMENT=Development`.

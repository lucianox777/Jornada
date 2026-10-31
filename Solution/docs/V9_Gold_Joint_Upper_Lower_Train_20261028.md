# V9 Gold — calibração conjunta dos limiares superior e inferior (28/10/2026)

**Estado: contrato C# TRAIN V4 com seleção superior e inferior em dois problemas distintos; executor físico conjunto ainda NÃO integrado. Testes desta revisão dependem de CI no SHA exato. Não aprovado, não ativado.**

## Decisão

O calibrador C# único `V9LowerNonMatchTrainCalibrator` passa a expor `SearchJoint` com grade declarada de limiares **superior × inferior**. O superior não é um parâmetro manual fixo da solução final. Cada par exige `0 < lower < upper <= 1`; ambos aparecem na sugestão e nos fingerprints do estudo. O `Search` anterior com `FixedUpperThreshold` permanece somente como reprodução de experimentos congelados, com versão histórica inalterada.

A grade, o plano de risco e a regra de seleção precisam ser pré-registrados e congelados **antes** de gerar medições. O contrato exige SHA-256 do plano, grade, fit Gold, snapshot Gold, evidência TRAIN e execução física. Um hash de plano, sozinho, não comprova sua anterioridade: o executor deve anexar manifesto assinado/timestamp e verificar proveniência externa.

## Medição obrigatória e limitações

Cada célula upper × lower deve ser medida no **mesmo Rank/Resolve FS/TF Gold congelado**, recalculando fisicamente a decisão superior para cada upper, em LocalDB descartável e nas três ordens canônicas do Runner `BASELINE_G0_G1_G2_G3`, `REVERSE_G3_G2_G1_G0`, `INTERLEAVED_G1_G3_G0_G2` (todas as quatro ondas obrigatórias por ordem; abreviações não são admitidas). O contrato recebe métricas agregadas: falsos vínculos, falsas criações, pendências, B³ F1 e grupos independentes. Recusa TEST, VALIDATION, grade incompleta, replay de ordem ausente, **reutilização do mesmo SHA de execução física em células/ordens distintas**, busca física incompleta, SHA divergente e decisão superior não recalculada. SHAs distintos são necessários, mas não bastam: o runner ainda deve provar a execução real e sua proveniência por artefatos verificáveis.

Para cada ordem física, o denominador de oportunidades de vínculos verdadeiros e a contagem de grupos independentes são invariantes entre limiares; o contrato recusa medições que mudem esse universo mesmo com fingerprint TRAIN recalculado. Isso não substitui auditoria externa da proveniência dos grupos.

A API não recalcula por si a decisão superior: **o runner físico deve produzir e atestar os dados**. Não adaptar os resultados do `FsDecisionThresholdCalibrator` V6 (seleção em VALIDATION) ao Gold V9 TRAIN; particionamento e evidência são distintos. O `V9LowerNonMatchStudyCli` atual ainda usa superior congelado do fit e não é um executor da busca conjunta.

Não há teto percentual pré-declarado de falsos vínculos: todas as células válidas entram na exploração TRAIN. **A escolha normativa é sequencial e não otimiza quatro objetivos juntos (especificação v5.00, §7.20)**: primeiro constrói Pareto superior entre recall real de vínculos aceitos (verdadeiro aceito / `TrueLinkOpportunities` explicitamente declarado) e FDR observado (falsos aceitos / total aceito); escolhe o ideal normalizado, com piores taxas por ordem e limiar inferior de sensibilidade. Depois, fixa o superior selecionado e constrói Pareto inferior entre falsas criações físicas e pendências no Runner incremental, selecionando o joelho normalizado. Retém finalistas descritivos (joelho, menor criação falsa, menor pendência) e máximos da pior ordem. A fronteira original de quatro objetivos continua **apenas exploratória**, sem autoridade para definir parâmetros. Taxas sem denominador são indefinidas, nunca artificialmente zero; um superior sem FDR observado não pode vencer a seleção. A sugestão é apenas descritiva, não equivale a limite estatístico de FDR, validação independente, DT-15 ou ativação. Nenhum limiar é publicado ou ativado.

## Primeira ligação física do limiar superior (TRAIN)
O Runner Gold agora admite `UPPER_TRAIN=<decimal>` **somente** para execução manual `TRAIN_SELECTION` em uma LocalDB nova, opcionalmente com `LOWER_NONMATCH=<decimal>` e uma ordem completa. Primeiro valida os bytes da variante Gold canônica do fit congelado e então deriva **em memória** nova variante content-addressed mudando **apenas** `Threshold`; o Rank/Resolve e a publicação SQL recebem a nova variante e registram seu SHA e o limiar superior no recibo `jointUpperTrain`. Proibido na execução de holdout ou na captura baseline; nenhum treino, refit ou ativação. Ainda falta automatizar a matriz física completa, verificar o plano/grade preregistrados contra cada execução e produzir os `JointMeasurement` atestados com denominadores por identidade-base; esta ligação, isoladamente, não certifica a seleção.

## Orquestração manual física da grade pré-registrada (31/10/2026)

O script `Solution/scripts/v9-gold-joint-train-matrix-localdb.ps1` agora percorre **todas** as combinações upper × lower × três ordens canônicas do plano SHA-nomeado. Em Windows/LocalDB, cada célula provisiona uma instância inédita com `--v9-gold-harness-create`, executa `--v9-gold-joint-train-trial` no **Runner C# real** com `UPPER_TRAIN` e `LOWER_NONMATCH`, verifica o recibo e chama `--v9-gold-joint-train-matrix-aggregate` para seleção descritiva pelo calibrador C# único. Não utiliza mocks para gerar as medições.

Exige explicitamente `-RunnerDll`, `-DatabaseSchema`, `-TrainSelectionEnvelope`, `-FinalistValidation`, `-GoldFittedSnapshot`, `-OriginalTrainCsv`, `-PreregisteredPlan` e `-OutputRoot` **inédito**. O preflight recusa SHA de plano, seleção, variante Gold ou CSV divergentes e planos com TEST/sem solicitação manual. A validação de grade/fingerprint completa permanece no C# por célula e na agregação final. Falha parcial deixa instâncias e recibos intactos para auditoria; nunca reutiliza ou apaga banco, não toca REF/PROD e não promove modelo.

**Estado de evidência:** a existência do executor e dos testes de contrato **não prova execução física desta grade**. O script ainda precisa ser exercitado em Windows/LocalDB no SHA final, com inspeção de artefatos e CI; a matriz produzida é `IndependentlyCertified=false` e `OperationallyActivated=false`. A anterioridade do plano exige registro externo auditável, além do SHA. Nenhuma campanha 30k ou TEST independente foi disparada por esta mudança.

## Gates antes de merge técnico desta frente

- Integrar ao runner físico o replay de todas as células, recalcular superior, comprovar busca completa e proveniência por identidade-base sem TEST na seleção.
- Congelar grade, regra de seleção e plano de evidência antes dos resultados, sem teto de falso vínculo antecipado; medir incerteza e FDR dependente posteriormente, testar casos positivos e negativos e as três ordens reais, não apenas registros agregados fabricados.
- Verificar compatibilidade com loader REF e snapshot versionado (#1089), documentação transversal, CI no SHA exato e revisão sem bloqueios.
- TEST independente **novo**, somente após congelar a seleção TRAIN; não reutilizar seeds 42/43/44, 20261019001 ou holdout 20261025001. Aprovação institucional e ativação são gates separados.

A PR #1199 permanece independente, default OFF e bloqueada pelos gates positivos DT-15/digest/atomicidade. Esta frente não modifica SQL operacional, REF ativa, Gold real, V8, IBGE, NAS, HML ou PROD.

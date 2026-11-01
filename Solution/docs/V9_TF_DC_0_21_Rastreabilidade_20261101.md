# V9 — rastreabilidade DC-V9-TF 0–21 (trabalho preparado, não certificado)

**Fonte normativa consultada:** `Solution/docs/Indice_Decisoes_Vigentes.md`,
seção `DC-V9-TF-20261031`. Essa seção enumera **0 a 19**, não 0 a 21.
Não inventar as decisões 20 e 21: estão explicitamente marcadas **SEM TEXTO
CANÔNICO LOCALIZADO** até conciliação da norma. Evidência de CI deve estar
vinculada ao SHA exato de cada código. `5c04176c` é o último HEAD **publicado**
da PR #1241 no instante da preparação desta matriz; os itens marcados
`blob` são **não publicados, não compilados e não testados**.

| Nº | Decisão canônica resumida | Código e contrato | Teste existente / preparado | Evidência pendente |
|---:|---|---|---|---|
| 0 | Pessoa/mãe aditivos no LLR; independência é hipótese | `CompositeNominalTfPreviewV9.CalculateBilateral` | `FellegiSunterV9JointNominalTests` | CI SHA e sensibilidade a dependência |
| 1 | ExactFull usa TF do prenome, não sobrenome adicional | `V9NominalTfUnitSelection`, `V9FittedSnapshot` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | paridade física ExactFull |
| 2 | TF prenome pessoa | `UnitTfOptions(Person,FirstName)` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | efetivo no 30k |
| 3 | TF prenome mãe | `UnitTfOptions(Mother,FirstName)` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | efetivo no 30k |
| 4 | Pesos separados incluindo zero | `V9FsUnitTfOption` e snapshot por unidade | `V9FsAdaptiveFirstNameTfTrainValidationTests` | busca e escolha automática com orçamento |
| 5 | TF0 histórico como controle intocado | `V9FittedSnapshot`, 81 células | `V9FittedSnapshotTests` e controles existentes | reprodução *bit a bit* dos 97,4754%, não uma comparação aproximada |
| 6 | Simétrico penaliza frequentes e permite bonificação | `CompositeNominalTfAdjustmentModeV9.Symmetric` | `FellegiSunterV9JointNominalTests` | distribuição de ajustes físicos |
| 7 | Assimétrico só penaliza frequentes | `AsymmetricPenaltyOnly`, `min(0, ajuste)` | `FellegiSunterV9JointNominalTests` | distribuição de ajustes físicos |
| 8 | Seleção automática Pareto (sem presumir vencedor) | `V9NestedCalibrationCoordinator`, `V9AdaptiveCalibrationCoordinator`; grade de 45 opções **em blob** | `test_v9_tf_option_grid.py` **em blob** | CI, orçamento físico e escolha TRAIN; não ler TEST |
| 9 | Sobrenome IBGE em qualquer posição como proxy | `FrozenIbgeNominalTfV9` | `FrozenIbgeNominalTfV9Tests` | cobertura/estabilidade por fonte |
| 10 | ExactFirst corresponde ao prenome | `V9NominalTfUnitSelection` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | paridade de score físico |
| 11 | ExactLast corresponde ao último sobrenome significativo | `V9NominalTfUnitSelection` | `FellegiSunterV9JointNominalTests` | paridade proxy IBGE |
| 12 | Comparar bilateral × contribuição única se sobrenome compartilhado × TF0 | `V9SharedSurnameModeV9` **em blob**; único = mínimo dos dois ajustes assinados (conservador contra bonificação excessiva) | `V9SharedSurnameDeclaresBilateralOrOneConservativeContributionAndNeverInfersFamily` **em blob** | validar interpretação, contar pares `SharedSurnameCandidatePairs` e executar comparação física |
| 13 | Preservar 81 células e suporte/backoff | `FrozenFellegiSunterJointNominalV9` | `FellegiSunterV9JointNominalTests` | controle TF0 bitwise |
| 14 | Não mudar blocking | passes/fingerprints existentes preservados | `V9PhysicalSqlAdaptiveCalibrationProbeTests` | confirmar mesmo universo SQL em duas variantes |
| 15 | Reutilizar o único calibrador C# | `V9FellegiSunterAdaptiveCalibrationAdapter` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | nenhuma implementação paralela |
| 16 | Proibir frequência FullName/FirstLastJoint inventada | `NominalFrequencyUnitV9`, guardas IBGE | `FrozenIbgeNominalTfV9Tests` | teste de rejeição em fronteiras de runtime |
| 17 | Distinguir escolha zero, suporte insuficiente e sem fonte | `V9SnapshotTfFitState` | `V9FittedSnapshotTests` | testar sem legado / serialização canônica |
| 18 | Reusar candidatos, TRAIN distinto de VALIDATION, medir cobertura/FP/FDR | `EvaluateValidation`, `RankFrozenCompositeNominalV9BilateralPreview` | `V9FsAdaptiveFirstNameTfTrainValidationTests` | ensaio físico final, TEST lacrado, auditoria por identidade |
| 19 | Ajuste `peso×ln(Uref/max(freq,floor))` dentro do único LLR | `SplinkCompatibleTermFrequency.LogBayesAdjustment` | `FellegiSunterV9JointNominalTests` | U alinhado ao evento e diagnóstico agregado |
| 20 | **SEM TEXTO CANÔNICO LOCALIZADO** | não presumir implementação | nenhum teste atribuível sem norma | obter decisão assinada e mapear |
| 21 | **SEM TEXTO CANÔNICO LOCALIZADO** | não presumir implementação | nenhum teste atribuível sem norma | obter decisão assinada e mapear |

## Grade finita preparada, mas não publicada

`Solution/scripts/v9-tf-option-grid.py`: **45 configurações**: 1 controle TF0,
16 variantes com uma unidade positiva, 24 com duas unidades positivas e quatro
variantes para o par de sobrenomes compartilhados (alternativa de contribuição
única). As 44 alternativas TF positivas declaram as quatro unidades, pesos em {0, 0,5, 1}
e modos {simétrico, assimétrico}; o controle TF0 histórico não ganha campos
novos. Não é a busca exaustiva (6^4).

- Busca aninhada: **2 regras iniciais × 45 opções × 2 margens = 180**
  configurações iniciais; teto **512** incluindo refinamentos, não tempo medido.
- Estudo físico adaptativo controlado: **1 ruleset existente e imutável × 45
  opções × 3 margens = 135** configurações novas; mais **3.726** configurações históricas incluídas preventivamente na
  multiplicidade => **3.861**.
- Cada novo estudo com peso positivo exige a versão IBGE, os hashes publicados
  e materializados e o bootstrap sintético **reais e fixados**; o script recusa
  inventar estes dados ou assumir a referência ativa.
- As antigas entradas dos scripts continuam gerando os planos históricos TF0.
  Nenhum novo ensaio foi executado e nenhum valor de recall/FDR foi medido.
- Os denominadores históricos de múltiplas consultas precisam ser reconciliados
  antes de certificação; o teto desta grade **não** atesta integralidade
  do histórico de exposições anteriores.

## Pendências deliberadamente não confundidas com conclusão

A remoção simultânea de `TfWeight`, `TfSide`, `TfCalibration`,
`MotherTfCalibration` e `NotSelected` afeta arquivos .fitted.json
historicamente versionados e testes de SHA canônico. Requer **migração do
produtor/consumidor e validação de todos os snapshots**, não uma substituição
textual parcial; o HEAD `5c04176c` ainda os contém. Nomes
`FirstName` em classes de quatro unidades também aguardam renomeação
tipada e revisão de referências. Não foi criado outro motor para isso.

**Nascimento é assunto distinto:** o fit atual V9 ainda usa binário
`EXACT/DIFF`, embora exista auditoria SQL TRAIN dos sete estados
semânticos. Não declarar a classificação semântica como calibrada/ativa.

**Gates finais:** CI completa no SHA publicado, teste efetivo da nova grade,
TF0 bitwise, contagens de pares afetados no 30k manual do SHA final, TEST
independente, revisão do protocolo Gold, governança DT-15. Sem merge,
auto-merge ou ativação.

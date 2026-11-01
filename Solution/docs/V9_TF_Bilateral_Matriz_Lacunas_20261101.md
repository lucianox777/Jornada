# V9 — TF bilateral: matriz de lacunas verificadas (01/11/2026)

**Estado: diagnóstico de implementação, não certificação.** Este documento não altera DT-15, não aprova ativação e não substitui a campanha física manual de 30 mil.

## Caminho de inferência efetivo

1. `Solution/src/Jornada.Linkage.Core/V9FittedSnapshot.cs`: `V9FittedSnapshotPayload.TfCalibration` contém um único `V9SnapshotFirstNameTf` com `Side` e `Reference`. `RankPreview` passa `CreateTf` e `TfCalibration?.Side` ao ranker.
2. `CreateTf` retorna `null` quando o lado selecionado não é `ExactFirst`. Procura apenas `FirstName` e a frequência e U TRAIN dessa chave; portanto o caminho **congelado** não cobre TF em `ExactFull`, `ExactLast` ou os dois lados conjuntamente.
3. `Solution/src/Jornada.Linkage.Core/CompositeNominalTfPreviewV9.cs`: o DTO representa um ajuste em um lado; o avaliador valida compatibilidade de unidade com nível e produz um único `LogBayesAdjustment`. **Decisão 0:** somar os dois ajustes calibrados, um de pessoa e um de mãe, no mesmo LLR conjunto. É proibido duplicar o ajuste no mesmo lado ou somar ajustes sem referência U compatível. **Decisão 1:** em ExactFull, usar o prenome FirstName IBGE como proxy explícito (V8); FullName requer fonte real observada.
4. `Solution/tests/Jornada.Tests/Unit/FellegiSunterV9JointNominalTests.cs` já testa ajuste diagnóstico de `ExactFirst`, rejeição de referência incompatível e `ExactFull` com referência independente. Isso **não comprova** suporte equivalente no snapshot congelado.

## Critérios de aceite para a correção funcional

| Critério | Estado em 01/11 | Prova exigida |
|---|---|---|
| TF pessoa e mãe no mesmo par | **Pendente** | Caso com duas frequências distintas e referência U consistente; comparação com cálculo esperado |
| `ExactFull` no snapshot congelado | **Pendente** | Proxy FirstName IBGE para nome inteiro igual, round-trip de snapshot, score idêntico antes/depois |
| Último sobrenome `ExactLast` | **Pendente** | Teste de projeção do último sobrenome e referência por unidade |
| Modos zero, simétrico e assimétrico | **Pendente no caminho congelado** | Scores e seleção de modo reproduzíveis, sem misturar TRAIN/TEST |
| Falha fechada por frequência/U ausentes | **Parcial: primeiro nome** | Testes para todos os lados e níveis habilitados |
| Sem dupla contagem do LLR nominal conjunto | **Parcial: ajuste único** | Invariante do scorer bilateral com parâmetros calibrados |
| Persistência e serialização canônica | **Pendente para contrato ampliado** | SHA e resultados estáveis após desserialização |
| Matriz completa DC-V9-TF itens 0–21 | **Pendente** | Arquivo, teste e evidência por item; não presumir cobertura |
| Campanha física 30k no SHA final | **Não executar ainda** | Disparo manual somente após correção e congelamento |

## Sequência de execução

1. Acrescentar testes falhando para contrato bilateral, `ExactFull`, `ExactLast` e round-trip.
2. Implementar calibração consistente por lado/unidade/evento e o scorer, com validação fail-closed.
3. Conectar o contrato ao snapshot e aos parâmetros escolhidos pelo calibrador; executar testes e CI.
4. Completar rastreabilidade DC-V9-TF 0–21 e validar TRAIN/VALIDATION/TEST e Gold incremental em três ordens.
5. Congelar SHA; só então considerar **uma** campanha 30k manual e sua evidência própria.

**Restrições:** sem retrocompatibilidade obrigatória em DEV; sem piso de recall ou teto FDR arbitrário; sem certificação por recibos de outro SHA; sem alteração de CI para disparar 30k automaticamente; sem HML/PROD.

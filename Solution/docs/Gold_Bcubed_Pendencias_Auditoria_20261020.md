# Gold incremental — B-cubed e penalização explícita de pendências

**20/10/2026.** Adição da biblioteca pura `GoldPartitionBcubedAudit`
para avaliar agrupamentos após publicação. Esta entrega é um componente
diagnóstico testado e reutilizável; **não altera** o Runner, a
política de `NOVA_IDENTIDADE`, a V9 ou o ensaio físico de 30 mil
registros. A integração da chamada na saída de
`V9GoldHarnessCommand` é uma entrega posterior, após a aprovação
da instrumentação e o fechamento da PR #1162.

## Contrato de entrada e escopo

- `truthBySource`: **todas** as observações rotuladas
  independentemente do corpus TRAIN; chave observacional e
  identidade-base sintética verdadeira.
- `goldBySource`: somente observações com uma referência Gold
  **efetivamente publicada** ao final das ondas e da reavaliação.
  Nenhuma observação sem referência é excluída do denominador global.
- Observações fora do gabarito, UUID vazio e gabarito vazio ou com
  rótulos ausentes causam erro. A saída contém somente agregados.
- Os rótulos são usados estritamente **depois** da publicação e jamais
  na consulta de blocking, scoring, escolha ou no Gold operacional.

## Métrica convencional, com fonte pendente como singleton

B-cubed convencional atribui a cada observação `i` precisão
`|C(i) ∩ G(i)|/|C(i)|` e recall
`|C(i) ∩ G(i)|/|G(i)|`, promediados sobre todas as observações.
`G(i)` é a identidade verdadeira; `C(i)` é o cluster previsto.
Para observação sem referência publicada, a versão convencional
trata a observação como **cluster singleton de si mesma**.

A convenção torna a precisão de uma observação pendente igual
a 1 e o recall `1/|G(i)|`. Se todas as pessoas verdadeiras forem
singletons, pode produzir P=R=100% **mesmo com todas pendentes**.
Por isso, os campos `StandardPrecision`, `StandardRecall` e
`StandardF1` servem à comparação com métricas convencionais,
mas **não** equivalem a cobertura operacional.

## Variante conservative coverage-aware

Para a avaliação do Gold que **efetivamente publicou** referências,
`CoverageAwarePrecision` e `CoverageAwareRecall` atribuem
P=R=0 a **cada observação pendente** e mantêm todas as observações
no denominador. Para fonte publicada, aplicam o B-cubed convencional
com o tamanho do Gold corrente e o tamanho da identidade verdadeira.
É uma **variante de cobertura**, explicitamente nomeada; não deve
ser apresentada como B-cubed padrão da literatura.

`PublishedOnlyPrecision` e `PublishedOnlyRecall` usam somente
fontes efetivamente publicadas no denominador e **não** devem
substituir as variantes globais. Quando nenhuma fonte é publicada,
ambos são `null`, não 100%.

As duas variantes preservam a penalização de fusão de pessoas
na precisão e a penalização de fragmentação de uma pessoa em
vários UUIDs no recall. A taxa `PendingSourceRate` é
reportada separadamente.

## Critérios de integração na Jornada

1. Os agregados desta biblioteca devem ser calculados a partir
   dos **mesmos** mapas finais que alimentam
   `V9GoldEndToEndAudit.Measure` e confrontados com
   `goldEndToEnd.PendingSources`.
2. Apresentar precisão/recall por pares **junto** com B-cubed
   convencional, variante de cobertura, duplicatas, fusões, FDR e
   pendência. Não transformar B-cubed em único score de aprovação.
3. Aplicar a mesma fórmula às três ordens de chegada pré-declaradas
   usando TRAIN e os UUIDs Gold finais de cada execução.
4. Não reutilizar VALIDATION consumida, não abrir TEST, não
   recalibrar FS nem ativar V9/DT-15 por causa de CI verde.

## Regressões incluídas

- Gold com clusters misturados, pessoa fragmentada e observação
  pendente: verifica P/R B-cubed e variante coverage-aware.
- Corpus todo pendente e corpus singleton com uma pendência:
  demonstra o viés de precisão da regra singleton.
- Mesma pessoa em dois UUIDs, duas pessoas no mesmo UUID e
  evidência inconsistente. Nenhum teste usa PII ou massa real.

Arquivos:
`src/Jornada.Linkage.Runner/GoldPartitionBcubedAudit.cs` e
`tests/Jornada.Tests/Unit/GoldPartitionBcubedAuditTests.cs`.

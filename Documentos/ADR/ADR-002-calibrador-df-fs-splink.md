# ADR-002 — Calibrador DF → Fellegi–Sunter com referência Splink em C#

- **Status:** Aceita para implementação/calibração; não ativa modelo em Produção
- **Data:** 2026-09-13

## Decisão

O Calibrador avaliará o fluxo `DF → Fellegi–Sunter`. DF é um primeiro estágio nominal baseado em similaridade versionada (`JARO_WINKLER@V1`) e term-frequency adjustment compatível com Splink, usando frequência populacional versionada do IBGE quando houver correspondência semântica. Se DF não resolver, seu score não é somado ao FS; o FS avalia o par com sua própria decomposição de evidências.

A Jornada não reconstruirá nomes raros suprimidos pelo IBGE e não inventará frequências. Ausência na referência permanece registrada como cobertura/censura da fonte. `INCONCLUSIVO` é resultado legítimo e pode ser reavaliado quando novas evidências entrarem na Jornada.

O candidato probabilístico completo inclui `m`, `u`, prior, thresholds, versões de comparadores, versão de TF, referência IBGE e blocking. Jornada e Splink podem produzir estimativas candidatas, que serão comparadas end-to-end no mesmo corpus independente.

Thresholds existem, mas são produtos da calibração. O split deve ocorrer por indivíduo-base antes da geração dos pares. Candidatos dominados são eliminados por Pareto em falsos positivos e falsos negativos; em empate desses erros, menor inconclusão domina. Se restar trade-off FP/FN, o Calibrador não inventa custo institucional.

Python/Splink é referência de comportamento para desenvolvimento e validação, não runtime de Produção. A lógica necessária é portada para C# com versão própria e testes de paridade. Mudança futura do Splink exige nova versão local e nova calibração.

A primeira função portada é `SPLINK_TERM_FREQUENCY_V1`: em fuzzy match usa a maior frequência dos dois lados, aceita `tf_adjustment_weight`, aceita `tf_minimum_u_value` e produz contribuição aditiva em log-Bayes-factor. A Jornada usa log natural, coerente com seu scorer FS atual.

## Evidência e replay

Cada execução deve preservar similaridade nominal, frequências esquerda/direita, frequência efetiva, indicação de censura, contribuição TF, `m/u/prior`, thresholds, versões/fingerprints, seed e resultados FP/FN/inconclusivos. Blocking e scoring permanecem mensuráveis separadamente.

## Invariantes

- DF não é somado ao FS após fallback.
- TF fuzzy usa a forma mais frequente de maneira conservadora.
- `tf_minimum_u_value` limita evidência de outliers raros.
- `tf_adjustment_weight=0` desliga o ajuste.
- A seleção Pareto não cria preferência implícita entre FP e FN.
- Nenhum nome ou frequência abaixo da cobertura IBGE é sintetizado como se fosse observado.

## Fora de escopo

Esta ADR não fixa thresholds numéricos, não escolhe a combinação final de `m/u/prior`, não altera CPF → UUID e não promove automaticamente o linkage probabilístico para Produção.

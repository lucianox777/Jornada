# Jaro–Winkler V3 — bônus de prefixo condicionado (experimental)

## Escopo
A versão `WHOLE_NAME_JARO_WINKLER_PREFIX_GATED_V3` acrescenta uma opção explícita de comparação de nome completo. O bônus Winkler só é aplicado quando a similaridade Jaro é estritamente maior que 0,7. O cálculo histórico `WHOLE_NAME_JARO_WINKLER_V1`, o alias `CompareName` e o guard de tokens V2 permanecem inalterados.

## Motivação e limites
O replay IBGE × Splink 4.0.17 registrou 53 divergências de estado em 20.000 pares sintéticos (20 TODOS e 33 FEMININO). A conferência dirigida das 53 fixtures históricas, com os estados Splink 4.0.17 congelados, mostra que a V3 coincide com o Splink em **31 casos** (todos mudaram em relação à V1) e ainda diverge em **22 casos**. O teste `ExperimentalV3_Explains31ObservedStates_AndLeaves22Unexplained` fixa essa caracterização. Isso é um diagnóstico dos 53 casos previamente divergentes, **não** a reexecução dos 20.000 pares nem demonstração de equivalência geral. A refatoração do scorer para `double` na PR #565 é independente da formação dos estados do comparador; não atribuir essas divergências ao tipo numérico do scorer sem evidência. A V3 testa apenas a hipótese do bônus de prefixo; **não** garante igualdade com o Splink, cujas demais diferenças precisam de investigação.

## Validação obrigatória antes de promoção
1. Executar os testes unitários, inclusive os 53 casos históricos, preservando seus estados C# V1.
2. Reexecutar offline os mesmos 20.000 pares e produzir matriz V1 × V3 × Splink por recorte, com hashes, estados e transições. Investigar cada divergência remanescente, incluindo limiares e normalização.
3. Se V3 for proposta para operação, versionar explicitamente o algoritmo de linkage e recalibrar m/u, prior, thresholds e gates adversariais; não reinterpretar modelos persistidos nem alterar silenciosamente V1.
4. Executar Jornada.Linkage.Conference em razão da mudança de comparador. A execução local e a promoção governada não fazem parte desta alteração.

A issue #506 e a avaliação independente de representatividade #31 permanecem separadas.
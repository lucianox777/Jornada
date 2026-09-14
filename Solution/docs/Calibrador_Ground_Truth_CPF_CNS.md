# Calibrador — ground truth CPF/CNS

**Estado:** especificação normativa complementar ao `Calibrador_DF_FS_Specification.md`.

## 1. Objetivo

Definir como o Calibrador constrói e governa rótulos positivos observados para estimação e avaliação do linkage sem introduzir uma segunda âncora de identidade nem contaminar blocking/scoring com a variável usada para definir a verdade.

## 2. Papéis arquiteturais

A implementação deve distinguir explicitamente:

- **âncora de identidade**: somente CPF;
- **fonte de rótulo**: CPF preferencial; CNS auxiliar condicionado;
- **candidate generation/blocking**: atributos independentes da fonte de rótulo;
- **scoring**: evidências independentes da fonte de rótulo.

CNS nunca cria, funde ou seleciona UUID por si só.

## 3. Estratos

Toda execução deve preservar pelo menos os estratos:

```text
WITH_CPF
WITHOUT_CPF_WITH_CNS
WITHOUT_CPF_WITHOUT_CNS
```

A suficiência da amostra não é avaliada apenas globalmente. Ela deve ser avaliada em relação ao estrato no qual o modelo será aplicado.

## 4. Ground truth por CPF

Pares positivos baseados em CPF permanecem a fonte preferencial quando:

1. há quantidade estatisticamente suficiente de pares independentes;
2. a amostra é considerada representativa para o estrato-alvo;
3. a proveniência é reproduzível e versionada.

A existência de CPF numa base de origem não torna automaticamente todos os pares utilizáveis; continuam válidas as regras de independência inter-Gestor, replay e prevenção de circularidade já estabelecidas pelo Calibrador.

## 5. Ground truth auxiliar por CNS

CNS pode rotular pares positivos de alta confiança exclusivamente como mecanismo auxiliar para o estrato sem CPF.

A elegibilidade deve ser fail-closed e considerar, no mínimo:

- `structurally_valid = true`, segundo validador homologado/versionado;
- `distinct_persons_observed <= maximum_distinct_persons_allowed`;
- `birth_date_distance_days <= maximum_birth_date_distance_days`.

`maximum_distinct_persons_allowed` e `maximum_birth_date_distance_days` são parâmetros versionados da política. Esta especificação não fixa valores numéricos sem evidência.

O rótulo CNS é evidência observacional de alta confiança, não verdade absoluta.

## 6. Isolamento contra label leakage

Se `label_source=CNS`, toda informação CNS deve ser excluída do pipeline avaliado.

A exclusão abrange:

- blocking/candidate generation;
- score;
- features compostas;
- hashes;
- prefixos;
- normalizações;
- indicadores de presença/ausência;
- chaves derivadas;
- qualquer transformação que preserve informação do CNS.

O mesmo princípio vale para qualquer futura fonte de ground truth.

O Calibrador deve validar explicitamente as listas de inputs da geração de candidatos e do scoring antes da execução. Detecção de vazamento bloqueia a calibração e sua promoção.

## 7. Representatividade

A população `WITHOUT_CPF_WITH_CNS` não deve ser assumida como amostra aleatória de `WITHOUT_CPF_WITHOUT_CNS` nem de todo o estrato sem CPF.

Cada execução deve publicar:

```text
label_source
population_stratum
eligible_population
target_population
coverage
positive_pair_count
statistically_sufficient
representative_for_target_stratum
```

`coverage = eligible_population / target_population`, quando o denominador for válido.

Suficiência estatística e representatividade são diagnósticos distintos. Uma amostra numerosa pode continuar não representativa.

## 8. Preferência entre fontes

A política é:

1. se CPF for suficiente e representativo para o estrato-alvo, usar CPF;
2. caso contrário, CNS pode ser usado como auxiliar somente se também for suficiente e representativo para o estrato explicitamente diagnosticado;
3. se nenhuma fonte satisfizer simultaneamente esses requisitos, a calibração permanece sem promoção.

A política não possui data de expiração do CNS. Sua retirada decorre da evidência de que CPF já fornece amostra suficiente e representativa nos estratos relevantes.

## 9. Evidência e replay

A execução deve preservar, no mínimo:

```text
label_source
label_policy_version
population_stratum
eligible_population
target_population
coverage
positive_pair_count
statistically_sufficient
representative_for_target_stratum
candidate_generation_inputs
scoring_inputs
derived_inputs_checked
label_leakage_check
```

Parâmetros de elegibilidade e versões de validadores fazem parte do fingerprint de replay.

## 10. Invariantes

- CPF é a única âncora de identidade entre CPF/CNS.
- CNS não resolve identidade deterministicamente.
- CNS pode apenas rotular pares auxiliares elegíveis.
- fonte do rótulo e derivados não participam do blocking ou score que ela avalia.
- amostra CNS não implica representatividade do estrato sem CNS.
- ausência de fonte suficiente e representativa é condição fail-closed.
- retirada do CNS é baseada em suficiência/representatividade observadas, não em prazo administrativo.

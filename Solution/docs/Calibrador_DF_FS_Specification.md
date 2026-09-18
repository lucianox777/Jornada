# Calibrador — especificação DF → Fellegi–Sunter

**Estado:** desenho metodológico aceito e infraestrutura inicial implementada. Nenhum threshold numérico ou modelo probabilístico é promovido automaticamente por este documento.

## 1. Objetivo

O Calibrador deve comparar configurações completas de linkage em benchmark reproduzível e selecionar tecnicamente apenas alternativas não dominadas. O fluxo candidato é `DF → FS`, preservando `INCONCLUSIVO` quando a evidência disponível não é suficiente.

## 2. Referência nominal brasileira

A referência nominal é o snapshot versionado do produto **IBGE — Nomes no Brasil** já internalizado pela Jornada. O Calibrador usa somente valores/frequências efetivamente presentes na referência e sua semântica publicada.

A Jornada não cria nomes raros fictícios, não reconstrói a cauda suprimida e não atribui frequência inventada a valor ausente. Quando a ausência puder ser interpretada como censura da publicação, essa condição é registrada como tal na evidência.

Primeiro nome e sobrenome permanecem semanticamente distintos. Estatística oficial de `Surname` somente pode ser associada a atributo de origem cuja fronteira de sobrenome seja estruturada e compatível; tokens inferidos de `nome_completo` não recebem automaticamente essa frequência.

## 3. Benchmark

O benchmark deve ter ground truth conhecido e replay determinístico. A parte nominal sintética permanece dentro do universo nominal observado na referência: a geração controla a associação/variação entre observações, não inventa vocabulário brasileiro.

O split `TRAIN/VALIDATION/TEST` é realizado por indivíduo-base antes da geração de pares, evitando que observações derivadas da mesma pessoa contaminem conjuntos diferentes. Seeds, gerador e snapshots são versionados.

A implementação inicial `IBGE_NOMINAL_BENCHMARK_V1` recebe indivíduos-base com atributos semanticamente estruturados. Prenome e sobrenome só entram no benchmark quando existem no snapshot tipado do IBGE e atendem ao suporte mínimo configurado. O corte de suporte é parâmetro versionado do benchmark; não redefine o universo nominal nem transforma ausência em frequência zero.

Para `MATCH`, uma segunda observação do mesmo indivíduo pode substituir prenome ou sobrenome por outro valor efetivamente observado no mesmo universo estatístico do IBGE. A escolha usa vizinhança nominal por `JARO_WINKLER@V1`, preservando valor de origem, valor de destino, frequências observadas e similaridade. Isso é um ensaio controlado de confundibilidade nominal, não uma afirmação sobre frequência real de erro cadastral.

Para `NON_MATCH`, são usados indivíduos-base distintos da mesma partição. A implementação pode selecionar pares nominalmente próximos para criar um benchmark de estresse; essa estratégia não deve ser interpretada como distribuição representativa de produção.

Não existem classes subjetivas de erro como “leve”, “moderado” ou “grave”. A dificuldade emerge das propriedades observadas dos pares e do comportamento dos candidatos.

O benchmark mede separadamente blocking e scoring. Perder um par verdadeiro no blocking não pode ser atribuído ao scorer.

## 4. DF — primeiro estágio nominal

DF combina duas evidências preservadas separadamente:

1. similaridade nominal, inicialmente `JARO_WINKLER@V1`;
2. term-frequency adjustment compatível com a referência Splink.

Não se cria um score arbitrário `distância × frequência`. O Calibrador pesquisa fronteiras observadas de similaridade e contribuição TF. Na implementação inicial, DF é assimétrico: resolve apenas `MATCH`; todo caso que não cruza uma fronteira candidata segue como `INCONCLUSIVO` para o estágio FS.

### 4.1. Term frequency

A implementação `SPLINK_TERM_FREQUENCY_V1` preserva:

- para fuzzy match, frequência efetiva = maior frequência entre os dois lados;
- `tf_adjustment_weight` para controlar a intensidade do ajuste;
- `tf_minimum_u_value` como piso contra evidência extrema de termos raros;
- contribuição aditiva em log-Bayes-factor, usando a mesma base logarítmica do scorer Jornada.

A frequência pode ser externa/versionada (IBGE) ou empírica quando a metodologia explicitamente definir esse uso; a proveniência nunca é descartada.

A implementação `NominalDfCalibrationDatasetFactory` conecta a referência IBGE tipada aos pares rotulados do Calibrador usando somente a projeção de primeiro nome `IBGE_CENSO_2022_NOMES_PUBLICACAO_V1`. O `referenceUProbability` do ajuste TF é a colisão exata analítica da própria projeção de primeiro nome, `sum(p_i^2)`. Entradas `SOBRENOME` não participam dessa distribuição e tokens de sobrenome não são inferidos de `nome_completo`. Quando um primeiro nome não existe na publicação, o par é preservado com `FrequencyCensored=true`, sem ajuste TF; consequentemente permanece `INCONCLUSIVO` no estágio DF e segue ao FS. O dataset carrega também `ReferenceId`, `ReferenceCode` e `ReferenceContentSha256`, além das versões de semântica/normalização/algoritmos, para replay da calibração.

Essa integração produz dataset/grade de calibração reproduzível; por si só não escolhe nem promove fronteira DF e não altera `T_LINKAGE`, prior ou o algoritmo operacional.

## 5. Fellegi–Sunter — segundo estágio

Quando DF não resolve, o par segue para o Fellegi–Sunter da Jornada. O score DF anterior não é adicionado ao FS.

O candidato FS deve registrar o conjunto completo de parâmetros relevantes:

- `m` por nível;
- `u` por nível;
- prior de match;
- thresholds inferior/superior quando aplicáveis;
- versão de normalização/comparadores;
- política de blocking;
- versão/fingerprint da referência nominal e demais fontes.

Estimadores Jornada e Splink podem produzir candidatos. A comparação é feita pela configuração completa, não por um parâmetro isolado.

### 5.1. Experimento fatorial m/u

Quando houver estimativas independentes de `m` e `u` produzidas pela Jornada e pelo Splink, o Calibrador deve materializar todas as combinações válidas disponíveis, inicialmente:

```text
M_JORNADA + U_JORNADA
M_JORNADA + U_SPLINK
M_SPLINK  + U_JORNADA
M_SPLINK  + U_SPLINK
```

O objetivo não é declarar “qual algoritmo é melhor”, mas identificar qual configuração completa produz melhor comportamento no corpus independente. É permitido, portanto, que uma combinação híbrida supere as duas combinações puras.

Os parâmetros comuns, como prior e política de thresholds, permanecem explicitamente versionados e não são silenciosamente herdados de um dos estimadores.

### 5.2. Referência populacional IBGE para u nominal

### Auditoria da calibração nominal corrente

No contrato corrente pós-#311, o Monte Carlo nominal deixou de ser apenas relatório: `u` de `NOME` e `NOME_MAE` usa a referência IBGE internalizada. O recorte materno usa prenomes `FEMININO`, mas ainda lê `periodo_nascimento='TODOS'`; portanto **não há condicionamento por coorte de nascimento da mãe**. Isso é uma hipótese explícita a medir na #31, não autorização para inferir idade materna ou corrigir pesos sem evidência.

Após #312, `EXACT/HIGH/MEDIUM/LOW` são ajustados por MLE com restrição de ordem quando a combinação de `m` e `u` produz inversão de LLR. A estimativa irrestrita permanece persistida em `UNRESTRICTED_M_<campo>_<estado>`. A instrumentação de auditoria também registra o bloco isotônico, o delta de LLR por estado, a quantidade de estados ajustados e o maior `|delta LLR|` por campo. Pooling grande deve ser tratado como evidência diagnóstica de tensão do estimador, não como prova de qualidade do modelo.

A composição `prenome × sobrenome` continua sob `INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1`. Eventual dependência deve ser estudada por sensibilidade/validação independente; não se introduz fator de correlação arbitrário no modelo canônico. A validação pré-HML deve ainda comparar artefatos congelados por `modelo_id`/fingerprints e repetir seeds para separar variabilidade Monte Carlo de efeito de mudança de modelo. Corpus sintético e adversarial servem para engenharia e falsificação; não constituem homologação populacional.

`IBGE_NOMINAL_U_BOOTSTRAP_V1` produz uma **estimativa de referência**, read-only, para os níveis `U_NOME_*`. Ela não substitui silenciosamente o `u` condicionado ao blocking usado pelo modelo operacional.

O recorte inicial usa somente as marginais nacionais publicadas `BRASIL/TODOS/TODOS` da referência versionada. Para tornar os estados `EXACT/HIGH/MEDIUM/LOW` comparáveis ao comparador nominal da Jornada, o bootstrap compõe sinteticamente `prenome + sobrenome` com sorteios independentes ponderados pelas frequências publicadas. Essa composição é explicitamente marcada por `INDEPENDENT_FIRST_NAME_SURNAME_MARGINALS_V1`: o IBGE não publica uma distribuição conjunta de nomes completos e a Jornada não interpreta essa composição como tal.

Não há canal de ruído administrativo na versão inicial (`CLEAN_PUBLISHED_REFERENCE_NO_ERROR_CHANNEL_V1`). Para `u`, os dois lados representam identidades distintas sorteadas da população sintética; coincidência exata de nome continua permitida. Qualquer canal futuro de erro deve possuir versão própria e ser sustentado por evidência independente, preferencialmente observações corroboradas da Jornada, e não por taxas inventadas.

A probabilidade de coincidência exata também é calculada analiticamente a partir das marginais publicadas (`sum(p_i^2)` para prenome e sobrenome; produto sob a hipótese sintética de independência) e funciona como controle do Monte Carlo. Os níveis fuzzy são estimados por amostragem determinística com seed explícita e comparador `IdentityComparison.CompareName` vigente.

O relatório deve preservar pelo menos: versão/SHA da referência IBGE, versão do método, hipótese de composição, canal de observação, seed, número de pares, suportes por nível, erro-padrão Monte Carlo e, quando existir, comparação lado a lado com `U_NOME_*` do modelo ATIVO.

Esse `u` é **populacional não condicionado ao blocking**. O `U_NOME_*` do modelo SQL Server corrente é estimado dentro do universo de candidatos do ruleset. A diferença entre ambos é evidência para análise metodológica, não autorização para copiar um sobre o outro. Promoção continua exigindo avaliação independente e os gates da #31.
## 6. Thresholds e seleção

Thresholds são produtos da calibração, não constantes escolhidas por intuição.

Para DF, a grade inicial é formada por fronteiras observadas de similaridade e TF no conjunto de validação, com redução determinística quando necessário para limitar custo combinatório.

Para a configuração completa, o Calibrador registra TP, TN, FP, FN e inconclusivos. Um candidato domina outro somente se não piorar FP e FN e melhorar ao menos um deles; quando FP/FN empatam, menor inconclusão domina.

Se dois candidatos trocam FP por FN, ambos permanecem na fronteira de Pareto. O Calibrador não inventa o custo institucional relativo desses erros.

## 7. Splink como implementação de referência

Splink/Python é usado em desenvolvimento e validação para produzir vetores de referência. Produção continua C#/.NET.

O port C# é versionado independentemente do upstream. Atualização futura do Splink não altera automaticamente a Jornada. Uma nova versão local exige testes de paridade e nova calibração.

A paridade exigida é semântica/numericamente tolerante, não bit a bit: mesmos inputs devem produzir mesma frequência efetiva, mesma contribuição TF dentro da tolerância e mesmas decisões para fronteiras equivalentes.

## 8. Invariantes

- nenhuma frequência ou nome ausente do IBGE é inventado;
- um indivíduo-base pertence a uma única partição do benchmark;
- substituições nominais permanecem no universo observado da mesma classe estatística;
- o benchmark controlado não declara frequência real de erro administrativo;
- DF não força NON_MATCH na versão inicial;
- DF não é somado ao FS após fallback;
- fuzzy TF usa conservadoramente a maior frequência dos lados;
- piorar o piso de TF não pode aumentar evidência de raridade;
- `tf_adjustment_weight=0` neutraliza TF;
- thresholds vêm de dados de validação;
- `INCONCLUSIVO` é resultado válido;
- novas evidências da Jornada podem permitir reavaliação posterior.

## 9. Evidência publicada pelo Calibrador

Cada candidato/execução deve registrar pelo menos:

```text
candidate_id
benchmark_version
train_population_version
validation_population_version
test_population_version
seed
minimum_nominal_support
ibge_frequency_version
similarity_algorithm_version
term_frequency_algorithm_version
m_estimator
u_estimator
m_estimator_version
u_estimator_version
prior_source
m_u_prior_version
df_thresholds
fs_thresholds
blocking_policy_version
TP / TN / FP / FN / INCONCLUSIVE
pareto_status
splink_reference_version
csharp_port_version
```

Para decisão individual/diagnóstico, preservar a decomposição da evidência DF e FS.

## 10. Critério de promoção

O Calibrador recomenda; não ativa por simples execução. Uma configuração nova deve ser reproduzível, não dominada no corpus de validação e manter comportamento esperado no conjunto de teste independente. Divergência de paridade Splink↔C# ou ausência de proveniência bloqueia a promoção.

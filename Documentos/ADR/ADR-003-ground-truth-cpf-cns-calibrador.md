# ADR-003 — Ground truth do Calibrador: CPF preferencial e CNS auxiliar condicionado

- **Status:** Aceita
- **Data:** 2026-09-13
- **Escopo:** identidade, linkage probabilístico, Calibrador, avaliação e replay

## Contexto

A Jornada adota CPF como âncora determinística de identidade e destino arquitetural do processo de resolução. Entretanto, enquanto existir estrato material de registros sem CPF, calibrar o parâmetro `m` somente com pares resolvidos por CPF pode produzir amostra pouco representativa justamente da população que depende do linkage probabilístico.

O CNS pode oferecer pares positivos de alta confiança no estrato sem CPF, mas não possui o mesmo papel arquitetural do CPF. Além disso, a população sem CPF que possui CNS não é uma amostra aleatória da população sem CPF: ela seleciona pessoas que passaram por sistemas que coletam esse identificador, especialmente saúde.

## Decisão

### 1. Papéis distintos

A arquitetura distingue explicitamente quatro papéis:

1. **âncora de identidade**;
2. **fonte de rótulo/ground truth**;
3. **geração de candidatos/blocking**;
4. **evidência de scoring**.

CPF pode exercer o papel de âncora determinística e é a fonte preferencial de ground truth quando sua amostra é estatisticamente suficiente e representativa do estrato em que o modelo será aplicado.

CNS **não é âncora de identidade**, não cria UUID, não funde UUIDs e não decide resolução. Pode ser utilizado apenas como fonte auxiliar de rótulos positivos de alta confiança para calibração, particularmente no estrato sem CPF.

### 2. Elegibilidade do CNS como rótulo

Um CNS somente pode produzir rótulo auxiliar quando satisfizer política versionada e auditável. A política deve considerar, no mínimo:

- validade estrutural do identificador segundo validador homologado;
- rejeição quando o mesmo CNS estiver associado a quantidade de pessoas distintas acima do limite parametrizado;
- rejeição quando as datas de nascimento associadas ao mesmo CNS divergirem acima do limite temporal parametrizado.

Os limites são parâmetros versionados do Calibrador. Esta ADR não fixa números por intuição.

Os filtros aumentam a qualidade do ground truth, mas não transformam CNS em verdade absoluta.

### 3. Proteção contra label leakage

Quando uma variável for utilizada como fonte do rótulo, ela e qualquer informação derivada dela devem ser excluídas de **todo o pipeline que será avaliado**.

Portanto, quando CNS rotular os pares:

- CNS não pode entrar no score;
- CNS não pode ser chave de blocking nem de candidate generation;
- hashes, flags, indicadores de presença, prefixos, normalizações, chaves compostas ou qualquer transformação derivada de CNS também não podem entrar em blocking ou scoring;
- métricas de recall do blocking devem ser calculadas sobre candidatos produzidos sem acesso ao CNS.

A violação deve falhar de forma fechada e bloquear a publicação da calibração.

### 4. Estratificação e representatividade

A proveniência do rótulo e o estrato populacional devem ser preservados em toda execução. No mínimo, distinguir:

- população com CPF;
- população sem CPF e com CNS elegível;
- população sem CPF e sem CNS elegível.

A amostra rotulada por CNS não pode ser declarada representativa de todo o estrato sem CPF sem evidência específica. O Calibrador deve publicar diagnóstico de cobertura e representatividade, inclusive a fração do estrato-alvo efetivamente coberta pela fonte de rótulo.

### 5. Critério de preferência e retirada

Não existe prazo administrativo para abandonar CNS.

CPF volta a ser a fonte preferencial exclusiva de ground truth quando a amostra baseada em CPF demonstrar, para os estratos relevantes de aplicação, suficiência estatística e representatividade segundo diagnóstico versionado do Calibrador.

Enquanto isso não ocorrer, CNS pode permanecer como fonte auxiliar condicionada. A decisão é orientada por evidência mensurável, não por data ou previsão de adesão dos Gestores.

## Evidência obrigatória

Cada calibração que utilize ground truth observado deve registrar pelo menos:

```text
label_source
population_stratum
label_policy_version
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

## Consequências

- O modelo preserva uma única âncora normativa de identidade: CPF.
- CNS melhora a calibração do estrato sem CPF sem virar segunda verdade de identidade.
- Blocking e scoring tornam-se auditáveis contra vazamento de rótulo.
- A limitação de seleção da população com CNS fica explícita, evitando leitura indevida de representatividade.
- A retirada do CNS passa a depender de suficiência e representatividade observadas da amostra CPF.

## Fora de escopo

Esta ADR não define thresholds numéricos de linkage, não fixa limites numéricos de elegibilidade do CNS, não altera CPF → UUID, não autoriza resolução determinística por CNS e não promove automaticamente qualquer modelo para Produção.

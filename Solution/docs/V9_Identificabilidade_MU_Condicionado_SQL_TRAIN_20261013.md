# V9 — identificabilidade empírica de m/u no mesmo SQL TRAIN, sem parâmetros operacionais

## Status

**INCONCLUSIVE_NO_INDEPENDENT_ESS_PRIOR_TF_VALIDATION_OR_OPERATIONAL_WEIGHTS**. Esta entrega não ativa nem autoriza ativação V9. Trata-se do primeiro cálculo **numérico e descritivo** de proporções m/u do experimento V9 **condicionado aos candidatos efetivamente recuperados pelo SQL físico**; não é ainda uma calibração governada nem substitui o modelo FS congelado no experimento anterior. Integra a sequência #1000–#1007, que disponibilizou massa-base sintética TF de TRAIN, 81 células nominais, suporte semântico de nascimento, perdas SQL, paridade lógica/física e fan-out.

## Denominadores, interpretação e cálculo

`V9SqlConditionalMuIdentifiabilityAudit` recebe as fontes já auditadas e **somente de TRAIN**:

- `V9SqlConditionedNominalSupportAudit`: 81 células mutuamente exclusivas (9 níveis de pessoa × 9 da mãe), com `MCount`, `UCount` e quantidades de pessoas-base e pares de pessoas-base distintos;
- `V9SqlConditionedBirthSupportAudit`: sete estados semânticos de nascimento, as mesmas contagens M/U e os mesmos pares SQL, com `EXACT` separado e soma do complemento binário;
- `V9SyntheticBaseFrequencyAudit`: quantidade de **identidades-base únicas de TRAIN**, não o número de pontas repetidas de candidatos.

Para cada uma das 81 células, calcula apenas a frequência empírica de **pares de observações** `MCount/M_SQL_TRAIN` e `UCount/U_SQL_TRAIN` (não a frequência de valores nominais na massa-base). Para os sete estados de nascimento usa os **mesmos denominadores**, impedindo mudança silenciosa do frame. As 81 frequências de M e as 81 de U devem somar 1 separadamente. Uma razão `(MCount/M_SQL_TRAIN)/(UCount/U_SQL_TRAIN)` é emitida apenas quando **as duas classes são efetivamente observadas** na célula e recebe o nome deliberadamente restritivo `DescriptiveObservedPairRatio`. **Ela não é um Bayes factor FS admissível**: sem estimativa de dependência entre pares que compartilham bases, suavização defensável, análise de incerteza, desenho amostral, estimativa do prior e validação de risco independente, não pode ser colocada no scorer.

### Estados que não podem ser confundidos

| Estado | Significado | Valor operacional permitido |
|---|---|---|
| `UNOBSERVED_BOTH_NOT_NEUTRAL` | Nem M nem U observados na célula | **Nenhum**; razão nula, não fator de Bayes 1 |
| `UNOBSERVED_M_NOT_NEUTRAL` | U observado, nenhum M | **Nenhum**; razão nula, não peso TF 0 |
| `UNOBSERVED_U_NOT_NEUTRAL` | M observado, nenhum U | **Nenhum**; razão nula, não probabilidade U fabricada |
| `BOTH_OBSERVED_PAIR_SHARES_NOT_INDEPENDENT_ESS` | As duas contagens positivas | Somente razão **descritiva**; nenhum peso calibrado |

A entrada na categoria nominal `Missing` significa que um ou ambos os nomes não estão disponíveis no par e é um **nível observável de comparação**, diferente de uma célula estatisticamente *desconhecida*. Nunca inferir que parâmetro ausente, fator de Bayes neutro (=1), `w_TF=0` e `w_TF=1` sejam a mesma situação. Essas quatro decisões necessitam de contratos e proveniências distintas. Sobretudo, atributos básicos da identidade progressiva não são todos automaticamente obrigatórios no estado `NOVO`; completude e obrigatoriedade dependem do **contrato versionado da fonte**. O recorte SQL exige DOB presente exclusivamente para permitir comparações controladas, não por mudança desse contrato.

`EmpiricalPairMatchShareNotPopulationPrior=M_SQL_TRAIN/(M_SQL_TRAIN+U_SQL_TRAIN)` é apresentado apenas para mostrar a composição **condicional e sintética** do bloco, **não** um prior representativo de cidadãos ou municípios. O `MayPromoteModelParameters` permanece explicitamente `false`, mesmo onde há M e U observados.

## Regra de segurança e suficiência dos 30 mil

A quantidade de pares recuperados é diferente da quantidade de **identidades-base** que os originaram. Para cada célula o relatório preserva `DistinctMBasePeople`, `DistinctUBasePairs` (duplas não ordenadas) e `DistinctUBasePeople`. Verifica que as contagens distintas não excedem os 30 mil da massa-base e seus subtotais; porém **não as chama de amostra independente, de ESS nem de intervalo de confiança**: duplas U podem compartilhar a mesma base. Mesmo com muitos pares, o suporte efetivo pode ser pequeno. Repetição não legitima suavizar 81 níveis raros com `alpha` arbitrário.

Assim, **30.000 pessoas-base por seed não são automaticamente suficientes**, mas também não há prova rigorosa de que sejam insuficientes para *todo* objetivo. A saída detalha quantas células têm M/U observados, uma só classe ou nenhuma classe. Próxima decisão deverá estimar dependência/ESS por clusters de identidade-base, exigir precisão e orçamento de falsos vínculos pré-registrados por classe/estrato e dimensionar massa pelo suporte realmente obtido. Se isso não for atingido, o resultado deve permanecer **inconclusivo**, sem substituir `UNKNOWN` por parâmetro neutro nem selecionar novo peso usando TEST.

## Separação das bases e política de promoção

O `u` condicional do SQL não deve ser substituído por marginais de prenome/sobrenome IBGE nem por produto de marginais. Para `TF`, `f(value)` das unidades `FullName`, `FirstLastJoint`, `FirstName`, `LastContentSurname` vem de **uma identidade sintética base única por pessoa em TRAIN**, separado por pessoa/mãe. As marginais IBGE originais congeladas continuam referência apenas para unidades semanticamente compatíveis; desconhecido e cobertura insuficiente impedem peso positivo implícito. Decisões `w_TF=0` e `w_TF=1` devem ser decisões calibradas/versionadas, não fallbacks ocultos.

Antes de estimar o modelo candidato e liberar **qualquer** ensaio congelado em TEST seeds 42/43/44, continua sendo necessário reconciliar o gate do Calibrador de 95% (frame e ruleset candidato) com 43,16% de recall observado no SQL ATIVO+C1, selecionar passes e limites de fan-out somente em TRAIN/VALIDATION, definir prior e orçamento estatístico, obter incerteza por cluster, abstenção e revisão independente. A CI prova aritmética, proveniência e isolamento, **não** qualidade de linkage. Os dados de TEST nunca entram nas proporções desta auditoria.

**Ambiente e restrições:** exclusivamente `JornadaHarness` efêmero e `DOTNET_ENVIRONMENT=Development`, sem históricos `VERSIONED_ALIAS` completos e sem extrapolar 30 mil para 11 milhões. Não altera ruleset ATIVO de produção, Rank/Resolve, V8 histórico, V9 decisória, DT-15, JornadaLocal, IBGE original, NODE/NAS, Gold ou HML/PROD.

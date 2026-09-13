# Calibrador — plano de análise de blocking

**Estado:** análise técnica e proposições. Este documento não homologa política de blocking, thresholds, pesos, prior, regras de promoção nem ativação probabilística.

## 1. Objetivo

Organizar o trabalho analítico do Calibrador para comparar atributos, projeções e combinações de passes de blocking usando somente capacidades já implementadas ou explicitamente preparatórias da Jornada.

O objetivo do blocking é preservar praticamente todos os vínculos verdadeiros de referência enquanto reduz de forma útil o universo de pares candidatos. Portanto, uma feature não deve ser escolhida apenas por seletividade, correlação ou razão de verossimilhança isolada.

CPF válido/confiável permanece fora deste plano: continua na rota determinística CPF -> UUID. Este documento trata apenas do universo probabilístico.

## 2. Capacidades já existentes

O estado corrente já oferece os blocos técnicos necessários para análise sem promover automaticamente uma política:

- `ResolutionProjectionPlanner` e `BlockingCandidateFeatureCatalog.CalibratorCandidates` fornecem features candidatas versionadas/fingerprinted;
- `BlockingFeatureDiagnostic` mede `TrueMatchRecall`, `NonMatchRetention`, `ReductionRatio`, `AgreementLogLikelihoodRatio` e `MissingRate`;
- o diagnóstico calcula correlação phi ponderada entre indicadores de concordância para sinalizar redundância potencial;
- `BlockingRuleSetSearch` executa busca bounded e determinística, avaliando passes primitivos, retendo pool limitado e testando complementaridade;
- `CalibrationReplayManifest` define as dimensões necessárias para replay de corpus, snapshots externos, catálogos, projeções e plano;
- o avaliador independente usa a mesma política versionada do Calibrador, evitando regras paralelas;
- o harness de escala observa pressão estrutural do blocking e contenção dos applocks relevantes, sem converter massa sintética em homologação HML.

Essas capacidades permitem medir e comparar propostas; não autorizam sua ativação em produção.

## 3. Unidade de análise

A unidade inicial deve ser a **feature candidata de blocking**, sempre identificada por sua semântica, projeção e versão de algoritmo. A análise deve separar:

1. valor original recebido da fonte;
2. projeção determinística calculada;
3. comparador aplicado a dois valores;
4. estatística externa eventualmente associada;
5. passe de blocking que usa uma ou mais features;
6. união de passes que forma o ruleset.

Essa separação impede transformar comparadores em colunas de Pessoa ou confundir uma heurística interna com semântica publicada por fonte externa.

## 4. Sequência proposta de análise

### 4.1. Inventário e elegibilidade

Para cada feature presente em `CalibratorCandidates`, registrar pelo menos:

- atributo de origem e projeção;
- `algoritmo@versão` responsável pela projeção;
- disponibilidade/missingness no corpus;
- estratégia de materialização e indexabilidade;
- semântica declarada da feature;
- eventual dependência de snapshot externo;
- fingerprint necessário ao replay.

Features ainda não homologadas como algoritmo de resolução permanecem fora do espaço de busca operacional, mesmo que tecnicamente calculáveis.

### 4.2. Diagnóstico isolado

Executar `BlockingFeatureDiagnostic` sobre corpus com vínculos e não-vínculos de referência e pesos positivos. O relatório por feature deve preservar as métricas já definidas pelo componente:

- `TrueMatchRecall`;
- `NonMatchRetention`;
- `ReductionRatio`;
- `AgreementLogLikelihoodRatio`;
- `MissingRate`.

O ranking serve para triagem, não para promoção automática. Recall de vínculos verdadeiros deve continuar tendo precedência sobre ganho de redução quando houver conflito.

### 4.3. Redundância e complementaridade

A correlação phi é sinal diagnóstico de redundância, não prova independência. Features muito correlacionadas devem ser analisadas quanto ao ganho incremental real quando combinadas.

Uma feature fraca isoladamente pode ser útil em interseção ou em passe complementar. Por isso, o descarte não deve ocorrer apenas porque seu ranking individual é inferior.

### 4.4. Busca de passes

Usar `BlockingRuleSetSearch` de forma bounded e determinística sobre o conjunto elegível. Para cada passe finalista, registrar:

- cobertura de vínculos verdadeiros;
- retenção/redução de não-vínculos;
- missingness dos componentes;
- número de candidatos produzido;
- interseção e sobreposição com outros passes;
- custo observável de execução;
- versões/fingerprints das features empregadas.

Não se deve ampliar a busca combinatória para índices compostos arbitrários. Índices simples continuam sendo a infraestrutura padrão; índices compostos só devem ser testados depois de escolhido pequeno conjunto de passes finalistas e quando houver benefício operacional demonstrado.

### 4.5. Avaliação da união de passes

O objeto a ser avaliado para política final não é somente cada passe, mas a união deduplicada dos passes. A análise deve medir:

- recall global de vínculos verdadeiros;
- número total de pares candidatos após deduplicação;
- contribuição marginal de cada passe;
- candidatos exclusivos adicionados por passe;
- concentração de chaves e risco de blocos excessivamente grandes;
- estabilidade por recortes relevantes do corpus.

A contribuição marginal é particularmente importante para evitar manter passes redundantes apenas porque apresentam bom desempenho isolado.

### 4.6. Avaliação operacional

Os finalistas devem ser exercitados com observabilidade de escala. A evidência técnica pode registrar quantidade de passes, linhas/chaves de blocking, maior concentração de Pessoas por chave, métricas por atributo e espera observada nos applocks de coordenação.

Essa medição é evidência de comportamento técnico. Não define, por si, P95/P99, SLA ou capacidade institucionalmente aceita; esses critérios dependem de HML representativa e baseline aprovado.

## 5. Frequência de nomes IBGE

A fonte `IBGE — Nomes no Brasil` é referência estatística agregada e versionada. Seu uso deve respeitar correspondência semântica explícita.

No estado corrente, somente:

- `name_first` pode consultar estatística oficial de `FirstName`;
- `mother_name_first` pode consultar estatística oficial de `FirstName`.

`name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` continuam disponíveis como heurísticas internas derivadas de tokenização e podem ser medidas com evidência da própria Jornada. Elas **não podem receber frequência oficial de `Surname` do IBGE**.

### P22 — sobrenome não materializado

A ausência de sobrenome materializado não deve ser classificada, por si só, como dívida técnica.

`nome_completo` não preserva uma fronteira estruturada confiável entre nome/nome composto e sobrenomes. Derivar “sobrenome” como todos os tokens após o primeiro, ou “último sobrenome” como último token, criaria uma semântica que a fonte original não forneceu e que não é equivalente à semântica publicada pelo IBGE.

Portanto, a posição técnica corrente é deliberadamente conservadora:

- não materializar sobrenome por inferência a partir de `nome_completo`;
- manter heurísticas de tokens apenas como features internas explicitamente identificadas como calculadas;
- permitir materialização semântica de sobrenome no futuro somente quando uma fonte fornecer fronteira estruturada confiável e contrato compatível;
- somente então avaliar eventual associação com estatística oficial `Surname` do IBGE.

P22 deve, assim, ser tratado como **limitação semântica deliberada / gate de fonte estruturada**, e não como implementação faltante a ser fechada por tokenização.

## 6. Parâmetros que este plano não fixa

Este documento não propõe valores numéricos para:

- recall mínimo de promoção;
- `m`, `u` ou prior;
- thresholds de match/review/non-match;
- número máximo institucional de candidatos por pessoa;
- P95/P99/SLA;
- pesos de atributos;
- quantidade ideal de passes;
- regra de ativação automática.

Esses valores exigem corpus representativo, verdade de referência governada, avaliação independente e, quando aplicável, decisão institucional. Inventá-los em documentação seria transformar ausência de evidência em política.

## 7. Evidência necessária para uma recomendação

Uma recomendação de nova versão de blocking deve ser acompanhada por artefato reproduzível contendo, no mínimo:

- identificação/fingerprint do corpus M/U e da verdade de referência;
- versão do Calibrador;
- versões/fingerprints dos catálogos de projeção e comparadores;
- snapshots externos consumidos, quando houver;
- métricas isoladas por feature;
- métricas dos passes finalistas;
- métricas da união deduplicada;
- análise de contribuição marginal/redundância;
- evidência operacional dos finalistas;
- avaliação independente sobre corpus separado quando exigido pelo gate de promoção.

A recomendação deve permanecer distinguível da decisão de homologação. Um relatório técnico pode concluir que uma alternativa é superior sem autorizar sua ativação.

## 8. Critério de saída desta etapa

Esta etapa pode ser considerada tecnicamente concluída quando o repositório conseguir produzir, de forma reproduzível, uma comparação entre o ruleset corrente e alternativas finalistas sobre corpus representativo, preservando fingerprints e evidências suficientes para replay e avaliação independente.

Até esse ponto existir, a política corrente permanece inalterada e qualquer promoção continua fail-closed.

## 9. Limites de governança

Este plano não cria, funde nem publica UUID; não altera Gold/Serving; não modifica CPF determinístico; não ativa modelo probabilístico; não substitui aprovação institucional; e não transforma corpus sintético ou estatística externa em verdade individual.

Seu papel é reduzir a próxima decisão a proposições mensuráveis e auditáveis, sem antecipar uma política que ainda depende de evidência representativa.

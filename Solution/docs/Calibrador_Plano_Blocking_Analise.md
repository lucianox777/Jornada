# Calibrador — plano de análise de blocking

**Contrato arquitetural superior:** [Decisão de blocking COMPLEMENTAR com referência IBGE — 27/09/2026](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md). Os passes por nome completo, dinâmico e combinado são capacidades complementares sobre **projeções compartilhadas**, não candidatos a sistemas mutuamente exclusivos. A promoção operacional dos novos passes permanece condicionada à avaliação de recall, seletividade e custo da **união deduplicada**. Este plano especifica **como medir** a decisão, não se ela deve existir.

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

`name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` continuam disponíveis como heurísticas internas derivadas de tokenização. Elas **não podem receber diretamente a frequência oficial de `Surname` do IBGE como se esta fosse a frequência observada da própria feature ou do último token**. Contudo, a estatística oficial de sobrenomes **em qualquer posição** é referência marginal útil para construir/planejar o **índice por presença de sobrenome**, selecionar casos de estresse, estimar cenários de seletividade e comparar com as frequências reais das projeções no corpus; essa utilização auxiliar é expressamente permitida pela decisão canônica.

### P22 — fronteira estruturada e índice por presença

A ausência de **sobrenome semanticamente estruturado** não deve ser classificada, por si só, como dívida técnica. **O índice por presença de sobrenomes publicados é uma decisão positiva de arquitetura:** a coleta do IBGE orientou registrar todos os sobrenomes e, em último caso, o último; a publicação contabiliza a ocorrência independentemente da posição. Essa semântica oferece uma base relevante para recuperar nomes que compartilhem tokens de sobrenome em diferentes posições. A falta de fronteira estruturada em `nome_completo` não deve eliminar essa capacidade; exige identificá-la como projeção **calculada**, distinta do campo censitário.

`nome_completo` não preserva uma fronteira estruturada confiável entre nome/nome composto e sobrenomes. Derivar “sobrenome” como todos os tokens após o primeiro, ou “último sobrenome” como último token, criaria uma semântica que a fonte original não forneceu e que não é equivalente à semântica publicada pelo IBGE.

Portanto, a posição técnica corrente é deliberadamente conservadora:

- não materializar **sobrenome semanticamente estruturado** por inferência a partir de `nome_completo`; é permitido materializar **projeções técnicas de tokens por presença e último token**, identificadas como heurísticas calculadas;
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

## 10. Experimento de blocking combinado (PR #526)

**Arquitetura decidida; eficácia quantitativa em avaliação:** a interseção de nome completo, nome completo materno e data exata é o núcleo combinado seletivo, mesmo no cenário de estresse `MARIA SILVA` + mãe `MARIA SILVA`; a união com inversão válida de dia/mês, ano adjacente válido, fonética e futuras vizinhanças censitárias recupera erros sem abandonar a seletividade da interseção. Os três mecanismos são **complementares**; somente recall, cardinalidade, custo e taxas de erro no universo de São Paulo são hipóteses a medir.

**Implementado no PR #526, integrado ao `master`:** `CombinedIdentityCandidatePlanner` gera cinco passes `combined-exact`, `combined-day-month-transpose` (válido), `combined-neighbor-year`, `combined-name-phonetic` e `combined-mother-phonetic`. Todos reutilizam o SQL parametrizado de `BlockingProjectionCandidateQueryBuilder` (`INTERSECT` por atributo, `UNION` entre passes). Há testes unitários, além do teste sintético reproduzível que lê a **projeção pública IBGE 2022** com hash verificado (120 pessoas, seed 526). O combinado já é adicional na **busca semicega** quando elegível, mas ainda não foi promovido ao **Runner em lote** nem integrado como política selecionável no Calibrador; o teste não constitui benchmark municipal representativo.

**Fonte estatística:** o Censo é levantamento populacional e suas frequências divulgadas podem servir de referência agregada para nomes por coorte e localidade. A contagem de sobrenomes **em qualquer posição favorece o blocking por presença**; a falta de uma fronteira estruturada na Jornada impede apenas o tratamento de tokens calculados como sobrenomes oficialmente identificados e o uso direto da frequência oficial como probabilidade posicional. A divulgação pública não contém o cruzamento individual de nome completo, nome materno e data exata. Não multiplicar marginais como se independência familiar, geográfica e geracional estivesse demonstrada. Grafias diferentes divulgadas podem ser variantes legítimas, não erros comprovados. Preservar snapshot/fingerprint e contrato semântico; consultar P22 e a decisão canônica.

**Plano de comparação:** medir, sobre corpus representativo com verdade de referência, recall@candidatos e recall da união, colisões casuais observadas, distribuição de candidatos inclusive piores caudas, erro de data e de nome simultâneo, registros sem atributos, ganho marginal por passe, tempo P50/P95/P99 e custo físico dos índices. Comparar com o ruleset dinâmico atual e avaliar Full-Text/Jaro apenas como alternativas mensuráveis. O cenário histórico ilustrativo de **1 em 599 milhões** corresponde a uma combinação específica e implica **0,019 outras pessoas esperadas** entre 11,5 milhões se a probabilidade hipotética se aplicar; não é uma medição do total de colisões, threshold ou SLA. Reproduzir as entradas/fórmulas quando disponíveis e comparar cenários com dependência mãe/filho, além do dado censitário marginal.

**Decisão de arquitetura:** **preservar em conjunto** as capacidades de nome completo, dinâmico e combinado, todas com o mesmo planejador/índices e com passes configurados por versão; o dinâmico não é mero fallback descartável. A política ativa pode habilitar ou desabilitar passes de cada capacidade conforme atributos disponíveis e prova de ganho; a união de candidatos é a unidade final de calibração. Nenhuma ativação probabilística, fusão de UUID ou publicação Gold decorre do protótipo.

### 10.1. Critérios adicionais de execução

1. Estudar a **presença** do sobrenome em qualquer posição com marginais oficiais, preservando as diferenças entre token técnico, último token e sobrenome semanticamente estruturado.
2. Construir versão experimental de catálogo de grafias públicas do IBGE (forma, frequência, recorte, fonte/hash), mais vizinhanças **calculadas** por ortografia/fonética; não rotular variantes legítimas como erros.
3. Medir a recuperação de pares verdadeiros por cada capacidade e pela **união sem parada antecipada**, inclusive ausência da mãe, erros simultâneos e todos os estados semânticos V5 de nascimento.
4. Relatar as maiores caudas de blocos, planos SQL Server, redução, deduplicação, ganho marginal, P50/P95/P99 e falha explícita por limites; testar índices compostos apenas para passes finalistas.
5. Verificar em múltiplas ondas as chaves antigas/novas, referências alteradas e observações anteriormente resolvidas, mantendo DT-05 e filas de reprocessamento governadas.
6. Distinguir claramente **modelo ilustrativo 599 milhões**, auditoria agregada da tripla em Gold, teste sintético IBGE e validação independente representativa.

### 10.2. Dois modelos para nascimento e otimização por seletividade conjunta

**Decisão:** a arquitetura tem **uma estratégia estatística multivariada de recuperação, com passes complementares**. A comparação de regras isoladas serve ao diagnóstico, não define motores rivais. Priorizar as combinações que têm menor cardinalidade observada ou estimada com confiança, **sem desistir** de passes complementares elegíveis quando o primeiro já encontrou candidatos. A avaliação deve relatar separadamente **custo da busca** e **vínculos verdadeiros recuperados**, inclusive casos em que nenhuma expansão alcança o par correto.

Estimativas de custo **não** podem confundir:
1. a **distribuição demográfica de nascimento real** — coortes e idades da população municipal por fonte compatível, com nomes/sobrenomes do município SP em V2 candidata e nomes maternos do Brasil conforme V1 vigente;
2. a **distribuição de erro no nascimento observado**, condicionada a Gestor/Sistema/Base, qualidade, preenchimento convencional, ausência de CPF/mãe e ondas de atualização da origem.

O IBGE de nomes por década **não é** histograma completo de nascimentos do município em 2026, nem contém a distribuição conjunta pessoa+mãe+nascimento. Para coortes, utilizar separadamente o [Censo 2022, população por idade e sexo](https://www.ibge.gov.br/estatisticas/sociais/populacao/22827-censo-demografico-2022.html?edicao=38166&t=resultados), com referência **01/08/2022**, versionamento e população-alvo declarada; simular ou observar nascimentos pós-Censo sem atribuir-lhes frequência zero. A mãe não tem sua idade conhecida só por existir `nome_mae`. Datas futuras são inconsistência objetiva em relação à **data civil congelada do run**; `29/02` é **registrado pelo cartório** quando o nascimento ocorreu nessa data em ano bissexto, mas `29/02` em ano comum não é data civil possível (Lei 6.015/1973, art. 54). Para índice de pessoas presumivelmente vivas, idade máxima verificada e datada da população brasileira é **sinal de plausibilidade** recalibrável; não é proibição legal, filtro rígido nem limite de retenção de registros históricos de pessoas já falecidas. Não usar as décadas extremas do produto de nomes como restrição etária de elegibilidade. Ver seção 5.1.1 da [decisão canônica](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md#511-critérios-versionados-de-plausibilidade-da-idade).

**Matriz mínima do ensaio:**
- casos de estresse `MARIA SILVA / mãe MARIA SILVA` e `JOSÉ DA SILVA / mãe MARIA DA SILVA`, outros prenomes/sobrenomes frequentes por recorte, cauda, agnomes, ordem e presença intermediária do sobrenome;
- exato, inversão válida/inválida dia/mês, `±1` ano com verificação de `29/02`, troca de dígitos, erros concomitantes nome+mãe+nascimento, datas convencionais `01/01`, dias `1/10/15`, ausência de data/mãe e registros pós-2022;
- estimar/medir `P(data_real | coorte)` separadamente de `P(data_observada | data_real, origem)`, preservando o denominador, incerteza e estratos; **nunca** transformá-las em segundo scorer não governado;
- testar interseção **EAV atual** sobre `identidade.blocking_chave` versus **tupla materializada horizontal** e índice composto para os poucos passes finalistas; um índice B-tree não cruza atributos armazenados em linhas separadas;
- publicar por passe e pela união: cardinalidade P50/P95/P99, custo, taxa de candidatos exclusivos, ganho incremental de recall, comparação ao ruleset atual, grandeza física dos índices, replay e comportamentos fail-closed para timeout/truncamento.

**Fronteira de implantação:** calibrar ordem e composição **em snapshot congelado**, não gerar plano independente por solicitação. O produto operacional mantém `u` condicionado ao universo que **realmente passou** pelos passes da união e `m` baseado em pares verdadeiros rotulados; fontes censitárias são bootstrap e diagnósticos, não autorização de vínculo.

### 10.3. Critério de aceite do índice estatístico multivariado (contrato do núcleo)

**Contrato de decisão:** [seção 5.3 da decisão canônica](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md#53-contrato-de-seleção-de-passes-custo-e-exaustividade). A prioridade não é provar 1/599 milhões: é **recuperar o vínculo verdadeiro sob orçamento operacional governado**, mesmo no cenário de nomes comuns, com as menores cardinalidades possíveis e sem falsa declaração de pessoa nova depois de busca incompleta.

| Estrato de avaliação | Entradas e fonte estatística | Evidências a publicar |
|---|---|---|
| Prenome/sobrenome da pessoa | SP 3550308, Censo 2022, V2 **candidata**; sobrenomes por presença em qualquer posição, último token como proxy com validação | Frequências divulgadas e suprimidas, versões, cardinalidades por passe/união, cobertura na Gold |
| Prenome/sobrenome da mãe | Brasil: prenome `FEMININO` e sobrenome `TODOS`, V1 técnica vigente | Cobertura da mãe, ausência por fonte, correlação familiar com sobrenome civil, impacto de coortes |
| Distribuição demográfica de nascimento | IBGE **população por idade e sexo**, recorte municipal e data de referência **separados** da estatística de nomes por década | Massa/coorte, extrapolação datada e estratos fora do Censo 2022; não usar últimas décadas como limite rígido |
| Distribuição de erros de nascimento | Par verdadeiro rotulado por Gestor/Sistema/Base, sem e com mãe/CPF; perfis sintéticos apenas como sensibilidade | Exato, dia/mês invertido válido, ano adjacente, troca de dígitos, valores convencionais e ausentes; ganho marginal |
| Grafias publicadas e vizinhanças | Contagens censitárias **distintas** de `SOUZA` e `SOUSA`; fonética, ortografia e aliases são relações calculadas/versionadas | Recuperação dos dois nomes sem fundir contagens públicas; custo de expansão e FN por estrato |
| Plano físico | Índice vertical `identidade.blocking_chave` com `INTERSECT/UNION` **versus** projeção horizontal composta candidata | Planos SQL Server, CPU/I/O, P50/P95/P99, custo de manutenção, equivalência da união e retries |

Para uma mesma observação, os passes elegíveis **não** podem ser encerrados após o primeiro candidato nem descartados por ranking interno sem estado de execução **incompleta**. `NOVA_IDENTIDADE` só é admissível quando a política versionada concluiu o universo completo. O scorer C# FS e as guardas de conflito continuam únicos e separados da previsão de seletividade. A documentação de testes deve congelar corpus, data civil do run, fontes por atributo, fingerprints e limites observados.



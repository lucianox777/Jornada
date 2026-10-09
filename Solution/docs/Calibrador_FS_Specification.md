> **Vigência 29/09/2026:** [decisões canônicas](Decisoes_Canonicas_Identidade_Linkage_20260929.md) prevalecem sobre regras legadas de guard exato, V6/V7 e nome social. A V8 atual não implementa todas essas decisões.

# Calibrador Fellegi–Sunter — especificação corrente

**Estado:** arquitetura técnica corrente. O corpus sintético/DEV prova engenharia, não homologação estatística municipal.

## 1. Fluxo canônico

O linkage probabilístico da Jornada possui um único resolvedor estatístico operacional: **Fellegi–Sunter (FS)**. O fluxo é:

1. gerar candidatos com o ruleset versionado de blocking;
2. deduplicar a união dos passes;
3. calcular estados de evidência e LLR no FS;
4. aplicar política de decisão versionada;
5. publicar apenas decisões que atravessem todos os guards de segurança.

Não existe estágio DF anterior ao FS. Similaridade nominal e frequência não constituem um resolvedor paralelo.

## 2. Separação TRAIN / VALIDATION / TEST

O split é determinístico por pessoa-base e acontece antes da geração das amostras.

- **TRAIN** estima m/u e seleciona o ruleset;
- **VALIDATION** constrói a fronteira de decisão e calibra threshold/margem;
- **TEST** reaplica o candidato congelado e pode somente reprovar.

CPF pode definir ground truth, mas é ocultado do blocking e do score.

## 3. m

m vem de pares positivos independentes, preferencialmente observações de Gestores distintos ligadas por identificador determinístico aceito como ground truth. Volume não substitui diversidade/representatividade dos erros cadastrais. Estados sem suporte observado permanecem dívida estatística e não devem ganhar interpretação forte apenas por suavização.

## 4. u e o universo candidato

O alvo operacional de u é:

> P(estado de evidência | não-match, par sobrevive ao blocking efetivo)

Não é o u incondicional da população.

O Runner atual une/deduplica os passes e depois pontua sem transportar o passe de origem para o scorer. Por isso, o u usado pelo FS corrente é estimado sobre a **união deduplicada do ruleset**. O Calibrador mede também suporte nominal por passe para revelar heterogeneidade e impedir que um passe com pouco suporte fique invisível.

### Bootstrap e convergência

O IBGE é referência externa versionada de **bootstrap** para NOME e NOME_MAE. Seu snapshot é carregado explicitamente uma vez na preparação do ambiente, validado integralmente na carga e preservado para os modelos que o utilizaram. Não há recarga periódica automática nem revalidação integral em cada calibração.

Na **primeira** preparação do ambiente, a carga única do snapshot IBGE exige integridade, compatibilidade e fingerprint auditáveis. Após esse bootstrap, `GENERATE_DRAFT` utiliza os parâmetros persistidos do modelo e os dados ingeridos da Jornada para calibrações posteriores; **não exige referência IBGE ativa**, não recarrega nem recalcula o snapshot. Se o primeiro bootstrap necessário ainda não ocorreu, falhar explicitamente; não transformar essa guarda inicial em dependência permanente. Mudanças futuras de referência demandariam nova decisão explícita, nunca atualização implícita.

Cada modelo registra o identificador, a versão e o fingerprint do snapshot efetivamente utilizado. Snapshots anteriores são imutáveis e retidos para replay e auditoria, inclusive após a convergência para `u` empírico; uma nova referência ativa não modifica retroativamente modelos antigos. A referência IBGE não é verdade de identidade individual.

A troca para u candidato-condicionado acontece por suficiência observável, nunca por data:

- mínimo de pares condicionados na união;
- mínimo de pares em cada passe;
- para nome da mãe, o requisito usa pares com o atributo presente;
- os limites são configuração explícita e persistida no modelo;
- a proveniência IBGE continua registrada mesmo quando o bootstrap deixa de ser aplicado.

O bootstrap IBGE é calculado **uma única vez** e seus parâmetros iniciais e proveniência ficam persistidos. As gerações seguintes não consultam nem exigem referência IBGE ativa: evoluem exclusivamente com evidências ingeridas da Jornada, observando suficiência e suporte do universo condicionado ao blocking. Se a evidência empírica ainda for insuficiente, o modelo conserva os parâmetros iniciais persistidos, sem recarregar o IBGE nem declarar convergência empírica inexistente. A exigência atual de suficiência da união e de todos os passes permanece até avaliação específica no Ensaio. Esta é a **decisão de arquitetura de 29/09/2026**, ainda dependente de adequação da implementação RF-052 (#602); não interpretar este texto como prova de que o Worker já a implementa.

## 5. Term frequency

`SPLINK_TERM_FREQUENCY_V1` é preservado apenas como matemática reutilizável. Ele **não é um estágio DF** e não está habilitado automaticamente no score corrente.

O executável `Jornada.Linkage.Evaluation --export-calibration` expõe parâmetros, proveniência e vetores sintéticos da matemática TF em JSON, somente leitura. Essa superfície existe para conferência externa e **não participa do score**.

Se TF vier a entrar no FS, deve:

- ser calibrada dentro do mesmo universo candidato;
- ter peso/piso/versionamento explícitos;
- não contornar os guards de conflito e segurança **vigentes na V8 após retirada do veto demográfico fixo**;
- não transformar ausência da referência IBGE em unicidade;
- passar TRAIN/VALIDATION/TEST e validação adversarial.

## 6. Identificabilidade

Nome completo + data de nascimento exatos não constituem chave única. **Decisão de 29/09/2026:** retirar da V8 o veto demográfico exato fixo; o FS deve decidir mediante distribuições e thresholds calibrados, com demais conflitos e gates preservados. A retirada **ainda não foi implementada** e exige teste adversarial de homônimos e reconferência (DT-14).

`EXACT/EXACT/EXACT` pode ocorrer tanto em match quanto em homônimo total; nenhum threshold, DF ou TF resolve essa indistinguibilidade sem evidência adicional independente.

## 7. ABBREV_COMPATIBLE e novas evidências

Abreviação é fenômeno do processo de registro. Um estado `ABBREV_COMPATIBLE` só pode entrar no LLR quando existirem m/u adequados ao canal de abreviação. Telefone, e-mail, CNS ou outros atributos podem ser avaliados como evidência adicional, mas presença no blocking não os promove automaticamente ao score.

## 8. Experimentos nominais, sem pesos presumidos

[DC-LK-03](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-03--núcleo-nominal-e-nome-social) fixa a hipótese de que a ausência unilateral de nome social **pode** sinalizar não-match; a ausência bilateral, concordância, divergência e comparação cruzada nome civil×social também precisam ser avaliadas. O Calibrador mede suporte por estado e estrato (Secretaria/sistema/período de coleta), estima m/u e dependência civil/social, avalia ganho em VALIDATION e audita em TEST. Não existe uma quarta evidência nominal independente pré-aprovada, peso nominal arbitrário nem presunção de neutralidade/penalização para nome social. Sem amostra que sustente efeito validado, o atributo continua como observação e feature de blocking. Alterar scorer aciona DT-14 e atualização de fingerprint/modelo.

## 9. Promoção

CI verde não equivale a homologação estatística. Antes de ativação probabilística real em HML/Produção continuam necessários corpus representativo, avaliação independente por estrato e aprovação institucional aplicável. A issue #31 concentra esse gate.


> **Norma vigente (09/10/2026) — FS, Splink, TF, IBGE e Calibrador:** consultar [DC-LK-TF](Decisoes_Canonicas_Identidade_Linkage_20260929.md#dc-lk-tf--norma-vigente-de-frequência-nominal-fs-e-calibrador-09102026). O peso TF zero é neutro (peso 1 aplica ajuste integral); os pesos e m/u devem ser estimados pelo Calibrador a partir do bootstrap sintético IBGE e, progressivamente, de evidência histórica real. Primeiro nome e último sobrenome significativo de pessoa e mãe devem participar do FS sem dupla contagem. V8 é referência histórica, não segunda implementação operacional. Em caso de divergência, prevalece a decisão canônica; este documento não certifica implementação concluída.


## 10. Nome completo e componentes nominais (decisão vigente)

A comparação FS principal preserva o **nome completo** da pessoa e da mãe. Primeiro nome e último sobrenome significativo são componentes auxiliares, não substitutos do nome completo. Exemplo: MARIA APARECIDA DE OLIVEIRA SANTOS continua integral; MARIA e SANTOS qualificam sua evidência.

O Calibrador deve estimar níveis nominais compostos ou distribuições conjuntas, evitando somar como independentes as evidências do nome completo e de seus componentes. O mesmo vale para o nome materno. A TF por componente e seu peso, incluindo zero, devem ser estimados e avaliados com partições TRAIN/VALIDATION/TEST; o scorer somente aplica o snapshot aprovado.

O IBGE fornece marginais iniciais: frequência de sobrenome em qualquer posição é **proxy**, não frequência observada do último sobrenome. A transição para dados municipais depende de suficiência e proveniência. Não inferir famílias a partir das marginais sintéticas.

**Estado atual:** o código compara nomes completos e ajusta TF de primeiros nomes nos estados EXACT; TF de último sobrenome, níveis compostos e seleção automática do peso ainda exigem implementação e testes. Consultar a norma DC-LK-TF no documento de decisões canônicas.


### Diagnóstico de implementação para a próxima frente (09/10/2026)

A geração de rascunho em `LinkageParametersWorker.GenerateDraftFromGoldAsync` ainda define explicitamente `TERM_FREQUENCY_WEIGHT = 1m` após preparar `NominalTermFrequencyReferenceStore`. Portanto, a presença do parâmetro e a correção para aceitar zero **não significam estimação automática**. A próxima implementação deve remover a atribuição fixa e selecionar o peso com dados de calibração, sem consultar TEST para ajuste. É obrigatório conservar a referência do snapshot e evidências de seleção.

A preparação atual (`NominalTermFrequencyReferenceStore`) publica apenas prenomes da pessoa e mãe; o suporte de sobrenome em `NominalTermFrequencySnapshot` está em evolução separada, e não deve ser habilitado no score antes de haver frequências governadas, calibração conjunta e validação contra dupla contagem. Na falta de evidência suficiente, não substituir um valor fixo por outro peso arbitrário. O corpus sintético de 30 mil pessoas é bootstrap metodológico, não prova de FDR real.


## 11. Avaliação de dependências condicionais pelo Calibrador (decisão vigente)

O Calibrador é responsável por avaliar dependências **condicionadas à classe de par** (match / non-match) e ao **universo de candidatos após blocking**. A hipótese de independência condicional do FS clássico não deve ser tomada como fato. A avaliação abrange: (a) nome integral, primeiro nome e último sobrenome significativo da pessoa; (b) os mesmos componentes da mãe; (c) dependências entre nome da pessoa e da mãe; (d) nome e nascimento; e (e) dependências e viés de seleção induzidos pelos passes de blocking.

### Método e governança

1. Em TRAIN, estimar distribuições conjuntas e marginais de níveis de comparação, separadas por classe, estrato e passe quando houver suporte; quantificar associação por medidas apropriadas a dados categóricos (informação mútua condicional, razões de probabilidades ou tabelas conjuntas com suavização), não presumir que Pearson seja apropriado.
2. Comparar um FS básico, níveis nominais mutuamente exclusivos e correções condicionais parcimoniosas. **Nunca somar evidência do nome integral e de seus componentes como se fossem independentes.** A TF deve ser estimada no mesmo procedimento, com peso zero elegível e sem contagem dupla.
3. Selecionar em VALIDATION, com os mesmos gates de suporte, FDR e minimização de FN estabelecidos para a fronteira de decisão. Usar TEST apenas para certificação independente, sem ajustar estrutura, pesos ou limiares depois de consultar seus resultados.
4. Persistir estrutura selecionada, parâmetros, suporte amostral, métricas por estrato, proveniência de dados e razões para manter ou descartar cada dependência. Se o suporte for insuficiente, manter representação mais simples e declarar a dependência **não identificável**, não assumir independência comprovada.
5. O runtime permanece com **um único scorer FS**, que aplica o snapshot calibrado; nenhuma segunda implementação de decisão, nem aprendizado em produção durante a resolução.

### Limite da referência IBGE e dados reais

Marginais publicadas de prenomes e sobrenomes **não identificam distribuições conjuntas** de nome, sobrenome, mãe e nascimento. O corpus sintético de 30 mil registros pode avaliar robustez sob hipóteses explícitas e correlações estruturais conhecidas, mas não autoriza inferir correlação familiar ou FDR real. Separar resultados `SINTETICO_HIPOTESE` de `REAL_OBSERVADO`; promover modelos dependentes de correlações populacionais apenas quando houver evidência municipal independente e suficiente. Amostragem de pares não-match deve refletir o blocking efetivo, evitando estimativas `u` da população irrestrita.

**Situação de implementação:** requisito aprovado e documentado; a seleção automática de estrutura conjunta e a correção de dependências ainda não estão implementadas no Calibrador. Não confundir este contrato com funcionalidade entregue.


## 12. Nascimento sintético por distribuição demográfica (decisão vigente)

**Obrigatório:** gerar a data de nascimento da pessoa sintética a partir de uma distribuição demográfica de idades/datas, nunca por sorteio uniforme de anos ou datas como aproximação de população real. A referência inicial versionada está em `data/reference/synthetic-birth-sp/`: projeção IBGE Revisão 2024 para SP (2026), com benchmark auxiliar do Censo 2022 para cauda 100+, distribuição diária derivada e manifesto com SHA-256, período, geografia, hipótese de uniformidade intrajanela etária e tratamento de idades 90+. O arquivo diário possui 39.268 linhas segundo o manifesto. São **pesos demográficos derivados**, não contagens observadas de nascimentos por dia.

O código `SyntheticDailyBirthDistribution.LoadAsync` valida esquema, datas, contagens positivas e duplicatas e `Draw` amostra pela distribuição cumulativa; `SyntheticCorpusGenerator.MakePerson` usa esse sorteio quando configurado em modo `demographicPrimary`. Entretanto, o construtor legado sem `dailyBirths` ainda cai em sorteio uniforme de anos 1935–2020. **A existência da distribuição não prova que todo fluxo de geração a utilize.** O fluxo de bootstrap/calibração deve exigir a referência demográfica versionada e falhar fechado se ausente, evitando fallback uniforme silencioso.

Preservar a mesma data de nascimento verdadeira entre observações da mesma identidade, aplicando degradações de data somente no modelo de erros observacionais. Separar a distribuição etária marginal da dependência entre nascimento e nomes: marginais IBGE não identificam automaticamente a distribuição conjunta. Testar distribuição empírica gerada versus pesos de referência por faixas etárias, cobertura de extremos, determinismo por seed, integridade SHA-256 e separação TRAIN/VALIDATION/TEST por identidade. Registrar fonte, transformação, hipótese intrafaixa, seed e desvios no relatório do Calibrador.

**Situação:** o amostrador demográfico e referência já existem; a eliminação/isolamento do fallback uniforme e os gates de cobertura de todos os caminhos de bootstrap ainda devem ser verificados/implementados. Não declarar esses gates concluídos.


## 13. Tabelas FS consolidadas e bootstrap inicial congelado em `ref` (decisão vigente)

A **primeira calibração de blocking e FS** é um bootstrap governado a partir da camada `ref` (frequências nominais IBGE, distribuição demográfica de nascimento e políticas de calibração). Seu **snapshot publicado é congelado**: não recalcular ou sobrescrever silenciosamente pesos, `m/u`, passes, limiares, TF, estrutura de dependências, proveniência ou fingerprints. Mudanças posteriores, inclusive substituição por evidência municipal real, criam **nova versão/modelo** e seguem VALIDATION, TEST, gates e promoção auditada. O congelamento refere-se ao snapshot publicado e seus vínculos, **não** à impossibilidade de publicar uma versão nova de `ref`.

### Mapa lógico consolidado das tabelas e evidências FS

| Conjunto lógico | Persistência atual ou referência | Fonte inicial | Regra de publicação |
| --- | --- | --- | --- |
| Frequências de nomes e sobrenomes | `ref.frequencia_nome_versao`, `ref.frequencia_nome` | IBGE versionado | Versão publicada imutável; novo modelo fixa ID da referência. |
| Distribuição demográfica de nascimento | `data/reference/synthetic-birth-sp/` (artefato versionado com manifesto e SHA-256) | Projeção IBGE SP 2026, cauda Censo 2022 | Fixar fingerprint e transformação no bootstrap; **não afirmar** que já existe tabela SQL em `ref` para nascimento. |
| Identidade e parâmetros FS | `identidade.modelo_linkage` e parâmetros associados ao modelo | Bootstrap calibrado | Modelo/snapshot versionado, sem mutação do publicado. |
| Blocking | `identidade.linkage_ruleset`, `identidade.linkage_ruleset_passe`, `identidade.linkage_ruleset_passe_campo` | Calibração inicial governada | Ruleset e passes imutáveis; mudança gera novo modelo. |
| TF materializada por modelo | `identidade.frequencia_linkage` | Frequências da versão `ref` fixada | Materialização vinculada ao modelo, sem consultar a referência ATIVA dinamicamente no score. |
| Dependências condicionais, pesos TF selecionados e diagnóstico | **Extensão de contrato pendente** | Treino sintético com hipóteses identificadas; dados reais quando suficientes | Não declarar persistência implementada; adicionar ao snapshot governado sem criar fonte concorrente. |

A expressão **“juntar tudo nas tabelas FS”** significa unificar a **visão lógica, as chaves de versão e a rastreabilidade do modelo**, sem copiar indiscriminadamente dados de referência para tabelas de decisão nem misturar referências imutáveis com métricas de execução. Referência `ref` → modelo FS/ruleset → evidências de validação → ativação → runs devem compartilhar IDs/fingerprints verificáveis.

### Invariantes e verificação

- Um bootstrap só é publicável com referência fixada, conteúdo verificável e configuração de blocking/FS coerente. A referência inicial não é recalibrada retroativamente quando chegarem novos lotes.
- Em caso de atualização de dados reais, criar candidato novo; não alterar `ref` nem modelo já publicado. Reproduzir uma decisão histórica deve utilizar **exatamente** o snapshot vigente naquele run.
- Conferir no código/migrações os gates de imutabilidade existentes e adicionar os que faltarem para nascimento e estrutura estatística conjunta. O banco já protege frequências publicadas em `ref` e rulesets de blocking, mas isso **não comprova** congelamento integral do bootstrap multidimensional.


### Precisão da fonte e do horizonte da projeção (09/10/2026)

A limitação de cobertura de nomes por **período de nascimento** no Censo 2022 não deve ser confundida com o horizonte da distribuição de **idades/datas de nascimento**. O usuário relembrou a discussão sobre projeção devido a cobertura histórica até aproximadamente 2002; esse ano exato **não foi confirmado** nos artefatos auditados e não deve constar como limite factual do IBGE. O produto Nomes no Brasil baseado no Censo 2022 informa frequências por período de nascimento; o recorte da fonte de nomes é distinto da projeção populacional utilizada para datas.

A distribuição diária efetivamente implementada está congelada em `data/reference/synthetic-birth-sp/manifest.json`: fonte `IBGE_PROJECAO_POPULACAO_REVISAO_2024`, geografia `UF_SP`, referência `2026-07-01`, população projetada `46.179.008`, com benchmark auxiliar `IBGE_CENSO_2022_SIDRA_9514_TAIL_BENCHMARK` para cauda 100+; idade 90+ modelada por decaimento geométrico e datas intrafaixa distribuídas uniformemente por dia. A uniformidade **intrafaixa** é hipótese de interpolação, não sorteio uniforme de anos nem série diária observada. A projeção é da UF São Paulo, enquanto frequências nominais da pessoa usam o município 3550308; declarar essa diferença de geografia em cada modelo.

A amostra gerada representa uma **distribuição etária projetada da população viva em 2026**, não uma série de nascimentos anuais nem uma previsão de nomes de recém-nascidos. Para associações entre prenomes e coortes de nascimento após a cobertura observada, não extrapolar frequências como se fossem medidas; marcar `PROJETADO/HIPOTESE` e aguardar fonte adicional verificável. Manter referência, método, fingerprint, recorte temporal, limites de extrapolação e validação empírica no snapshot FS congelado.


### Matriz de implementação do congelamento FS — revisão de código (09/10/2026)

A revisão das migrações `20260912_Frequencia_Nomes_Referencia.sql` e `20260910_Linkage_RuleSet_Passes.sql` comprova mecanismos **já implementados**, mas não comprova ainda o congelamento integral do bootstrap FS:

| Invariante | Evidência existente | Situação |
|---|---|---|
| Referência nominal `ref` versionada | `ref.frequencia_nome_versao` e `ref.frequencia_nome`, publicação ATIVA | Implementado |
| Versão nominal capturada atomicamente na criação do modelo | `identidade.tr_modelo_linkage_fixa_frequencia_nome_versao` com `HOLDLOCK` | Implementado |
| Versão nominal do modelo imutável | Mesmo trigger proíbe troca de `frequencia_nome_versao_id` | Implementado |
| Passes e campos de blocking vinculados a modelo | `identidade.linkage_ruleset*` | Implementado |
| Alteração/remoção de ruleset e passes impedida | Triggers `INSTEAD OF UPDATE, DELETE` | Implementado |
| Distribuição diária de nascimento em tabela `ref` versionada e vinculada ao modelo | Apenas arquivo + manifesto em `data/reference/synthetic-birth-sp` identificados | **Pendente** |
| Snapshot único de todos os parâmetros FS (m/u/TF/prior/limiares) com fingerprint verificável | Não comprovado pelas duas migrações auditadas | **Pendente de implementação/auditoria** |
| Validação das correlações condicionais por blocking e congelamento do resultado | Não comprovado pelas duas migrações auditadas | **Pendente** |

**Critério de aceite para fechamento:** publicar modelo inicial somente com todas as referências requeridas fixadas e fingerprints consistentes; proibir mutação posterior; permitir recalibração apenas por nova versão; teste de tentativa de alteração rejeitada e teste de replay determinístico com referências antigas. Não introduzir cópias redundantes de `ref.frequencia_nome` em tabelas FS: armazenar FKs e fingerprints de origem, além dos parâmetros FS efetivamente estimados.

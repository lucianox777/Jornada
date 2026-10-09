# Linkage — frequência externa de nomes IBGE

Estado: contrato preparatório da issue #31. Não habilita nem modifica Linkage probabilístico operacional.

## Fonte e finalidade

A fonte prevista é **IBGE — Nomes no Brasil**, tratada exclusivamente como referência estatística externa agregada. O catálogo não é verdade individual, não depende de CPF e não substitui frequências observadas no corpus da Jornada.

A primeira versão preserva a decisão registrada na issue #31: não aplicar normalização fonética, colapso de letras duplicadas ou equivalências probabilísticas. São permitidas apenas adaptações técnicas de consulta, como `Trim` e uniformização de caixa. A grafia/frequência publicada pela fonte continua sendo a unidade estatística de referência.

## Contrato legado de snapshot

`ExternalNameFrequencyCatalog` permanece disponível para snapshots locais simples já usados pelos testes e ferramentas preparatórias. Ele mantém identificador fixo da fonte, versão explícita, pares nome/ocorrências e fingerprint SHA-256 determinístico.

Esse formato não é suficiente para alimentar o otimizador porque não distingue semanticamente frequência de **primeiro nome** e frequência de **sobrenome**, nem identifica o recorte territorial. Por isso ele não deve ser usado para atribuir frequência externa a um atributo do blocking.

## Snapshot tipado para uso downstream

`IbgeTypedNameFrequencyCatalog` preserva as classes estatísticas publicadas. Cada entrada registra:

- `IbgeNameStatisticKind.FirstName` ou `IbgeNameStatisticKind.Surname`;
- grafia da entrada com apenas adaptação técnica de consulta;
- número de ocorrências;
- versão explícita da fonte;
- escopo territorial `Brazil`, `State` ou `Municipality`;
- código territorial obrigatório para UF/Município e ausente para Brasil;
- fingerprint SHA-256 incluindo tipo estatístico, escopo, código e conteúdo.

Primeiro nome e sobrenome com a mesma grafia são entradas distintas e podem possuir frequências diferentes. O fingerprint também muda quando muda o recorte territorial, evitando que uma estatística municipal seja confundida com Brasil/UF.

O snapshot tipado preservar estatística oficial de `Surname` **não significa** que qualquer token interno da Jornada seja semanticamente um sobrenome do IBGE. A publicação oficial parte de campos separados de nome e sobrenome; `nome_completo` da Jornada não preserva essa fronteira original.

## Mapeamento seguro para o Calibrador

`IbgeCalibrationAttributeCatalog` faz a ponte semântica entre atributo da Jornada e snapshot tipado. Na versão corrente, somente:

- `name_first` usa estatística oficial de primeiro nome;
- `mother_name_first` usa estatística oficial de primeiro nome.

As features internas `name_surnames`, `name_last`, `mother_name_surnames` e `mother_name_last` continuam disponíveis para diagnóstico/calibração com evidência da própria Jornada, mas são derivadas por tokenização do nome completo normalizado. Elas **não recebem frequência oficial de sobrenome do IBGE**, porque isso confundiria uma heurística interna de blocking com a semântica publicada da fonte.

A estatística tipada de `Surname` permanece no modelo de referência para uso futuro quando existir atributo de origem com fronteira estruturada confiável e semântica compatível. Nome completo e atributos sem correspondência explícita permanecem sem enriquecimento externo em vez de receber aproximação artificial.

## Entrada local versionada

`ExternalNameFrequencySnapshotReader` continua aceitando o formato local legado para validação de fonte/fingerprint. A evolução seguinte deverá preservar fail-closed, proveniência e detecção de mudança antes de qualquer automatização de obtenção da fonte.

A entrada local separa responsabilidades: obtenção/licenciamento/atestado da publicação externa ocorre fora do runtime de Linkage; o código da Jornada valida, tipa e identifica de forma reproduzível o snapshot recebido. O arquivo não é promovido automaticamente a parâmetro de modelo.

## Limites

Esta fatia não transforma frequência IBGE em peso, raridade, prior ou decisão de identidade por convenção. Também não cria/funde UUID, não altera fatos, Gold ou Serving.

Qualquer uso estatístico da frequência no Calibrador deve ser explicitamente versionado e comparado no corpus de calibração, com avaliação independente. Para sobrenomes, existe um gate adicional: somente uma fonte/atributo com semântica estruturada compatível pode receber a estatística oficial de `Surname`; tokens derivados de `nome_completo` não satisfazem esse gate.


## Consolidação decisória de 09/10/2026 — bootstrap estatístico e substituição histórica

**Decisão expressa, vigente para o desenvolvimento (não equivale a implementação ou promoção de modelo):**

1. **Finalidade do bootstrap.** Não se exige correspondência perfeita com a população real antes da primeira ingestão. O corpus primário de **30.000 pessoas** usa distribuições publicadas pelo IBGE para nomes e últimos sobrenomes, como aproximação estatística inicial. O universo de comparação de planejamento é de **10 milhões** de habitantes: fração amostral **0,3%**; para amostragem aleatória simples e uma proporção de 50%, margem de erro teórica de aproximadamente **±0,565 ponto percentual** a 95% (com correção de população finita). Essa margem diz respeito a proporções marginais de pessoas, **não** certifica estados raros, dependências, pares bloqueados nem FDR do linkage. Para uma categoria com frequência `p`, a contagem esperada é `30.000 × p`; a suficiência de estados conjuntos e amostras efetivas deve ser diagnosticada pelo Calibrador.
2. **Coincidência não é parentesco.** Nomes e sobrenomes frequentes produzem naturalmente colisões nominais e numerosos pares distintos, sem necessidade de simular famílias. Compartilhar `SILVA` ou nome de mãe não autoriza inferir parentesco nem identidade. **Não introduzir fração artificial de irmãos, famílias ou households como requisito de bootstrap**, pois não existe distribuição familiar real validada para parametrizá-la. Distribuições marginais não determinam parentesco real; essa limitação é conhecida, aceita e **não bloqueia** a operação inicial.
3. **Proveniência sem veto.** O gerador estabelece hipóteses sintéticas sobre distribuições conjuntas. O Calibrador **pode e deve executar** a seleção de representações e parâmetros sobre essa evidência inicial; registrar `fonte=SINTETICO_IBGE`, versão/seed do gerador, hipóteses, corpus, fingerprints e limites. O resultado descreve o corpus gerado e **não deve ser apresentado como comprovação empírica de dependência familiar/populacional real**. Isso não impede usar o bootstrap nem impõe revisão humana.
4. **Substituição progressiva, não mistura cega.** À medida que ingressam registros reais e se forma histórico suficiente, **substituir a base de evidências sintéticas de calibração por evidências reais históricas**, com rastreabilidade por origem, período, versão e estrato. A ingestão, por si, não promove modelo: recalibração, validação e promoção seguem governança. Evitar dupla contagem e rótulos circulares; preservar auditoria e modelos históricos. A dependência das hipóteses sintéticas deve diminuir até cessar nos estratos com suporte real suficiente. **Não exigir 100% de representatividade no início**.
5. **CPF tardio, sem revisão humana.** A arquitetura **não admite revisão humana** de pares. CPF determinístico e CPF obtido tardiamente podem fornecer âncoras retrospectivas independentes do próprio resultado probabilístico; reconhecer viés de seleção de quem eventualmente recebe CPF e validar suficiência por estrato. Reconciliar qualquer menção legada à revisão humana, inclusive na issue #31, antes de tratá-la como requisito.
6. **Sobrenomes e FS único.** Reutilizar `BrazilianNameComponents.LastContentSurname` e referência IBGE `Surname` mediante contrato semântico versionado. O último sobrenome da pessoa e da mãe deve participar da avaliação de representações candidatas do **único scorer FS**; Splink permanece referência independente. A representação com estados próprios de primeiro nome e último sobrenome requer seus próprios parâmetros `m/u` e TF; **não** acrescentar duas correções sobre o mesmo `u_EXACT` do nome inteiro sem reformular/calibrar o modelo, sob risco de dupla contagem. Uma V9, se necessária, é **versão técnica** do scorer/contrato, não segundo motor nem decisão a priori sobre melhor representação. Reconciliar o gate legado de sobrenome estruturado com a decisão de extração `LastContentSurname` e seus limites.
7. **Pendências reais, sem reabrir decisões.** Executar testes com parâmetros calibrados (nome isolado raro/comum, EXATO/HIGH, mãe/nascimento ausentes/divergentes e margem de candidatos); conciliar contrato semântico; implementar TF/estados de sobrenome e seleção calibrada; validar CPF tardio e substituição histórica. **Não adicionar geração artificial de famílias** à lista de pendências. Não declarar código, testes, recalibração ou merge concluídos por esta atualização documental.

**Precedência:** esta consolidação registra as decisões explícitas de 09/10/2026 e prevalece sobre recomendações preparatórias incompatíveis; não altera automaticamente o código, a release selada ou modelos ATIVOS.

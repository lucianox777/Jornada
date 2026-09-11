# Linkage - blocking dinâmico compartilhado

## Regra normativa corrente

Calibrador e avaliador devem construir o universo de candidatos dinamicamente e consumir **a mesma política versionada**, sem manter regras paralelas. A política deve ser reproduzível, possuir fingerprint e registrar versões do método, normalização e fontes externas utilizadas.

CPF válido/confiável permanece fora do blocking probabilístico: segue a rota determinística CPF -> UUID estável. O blocking probabilístico é exclusivo das hipóteses admitidas sem CPF válido.

## Atributos originais, derivações e modelos de resolução

Atributo original é somente o valor efetivamente recebido da fonte/Secretaria. Qualquer valor obtido por transformação é **calculado**, inclusive `FirstName`, `Surnames`, `LastName`, componentes de data, normalizações, fonéticas, prefixos ou outras representações.

O modelo informa a semântica do atributo original; o Calibrador decide como explorá-lo **somente com base em modelos/algoritmos previamente homologados**. Homologação apenas autoriza o uso. Poder discriminante, recall, redução do espaço de pares, missingness, redundância, ganho incremental, estabilidade e custo são medidos pelo próprio Calibrador sobre o corpus da Jornada.

Não existe componente arquitetural separado chamado Otimizador. Classes internas históricas com esse nome implementam apenas algoritmos auxiliares do Calibrador.

### Catálogo homologado

`HomologatedResolutionModelCatalog` é o catálogo governado de técnicas autorizadas. A V1 contém apenas transformações já existentes no código da Jornada:

- normalização canônica de nome;
- primeiro token;
- tokens de sobrenome;
- último token;
- dia, mês e ano de uma data.

A presença no catálogo **não promove** a transformação para uso operacional. Algoritmos adicionais (por exemplo Jaro/Jaro-Winkler, Levenshtein/Damerau, fonética, n-grams ou técnicas específicas de endereço/telefone) só entram após homologação explícita; sempre que existir implementação madura e compatível, ela deve ser reutilizada em vez de reimplementada.

### Projeção gerada pelo Calibrador

`ResolutionProjectionPlanner` recebe atributos originais com sua semântica e gera automaticamente um `ResolutionProjectionPlan` versionado e com fingerprint. Cada feature registra:

- atributo de origem;
- se é original ou calculada;
- modelo de resolução e algoritmo/versionamento;
- estratégia de materialização sugerida;
- se é multivalorada;
- se pode participar do espaço de busca de blocking.

O plano corrente preserva o vocabulário operacional anterior para nome, nome da mãe e componentes de nascimento, mas a lista deixa de ser a fonte de verdade hardcoded. Novos atributos podem passar pelo mesmo mecanismo sem alterar o algoritmo de busca do ruleset.

As representações calculadas possuem ciclo de vida independente do `BLOCKING_PLAN`:

1. `Candidate`: derivação disponível para avaliação durante a calibração;
2. `Promoted`: derivação aprovada e mantida fisicamente mesmo se não participar do plano corrente;
3. `Indexed`: derivação utilizada por um passe vencedor e candidata natural a índice simples.

Atributos originais permanecem `Source` e nunca são reclassificados como calculados.

`ResolutionProjectionPromotionPlanner` transforma a projeção e os passes vencedores em um plano físico **provider-independent**. Ele não altera a semântica do blocking e falha fechado se um passe referenciar feature inexistente na projeção.

## Silver e Gold

A Gold permanece canônica e não é alterada por experimentações do Calibrador.

A Silver distingue explicitamente:

- colunas/atributos reais recebidos da fonte;
- colunas/projeções técnicas calculadas pelo Calibrador.

Derivações determinísticas escalares podem ser implementadas como computed/generated columns quando a expressão for suportada de forma segura pelo provider; quando isso não for portável ou adequado, a mesma semântica pode ser materializada pelo Processor. Derivações multivaloradas permanecem em estrutura própria de projeção/chaves.

Nenhuma decisão semântica ou estatística pode depender de funcionalidade exclusiva de SQL Server, PostgreSQL ou SQL Database in Microsoft Fabric. Otimizações específicas de provider são permitidas somente quando sua ausência não altera o conjunto lógico de candidatos nem a decisão de identidade.

## Índices

Índices simples são a infraestrutura física padrão das features promovidas para blocking. O banco pode combinar índices simples para predicados `AND`/`OR` conforme seu otimizador físico.

Índices compostos não participam da busca combinatória estatística. Depois de escolhido o `BLOCKING_PLAN`, o Calibrador pode medir os poucos passes vencedores e testar índice composto apenas onde os índices simples não atendam ao orçamento operacional. O índice composto é otimização física, nunca requisito semântico do passe.

Assim, a descoberta do plano pode usar operações de conjuntos sobre features candidatas sem criar estruturas físicas para cada hipótese. Somente finalistas chegam à experimentação física.

## Busca do ruleset

`BlockingRuleSetSearch` continua uma busca bounded e determinística. Primeiro avalia passes primitivos, retém um pool limitado e então testa complementaridade entre poucos passes. A promoção é fail-closed: se nenhuma alternativa atingir o recall mínimo, nenhum ruleset é publicado.

O espaço de busca corrente é obtido de `BlockingCandidateFeatureCatalog.CalibratorCandidates`, que é derivado de `ResolutionProjectionPlanner`. `RequiredOptimizerCandidates` permanece apenas como alias de compatibilidade temporária.

O Calibrador deve medir tanto features individuais quanto combinações. Uma feature com baixo poder isolado pode acrescentar discriminação numa combinação; da mesma forma, duas features fortes podem ser redundantes. A decisão não pode ser inferida apenas do nome/semântica do atributo.

## Implementação operacional existente

O Calibrador PostgreSQL mantém `m` como evidência independente inter-Gestores e forma `u` no corpus capturado sob snapshot consistente. O blocking é calibrado contra os mesmos pares M/U usados na estimação do modelo, sem segunda leitura do corpus entre estimação e escolha dos passes.

`Jornada.Linkage.Evaluation` aplica a política versionada e deve registrar versão/fingerprint utilizados. Calibrador, avaliador, Processor e Runner não podem manter interpretações diferentes da mesma feature.

## Evolução dos modelos homologados

A ampliação do catálogo deve priorizar algoritmos de mercado/pesquisa consolidados e implementações existentes. Código próprio é reservado a lacunas reais da Jornada, especialmente regras brasileiras/municipais que não tenham implementação adequada.

Para técnicas dependentes de idioma/cultura, a homologação deve considerar adequação ao domínio brasileiro; depois de homologado, é o Calibrador que determina empiricamente se a técnica tem poder discriminante suficiente nos dados da Jornada.

Normalização básica PT-BR já controlada pela Jornada (por exemplo normalização Unicode/case/acentuação conforme regra canônica existente) continua sendo base determinística. Transformações que perdem informação, como equivalências fonéticas, devem permanecer representações adicionais e nunca destruir o valor original.

## Próximas fatias operacionais

A arquitetura desta versão cria o catálogo, a geração automática da projeção e o plano provider-independent de promoção/índices. Permanecem como fatias posteriores, sem declaração antecipada de homologação:

- descoberta automática dos demais atributos semânticos de Pessoa presentes na Silver e em `pessoa_atributo`;
- inclusão de novos modelos/algoritmos homologados para telefone, email, endereço, nomes/fonética e comparadores fuzzy;
- geração/aplicação controlada de migrations para computed/generated columns e materializações equivalentes em SQL Server/Fabric e PostgreSQL;
- benchmark dos passes vencedores e criação opcional de índices compostos quando houver ganho demonstrável;
- integração de frequência IBGE onde houver ganho incremental comprovado.

## Paralelismo

Calibrador e avaliador devem usar paralelismo apenas em etapas independentes quando benchmark/regressão demonstrar ganho no ambiente-alvo. A versão paralela deve ser semanticamente equivalente à serial: mesmos pares elegíveis, mesmos parâmetros/métricas e fingerprints determinísticos. Se houver degradação, contenção ou perda de reprodutibilidade, o caminho serial é preferido.

## Regressões obrigatórias

Conforme RNF12 e RNF34-A/B:

- **unitárias:** catálogo homologado, geração/fingerprint de projeção, distinção original/calculada, lifecycle Candidate/Promoted/Indexed, política/fingerprint, limites/configuração e fail-closed;
- **integração:** execução real dos caminhos de Calibrador e avaliador nos providers suportados;
- **novos algoritmos:** vetores positivos/negativos, colisões, estabilidade, equivalência entre providers quando aplicável e regressão sobre corpus independente.

Toda alteração futura no blocking deve atualizar código, regressões, contratos de evidência e documentação no mesmo change-set.

## UML

A sequência normativa desta arquitetura está em `docs/uml/Linkage_Dynamic_Blocking_Sequence.puml`, mantida em padrão UML e versionada junto com a implementação.

## Limite de governança

Blocking dinâmico tecnicamente correto não equivale a homologação estatística. A issue #31 continua aberta até existir corpus representativo/atestado, avaliação independente de recall, precisão, calibração e falsos vínculos, análise de subgrupos/dependências e aprovação institucional explícita. Nenhum resultado desta implementação autoriza criação/fusão automática de UUID ou publicação probabilística em Gold/Serving.

# Linkage - blocking dinâmico compartilhado

**Decisão arquitetural canônica:** [Blocking complementar com referência IBGE, 27/09/2026](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md). **Nome completo, dinâmico e combinado são três capacidades COMPLEMENTARES de um único sistema de recuperação**, unidas e deduplicadas antes do único scorer C# Fellegi–Sunter. CPF confiável é rota determinística separada. A decisão de arquitetura está fixada; a promoção de novos passes e os parâmetros de desempenho dependem dos gates documentados. Este documento descreve o mecanismo compartilhado e não cria um terceiro executor para nome completo.

## Regra normativa corrente

Calibrador e avaliador devem construir o universo de candidatos dinamicamente e consumir **a mesma política versionada**, sem manter regras paralelas. A política deve ser reproduzível, possuir fingerprint e registrar versões de projeções, comparadores e fontes consumidas.

CPF válido/confiável permanece fora do blocking probabilístico: segue a rota determinística CPF -> UUID estável.

## Atributos originais e projeções calculadas

Atributo original é somente o valor efetivamente recebido da fonte/Secretaria. Qualquer valor obtido por transformação é **calculado**, inclusive `FirstName`, `Surnames`, `LastName`, componentes de data, normalizações, fonéticas, prefixos ou outras representações.

O modelo informa a semântica do atributo original; o Calibrador decide como explorá-lo somente com base em algoritmos previamente homologados. Homologação autoriza uso; poder discriminante, recall, redução, missingness, redundância, ganho incremental, estabilidade e custo são medidos pelo Calibrador.

Não existe componente arquitetural separado chamado Otimizador. Classes internas históricas com esse nome são algoritmos auxiliares do Calibrador.

### Projeções homologadas

`HomologatedResolutionAlgorithmCatalog` é extensível. A unidade imutável é `algoritmo@versão`, incluindo o conjunto exato de colunas produzidas. A versão corrente inclui normalização básica PT-BR, componentes de nome, fonética brasileira, componentes de data, telefone e e-mail.

`PERSON_NAME_METAPHONE_BR@V1` produz a projeção `phonetic`, preservando o nome original. A implementação congela como referência o `metaphonebr` do Ipea 0.0.5 no commit upstream `17fdee95581442cdcc98fddc30aea3079caf27ae` e possui vetores locais de conformidade.

`ResolutionProjectionPlanner` gera um `ResolutionProjectionPlan` versionado/fingerprinted. Cada feature registra origem, algoritmo, estratégia de materialização, cardinalidade e elegibilidade para blocking. A presença no catálogo não implica promoção operacional.

As representações possuem ciclo de vida independente do `BLOCKING_PLAN`:

1. `Candidate`: disponível para avaliação;
2. `Promoted`: mantida fisicamente mesmo se não participar do plano corrente;
3. `Indexed`: usada por passe vencedor e candidata a índice simples.

Atributos originais permanecem `Source`.

### Comparadores universais

Comparadores operam sobre **dois valores** e não constituem coluna da Pessoa. `HomologatedResolutionComparatorCatalog` registra capacidades universais versionadas. A versão inicial contém `EXACT_ORDINAL@V1` e `JARO_WINKLER@V1`.

O Calibrador pode, por exemplo, selecionar `name_upper_no_diacritics` e avaliar `JARO_WINKLER@V1` com limiar calibrado. A projeção é configurável; o comparador é capacidade padrão homologada. O limiar é parâmetro do plano, não semântica escondida no algoritmo.

O blocking primário privilegia projeções indexáveis para reduzir o universo. Comparadores fuzzy operam sobre o universo candidato; não se cria artificialmente uma coluna `Jaro` ou `Levenshtein`. Métodos de acesso aproximado específicos de provider, se futuramente homologados, são otimização física e não mudam a semântica.

## Silver e Gold

A Gold permanece canônica e não é alterada por experimentações do Calibrador.

A Silver distingue valores recebidos da fonte e projeções técnicas calculadas. Derivações determinísticas escalares dependentes apenas da própria linha podem ser computed/generated columns quando a expressão for portável; quando isso não for adequado, podem ser materializadas pelo Processor. Derivações multivaloradas permanecem em estrutura auxiliar.

Uma derivação que depende de outra tabela, arquivo ou fonte externa não pode ser tratada como computed/generated column da linha corrente. O valor efetivamente calculado deve ser materializado com referência ao snapshot imutável consumido.

Nenhuma decisão semântica ou estatística pode depender de detalhe físico acidental do runtime ou da camada analítica. SQL Server é o runtime relacional corrente; SQL Database in Microsoft Fabric não substitui essa fonte de verdade operacional.

## Replay e fontes

A projeção **não conhece o plano**. O plano conhece e referencia projeções, comparadores e fontes versionadas.

`CalibrationReplayManifest` registra as dimensões necessárias ao replay:

1. snapshot/fingerprint de Pessoa;
2. snapshot/fingerprint do corpus M/U;
3. snapshots externos com versão lógica, fingerprint do conteúdo e parser quando aplicável;
4. versões dos catálogos de projeção/comparadores e fingerprint do schema de projeção;
5. versão do Calibrador e versão/fingerprint do plano de blocking.

Assim uma fonte externa como IBGE não é registrada apenas como `IBGE`: a calibração aponta para o conteúdo exato consumido. Uma republicação posterior não altera o replay de um plano antigo.

## Índices

Índices simples são a infraestrutura padrão das features promovidas. Índices compostos não participam da busca combinatória estatística: depois de escolhido o plano, o Calibrador pode medir os poucos passes finalistas e testar índice composto somente quando houver benefício operacional demonstrado.

O comportamento lógico deve permanecer equivalente entre os ambientes SQL Server suportados de desenvolvimento, CI, HML e Produção.

## Busca do ruleset

`BlockingRuleSetSearch` V2 continua bounded e determinístico: avalia passes primitivos, retém pool limitado preservando explicitamente alternativas fortes em recall e em redução e testa complementaridade entre passes. Não existe score composto ou peso oculto na busca.

O recall mínimo é uma **restrição fail-closed**, não o objetivo a ser maximizado sem limite. Depois que uma alternativa satisfaz esse piso e, quando exigido, mantém suporte observado de não-vínculos, `BlockingRuleSetOptimizer` V2 prioriza a maior redução do universo candidato; recall é o primeiro desempate, seguido por cobertura e menor custo estrutural. Isso impede que uma regra quase universal de recall 100% vença uma regra discriminante que já atende ao recall mínimo.

O espaço corrente vem de `BlockingCandidateFeatureCatalog.CalibratorCandidates`, derivado do `ResolutionProjectionPlanner`. Features são medidas isoladamente e em combinações; uma feature fraca isoladamente pode acrescentar informação numa interseção, enquanto duas fortes podem ser redundantes.

A fonética PT-BR participa exatamente desse mecanismo: o Calibrador pode mantê-la, descartá-la ou combiná-la com nascimento/mãe conforme evidência do corpus.

A auditoria sintética read-only também recusa o caso degenerado em que a média do conjunto de candidatos coincide com a população Gold inteira. Essa guarda é um invariante técnico de que houve alguma redução; não substitui thresholds de qualidade/homologação HML.

## Implementação operacional existente

O Calibrador SQL Server mantém `m` como evidência independente inter-Gestores e forma `u` sob snapshot consistente. O blocking é calibrado contra os mesmos pares M/U usados na estimação do modelo.

`Jornada.Linkage.Evaluation` aplica política versionada e deve registrar versão/fingerprint utilizados. Calibrador, avaliador, Processor e Runner não podem manter interpretações diferentes da mesma feature.

## Próximas fatias operacionais

Permanecem como evolução posterior:

- descoberta automática dos demais atributos semânticos de Pessoa presentes na Silver e em `pessoa_atributo`;
- persistência operacional do `CalibrationReplayManifest` e das referências de snapshot;
- integração dos comparadores universais à avaliação/calibração de thresholds sobre candidatos;
- novos algoritmos homologados de endereço e outras semânticas ainda não cobertas;
- integração de frequências IBGE quando houver correspondência semântica e ganho comprovado;
- migrations controladas e materializações reproduzíveis no SQL Server;
- benchmark de passes vencedores e índices compostos opcionais.

## Regressões obrigatórias

Conforme RNF12 e RNF34-A/B:

- unitárias: contratos imutáveis de algoritmo, geração/fingerprint de projeção, comparadores, replay/snapshots, lifecycle e fail-closed;
- integração: caminhos reais de Calibrador e avaliador no SQL Server suportado;
- novos algoritmos: vetores positivos/negativos, colisões, estabilidade e regressão sobre corpus independente.

Toda alteração futura deve atualizar código, testes, contratos de evidência e documentação no mesmo change-set.

## UML

A sequência normativa está em `docs/uml/Linkage_Dynamic_Blocking_Sequence.puml`.

## Limite de governança

Blocking dinâmico tecnicamente correto não equivale a homologação estatística. A issue #31 continua aberta até existir corpus representativo/atestado, avaliação independente e aprovação institucional explícita. Nenhum resultado desta implementação autoriza criação/fusão automática de UUID ou publicação probabilística em Gold/Serving.

## Explicação operacional: três capacidades complementares; CPF em rota separada

**Composição lógica obrigatória:** interseção de atributos em cada passe, união entre valores alternativos de uma feature, união deduplicada dos passes de nome completo, dinâmico e combinado. A busca de nome completo é uma capacidade de passes compartilhados, não um terceiro motor. A execução integral da união no Runner é **alvo arquitetural ainda sujeito a promoção**: o batch corrente permanece no dinâmico; a busca semicega usa a união dinâmico+combinado quando elegível e está desabilitada para dados reais até o gate institucional.

**Rota determinística (CPF válido/confiável):** consulta o identificador forte e resolve a identidade conforme as regras próprias de qualidade e conflito. Não é um terceiro *blocking probabilístico*: é uma rota distinta, anterior à recuperação probabilística de candidatos.

**Blocking dinâmico (vigente):** para observações sem identificador forte confiável, aplica um **ruleset versionado e configurável** sobre chaves/projeções calculadas disponíveis. Uma regra pode combinar nome normalizado e nascimento; outra, nome da mãe e nascimento; outra, fonética e ano de nascimento — exemplos ilustrativos, não afirmação de que essas três regras estejam todas habilitadas no ambiente. Apenas regras cujos atributos estejam disponíveis e cuja política permita a execução participam da consulta. O conjunto de candidatos é a união deduplicada dos passes aplicáveis. A política deve manter fingerprint, evidência da versão, limites de volume e rastreabilidade da regra que recuperou cada candidato. “Dinâmico” significa seleção/execução conforme política e atributos disponíveis; **não** significa inventar regras autonomamente a cada busca.

**Blocking combinado (protótipo de avaliação do PR #526):** experimenta passes compostos específicos, inclusive variações de data e fonética, para medir recall, seletividade e contribuição marginal. Não está automaticamente ativado no Runner nem substitui o ruleset dinâmico; seus passes poderão ser incorporados à política versionada apenas após testes e decisão explícita de promoção.

**Nome completo (capacidade complementar):** o valor original permanece preservado e pode contribuir para a comparação probabilística (*scoring*) após recuperar candidatos. Igualdade exata do nome completo **não é requisito universal** de blocking; abreviações, partículas, sobrenomes intermediários e erros de grafia não devem excluir, por si, candidatos alcançáveis por outros passes. `last_name_token` é o último token lexical do nome completo normalizado (ex.: `JOSÉ CARLOS DA SILVA` → `SILVA`), como definição operacional interna, com tratamento testado de partículas, hífens e dados incompletos.

**Limite estatístico do IBGE:** o Censo 2022 publicou frequências de sobrenomes individuais sem preservar a posição. Sua orientação de coleta previa todos os sobrenomes preferencialmente e apenas o último quando necessário. Portanto, frequência publicada de `SILVA` não equivale à frequência de `last_name_token = SILVA`. Usar as frequências como referência marginal auxiliar, nunca como estimativa posicional exata de `u` sem validação. Fonte: IBGE, Nota Técnica 01/2025, *Nomes no Brasil*, https://biblioteca.ibge.gov.br/visualizacao/livros/liv102228.pdf .

**Separação obrigatória:** recuperar candidatos não equivale a vincular identidades. O scoring probabilístico avalia os candidatos recuperados e a política de decisão/publicação aplica seus limiares e salvaguardas; nenhuma regra de blocking, isoladamente, autoriza fusão ou criação automática de UUID.

## P22 — último sobrenome e frequências do IBGE (decisão de modelagem)

**Questão arquitetural resolvida para blocking por presença:** o pedido censitário de todos os sobrenomes, com registro do último quando não for possível captar todos, e a divulgação independente de posição **favorecem o índice invertido por presença do token/sobrenome**. Frequências oficiais de ocorrência em qualquer posição podem orientar seletividade, priorização e ensaios desse índice; não exigimos descobrir o último sobrenome para formar a chave. A restrição semântica restante refere-se somente a transformar a frequência oficial em probabilidade exata da heurística interna `last_name_token` ou de nome completo não estruturado. A aferição quantitativa é feita pelo Calibrador no corpus. Consultar a [decisão canônica](Decisao_Arquitetural_Blocking_Complementar_IBGE_20260927.md#3-por-que-o-ibge-melhora-a-construção-do-índice).

Na Jornada, `last_name_token` é **o último token lexical do nome completo normalizado**; por exemplo, `MARIA APARECIDA DE SOUZA SILVA` produz `SILVA`. O nome completo original deve ser preservado. Tratar partículas (`DE`, `DA`, `DO`, `DOS`, `DAS`), espaços, hífens, nomes monônimos e dados incompletos em testes explícitos; não inferir parentesco nem identidade pelo token isolado. Esta é uma **definição operacional interna**, não a definição estatística da variável divulgada pelo IBGE.

**Fonte primária:** IBGE, Censo Demográfico 2022, Nota Técnica 01/2025, *Nomes no Brasil*, p. 1: https://biblioteca.ibge.gov.br/visualizacao/livros/liv102228.pdf . O recenseador foi orientado a registrar preferencialmente todos os sobrenomes e, caso não fosse possível, apenas o último. Na publicação, o IBGE contou os sobrenomes **independentemente da ordem em que foram registrados**. Portanto, cada entrada agregada ser um único token não demonstra que esse token era o último sobrenome da pessoa. A base publicada mede ocorrência do sobrenome em qualquer posição; não identifica diretamente a distribuição dos últimos sobrenomes.

**Regra de calibração:** permitir `last_name_token` como feature candidata para blocking combinado com primeiro nome, nascimento e atributos maternos, sujeito à avaliação do calibrador. Não atribuir diretamente `u_last_name_token = frequência(SOBRENOME_IBGE) / população` como se fosse uma probabilidade posicional comprovada. Registrar a frequência censitária como referência marginal auxiliar, com proveniência, ano, recorte geográfico e ressalva de posição; estimar e validar o `u` posicional em amostra de nomes completos ou por análise de sensibilidade. Não presumir independência entre atributos nem encerrar a pendência estatística P22 antes dessa validação.

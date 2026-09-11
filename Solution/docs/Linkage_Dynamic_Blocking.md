# Linkage - blocking dinâmico compartilhado

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

Nenhuma decisão semântica ou estatística pode depender de funcionalidade exclusiva de SQL Server, PostgreSQL ou SQL Database in Microsoft Fabric.

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

O comportamento lógico deve permanecer equivalente nos três providers de referência.

## Busca do ruleset

`BlockingRuleSetSearch` continua bounded e determinístico: avalia passes primitivos, retém pool limitado e testa complementaridade. Promoção é fail-closed se nenhuma alternativa atingir recall mínimo.

O espaço corrente vem de `BlockingCandidateFeatureCatalog.CalibratorCandidates`, derivado do `ResolutionProjectionPlanner`. Features são medidas isoladamente e em combinações; uma feature fraca isoladamente pode acrescentar informação numa interseção, enquanto duas fortes podem ser redundantes.

A fonética PT-BR participa exatamente desse mecanismo: o Calibrador pode mantê-la, descartá-la ou combiná-la com nascimento/mãe conforme evidência do corpus.

## Implementação operacional existente

O Calibrador PostgreSQL mantém `m` como evidência independente inter-Gestores e forma `u` sob snapshot consistente. O blocking é calibrado contra os mesmos pares M/U usados na estimação do modelo.

`Jornada.Linkage.Evaluation` aplica política versionada e deve registrar versão/fingerprint utilizados. Calibrador, avaliador, Processor e Runner não podem manter interpretações diferentes da mesma feature.

## Próximas fatias operacionais

Permanecem como evolução posterior:

- descoberta automática dos demais atributos semânticos de Pessoa presentes na Silver e em `pessoa_atributo`;
- persistência operacional do `CalibrationReplayManifest` e das referências de snapshot;
- integração dos comparadores universais à avaliação/calibração de thresholds sobre candidatos;
- novos algoritmos homologados de endereço e outras semânticas ainda não cobertas;
- integração de frequências IBGE quando houver correspondência semântica e ganho comprovado;
- migrations controladas/materializações equivalentes nos providers;
- benchmark de passes vencedores e índices compostos opcionais.

## Regressões obrigatórias

Conforme RNF12 e RNF34-A/B:

- unitárias: contratos imutáveis de algoritmo, geração/fingerprint de projeção, comparadores, replay/snapshots, lifecycle e fail-closed;
- integração: caminhos reais de Calibrador e avaliador nos providers suportados;
- novos algoritmos: vetores positivos/negativos, colisões, estabilidade e regressão sobre corpus independente.

Toda alteração futura deve atualizar código, testes, contratos de evidência e documentação no mesmo change-set.

## UML

A sequência normativa está em `docs/uml/Linkage_Dynamic_Blocking_Sequence.puml`.

## Limite de governança

Blocking dinâmico tecnicamente correto não equivale a homologação estatística. A issue #31 continua aberta até existir corpus representativo/atestado, avaliação independente e aprovação institucional explícita. Nenhum resultado desta implementação autoriza criação/fusão automática de UUID ou publicação probabilística em Gold/Serving.

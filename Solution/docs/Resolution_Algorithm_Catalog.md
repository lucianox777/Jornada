# Catálogo de algoritmos de resolução

## Regra normativa

O catálogo é extensível e contém somente algoritmos explicitamente homologados para experimentação pelo Calibrador.

A unidade de contrato de uma **projeção** é `algoritmo@versão`. Para cada versão ficam congelados a semântica de origem, o conjunto exato de colunas calculadas, a cardinalidade, a estratégia lógica de materialização, a elegibilidade para blocking e as referências de implementação/teste. Adicionar, remover ou alterar a semântica de uma coluna exige nova versão.

Tudo que não veio da origem é calculado. Portanto `first`, `surnames`, `last`, componentes de data, normalizações e fonética são projeções calculadas; nunca substituem o valor original.

## Projeções homologadas iniciais

| algoritmo@versão | semântica | colunas fixas |
|---|---|---|
| `PERSON_NAME_BASIC_PTBR@V1` | PersonName | `upper`, `upper_no_diacritics`, `without_particles` |
| `PERSON_NAME_COMPONENTS@V2` | PersonName | `normalized`, `first`, `surnames`, `last` |
| `PERSON_NAME_METAPHONE_BR@V1` | PersonName | `phonetic` |
| `DATE_COMPONENTS@V2` | Date | `day`, `month`, `year` |
| `TELEFONE_BR_CANONICO@V2` | Phone | `canonical` |
| `EMAIL_CANONICO@V2` | Email | `canonical` |

`PERSON_NAME_BASIC_PTBR@V1` separa deliberadamente UPPER, remoção de diacríticos e remoção das partículas exatas `DA`, `DAS`, `DE`, `DO`, `DOS`. A separação existe para o Calibrador medir cada representação e suas combinações, sem pressupor que normalizar mais é sempre melhor.

`PERSON_NAME_METAPHONE_BR@V1` é uma projeção fonética materializável/indexável para nome brasileiro. A implementação C# da Jornada congela como referência o pacote público `metaphonebr` do Ipea, versão 0.0.5, commit upstream `17fdee95581442cdcc98fddc30aea3079caf27ae`, e possui vetores locais de conformidade. Uma mudança de regras fonéticas, de upstream ou de coluna exige nova versão do algoritmo local.

`TELEFONE_BR_CANONICO@V2` e `EMAIL_CANONICO@V2` continuam usando implementações e testes já existentes no Processor/SQL.

## Comparadores universais

Comparadores são outro tipo de algoritmo. Eles recebem **dois valores** e produzem acordo ou similaridade; portanto não são colunas calculadas da Pessoa.

O catálogo inicial de comparadores universais do sistema contém:

| comparador@versão | saída | limiar calibrável |
|---|---|---|
| `EXACT_ORDINAL@V1` | acordo exato 0/1 | não |
| `JARO_WINKLER@V1` | score de similaridade | sim |

Assim, uma composição como `name_upper_no_diacritics + JARO_WINKLER@V1(threshold=t)` significa: a projeção `upper_no_diacritics` é escolhida pelo Calibrador, Jaro-Winkler é capacidade universal homologada e `t` pode ser aprendido pelo Calibrador.

Comparadores de distância não devem ser transformados artificialmente em coluna ou índice comum. O blocking primário continua privilegiando projeções indexáveis/exatas para reduzir o universo; comparadores fuzzy operam sobre candidatos. Se futuramente um provider possuir método de acesso aproximado homologado, isso será uma otimização física do provider, não mudança da semântica do comparador.

## Relação com o Calibrador

Estar no catálogo autoriza experimentação; não implica promoção física nem uso operacional. O Calibrador pode medir projeções isoladas, interseções (`AND`) dentro de um passe e uniões (`OR`) entre passes, além de avaliar comparadores e limiares sobre o universo candidato. Combinações redundantes devem perder para alternativas equivalentes mais simples.

CPF válido permanece fora do blocking probabilístico e segue a resolução determinística CPF -> UUID.

## Materialização e fontes externas

Uma projeção determinística que depende apenas da própria Pessoa pode ser `GeneratedColumn` quando portável entre SQL Server, PostgreSQL e Fabric, ou `ProcessorMaterialized` quando essa forma for mais adequada. Projeções multivaloradas usam estrutura auxiliar.

Uma projeção que depende de tabela, arquivo ou fonte externa **não pode ser tratada como computed/generated column da linha atual**. Ela deve registrar o valor efetivamente produzido e a identidade imutável do snapshot de referência usado naquela execução. O valor materializado pode permanecer disponível mesmo depois de a fonte externa mudar.

A projeção não conhece o plano. O plano referencia a versão/fingerprint das projeções que utiliza.

## Replay

O replay forte de uma calibração exige congelar, no mínimo:

1. snapshot/fingerprint dos dados Pessoa consumidos;
2. snapshot/fingerprint do corpus M/U;
3. snapshots/fingerprints e versão de parser das fontes externas, quando existirem;
4. versões dos algoritmos de projeção e dos comparadores;
5. versão/fingerprint do schema de projeção, do Calibrador e do plano de blocking.

`CalibrationReplayManifest` registra essas dimensões e produz fingerprint determinístico. A ordem de fontes externas não altera o fingerprint; qualquer mudança efetiva de conteúdo, parser, catálogo, projeção ou plano altera o replay.

Uma fonte como IBGE deve ser identificada por versão lógica e fingerprint do conteúdo efetivamente consumido, não apenas pelo nome `IBGE`. Isso permite reproduzir uma calibração mesmo se a fonte publicar posteriormente uma revisão.

## Extensão

Para acrescentar uma projeção nova, registra-se novo `algoritmo@versão` com colunas fixas e evidência de implementação/teste. Para acrescentar um comparador universal, registra-se novo `comparador@versão` com executor e testes. Fontes externas entram por snapshots, não como dependências implícitas.

O catálogo define o espaço permitido; o Calibrador decide empiricamente o que tem poder discriminante e custo aceitável no corpus da Jornada.

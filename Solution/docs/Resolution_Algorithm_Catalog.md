# Catálogo de algoritmos de resolução

## Regra normativa

O catálogo é extensível e contém somente algoritmos explicitamente homologados para experimentação pelo Calibrador.

A unidade de contrato é `algoritmo@versão`. Para cada versão, ficam congelados:

- a semântica de atributo de origem à qual o algoritmo se aplica;
- o conjunto exato de colunas calculadas produzidas;
- a cardinalidade de cada coluna (simples ou multivalorada);
- a estratégia lógica de materialização;
- a elegibilidade da coluna para avaliação de blocking;
- as referências de implementação;
- as referências de testes/conformidade.

Uma coluna não pode ser adicionada, removida ou ter sua semântica alterada dentro de uma versão já publicada. Qualquer mudança desse tipo exige nova versão do algoritmo.

O catálogo não é limitado a um algoritmo por tipo de atributo. Vários algoritmos podem coexistir para a mesma semântica, permitindo acrescentar, por exemplo, uma transformação fonética de nome sem modificar os algoritmos já publicados.

## Algoritmos iniciais

O catálogo registra somente implementações existentes e cobertas por testes no repositório:

| algoritmo@versão | semântica | colunas fixas |
|---|---|---|
| `PERSON_NAME_BASIC_PTBR@V1` | PersonName | `upper`, `upper_no_diacritics`, `without_particles` |
| `PERSON_NAME_COMPONENTS@V2` | PersonName | `normalized`, `first`, `surnames`, `last` |
| `DATE_COMPONENTS@V2` | Date | `day`, `month`, `year` |
| `TELEFONE_BR_CANONICO@V2` | Phone | `canonical` |
| `EMAIL_CANONICO@V2` | Email | `canonical` |

`PERSON_NAME_BASIC_PTBR@V1` separa deliberadamente três estágios de representação. `upper` faz UPPER com trim/colapso de espaços preservando diacríticos; `upper_no_diacritics` remove diacríticos sobre essa representação; `without_particles` acrescenta remoção das partículas exatas `DA`, `DAS`, `DE`, `DO`, `DOS`. A remoção de partículas nunca transforma um valor não vazio em chave vazia. O valor original não é alterado.

A separação é intencional: o Calibrador não assume que a transformação mais agressiva é a melhor. Ele mede cada projeção e pode concluir que preservar acento, remover acento ou retirar partículas tem melhor relação recall/redução para determinado corpus e combinação de atributos.

`PERSON_NAME_COMPONENTS@V2` e `DATE_COMPONENTS@V2` correspondem às projeções executadas por `BlockingProjectionKeyProjector`. `TELEFONE_BR_CANONICO@V2` e `EMAIL_CANONICO@V2` correspondem às regras já implementadas no Processor e no SQL, com testes unitários, property/fuzz e/ou vetores de conformidade existentes.

## Relação com o Calibrador

Estar no catálogo significa que o algoritmo pode ser aplicado a um atributo compatível e que suas colunas calculadas podem ser avaliadas. Não significa que a coluna será automaticamente promovida para o plano de blocking nem que receberá índice físico.

O fluxo permanece:

1. atributos originais de Pessoa são as fontes;
2. algoritmos homologados produzem colunas calculadas segundo contratos versionados;
3. o Calibrador mede cada projeção no corpus M/U;
4. o Calibrador testa interseções (`AND`) dentro de um passe e uniões de passes (`OR`) usando as projeções candidatas;
5. métricas equivalentes favorecem a alternativa mais simples, evitando conservar combinações redundantes sem ganho;
6. somente projeções escolhidas são candidatas à promoção física e ao plano publicado;
7. a estratégia física de índice é decidida separadamente.

Exemplo: `name_upper` e `name_upper_no_diacritics` podem ser altamente redundantes. Se a interseção entre ambas não trouxer ganho sobre uma delas, o desempate por menor número de cláusulas favorece a representação simples. Já `name_without_particles + birth_year` pode vencer se acrescentar redução sem comprometer o recall exigido.

CPF válido continua fora do blocking probabilístico e segue a resolução determinística CPF -> UUID.

## Extensão

Para acrescentar um algoritmo novo deve ser criada uma nova definição no catálogo com identificador, versão, semântica, colunas de saída, referências de implementação e referências de teste. O catálogo aceita múltiplos algoritmos para a mesma semântica.

Exemplo futuro, sem homologação nesta versão:

`PERSON_NAME_PHONETIC@V1 -> phonetic_full, phonetic_first, phonetic_last`

Esse algoritmo poderá coexistir com `PERSON_NAME_BASIC_PTBR@V1` e `PERSON_NAME_COMPONENTS@V2`; o Calibrador decidirá empiricamente se suas colunas acrescentam recall/redução suficientes para serem promovidas.

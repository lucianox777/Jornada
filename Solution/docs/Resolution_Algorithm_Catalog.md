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

O catálogo não é limitado a um algoritmo por tipo de atributo. Vários algoritmos podem coexistir para a mesma semântica, permitindo acrescentar, por exemplo, uma transformação fonética de nome sem modificar `PERSON_NAME_COMPONENTS@V2`.

## Algoritmos iniciais

A primeira versão do catálogo registra apenas implementações já existentes no repositório:

| algoritmo@versão | semântica | colunas fixas |
|---|---|---|
| `PERSON_NAME_COMPONENTS@V2` | PersonName | `normalized`, `first`, `surnames`, `last` |
| `DATE_COMPONENTS@V2` | Date | `day`, `month`, `year` |
| `TELEFONE_BR_CANONICO@V2` | Phone | `canonical` |
| `EMAIL_CANONICO@V2` | Email | `canonical` |

`PERSON_NAME_COMPONENTS@V2` e `DATE_COMPONENTS@V2` correspondem às projeções já executadas por `BlockingProjectionKeyProjector` (`BLOCKING_PROJECTION_KEY_PROJECTOR_V2`).

`TELEFONE_BR_CANONICO@V2` e `EMAIL_CANONICO@V2` correspondem às regras já implementadas no Processor e no SQL, com testes unitários, property/fuzz e/ou vetores de conformidade existentes.

## Relação com o Calibrador

Estar no catálogo significa que o algoritmo pode ser aplicado a um atributo compatível e que suas colunas calculadas podem ser avaliadas. Não significa que a coluna será automaticamente promovida para o plano de blocking nem que receberá índice físico.

O fluxo permanece:

1. atributos originais de Pessoa são as fontes;
2. algoritmos homologados produzem colunas calculadas segundo contratos versionados;
3. o Calibrador mede as colunas e combinações no corpus M/U;
4. somente as projeções escolhidas entram no plano publicado;
5. a estratégia física de índice é decidida separadamente.

CPF válido continua fora do blocking probabilístico e segue a resolução determinística CPF -> UUID.

## Extensão

Para acrescentar um algoritmo novo deve ser criada uma nova definição no catálogo com identificador, versão, semântica, colunas de saída, referências de implementação e referências de teste. O catálogo aceita múltiplos algoritmos para a mesma semântica.

Exemplo futuro, sem homologação nesta versão:

`PERSON_NAME_PHONETIC@V1 -> phonetic_full, phonetic_first, phonetic_last`

Esse algoritmo poderá coexistir com `PERSON_NAME_COMPONENTS@V2`; o Calibrador decidirá empiricamente se suas colunas acrescentam recall/redução suficientes para serem promovidas.

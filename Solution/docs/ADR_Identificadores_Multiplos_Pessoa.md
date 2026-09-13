# ADR — Identificadores múltiplos por observação de Pessoa

**Status:** aceita; implementação aditiva iniciada na PR #162.

## Contexto

Uma Pessoa pode chegar à Jornada com CPF, CNS, RG, código interno de um cadastro, UUID Jornada, combinações desses identificadores ou nenhum identificador estável. Portanto, `codigoPessoaOrigem` não pode ser tratado como sinônimo de “identificador da Pessoa”.

A unidade observada pela Jornada é a observação cadastral recebida. Os identificadores são evidências associadas a essa observação.

## Decisão

O modelo passa a distinguir três conceitos:

1. `silver.pessoa_observacao`: dados pessoais observados em uma transmissão;
2. `silver.pessoa_identificador_observacao`: zero, um ou vários identificadores observados;
3. `pessoa_uuid`: identidade Jornada à qual a observação foi resolvida, quando houver resolução.

Uma observação sem identificador é válida. Ausência de identificador não deve ser convertida artificialmente em discordância nem impedir persistência da observação.

## Chave semântica do identificador

Um identificador é interpretado por:

```text
(tipo_identificador, namespace, valor_normalizado)
```

Exemplos:

```text
CPF / BR / 12345678901
CNS / BR / 700000000000000
RG / SSP-SP / 12345678
CODIGO_BASE_ORIGEM / CADASTRO_X / 8721
UUID_JORNADA / JORNADA / <uuid>
```

O valor textual sozinho nunca é suficiente para tipos cujo namespace faz parte da identidade do documento.

## Hierarquia de resolução

Os identificadores possuem prioridade explícita de resolução. A ordem inicial normativa é:

```text
100  CPF
 90  UUID_JORNADA
 80  CODIGO_BASE_ORIGEM homologado
 70  CNS validado/homologado
 60  RG qualificado e validado/homologado
 10  OUTRO
```

O CPF é obrigatoriamente o identificador de maior prioridade da Jornada. Nenhum outro tipo ativo pode empatar ou ultrapassá-lo.

A hierarquia possui duas funções:

1. definir qual identificador elegível funciona como âncora principal da tentativa de resolução;
2. estabelecer ordem determinística de avaliação quando vários identificadores estão disponíveis.

A hierarquia **não** significa que identificadores inferiores possam ser ignorados. Depois de selecionar a âncora principal, os demais identificadores determinísticos válidos continuam sendo conferidos.

Assim:

```text
CPF -> UUID A
UUID_JORNADA -> UUID A
=> RESOLVIDO em UUID A, com CPF como âncora principal

CPF -> UUID A
UUID_JORNADA -> UUID B
=> CONFLITO_IDENTIDADE
```

O CPF continua sendo a referência principal, mas a contradição é preservada e exige trilha de divergência/correção governada. A Jornada não sobrescreve silenciosamente o UUID B nem finge que o conflito não existe.

A prioridade também não transforma um identificador condicional em determinístico. CNS, RG e código de base só podem atuar deterministicamente quando suas regras de validação, namespace e homologação permitirem.

## Tipos iniciais

- `CPF`: namespace `BR`; prioridade 100; rota determinística governada pela Jornada;
- `UUID_JORNADA`: namespace `JORNADA`; prioridade 90; determinístico por lookup/redirecionamento, nunca por criação implícita;
- `CODIGO_BASE_ORIGEM`: prioridade 80; determinístico apenas para base homologada;
- `CNS`: namespace `BR`; prioridade 70; armazenável, com elegibilidade determinística condicionada à validação específica;
- `RG`: prioridade 60; exige emissor e UF e elegibilidade determinística condicionada; número isolado não é globalmente único;
- `OUTRO`: prioridade 10; identificador institucional extensível, sem resolução automática por padrão.

## Múltiplos identificadores

A presença de múltiplos identificadores não cria múltiplas Pessoas. Todos pertencem à mesma observação recebida.

A resolução avalia cada identificador segundo sua política e prioridade. Identificadores válidos e determinísticos devem convergir para o mesmo `pessoa_uuid`.

Se dois ou mais identificadores determinísticos válidos apontarem para UUIDs distintos, a Jornada não escolhe um resultado final apenas por prioridade. O resultado é conflito de identidade, com trilha de divergência e correção governada.

## Observação sem identificador

Quando uma origem não dispõe de CPF, CNS, RG, código interno, UUID Jornada ou outro ID estável, a observação ainda pode ingressar com seus atributos pessoais.

Nesse caso ela não possui âncora determinística de origem. A associação a uma identidade depende da política de linkage e dos perfis de evidência homologados.

Uma retransmissão futura sem qualquer chave estável não deve ser presumida como atualização da observação anterior apenas por semelhança cadastral. Isso evita transformar linkage probabilístico em chave de versionamento de origem.

## Retorno do UUID Jornada

Após uma resolução válida, a Jornada pode publicar seu UUID ao sistema integrado. Se o sistema passar a armazená-lo, transmissões futuras podem incluir `UUID_JORNADA / JORNADA / <uuid>` como identificador observado.

Isso permite que uma origem inicialmente sem identificador próprio evolua para integração determinística sem exigir criação de uma segunda numeração municipal redundante.

O UUID publicado continua sujeito às regras de redirecionamento/fusão da identidade Jornada. Um UUID Jornada recebido nunca autoriza criação de uma Pessoa inexistente.

## Relação com Base de Pessoa de Origem

`base_pessoa_origem` permanece válida, mas seu código interno passa a ser apenas um tipo particular de identificador (`CODIGO_BASE_ORIGEM`). A base identifica a autoridade cadastral que emitiu o código; o sistema de origem identifica quem transmitiu/produziu a observação ou o fato.

Assim:

```text
sistema_origem != base_pessoa_origem != pessoa_uuid
```

Uma mesma observação pode conter simultaneamente código da base, CPF, CNS, RG e UUID Jornada.

## Compatibilidade

Durante a migração:

- a coluna legada `cpf` permanece disponível;
- `codigoPessoaOrigem` permanece disponível para integrações atuais;
- ambos são materializados também em `silver.pessoa_identificador_observacao`;
- nenhuma coluna legada é reinterpretada silenciosamente;
- novos contratos deverão expor `identificadores[]` como coleção e permitir ausência completa de IDs.

O contrato antigo CPF/código continua como compatibilidade até o cutover explícito de cada versão de schema.

## Impacto no linkage e no Calibrador

Identificadores determinísticos homologados formam uma camada anterior ao linkage probabilístico. Eles não devem ser transformados em features comuns do Fellegi–Sunter sem desenho metodológico explícito.

Pares verdadeiros derivados de identificadores diferentes poderão futuramente participar da calibração apenas com proveniência do critério de verdade e controle de independência/transportabilidade (P3/P6).

A ausência de qualquer identificador passa a ser uma dimensão explícita do perfil de observabilidade (P26), não uma falha de ingestão.

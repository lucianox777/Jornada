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

## Invariante CPF → UUID

A Jornada já adota uma âncora determinística permanente CPF→UUID. Este ADR não altera essa decisão.

Para um CPF válido e governado, a relação é funcional e estável:

```text
mesmo CPF -> mesmo UUID âncora, sempre
```

O UUID âncora associado ao CPF não é recalculado, substituído nem escolhido novamente porque chegaram CNS, RG, código de base ou UUID Jornada. Fusões e correções podem alterar a resolução canônica da identidade conforme as regras já existentes, mas não reescrevem a âncora histórica CPF→UUID.

Consequentemente, quando há CPF válido, os demais identificadores não disputam qual UUID deve ser criado ou escolhido para aquele CPF. Eles servem para continuidade, proveniência, enriquecimento e detecção de inconsistências cadastrais.

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

## CPF e hierarquia externa

A hierarquia aplica-se aos identificadores **externos** usados pela Jornada quando não existe uma resolução determinística anterior suficiente.

A ordem inicial é:

```text
100  CPF
 80  CODIGO_BASE_ORIGEM homologado
 70  CNS validado/homologado
 60  RG qualificado e validado/homologado
```

`OUTRO` permanece fora da hierarquia até possuir política institucional própria.

O CPF é obrigatoriamente o identificador externo de maior prioridade e, quando válido e governado, é suficiente para resolver a âncora CPF→UUID já existente ou criá-la uma única vez segundo a política de identidade da Jornada.

A prioridade não transforma um identificador condicional em determinístico. CNS, RG e código de base só podem atuar deterministicamente quando suas regras de validação, namespace e homologação permitirem e, na presença de CPF válido, não substituem sua âncora.

## UUID Jornada fora da hierarquia

`UUID_JORNADA` não participa da hierarquia externa.

Ele representa uma identidade que a própria Jornada já resolveu e publicou anteriormente. Quando um sistema integrado devolve esse UUID numa transmissão posterior, ocorre um ciclo de retroalimentação:

```text
observação sem UUID Jornada
    -> Jornada resolve a Pessoa
    -> Jornada publica pessoa_uuid
    -> sistema de origem armazena o UUID
    -> transmissão futura devolve UUID Jornada
    -> Jornada reencontra a identidade já conhecida
```

Portanto, `UUID_JORNADA` tem `papel_resolucao = RETROALIMENTACAO_INTERNA` e `prioridade_resolucao = NULL`.

Seu tratamento é próprio:

1. deve existir na trilha de identidade da Jornada ou ser redirecionável por uma fusão válida;
2. nunca cria implicitamente uma Pessoa nova;
3. serve para continuidade e idempotência semântica da relação com a Jornada;
4. não concorre com CPF, CNS, RG ou código da base como se fosse outro documento externo.

Quando UUID Jornada retornado e CPF válido convergem para a identidade esperada, a retroalimentação está consistente.

Quando divergem, **não existe ambiguidade sobre qual UUID o CPF produz**. O CPF continua resolvendo para sua âncora determinística permanente. A divergência significa que o UUID devolvido pelo sistema está desatualizado, incorreto, associado indevidamente ou precisa ser redirecionado pela trilha governada da Jornada.

Exemplo:

```text
CPF válido            -> UUID âncora A
UUID_JORNADA recebido -> UUID B

=> resolução pelo CPF permanece UUID A
=> NÃO criar UUID novo
=> NÃO trocar a âncora CPF→UUID
=> registrar INCONSISTENCIA_RETROALIMENTACAO
=> preservar UUID B para diagnóstico/correção
```

Se UUID B for um UUID histórico legitimamente redirecionado para A, o redirecionamento fecha a consistência e não há conflito de identidade.

Assim, divergência entre CPF e UUID Jornada deve ser modelada como ocorrência de integridade/retroalimentação, e não como `CONFLITO_IDENTIDADE` entre duas âncoras equivalentes.

## Tipos iniciais

- `CPF`: namespace `BR`; prioridade externa 100; âncora determinística permanente CPF→UUID;
- `CODIGO_BASE_ORIGEM`: prioridade externa 80; determinístico apenas para base homologada;
- `CNS`: namespace `BR`; prioridade externa 70; armazenável, com elegibilidade determinística condicionada à validação específica;
- `RG`: prioridade externa 60; exige emissor e UF e elegibilidade determinística condicionada; número isolado não é globalmente único;
- `UUID_JORNADA`: namespace `JORNADA`; fora da hierarquia, papel `RETROALIMENTACAO_INTERNA`, determinístico somente por lookup/redirecionamento;
- `OUTRO`: fora da hierarquia, sem resolução automática por padrão.

## Múltiplos identificadores

A presença de múltiplos identificadores não cria múltiplas Pessoas. Todos pertencem à mesma observação recebida.

A resolução distingue duas etapas:

```text
1. continuidade interna
   UUID_JORNADA, se presente

2. identificação externa
   CPF > código de base homologado > CNS > RG
```

Na presença de CPF válido, a etapa externa não é uma votação: o CPF aponta para sua âncora permanente. Os demais IDs são reconciliados contra essa identidade e podem produzir evidência de consistência ou ocorrência de integridade.

Na ausência de CPF, identificadores homologados de menor nível podem resolver deterministicamente conforme suas políticas específicas; se insuficientes, a resolução segue para atributos/linkage.

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

A âncora CPF→UUID permanece anterior ao linkage probabilístico. Identificadores determinísticos homologados não devem ser transformados em features comuns do Fellegi–Sunter sem desenho metodológico explícito.

Pares verdadeiros derivados de identificadores diferentes poderão futuramente participar da calibração apenas com proveniência do critério de verdade e controle de independência/transportabilidade (P3/P6).

A ausência de qualquer identificador passa a ser uma dimensão explícita do perfil de observabilidade (P26), não uma falha de ingestão.

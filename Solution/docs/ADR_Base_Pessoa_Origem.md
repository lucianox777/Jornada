# ADR — Base de Pessoa de Origem como namespace da identidade externa

**Status:** proposta implementada de forma aditiva; runtime de resolução por base ainda depende do cutover do Processor.

## Contexto

A Jornada distinguia Gestor e `sistema_origem`, mas tratava a chave estável da Pessoa de origem como `(sistema_origem_id, codigo_pessoa_origem)`. Isso é insuficiente quando dois ou mais sistemas usam o mesmo cadastro de Pessoas.

Esse compartilhamento pode ocorrer:

- entre sistemas distintos de uma mesma Secretaria;
- entre sistemas de Secretarias diferentes;
- entre um sistema finalístico e um cadastro corporativo comum.

Nesses casos, `codigoPessoaOrigem` não pertence semanticamente ao sistema que transmite o fato. Ele pertence à base cadastral que atribuiu o identificador.

A proveniência do registro e a proveniência da identidade são conceitos diferentes:

- `sistema_origem`: quem produziu/transmitiu a observação ou o fato;
- `base_pessoa_origem`: qual cadastro atribuiu o `codigo_pessoa_origem`;
- `codigo_pessoa_origem`: identificador da Pessoa dentro dessa base.

## Decisão

A identidade externa de origem passa a ter como chave semântica:

```text
(base_pessoa_origem_id, codigo_pessoa_origem)
```

`gestor_id` e `sistema_origem_id` permanecem como proveniência e autorização, mas não definem mais, por si sós, o namespace da Pessoa.

É criado o catálogo global `ref.base_pessoa_origem`, com código institucional único, custodiante opcional, escopo e nível de confiança de identidade.

É criada a relação N:N `ref.sistema_origem_base_pessoa`. Uma mesma base pode ser autorizada para vários sistemas, inclusive pertencentes a Gestores diferentes. Um sistema pode, no futuro, ser autorizado a mais de uma base; apenas uma relação ativa pode ser marcada como padrão.

É criada `silver.pessoa_origem_sistema` para registrar o uso efetivo da mesma identidade de origem por mais de um sistema, sem transformar o campo histórico `silver.pessoa_origem.sistema_origem_id` em falso proprietário exclusivo.

## Governança

`ref.base_pessoa_origem.confianca_identidade` admite:

- `HOMOLOGADA_DETERMINISTICA`: a base pode funcionar como âncora determinística de origem;
- `REFERENCIAL`: o código pode ser preservado e consultado, mas não autoriza resolução determinística por si só;
- `NAO_HOMOLOGADA`: não pode ser usada como âncora de identidade.

Nenhuma base compartilhada é criada automaticamente. O compartilhamento entre sistemas, em especial entre Gestores, exige cadastro explícito e homologado.

Coincidência textual de `codigo_pessoa_origem` entre sistemas ou Gestores não produz vínculo se eles não estiverem autorizados para a mesma `base_pessoa_origem`.

## Compatibilidade

A migração cria automaticamente uma base privada para cada `sistema_origem` já existente:

```text
SYS_<GESTOR>_<SISTEMA>
```

Essas bases privadas são `HOMOLOGADA_DETERMINISTICA` porque reproduzem exatamente o namespace que já era tratado como determinístico pela aplicação. Cada sistema existente é associado à própria base como relação padrão.

As linhas existentes de `silver.pessoa_origem` são retroalimentadas com a base privada correspondente. Portanto, o estado anterior continua semanticamente equivalente e nenhum compartilhamento intersistema é inferido no upgrade.

A antiga unicidade `(sistema_origem_id,codigo_pessoa_origem)` é substituída por unicidade `(base_pessoa_origem_id,codigo_pessoa_origem)`.

## Contrato de integração

O contrato alvo passa a admitir `codigoBasePessoaOrigem` no manifesto da Entrega.

Quando ausente, o runtime deve utilizar exclusivamente a base padrão ativa do `sistema_origem`, preservando compatibilidade com integrações existentes.

Quando informado, o runtime deve validar que:

1. a base existe e está ativa;
2. o sistema da Entrega está explicitamente autorizado para a base;
3. a base está `HOMOLOGADA_DETERMINISTICA` antes de utilizá-la como âncora determinística;
4. a resolução de Pessoa é feita por `(base_pessoa_origem_id,codigo_pessoa_origem)`;
5. o sistema observador é registrado em `silver.pessoa_origem_sistema`.

Uma Entrega referencia uma única Base de Pessoa de Origem. Sistemas que operem com mais de uma base devem separar as Entregas por base. Isso mantém `codigoPessoaOrigem` não ambíguo também em `registros.jsonl`.

## Relação com CPF e linkage

A existência de uma Base de Pessoa de Origem homologada não substitui a âncora CPF nem autoriza sobrescrever conflito de CPF.

A ordem conceitual é:

```text
CPF válido e governado
    -> âncora municipal forte

base_pessoa_origem homologada + codigo_pessoa_origem
    -> âncora determinística da origem

atributos pessoais observados
    -> evidência para linkage probabilístico
```

Pares convergentes pela mesma base homologada poderão futuramente compor corpus determinístico de calibração, desde que a metodologia declare explicitamente esse uso e preserve independência entre capturas. Isso não é ativado por esta ADR.

## Não decisões

Esta ADR não:

- transforma qualquer base compartilhada em cadastro mestre municipal;
- presume que sistemas da mesma Secretaria compartilham identidade;
- presume que códigos iguais entre Secretarias representam a mesma Pessoa;
- altera a semântica de `codigoRegistroOrigem`, cujo namespace continua sendo o sistema finalístico;
- ativa automaticamente composição probabilística ou altera P24;
- substitui a governança institucional necessária para homologação de uma base compartilhada.

## Próximo passo técnico

O Processor deve ser alterado para resolver a base do manifesto, selecionar/criar `silver.pessoa_origem` pela nova chave e registrar cada sistema observador. A API de consulta progressiva deve ganhar consulta por base, mantendo a rota histórica por sistema apenas como compatibilidade quando o sistema possuir uma única base autorizada e não ambígua.

# Escopo de produto — Jornada do Cidadão — Fase 1

Este documento registra limites do **produto atualmente implementado**. Ele não cria nova política pública, não redefine competência administrativa e não substitui a Base Normativa publicada. Seu objetivo é impedir que capacidades conceituais ou futuras sejam apresentadas como respostas já suportadas pela Fase 1.

## O que a Fase 1 consolida

A Jornada integra Pessoas e fatos administrativos declarados pelos Gestores, incluindo **Benefícios Concedidos** e **Serviços Prestados**, preservando origem, temporalidade, identidade e regras de projeção. A plataforma é uma camada municipal de consolidação, identidade e interoperabilidade; os sistemas finalísticos permanecem como sistemas de origem e decisão.

No domínio de benefícios, o fato implementado na Fase 1 é **CONCESSAO / Benefício Concedido**. A situação de vigência da concessão pode ser representada conforme o contrato vigente, mas isso não transforma a concessão em pagamento realizado.

## Limite monetário

`PAGAMENTO` e `RECEBIMENTO` são conceitos distintos de `CONCESSAO` e permanecem fora do runtime factual da Fase 1. Consequentemente, valores associados a um Benefício Concedido não devem ser interpretados como pagamento ocorrido, despesa orçamentária, desembolso, competência paga ou valor efetivamente recebido pelo cidadão.

A Fase 1 pode responder perguntas sobre os fatos que efetivamente integra e sobre os campos declarados nesses fatos. Ela **não pode responder, como fato financeiro realizado**, perguntas que dependam de eventos de pagamento/recebimento ou de competência financeira que ainda não integrem o produto.

Essa limitação é de **escopo factual do produto**, não de identidade. Um benefício pode estar corretamente associado a uma Pessoa e ainda assim não existir, na Jornada Fase 1, evidência de pagamento ou recebimento correspondente.

## Cobertura por Gestor e Tipo

A cobertura analítica da Jornada é limitada aos **Gestores e Tipos/versões efetivamente integrados e publicados**. A existência de um modelo extensível para novos Benefícios ou Serviços não significa que todas as Secretarias, políticas públicas ou tipos municipais já estejam presentes na Fase 1.

Assim, qualquer consulta agregada ou pergunta do tipo “quanto”, “quem recebeu”, “quanto foi pago”, “em qual competência” ou equivalente deve ser interpretada dentro de duas fronteiras simultâneas:

1. somente Gestores e Tipos/versões efetivamente integrados entram no universo observado; e
2. fatos financeiros só podem ser afirmados quando o fato financeiro correspondente estiver implementado e integrado — o que não ocorre para `PAGAMENTO`/`RECEBIMENTO` na Fase 1 atual.

Ausência de registro fora dessa cobertura não deve ser convertida em afirmação de inexistência administrativa no Município.

## Reflexo no BI e nas APIs

O BI vigente já explicita que seu aviso monetário não representa pagamento ocorrido, despesa orçamentária ou desembolso. APIs e produtos derivados devem preservar a mesma semântica: **Benefício Concedido não é sinônimo de pagamento**.

A inclusão futura de `PAGAMENTO`, `RECEBIMENTO`, novas Secretarias ou novos Tipos/versões deve ocorrer por evolução contratual/versionada e passar pelos gates aplicáveis. Este documento não antecipa formato, regra institucional, SLA ou fonte desses futuros fatos.

## Evidência documental relacionada

- `Documentos/Requisitos/01_Requisitos_de_Negocio_Jornada_v1.1.md`: preserva os sistemas finalísticos e separa fatos administrativos da Gold cadastral.
- `Documentos/Estado_Engenharia_v4.04.md`: distingue conceitualmente `CONCESSAO`, `PAGAMENTO` e `RECEBIMENTO`, implementando somente concessão na Fase 1.
- `Solution/bi/Jornada.SemanticModel/definition/tables/BeneficiosConcedidos.tmdl`: contém o aviso monetário do modelo semântico.

## Não objetivos

Este registro não altera DDL, OpenAPI, runtime produtivo, regras de identidade, autorização, Fabric, `RELEASE_INFO.txt`, tag ou release. Também não declara cobertura institucional que não esteja materializada nos contratos e dados efetivamente integrados.
# API V1 — consulta de identidade progressiva por origem

A consulta de origem expõe o estado da identidade técnica mantida pela Jornada sem executar resolução, criar UUID, alterar vínculos ou consultar fatos. Não substitui `POST /api/v1/identidade/resolver` nem os GETs de Pessoa, Registros e Possibilidades.

## Requisição

`POST /api/v1/identidade/origens/consulta`, com `Content-Type: application/json`, `X-Jornada-Access-Key` e `X-Jornada-Gestor`.

O corpo mantém `codigoSistemaOrigem` e `codigoPessoaOrigem` obrigatórios e acrescenta `codigoBasePessoaOrigem` opcional:

```json
{
  "codigoSistemaOrigem": "ASSISTENCIA",
  "codigoBasePessoaOrigem": "CADASTRO_SMADS",
  "codigoPessoaOrigem": "origem-1"
}
```

`codigoSistemaOrigem` identifica o contexto de integração cuja autorização será conferida. `codigoPessoaOrigem` é o código local da Pessoa, nunca CPF. Quando `codigoBasePessoaOrigem` é omitido, a API preserva a semântica legada por `(sistema,codigo)`. Quando a base é informada, a chave de identidade passa a ser `(base_pessoa_origem,codigo_pessoa_origem)`; o sistema não integra a chave, servindo apenas para provar que o Gestor autenticado possui um sistema explicitamente autorizado a usar aquela base.

Essa distinção permite que dois sistemas, inclusive de Gestores diferentes, compartilhem legitimamente a mesma Base de Pessoa sem criar identidades de origem artificiais por sistema. O fato de uma base possuir `gestor_custodiante_id` não concede acesso por si só: a autorização operacional continua explícita em `ref.sistema_origem_base_pessoa`.

O código de sistema admite até 80 caracteres, o código da Pessoa até 255 e o código da base até 120 no domínio `[A-Z0-9_-]`. Não há CPF, nome ou outro atributo pessoal no filtro. Os códigos são comparados exatamente, sem busca aproximada. Identificadores de origem não devem ser colocados em URLs, logs ou mensagens de erro.

A autorização exige credencial GESTOR e o scope `jornada.identidade.origem.read`. Credenciais BENEFICIO e SERVICO não são aceitas, inclusive se receberem o scope por erro. Na consulta base-aware, o SQL exige simultaneamente Gestor autenticado, sistema pertencente a esse Gestor e vínculo ativo desse sistema com a base solicitada. A linha `silver.pessoa_origem` é então localizada por base+código, sem exigir que seu `sistema_origem_id` histórico seja o mesmo sistema que faz a consulta.

## Resposta 200

Consulta base-aware:

```json
{
  "codigoSistemaOrigem": "ASSISTENCIA",
  "codigoPessoaOrigem": "origem-1",
  "initialUuid": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1",
  "canonicalUuid": null,
  "estado": "PROVISORIA",
  "versao": 0,
  "criadoEm": "2026-09-08T12:00:00Z",
  "atualizadoEm": "2026-09-08T12:00:00Z",
  "ultimaResolucaoEm": null,
  "codigoBasePessoaOrigem": "CADASTRO_SMADS"
}
```

Na chamada legada, `codigoBasePessoaOrigem` permanece `null`, preservando a forma anterior. `codigoSistemaOrigem` representa o sistema usado como contexto de autorização; em uma base compartilhada ele não significa propriedade exclusiva da identidade de origem.

`initialUuid` é a referência técnica imutável da identidade de origem. `canonicalUuid` só é publicado quando `estado=REFERENCIA`; pode ser o UUID inicial ou uma referência canônica diferente. `PROVISORIA` significa que ainda não há referência canônica publicada; não significa ausência de CPF. `INDEFINIDA` significa que uma execução completa não pôde definir a referência, sem escolher destino arbitrário. O estado não informa certeza absoluta de identidade civil, propriedade de fatos ou elegibilidade a serviços.

`versao` é a versão monotônica do estado progressivo, não a versão cadastral da origem. As datas são instantes UTC. O contrato não expõe scores, candidatos, parâmetros de Linkage, evidências internas, CPF, dados pessoais nem fatos. A serialização de `estado` é textual, com os valores exatos `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`.

## Erros e segurança

401 indica credencial ausente ou inválida; 403 indica tipo ou scope não autorizado; 400 indica corpo ou códigos inválidos; 404 indica ausência de resultado no escopo autorizado, sem distinguir inexistência de origem de falta de autorização; 429 indica limite excedido; 503 com código `IDENTIDADE_PROGRESSIVA_INDISPONIVEL` indica ausência de schema ou view necessários. Inconsistência de estado ou duplicidade falha fechada e não produz referência arbitrária. As respostas de erro não contêm o código interno solicitado nem UUIDs.

A borda utiliza auditoria e limites da classe IDENTIDADE por credencial autenticada. Somente UUIDs efetivamente retornados são adicionados ao contexto de auditoria. O endpoint é somente leitura e não é uma API pública de enumeração de Pessoas.

## Relação com UUID Jornada e múltiplos identificadores

Esta API consulta uma **identidade de origem** por código de uma Base de Pessoa. Ela não deve ser confundida com `UUID_JORNADA`, que é retroalimentação interna emitida pela própria plataforma, nem com CPF/CNS/RG presentes em `pessoa_identificador_observacao`. A presença desses identificadores não transforma esta rota em uma API genérica de busca por documento.

CPF continua obedecendo à âncora municipal permanente CPF→UUID. `UUID_JORNADA` segue apenas a identidade Jornada e seus redirecionamentos governados; não compete com CPF. CNS e RG dependem de suas políticas próprias e não são implicitamente promovidos pela consulta de base.

## Implantação e limites

Instalar a persistência progressiva, a migração de Base de Pessoa e o cutover v4 antes de disponibilizar a semântica base-aware. O contrato usa SQL Server operacional. A consulta não habilita o executor probabilístico e não cria identidade. Fusões, separações e histórico continuam sujeitos às políticas de identidade correspondentes.

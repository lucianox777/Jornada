# API V1 — consulta de identidade progressiva por origem

A consulta de origem expõe o estado da identidade técnica mantida pela Jornada sem executar resolução, criar UUID, alterar vínculos ou consultar fatos. Complementa o contrato de API e a projeção `serving.v_identidade_origem_progressiva` em SQL Server. Não substitui `POST /api/v1/identidade/resolver` nem os GETs de Pessoa, Registros e Possibilidades.

## Requisição

`POST /api/v1/identidade/origens/consulta`, com `Content-Type: application/json`, `X-Jornada-Access-Key` e `X-Jornada-Gestor`. O corpo contém `codigoSistemaOrigem` e `codigoPessoaOrigem`, ambos obrigatórios. O primeiro admite até 80 caracteres e o segundo até 255. Não há CPF, nome ou outro atributo pessoal no filtro. Os códigos são comparados exatamente, sem normalização ou busca aproximada. Códigos de origem podem ser identificadores sensíveis e não devem ser colocados em URLs, logs ou mensagens de erro.

A autorização exige credencial GESTOR e o scope específico `jornada.identidade.origem.read`. A credencial deve pertencer ao Gestor proprietário do sistema de origem. A consulta SQL aplica também o código do Gestor autenticado, o sistema e o código de pessoa, impedindo consulta transversal mesmo quando um código interno coincide com o de outro órgão. Credenciais BENEFICIO e SERVICO não são aceitas, inclusive se receberem o scope por erro. O scope não concede acesso a fatos, projeções de Pessoa nem ao histórico de outras origens.

## Resposta 200

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
  "ultimaResolucaoEm": null
}
```

`initialUuid` é a referência técnica imutável da identidade de origem. `canonicalUuid` só é publicado quando `estado=REFERENCIA`; pode ser o UUID inicial ou uma referência canônica diferente. `PROVISORIA` significa que ainda não há referência canônica publicada; não significa ausência de CPF. `INDEFINIDA` significa que uma execução completa não pôde definir a referência, sem escolher destino arbitrário. O estado não informa certeza absoluta de identidade civil, propriedade de fatos ou elegibilidade a serviços.

`versao` é a versão monotônica do estado progressivo, não a versão cadastral da origem. As datas são instantes UTC. O contrato não expõe scores, candidatos, parâmetros de Linkage, evidências internas, CPF, dados pessoais nem fatos. A serialização de `estado` é textual, com os valores exatos `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`.

## Erros e segurança

401 indica credencial ausente ou inválida; 403 indica tipo ou scope não autorizado; 400 indica corpo ou códigos inválidos; 404 indica ausência de resultado no escopo do Gestor, sem distinguir inexistência de origem de falta de propriedade; 429 indica limite excedido; 503 com código `IDENTIDADE_PROGRESSIVA_INDISPONIVEL` indica ausência de schema ou view necessários. Inconsistência de estado ou duplicidade falha fechada e não produz uma referência arbitrária. As respostas de erro não contêm o código interno solicitado nem UUIDs.

A borda utiliza auditoria e limites da classe IDENTIDADE por credencial autenticada. Somente UUIDs efetivamente retornados são adicionados ao contexto de auditoria. O endpoint é somente leitura e não é uma API pública de enumeração de Pessoas.

## Implantação e limites

Instalar a persistência progressiva, concluir o cutover e instalar a projeção Serving antes de disponibilizar a rota. O contrato usa o provider SQL Server operacional atualmente configurado para a API; a view PostgreSQL permanece disponível para seu Serving, mas esta entrega não anuncia uma API PostgreSQL ainda não implementada. Em HML/Produção, a autenticação corporativa e a concessão institucional de scopes continuam deny-by-default até integração e autorização próprias. Chaves sintéticas são exclusivamente de Development.

A consulta não habilita o executor probabilístico. A issue #31 continua sendo o gate independente de validação estatística e aprovação institucional. Fusões, separações e aliases históricos exigem política e implementação próprias; nenhuma referência dividida pode ser redirecionada automaticamente para um sucessor arbitrário.

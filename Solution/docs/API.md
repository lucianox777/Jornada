# Jornada API — contrato normativo v3.62 / implementação de engenharia v3.72


## Saúde operacional e limite de borda — engenharia v3.69

- `GET /health` e `GET /health/live`: liveness do processo; `/health` é mantido por compatibilidade.
- `GET /health/ready`: readiness de SQL, diretório Bronze e staging; retorna `503` quando uma dependência essencial não está pronta. O SQL só fica `READY` quando o banco declara `Jornada.BaseNormativa=3.62` e `Jornada.SolutionSchema=3.69` e contém os objetos essenciais; banco vazio, antigo ou incompatível retorna `SQL_SCHEMA_INCOMPATIVEL`.
- O teto contratual do ZIP continua 250 MiB. A implementação ajusta `IHttpMaxRequestBodySizeFeature` somente em `POST /api/v1/ingestao/entregas`, antes da leitura do corpo. Proxy/ingress corporativo continua responsável por permitir ao menos o mesmo tamanho.
- Rate limits de aplicação são configuráveis na seção `ApiRateLimiting`; mudança de HML não exige recompilar.
- O contrato máquina está em `openapi/jornada-v1.openapi.json` e `scripts/openapi-contract-gate.py` falha se método+rota divergirem do `Program.cs`.


## Fato finalístico e atribuição de identidade (v3.45)

Todo Benefício Concedido ou Serviço Prestado validamente declarado pelo Gestor é materializado independentemente da resolução de identidade. A versão factual preserva `pessoa_origem_id`, `sistema_origem_id`, `codigo_pessoa_origem`, `cpf_declarado` (quando informado) e `cpf_ausente_motivo`. `codigo_pessoa_origem` é opaco: a finalística pode inclusive usar o CPF como código local, mas a Jornada nunca infere semântica de CPF pelo formato; somente o campo `cpf` é validado como CPF.

`pessoa_uuid` é atribuição canônica corrente, anulável e mutável. `estado_atribuicao_identidade` assume `ATRIBUIDA`, `PENDENTE_IDENTIDADE` ou `CONFLITO_IDENTIDADE`. Projeções individuais usam somente fatos `ATRIBUIDA`; agregados factuais contabilizam todos os fatos vigentes. Correção cadastral da Pessoa não reescreve retroativamente `cpf_declarado`; alterar o sujeito declarado de um fato exige nova versão factual/RETIFICACAO.

### POST /api/v1/identidade/casos
Abre caso governado geral (`CPF_COMPARTILHADO`, `FUSAO_HISTORICA`, `LINKAGE_INCORRETO` ou `OUTRO`) para observações explicitamente indicadas pelo Gestor. A abertura suspende a atribuição canônica dessas observações/fatos sem excluir a ocorrência factual.

### POST /api/v1/identidade/casos/{casoId}/aplicar
Aplica a classificação exaustiva das observações do caso em grupos e UUIDs de destino. A decisão é declarada pela finalística; a Jornada executa a reassociação, recompõe a Pessoa Gold e invalida Possibilidades derivadas.

### GET /api/v1/divergencias?limit=
Fila institucional de divergências **abertas** do Gestor, limitada a 1..500 itens (`limit` padrão 100). Na Fase 1, a tipologia produzida automaticamente pela Solution é `DIVERGENCIA_IDENTIDADE`. Não devolve CPF, UUID nem conteúdo factual; entrega somente identificadores de observação/origem, tipologia, motivo, correlação e instante de abertura.

### POST /api/v1/divergencias/{divergenciaId}/desfecho
Registra `RESOLVIDA` ou `DESCARTADA`, desfecho e observação. O Serviço de Divergências **notifica e registra desfecho; não corrige dados**. Correção cadastral e nova evidência voltam pela ingestão de nova observação de Pessoa; correção de fatos usa `ALTERACAO` ou `RETIFICACAO`; conflitos de identidade usam exclusivamente o fluxo governado próprio de identidade. A autenticação do agente permanece sob responsabilidade da finalística.

## Compartilhamento municipal e restrições de projeção (v3.45)

O conjunto municipal compartilhado de identificação da Pessoa é compartilhado por padrão por **todas as credenciais autorizadas**. A ausência de observação ou fato prévio do Gestor/Tipo não bloqueia a consulta da Pessoa. Credenciais técnicas `BENEFICIO`/`SERVICO` continuam limitadas ao próprio recurso por scope/código autorizado; essa limitação não define uma população histórica de Pessoas.

A projeção retornada continua minimizada por contrato (`pessoa.schema.json`) e por exceções negativas versionadas em `controle.restricao_projecao_jornada_versao`. Sem restrição `ATIVA` aplicável, vale a projeção municipal padrão. Restrições podem reduzir `ATRIBUTO_PESSOA`, `REGISTRO` ou `POSSIBILIDADE`; nunca ampliam scope, schema ou permissão. Uma restrição `ATIVA` com alvo inexistente é rejeitada, evitando exceção silenciosamente fail-open por erro de cadastro.

## Princípio da Fase 1

A Fase 1 recebe fatos já ocorridos na Prefeitura: **Benefícios Concedidos** e **Serviços Prestados**. O catálogo continua usando as Naturezas `BENEFICIO` e `SERVICO`; isso preserva a âncora para a Fase 2, que tratará Benefícios/Serviços Disponíveis e regras de possibilidade sem transformar disponibilidade em fato da Pessoa.

A Jornada não é o frontend transacional nem o sistema de registro dos Gestores. O ato é gravado no sistema finalístico; a publicação para a Jornada deve ser idempotente, preferencialmente por transactional outbox.

## Autenticação

Endpoints funcionais exigem `X-Jornada-Access-Key` e exatamente um identificador de credencial:

- `X-Jornada-Gestor: <CODIGO>`; ou
- `X-Jornada-Beneficio: <CODIGO_TIPO>`; ou
- `X-Jornada-Servico: <CODIGO_TIPO>`.

Os códigos de Tipo têm exatamente quatro caracteres alfanuméricos maiúsculos (`^[A-Z0-9]{4}$`). Credenciais de Tipo são aceitas apenas nas superfícies autorizadas pelo policy engine; ingestão exige contexto de Gestor.

### Identificação opcional do agente pela finalística

A Jornada **não autentica nem autoriza o agente humano dos sistemas finalísticos** e não determina se o Gestor usa gov.br, AD, identidade corporativa ou outro mecanismo. Essa responsabilidade é integralmente da Secretaria/Gestor.

Quando quiser tornar a trilha da Jornada correlacionável por agente, o sistema finalístico pode enviar:

```text
X-Jornada-Agente-CPF: <CPF_DO_AGENTE>
```

O header é opcional. Quando presente, a Jornada valida formato/dígitos verificadores, calcula `HMAC-SHA-256` com chave secreta da instalação e separação de domínio `JORNADA:AGENTE:v1:`, persiste somente `controle.api_evento.agente_cpf_hash BINARY(32)` + `agente_hash_versao` e descarta o CPF em claro. CPF inválido é rejeitado. A Jornada não verifica se o CPF informado corresponde ao usuário autenticado no sistema finalístico; a veracidade dessa associação permanece responsabilidade do Gestor. O segredo HMAC fica em secret store/configuração protegida, nunca no banco ou no código.

## Fronteira municipal de Pessoa

Consultas de Pessoa e Identidade não exigem `X-Jornada-Finalidade`. A Jornada autoriza pela credencial autenticada, scopes e recurso aplicável. GESTOR, BENEFICIO e SERVICO compartilham a Pessoa em âmbito municipal, sem prova de vínculo ou fato prévio; credenciais de Tipo continuam limitadas ao próprio código de recurso/schema.

A cardinalidade de `POST /api/v1/pessoas/consulta` é propriedade do endpoint: de 1 a 1000 UUIDs por requisição. Controles de enumeração e abuso são aplicados por rate limit de borda e por `CredentialId` autenticado, segmentados por classe de endpoint, sem depender de um motivo declarado pelo consumidor.


## Pessoa

A fronteira está formalizada em `serving.v_pessoa`. Na v3.45 ela contém `pessoa_uuid`, `cpf`, `status_cpf`, `nome_completo`, `data_nascimento`, `nome_mae`, `fontes_distintas`, `estado_concordancia` e `atualizado_em`. Esses campos constituem o núcleo comum e não são reduzidos por `restricao_projecao_jornada_versao`; atributos transversais, Registros e Possibilidades continuam sujeitos às exceções negativas. Alterar esse conjunto exige mudança explícita de especificação e da view.

## Auditoria e BI

`controle.api_evento` e `controle.api_evento_pessoa` permanecem a trilha restrita para investigação, com `correlation_id`, Gestor/credencial, recurso, alvo(s) e, quando fornecido pela finalística, o HMAC do CPF do agente. `serving.v_bi_api` é deliberadamente minimizada e não publica `pessoa_uuid`, `agente_cpf_hash`, `credencial_id` nem `api_evento_id`; o BI trabalha com Gestor, rota, recurso, status, latência, volume e contagem de Pessoas.

## Resolução de identidade

### `POST /api/v1/identidade/resolver`

O CPF é enviado no corpo, nunca na query string. A rota aceita qualquer credencial autorizada pelo policy engine e pelo scope aplicável; credenciais técnicas de BENEFICIO/SERVICO continuam limitadas ao próprio recurso, mas não a uma população histórica. A resolução devolve o UUID existente sem exigir vínculo prévio da Secretaria/Tipo. Auditoria permanece obrigatória; finalidade declarada não integra a autorização da Fase 1.


### `POST /api/v1/identidade/conflitos/detalhe`

Somente credencial institucional `GESTOR` com scope `jornada.identidade.conflitos.read`. Recebe CPF no corpo e devolve o estado global do identificador e os núcleos observados lado a lado (observação, Gestor, origem, nome, nascimento, nome da mãe e estado do vínculo). É uma superfície operacional restrita; não integra o Power BI padrão.

### `POST /api/v1/identidade/correcoes`

Somente `GESTOR` com scope `jornada.identidade.corrigir`. O Gestor informa explicitamente o agrupamento das observações, qual grupo é o titular do CPF, UUID de destino opcional por grupo, `atoReferencia` e `justificativa`. A Jornada não decide documentalmente quem é o titular: executa a decisão institucional, preserva o histórico em `identidade.correcao_identidade`/`correcao_identidade_item`, encerra o mapa conflitado, cria o novo mapa ativo e altera somente a atribuição canônica dos fatos já materializados; não reabre lotes nem reescreve o sujeito declarado histórico.

A operação permite **separação** (grupo sem UUID de destino recebe novo UUID), **reassociação** (grupo aponta para UUID existente) e **fusão governada** (grupos podem apontar para o mesmo UUID). Não existe CRUD administrativo genérico de UUID.

## Ingestão

### `POST /api/v1/ingestao/entregas`

Headers mínimos:

```text
Content-Type: application/zip
Content-Disposition: attachment; filename="<NOME_CANONICO>.zip"
Idempotency-Key: <chave idempotente da Entrega>
X-Jornada-Gestor: <GESTOR>
X-Jornada-Access-Key: <segredo>
```

O cliente não envia `entregaId`, `loteSeq`, `loteTotal` nem número de versão de Pessoa/fato.

### Envelope único v2

Todo ZIP contém exatamente:

```text
manifest.json
pessoas.jsonl
registros.jsonl
```

`pessoas.jsonl` não pode estar vazio e deve conter, no mínimo:

1. Pessoas **incluídas no período**;
2. Pessoas cujos dados cadastrais foram **alterados no período**;
3. todas as Pessoas relacionadas aos Benefícios Concedidos ou Serviços Prestados presentes em `registros.jsonl`.

A ausência de fatos no período não dispensa o envio de atualizações cadastrais. `registros.jsonl` pode ter zero bytes.

Quando o arquivo factual estiver vazio, `natureza`/`codigoTipo`/`tipoVersao` podem ser todos nulos (cadastro puro) ou todos preenchidos (confirmação explícita de zero fatos daquele Tipo no período). Se `registros.jsonl` tiver conteúdo, os três campos são obrigatórios.

Exemplo cadastral puro:

```json
{
  "formatoVersao": 2,
  "pessoaSchemaVersao": 1,
  "codigoSistemaOrigem": "SAUDE",
  "natureza": null,
  "codigoTipo": null,
  "tipoVersao": null,
  "dataReferencia": "2026-08-27T00:00:00-03:00"
}
```

Exemplo com contexto de Benefício Concedido (o mesmo manifesto é válido mesmo se `registros.jsonl` estiver vazio):

```json
{
  "formatoVersao": 2,
  "pessoaSchemaVersao": 1,
  "codigoSistemaOrigem": "HABITACAO",
  "natureza": "BENEFICIO",
  "codigoTipo": "AA01",
  "tipoVersao": 1,
  "dataReferencia": "2026-08-27T00:00:00-03:00"
}
```

### Sistema de origem e chaves estáveis

Todo manifesto declara `codigoSistemaOrigem`, identificando o sistema finalístico que atribui as chaves locais. Um mesmo Gestor pode possuir vários sistemas; por isso a identidade externa é sempre no namespace do sistema.

- toda Pessoa exige `codigoPessoaOrigem`;
- todo fato exige `codigoRegistroOrigem`;
- a finalística **não envia número de versão**.

A Jornada calcula um hash canônico do conteúdo de negócio. Para a mesma chave:

- mesmo conteúdo = retransmissão idempotente, sem nova versão lógica;
- conteúdo diferente = a Jornada cria automaticamente a próxima `versao_interna`;
- `operacao` informa a semântica, não a versão: `INCLUSAO`, `ALTERACAO`, `RETIFICACAO` ou `EXCLUSAO`.

`codigoRegistroOrigem` não pode ser reciclado para outro Tipo/Natureza no mesmo sistema de origem.

### Pessoa

Exemplo mínimo:

```json
{
  "codigoPessoaOrigem": "SEH001",
  "cpf": "11144477735",
  "cpfAusenteMotivo": null,
  "nomeCompleto": "Maria da Silva",
  "dataNascimento": "1982-04-10",
  "nomeMae": "Ana de Souza"
}
```

A mesma chave de Pessoa com conteúdo diferente cria nova versão cadastral interna. `sourceTransactionId` pode ser enviado para rastreabilidade, mas não é número de versão e não altera sozinho o hash de negócio.

### Benefício Concedido

Desde a v3.48, os nomes canônicos de temporalidade/valor são exclusivamente `dataInicioConcessao`, `dataFimConcessao`, `dataEventoConcessao` e `valorConcedido`. A v3.64 acrescenta a terminologia mínima comum de vigência: `situacaoVigencia` = `VIGENTE | SUSPENSA | ENCERRADA`; `situacaoVigenciaDesde` é opcional; `motivoEncerramento` é obrigatório somente em `ENCERRADA` e admite `TERMINO_REGULAR | CANCELAMENTO | CESSACAO`. Os aliases genéricos `dataInicio`, `dataFim`, `dataEvento`, `valorMonetario` e `valor` não integram o contrato de Benefício. `regimeVigencia` e a janela permitida de concessão são metadados versionados do Tipo, geridos no catálogo, e não são enviados em cada ocorrência.

Exemplo de linha em `registros.jsonl`:

```json
{
  "codigoPessoaOrigem": "SEH001",
  "codigoRegistroOrigem": "AA-2026-004711",
  "operacao": "INCLUSAO",
  "dataInicioConcessao": "2026-01-01",
  "dataEventoConcessao": "2026-08-25",
  "situacaoVigencia": "VIGENTE",
  "valorConcedido": 600.00
}
```

Se o Gestor constatar que o valor correto sempre foi R$ 1.000, envia a **mesma chave** com `RETIFICACAO` e conteúdo corrigido. Se R$ 600 era correto e o valor realmente passou para R$ 1.000, usa `ALTERACAO`. `EXCLUSAO` é explícita: ausência em Entrega posterior nunca significa exclusão.

### Serviço Prestado

O mesmo modelo vale para Serviços Prestados: `codigoRegistroOrigem` estável, operação semântica e versionamento interno automático. O schema de `registros.jsonl` é selecionado pelo `codigoTipo`/`tipoVersao` do manifesto.

### Nome canônico e SHA-256

```text
ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip
```

Exemplos:

```text
ENTREGA_SMS_SAUDE_v2_<64-hex>.zip
ENTREGA_SEHAB_HABITACAO_v2_<64-hex>.zip
ENTREGA_SMADS_ASSISTENCIA_v2_<64-hex>.zip
```

Subdiretórios, arquivos extras e nomes legados (`beneficios_concedidos.jsonl`, `servicos_prestados.jsonl`) são rejeitados. Nomes internos são canônicos e case-sensitive. Limites da implementação de referência: 250 MB compactados, 2 GB descompactados e 64 KB para `manifest.json`.

Retorno: `202 Accepted` com `entregaId` gerado pelo servidor, status e metadados do payload.

### `GET /api/v1/ingestao/entregas/{entregaId}`

Retorna o estado da Entrega. `ingestao.lote` é detalhe interno.

## Persistência e histórico de versões

O instante oficial de recebimento (`recebido_em`) é gerado pelo servidor da API no aceite do ZIP, persistido em UTC e não pode ser informado pelo Gestor. Para calendário/SLA, a data civil é derivada no fuso institucional de São Paulo.

Bronze preserva cada Entrega ZIP recebida. Silver guarda as versões lógicas distintas por chave de origem. Gold/Serving materializam a versão factual e registram `versao_interna`, `operacao` e `status_analitico`. `ingestao.item_processado` registra o resultado de cada item recebido, inclusive retransmissões idênticas, e as identidades de origem guardam a última recepção/referência confirmada.

As views operacionais padrão expõem somente `VIGENTE`. `HISTORICO`, `RETIFICADO` e exclusões permanecem na trilha `serving.v_bi_registro_versoes`, evitando dupla contagem no BI.

## Atributos transversais e conferência documental

`pessoas.jsonl` pode trazer `atributosTransversais`, `sourceTransactionId` e `conferenciasDocumentais`. Não existe módulo de Regularização Cadastral na Jornada e não são armazenadas imagens de documentos. O Gestor corrige seu próprio cadastro e publica nova observação.

Campos documentais admitidos no núcleo: `CPF`, `NOME_COMPLETO`, `DATA_NASCIMENTO` e `NOME_MAE`.

## Enriquecimento geográfico interno

Distrito e Subprefeitura são classificação histórica da `REFERENCIA_TERRITORIAL` selecionada. A fonte pode declarar referência `DOMICILIAR`, `ACOLHIMENTO_INSTITUCIONAL` ou `REFERENCIA_TERRITORIAL_DECLARADA`; esta última pode vir apenas com Distrito/Subprefeitura. Na ausência de referência explícita, `ENDERECO_RESIDENCIAL` pode originar um fallback `DOMICILIAR`; a partir daí, a leitura territorial usa o snapshot selecionado e não o endereço cadastral diretamente. Quando a referência possui endereço, ele é o Endereço de Referência daquela observação; quando não possui, Distrito/Subprefeitura bastam. A Jornada não infere referência a partir do local do atendimento e não expõe API territorial.

Não existe endpoint territorial da Jornada e não existem rotas da Jornada para descobrir Serviços por Distrito/Subprefeitura. A geografia é insumo interno/analítico, não contrato público.

## Consulta cadastral de Pessoa

- `GET /api/v1/pessoas/{uuid}`
- `POST /api/v1/pessoas/consulta`

A consulta em lote aceita de 1 a 1000 UUIDs e aplica autorização atomicamente ao conjunto. O envelope estável retorna `pessoaUuid`, `dados`, `schemaRef` e `metadados`. `dados` continua validando contra o schema selecionado da Pessoa; `metadados` é interpretação da Gold e não campo cadastral da origem:

```json
{
  "pessoaUuid": "...",
  "dados": { "nomeCompleto": "Maria da Silva", "atributosTransversais": [] },
  "schemaRef": "...",
  "metadados": {
    "fontesDistintas": 3,
    "estadoConcordancia": "CORROBORADO",
    "atualizadoEm": "2026-08-30T12:00:00-03:00"
  }
}
```

Não existem na Fase 1 endpoints `/pessoas/{uuid}/convergencia` ou `/pessoas/{uuid}/verificacoes`. Comparação cadastral assistida é evolução de leitura para fase posterior; conferências documentais são declaradas em `pessoas.jsonl` e seguem Bronze → Silver → Gold.

## Histórico factual corrente

- `GET /api/v1/pessoas/{uuid}/registros`
- `GET /api/v1/pessoas/{uuid}/beneficios-concedidos`
- `GET /api/v1/pessoas/{uuid}/servicos-prestados`

As projeções especializadas retornam a versão lógica corrente de cada registro de origem. A trilha completa de versões é destinada a auditoria/BI de controle, não à superfície operacional padrão.

## Possibilidades

Possibilidade compatível não é Benefício Concedido nem Serviço Prestado e não altera automaticamente a base finalística. Na Fase 2, disponibilidade/oferta e regras de elegibilidade serão modeladas separadamente dos fatos da Fase 1.

## Frontend

A Solution não contém `Jornada.Web`, portal de atendimento ou frontend operacional central. Interfaces e persistência transacional pertencem aos sistemas finalísticos.

## Segurança de ambientes

Fixtures de access key existem apenas para Development. HML/Produção permanecem `DENY_BY_DEFAULT_PENDING_CORPORATE_IDENTITY` até IdP/secret store corporativo. A auditoria não persiste body, CPF/nome, access key ou headers de autenticação.


### `POST /api/v1/identidade/conflitos/detalhe`

Somente credencial institucional `GESTOR` com scope `jornada.identidade.conflitos.read`. Recebe CPF no corpo e devolve o estado global do identificador e os núcleos observados lado a lado (observação, Gestor, origem, nome, nascimento, nome da mãe e estado do vínculo). É uma superfície operacional restrita; não integra o Power BI padrão.

### `POST /api/v1/identidade/correcoes`

Somente `GESTOR` com scope `jornada.identidade.corrigir`. O Gestor informa explicitamente o agrupamento das observações, qual grupo é o titular do CPF, UUID de destino opcional por grupo, `atoReferencia` e `justificativa`. A Jornada não decide documentalmente quem é o titular: executa a decisão institucional, preserva o histórico em `identidade.correcao_identidade`/`correcao_identidade_item`, encerra o mapa conflitado, cria o novo mapa ativo e altera somente a atribuição canônica dos fatos já materializados; não reabre lotes nem reescreve o sujeito declarado histórico.

A operação permite **separação** (grupo sem UUID de destino recebe novo UUID), **reassociação** (grupo aponta para UUID existente) e **fusão governada** (grupos podem apontar para o mesmo UUID). Não existe CRUD administrativo genérico de UUID.

## Ingestão e uso de memória

A API recebe o ZIP por streaming para arquivo temporário limitado a 250 MB, calcula SHA-256 durante a recepção e lê primeiro apenas `manifest.json`. A autorização genérica de ingestão ocorre antes do recebimento; a autorização do `codigoTipo` ocorre antes da validação pesada do conteúdo descompactado. Depois dessas validações, o payload é gravado idempotentemente no armazenamento Bronze definitivo, content-addressed pelo SHA-256, e somente então a API persiste no SQL a `objeto_chave` e os metadados da Entrega. Não há `VARBINARY(MAX)` para os bytes do ZIP.

## CPF na Fase 1

CPF é chave operacional de resolução determinística e qualidade de identidade. A Fase 1 não aplica criptografia de coluna/tokenização na aplicação. O valor não integra views de BI, auditoria HTTP nem logs; somente flags agregáveis de presença/ausência podem ser expostas ao modelo semântico. Desde a v3.42, um CPF já associado a UUID só aceita novo vínculo de fonte após verificação conservadora de consistência do núcleo. Na v3.45 o conflito sobe também para `identidade.identity_map`: divergência forte gera `EM_CONFLITO`, e `/identidade/resolver` devolve `CONFLITO / CPF_EM_CONFLITO_IDENTIDADE` sem UUID enquanto houver ambiguidade. `CPF_COMPARTILHADO_SUSPEITO` e `CPF_NUCLEO_EXISTENTE_INDISPONIVEL` não são enviados ao linkage probabilístico. A reativação do CPF só ocorre por correção governada e auditável.


BronzeStorage v3.38: provider inicial `FileSystem`; chave lógica `sha256/ab/cd/<sha256>.zip`. Em HML/Produção, `BronzeStorage:RootPath` deve ser caminho absoluto em storage compartilhado/durável acessível por API e Processor. A chave relativa fica no SQL; o objeto permanece no mesmo endereço após o processamento.

### Falhas da Bronze

A persistência física ocorre antes do commit relacional e é protegida por lock compartilhado por SHA-256 contra o GC. Se o storage estiver indisponível, a API responde `503 Service Unavailable`, corpo com `codigo=BRONZE_STORAGE_INDISPONIVEL` e `Retry-After: 60`. Se um objeto canônico existente falhar na revalidação de tamanho/SHA-256, responde igualmente 503 com `codigo=BRONZE_INTEGRIDADE_DIVERGENTE`; não é erro de contrato do Gestor. O reenvio com a mesma `Idempotency-Key` é seguro.

Para maximizar deduplicação de bases integrais inalteradas, o gerador homologado deve produzir ZIP determinístico conforme o perfil de `DeterministicIngestionZipWriter` (ordem/nome/timestamp das entries e versão homologada do gerador/runtime). Mesma informação lógica em ZIP arbitrário não implica o mesmo SHA-256.


## Territorialização obrigatória da origem - v3.38

Para `ENDERECO_RESIDENCIAL` e `REFERENCIA_TERRITORIAL`, `situacaoGeografia` é obrigatória. `RESOLVIDA` exige geografia completa (Distrito/Subprefeitura) e `referenciaMalha`; os motivos `FORA_MUNICIPIO`, `SEM_ENDERECO_APTO` e `NAO_RESOLVIDA_ORIGEM` exigem `geografia:null`. A Jornada não consulta PRODAM Pessoa-a-Pessoa na Fase 1.

## Staging temporário da recepção

A API recebe o ZIP em `IngestionStaging:RootPath`, separado da Bronze durável. Em HML/Produção a raiz deve ser absoluta e possuir capacidade/ACL adequadas. O arquivo é removido no caminho normal e o cleanup por idade cobre crashes/falhas de exclusão. Indisponibilidade do staging retorna `503` com `Retry-After`.


### Contratos versionados e cache em runtime (v3.39)

O catálogo de versões permanece dinâmico: novas pastas `vN` podem ser publicadas enquanto API e Processor estão em execução. A descoberta da versão vigente continua consultando o catálogo em disco; o JSON Schema/projetor daquela versão é carregado sob demanda na primeira utilização e então cacheado. Versões já utilizadas são imutáveis: alteração *in-place* do mesmo arquivo é erro operacional; publique nova `vN`.


## Reverse proxy confiável - consolidado na v3.46

`ReverseProxy:KnownProxies` aceita IPs individuais e `ReverseProxy:KnownNetworks` aceita redes CIDR IPv4/IPv6. Se ambos estiverem vazios, Forwarded Headers permanece desabilitado. Valor inválido falha no startup; a aplicação nunca confia diretamente em `X-Forwarded-For`.


## Janela de concessão e replay (v3.50)

`dataInicioConcessao` permanece semanticamente anulável. Se a versão do Tipo possuir `data_inicio_permitida_concessao` e/ou `data_fim_permitida_concessao` e a ocorrência vier com `dataInicioConcessao=null`, a ingestão não é bloqueada: o QC estrutural registra `NAO_VERIFICAVEL` com regra `CONCESSAO_JANELA_NAO_VERIFICAVEL_V1`. Replay/reprocessamento técnico usa a versão do Tipo originalmente registrada na Entrega; mudança normativa posterior só alcança fatos históricos por reavaliação governada explícita.


### Endereço de casa-abrigo-sigilosa

`ENDERECO_CASA_ABRIGO_SIGILOSA` é um atributo reservado para o endereço de uma **casa-abrigo-sigilosa**. Ele não é sinônimo de `ENDERECO_RESIDENCIAL`, não cria uma categoria de “Pessoa protegida” e não é inferido pela Jornada. O atributo só é aceito em Entrega de `SERVICO` cujo `ref.tipo_registro_versao.origina_endereco_casa_abrigo_sigilosa=1`; portanto, o dado tem de vir do próprio serviço de casa-abrigo cadastrado para essa finalidade.

Por regra estrutural, esse atributo não é projetado pela API a Gestor diferente do Gestor responsável, independentemente da ausência de restrições manuais em `controle.restricao_projecao_jornada_versao`. Os demais dados da Pessoa seguem as regras normais de compartilhamento. O endereço sigiloso também não alimenta `REFERENCIA_TERRITORIAL`.


## Cardinalidade de atributos e UUID canônico — normativa v3.59 / engenharia v3.63

`TELEFONE_CONTATO` e `EMAIL_CONTATO` podem aparecer mais de uma vez no objeto projetado quando o JSON Schema da credencial os autoriza. Cada instância é normalizada internamente; a API preserva o contrato de atributos como coleção e não escolhe arbitrariamente um único telefone/e-mail.

Quando o UUID solicitado foi absorvido por `FUSAO_HISTORICA`, a consulta é resolvida para o sucessor canônico. Nas respostas de Pessoa, `pessoaUuid` é o UUID canônico e `metadados.pessoaUuidSolicitado`/`metadados.redirecionadoPorFusao` registram a continuidade. Nas Possibilidades, esses dois campos ficam no envelope. Registros/Benefícios/Serviços consultam a mesma Pessoa canônica e a auditoria registra UUID solicitado e canônico.

**Semântica de ausência:** um atributo ausente da resposta não significa necessariamente que inexista na origem ou na Gold. Ele pode não integrar o schema autorizado ou pode ter sido suprimido pela regra de projeção. A API deliberadamente não retorna placeholder `RESTRITO`, para não revelar a existência de dado não projetável.


### Fusão histórica exaustiva — v3.59/v3.63

`FUSAO_HISTORICA` é operação 2→1 exaustiva por UUID absorvido. A aplicação deve incluir todos os vínculos correntes `RESOLVIDO` da origem no mesmo UUID canônico. Se houver qualquer vínculo corrente da origem fora do caso ou destinado a outro UUID, a aplicação falha antes das mutações com SQL error `51119`.

### Chave canônica de telefone — v3.59/v3.63

`TELEFONE_CONTATO` usa `TELEFONE_BR_CANONICO_V2`. Para números brasileiros, `11 99999-0001`, `+55 (11) 99999-0001`, `00 55 11 99999-0001` e `5511999990001` representam a mesma instância `5511999990001`. Número sem DDD e número internacional não brasileiro sem prefixo internacional explícito falham fechado.


## Hardening normativo v3.60 / engenharia v3.64

### Preflight completo de FUSAO_HISTORICA

A aplicação de `FUSAO_HISTORICA` valida antes da primeira mutação: exaustividade e convergência 2→1 (`51119`), sucessão canônica inválida/cíclica (`51117`) e CPF ativo divergente (`51118`). Uma mesma origem repartida entre dois destinos também é `51119`; o caso não é convertido em reclassificação parcial.

### Upgrade TELEFONE_BR_CANONICO_V2

O upgrade v3.59→v3.60 não transforma apenas a chave já persistida. Silver e Gold recomputam `atributo_instancia_chave` a partir do `valor` original por `ref.fn_telefone_br_canonico_v2`, preservando a evidência de prefixo explícito `+`/`00`. Assim, `00 55 11 99999-0001` converge a `5511999990001`. Valor legado que não satisfaz o contrato V2 interrompe o upgrade para saneamento explícito.

A mudança é migração normativa: reprocessamentos executados sob v3.60 usam a regra corrente `TELEFONE_BR_CANONICO_V2`. A existência do helper legado `TELEFONE_DIGITOS_V1` no assembly não seleciona automaticamente a regra histórica da carga.


## Fechamento técnico de atomicidade e conformidade — engenharia v3.65

Não há mudança de contrato HTTP ou normativa. As operações governadas de identidade são atomicamente autocontidas também quando executadas administrativamente por `EXEC`: a procedure abre transação somente se `@@TRANCOUNT=0`; quando a API já possui transação, não executa commit/rollback da transação do chamador. O mesmo princípio protege sincronização de atribuição factual e recálculo agregado de Entrega.

Os caminhos de retry/poison/rejeição/quarentena do Processor mantêm a alteração do lote e o recálculo da Entrega na mesma transação.

A semântica executável de `TELEFONE_BR_CANONICO_V2` usa como envelope removível somente SPACE, TAB, CR, LF e NBSP nas extremidades. O arquivo `tests/fixtures/phone/telefone-br-canonico-v2.json` é consumido pelos testes C# e SQL; divergência entre as duas implementações é falha de integração.


## Convergência normativa/engenharia v3.62 / v3.68

`EMAIL_CONTATO` usa `EMAIL_CANONICO_V2`. A chave aplica o mesmo contrato no Processor e no SQL: trim apenas de SPACE/TAB/CR/LF/NBSP nas extremidades, exatamente um `@` com partes não vazias, lowercase somente de ASCII A–Z, preservação dos demais codepoints e nenhuma normalização Unicode implícita. A função `ref.fn_email_casefold_v1` permanece somente como legado de diagnóstico/replay histórico e não é selecionada pelo catálogo corrente.

Upgrade de e-mail inválido sob V2 interrompe a migração com `51087`; não há `COALESCE` para uma regra antiga. Isso mantém simetria entre upgrade e reprocessamento corrente. Para atributos `MULTI`, duas representações textuais que produzem a mesma chave de instância não são, por si, `CONFLITO_EVIDENCIA`.

O readiness SQL exige versão exata do schema por extended properties e os objetos críticos, incluindo as funções canônicas V2.

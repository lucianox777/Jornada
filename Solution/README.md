# Jornada — Solution de Referência (Fase 1) — engenharia v3.90

> **Base normativa vigente: v3.62; schema persistido esperado: Base 3.62 / SolutionSchema 3.68.** A release de engenharia v3.90 preserva a semântica funcional da Base 3.62/SolutionSchema 3.68, mantém os hardenings SQL/Bronze/Docker da v3.88 e corrige o isolamento da suíte Integration para que o banco descartável satisfaça os próprios guards fail-closed Test/Dev/Local. Também incorpora scripts canônicos de limpeza e validação local. Veja `../RELEASE_INFO.txt` e `docs/Governanca_Tecnica_Readiness.md`.

Stack: **C# 12 / .NET 8**, Microsoft SQL Server e Power BI Project (PBIP/TMDL/PBIR).

Desenvolvimento local recomendado: **SQL Server 2022 Developer em Docker**, explicitamente `MSSQL_PID=Developer`, sem uso permitido em Produção. `docker-compose.yml` + `scripts/local-db.*` criam `JornadaLocal`, aplicam DDL/seed e permitem executar os testes SQL sem instalar SQL Server no host. O repositório Git é a fonte oficial dos artefatos editáveis; releases usam tag `jornada-fase1-vX.YY` e registram o commit em `RELEASE_INFO.txt`. Veja `docs/Runbook_Desenvolvimento_Local.md`, `docs/Runbook_Testes_Tecnicos.md` e `docs/Runbook_Git_Release.md`.

### Limpeza e validação local canônicas — v3.90

Na raiz `Solution`:

```powershell
.\scripts\local-clean.ps1
.\scripts\local-validate-release.ps1
```

`local-clean.ps1` remove banco/volume/container locais e saídas `bin/obj`, preserva a imagem Docker do SQL Server e trata `.vs` apenas em best-effort para não abortar quando Visual Studio/Copilot mantém cache aberto. `local-validate-release.ps1` executa restore `--locked-mode`, build Release, Unit e Integration; a Integration usa Testcontainers com banco descartável `JornadaIntegrationTest_<guid>`.

A `Jornada.Api` é a borda externa oficial. O processamento Bronze→Silver→Gold/Serving e o linkage são internos e auditáveis.

**Fronteira de responsabilidade:** a Solution não contém frontend central. Interfaces de atendimento, regras transacionais e persistência do ato pertencem aos sistemas dos Gestores finalísticos. A integração com a Jornada ocorre por API; quando nasce de alteração transacional no sistema setorial, recomenda-se outbox local + publicação idempotente, em vez de dual-write síncrono entre bancos.

## Decisões estruturais vigentes

- Cada versão de Tipo pode monitorar atraso de recebimento por `monitorar_atraso`, `prazo_recebimento_dias` e `marco_atraso_codigo`; o BI deriva `dias_para_recebimento` e `dias_atraso` em `serving.v_bi_atrasos`.
- `recebido_em` é atribuído exclusivamente pelo servidor da API no aceite do ZIP e persistido em UTC. Indicadores de calendário derivam `data_recebimento_sla` no fuso institucional de São Paulo antes de calcular dias de SLA.
- O PBIP inclui a página **Atraso de Recebimento** e a tabela semântica `Atrasos`.

### 1. Envelope único de Entrega em ZIP

`POST /api/v1/ingestao/entregas` aceita um único formato de pacote, sempre com exatamente três arquivos na raiz:

```text
manifest.json
pessoas.jsonl
registros.jsonl
```

`pessoas.jsonl` é obrigatório e não pode estar vazio. Ele deve conter, no mínimo, **as Pessoas incluídas ou alteradas no período de referência e todas as Pessoas relacionadas aos Benefícios Concedidos ou Serviços Prestados enviados na mesma Entrega**. Isso permite atualização cadastral mesmo quando nenhum fato ocorreu no período.

`registros.jsonl` pode ter **zero bytes**. Quando estiver vazio, há duas formas válidas:

- `natureza`, `codigoTipo` e `tipoVersao` todos nulos: Entrega exclusivamente cadastral, como carga inicial ou atualização de Pessoas;
- os três campos preenchidos: confirmação explícita de que não houve fatos daquele Tipo no período, embora possa haver Pessoas incluídas/alteradas.

Quando `registros.jsonl` contiver ao menos um fato, `natureza`, `codigoTipo` e `tipoVersao` são obrigatórios. Todos os fatos da Entrega pertencem ao mesmo Tipo/versão declarado no manifesto.

O cliente não cria `entregaId`, `loteSeq` nem `loteTotal`. A API gera `entregaId`; `ingestao.lote` é apenas particionamento técnico interno.

Todo ZIP usa nome canônico único com SHA-256 dos próprios bytes:

```text
ENTREGA_<GESTOR>_<SISTEMA_ORIGEM>_v2_<sha256>.zip
```

A API recalcula o hash e rejeita divergência. As cargas válidas descompactadas estão em `tests/fixtures/ingestao/` e incluem exemplos cadastral puro, factual, e factual sem ocorrências no período.

### 2. Tipos factuais unificados nas bordas

Os códigos de Tipo de Benefício e Tipo de Serviço possuem **exatamente 4 caracteres alfanuméricos maiúsculos (`A-Z0-9`)**. Não existe código de Tipo com cinco caracteres na Fase 1.

O catálogo comum é `ref.tipo_registro` + `ref.tipo_registro_versao`; Silver usa `silver.registro_observacao`; Serving usa `serving.registro_integrado`; Gold permanece especializada em `gold.beneficio_concedido` e `gold.servico_prestado`.

`tipo_medida` é obrigatório em toda versão. Benefícios usam `MONETARIO`, `QUANTIDADE`, `MONETARIO_E_QUANTIDADE` ou `SEM_MEDIDA`; Serviços declaram explicitamente `SEM_MEDIDA`.

Desde a v3.48, Benefício Concedido usa vocabulário factual próprio: `data_inicio_concessao`, `data_fim_concessao`, `data_evento_concessao` e `valor_concedido` no canônico SQL (camelCase equivalente no JSON/API). `ref.tipo_registro_versao` também versiona `regime_vigencia` e a janela permitida de concessão. Benefícios ATIVOS exigem `PRAZO_DETERMINADO` ou `PRAZO_INDETERMINADO`; Serviços usam `NAO_APLICAVEL`. O Processor não aceita aliases genéricos de Benefício.

### 3. Identidade de origem, versionamento interno e confirmação

Todo manifesto declara `codigoSistemaOrigem`. Toda Pessoa declara `codigoPessoaOrigem`; todo fato declara também `codigoRegistroOrigem`. O namespace das chaves é o sistema de origem, não apenas o Gestor.

A finalística **não envia número de versão**. A Jornada calcula hash canônico do conteúdo de negócio. Para a mesma chave de origem: conteúdo idêntico é retransmissão idempotente e não cria nova observação lógica; conteúdo diferente cria automaticamente a próxima `versao_interna`. O campo `operacao` declara somente a semântica da mudança: `INCLUSAO`, `ALTERACAO`, `RETIFICACAO` ou `EXCLUSAO`.

Bronze preserva todas as Entregas recebidas. `ingestao.item_processado` registra o resultado de cada Pessoa/fato (`INCLUIDO`, `VERSIONADO`, `RETRANSMITIDO`, `EXCLUIDO` ou `REABERTO`) e permite medir confirmações/retransmissões sem criar versões artificiais na Silver. `silver.pessoa_origem` e `silver.registro_origem` mantêm `ultima_recepcao_em` e `ultima_referencia_recebida`.

Silver preserva as versões lógicas distintas. Nas views operacionais e no BI factual entra somente a versão `VIGENTE`; `HISTORICO`, `RETIFICADO` e exclusões permanecem na trilha `serving.v_bi_registro_versoes`. Assim, corrigir um Auxílio Aluguel de R$ 600 para R$ 1.000 não produz soma de R$ 1.600.

### 4. Integridade por Natureza nas estruturas comuns

`silver.registro_observacao` e `serving.registro_integrado` possuem `CHECK` que impede atributos próprios de Benefício em Serviço e vice-versa. Serving também possui FKs compostas Tipo↔Gestor↔Natureza e Tipo↔Versão, além de FK para a observação Silver.

O QC foi generalizado para `qualidade.qc_registro_implementacao` e `qualidade.qc_registro_resultado`, com `serving.v_bi_qc` cobrindo ambas as Naturezas.

### 5. Atributos transversais de Pessoa sem churn de DDL

`gold.pessoa` mantém o núcleo Golden; `ref.atributo_transversal`, `silver.pessoa_atributo_observacao` e `gold.pessoa_atributo` armazenam atributos transversais e histórico SCD2. Novo atributo transversal custa catálogo/configuração, não `ALTER TABLE gold.pessoa`.

Não há `dados_json` em `silver.registro_observacao` nem `atributos_json` em Gold factual na Fase 1.

### 6. Serving e proteção monetária

`serving.registro_integrado` é físico comum, mas consumidores e Power BI usam exclusivamente views `serving.v_*`. `v_bi_servicos_prestados` e `v_bi_registros` não expõem valores monetários.

### 7. Bronze do ZIP

A Fase 1 armazena o ZIP original fora do SQL Server em Bronze content-addressed. `bronze.entrega_arquivo` mantém `objeto_chave`, SHA-256, tamanho, nome e metadados; os bytes ficam no `BronzeStorage` configurado. A implementação inicial é `FileSystem` sobre diretório/mount, com chave lógica `sha256/ab/cd/<sha256>.zip`. Em Development, `RootPath` relativo é resolvido pela raiz da Solution; em HML/Produção, `BronzeStorage:RootPath` é obrigatório, absoluto, compartilhado e durável para todas as instâncias de API/Processor. O objeto é gravado antes da linha SQL, de forma idempotente, e não é movido após processamento: o estado do pipeline permanece no banco. A interface `IBronzeObjectStore` permite substituir o provider físico por object storage/NAS sem alterar a chave lógica nem o esquema relacional.

### Conferência documental sem cadastro paralelo

Não existe Módulo de Regularização Cadastral na Jornada. O Gestor altera seu próprio sistema e publica a nova observação pelo envelope único da API de ingestão. `pessoas.jsonl` pode trazer `sourceTransactionId` e `conferenciasDocumentais` por campo (`CPF`, `NOME_COMPLETO`, `DATA_NASCIMENTO`, `NOME_MAE`). A Jornada guarda somente metadados da conferência — nunca imagem do documento — em `silver.pessoa_campo_verificacao_observacao`. O núcleo Golden prefere evidência documental mais recente por campo e cai para a observação mais recente somente quando não há conferência.

### Territorialização de origem — Fase 1

A Jornada não oferece endpoint geográfico aos sistemas finalísticos e não executa chamada PRODAM Pessoa-a-Pessoa no Processor. Distrito é unidade territorial e Subprefeitura é o órgão que administra vários Distritos. `REFERENCIA_TERRITORIAL` permanece atributo próprio e fonte territorial da visualização, distinta de `ENDERECO_RESIDENCIAL`. Para `ENDERECO_RESIDENCIAL` e `REFERENCIA_TERRITORIAL`, o Gestor deve informar `situacaoGeografia`: quando `RESOLVIDA`, são obrigatórios Distrito/Subprefeitura e `referenciaMalha`; nas demais situações, a ausência é qualificada explicitamente. Se não houver `REFERENCIA_TERRITORIAL` explícita, um `ENDERECO_RESIDENCIAL` pode funcionar como fallback `DOMICILIAR`, preservando a geografia declarada pela origem. A plataforma nunca promove automaticamente unidade de serviço, local de atendimento, CRAS, UBS, Centro POP ou acolhimento a referência territorial. O snapshot e sua natureza são preservados historicamente e cada fato materializado guarda a FK da referência efetivamente usada. A antiga `silver.endereco_residencial_geografia_observacao` é migrada e removida; não há dual-write. Mapas/segmentações usam somente Distrito/Subprefeitura, situação geográfica, referência da malha e natureza da referência selecionada.

## Executáveis da Fase 1

- `Jornada.Api` — autenticação/autorização, resolução CPF→UUID, ingestão ZIP e consultas autorizadas.
- `Jornada.Processor.Worker` — processamento interno Bronze→Silver→Gold/Serving; pode dividir uma Entrega em lotes técnicos.
- `Jornada.Linkage.Parameters.Worker` — gera/versiona parâmetros m/u/prior/threshold do fallback probabilístico.
- `Jornada.Linkage.Runner` — executa scoring probabilístico versionado sob demanda ou por scheduler.
- `Jornada.Linkage.Evaluation` — ferramenta DEV/HML somente leitura para comparar blocking V1×V2 candidato e medir transportabilidade de `m` em amostra SEM_CPF rotulada; nunca publica resultado.
- `Jornada.Contracts` — contratos compartilhados.
- `Jornada.Ingestion` — inspeção segura de ZIP, nome canônico/SHA-256 e validação JSON Schema da Fase 1.
- `Jornada.Tests` — testes Unit/OpenAPI runtime NUnit.
- `Jornada.Integration.Tests` — testes Integration SQL Server real/Testcontainers, em assembly dedicado.

## Identidade e linkage

CPF válido é o identificador externo primário para resolução normal; `PESSOA_UUID` é chave técnica interna, aleatória e estável. Desde a v3.42, CPF já associado a um UUID não cria automaticamente novo vínculo de fonte: o núcleo informado é confrontado com o núcleo existente antes da associação. Divergência forte (V1: nome em estado LOW + data de nascimento diferente) produz `CONFLITO / CPF_COMPARTILHADO_SUSPEITO`, sem `pessoa_uuid` e sem fallback probabilístico. Se o CPF já estiver mapeado mas o núcleo existente não puder ser carregado para comparação, a resolução também falha fechada (`CONFLITO / CPF_NUCLEO_EXISTENTE_INDISPONIVEL`) em vez de reutilizar o UUID sem evidência. CPF ausente em hipótese admitida pode entrar no linkage sob demanda; CPF informado e inválido continua sendo conflito.

O baseline é `FELLEGI_SUNTER_ANCHORED_V1`. Modelo, parâmetros, runs e resultados são versionados. `identidade.linkage_run_item` materializa o universo congelado de cada execução; `identidade.linkage_resultado` é append-only; apenas runs `PUBLICADO` alimentam `identidade.v_vinculo_corrente`.

O Runner persiste scores em transações curtas por lote e publica a execução por uma transição final atômica do cabeçalho. Não mantém uma transação SQL de horas.

Na Fase 1 o corpus integrado é de escritor único. `Jornada.Api` continua recebendo Entregas e persistindo Bronze sem participar do gate; `Jornada.Processor.Worker` materializa um lote por vez; `GENERATE_DRAFT` e `Jornada.Linkage.Runner` obtêm janela exclusiva do corpus. A coordenação usa `sp_getapplock` com owner `Session` em conexão dedicada sem pooling/enlistment. Um lock de intenção exclusiva, adquirido com espera curta e finita, impede novos lotes assim que o job consegue declarar a janela; o job aguarda somente o lote corrente terminar. Cada lease executa heartbeat na própria sessão e cancela o trabalho fail-closed se perder a conexão ou qualquer lock esperado.

Com o corpus imóvel durante Parameters/Runner, a Fase 1 não depende de `ALLOW_SNAPSHOT_ISOLATION` nem de row-versioning para consistência do linkage. `identidade.linkage_run_item` permanece porque materializa, para auditoria/replay, a lista exata de observações de cada run. Os comandos SQL de leitura/materialização continuam com timeout finito; isso limita comandos degradados sem reintroduzir transação SNAPSHOT.

## Segurança de Development

**Fronteira de autorização institucional + restrições versionadas de projeção.** Consultas de identidade/Pessoa/Registros/Possibilidades são autorizadas pela credencial autenticada, scopes e recurso aplicável, sem `X-Jornada-Finalidade`. O `MunicipalAccessPolicyEngine` não exige vínculo, observação ou fato prévio da Pessoa; credenciais técnicas de BENEFICIO/SERVICO continuam restritas ao próprio recurso, scopes e schemas autorizados. O endpoint em lote autoriza o conjunto de UUIDs atomicamente, sem informar qual item falhou. Rate limits são segmentados por classe de endpoint e credencial autenticada.

**HML/Produção continuam bloqueados por desenho.** `/health` e `/health/live` são liveness e expõem `securityMode=DENY_BY_DEFAULT_PENDING_CORPORATE_IDENTITY` fora de Development. `/health/ready` testa SQL, escrita na Bronze e escrita no staging e retorna 503 enquanto uma dependência essencial estiver indisponível ou mal configurada. SQL de negócio existir não significa que a API esteja pronta para ambiente compartilhado: sem IdP/secret store corporativo, nenhuma credencial funcional é resolvida.

A Solution não contém gerador de chaves. `config/security/test-access-keys.json` contém **chaves sintéticas pré-geradas** somente para teste local. `Jornada.Api` carrega esse arquivo apenas quando `ASPNETCORE_ENVIRONMENT=Development`. Em HML/Produção, o resolver de Development não é registrado e a API permanece deny-by-default até a ligação com o secret store/identidade corporativa.

Não reutilize as chaves de teste fora de Development.

## Banco e massa DEV

1. Execute `database/Jornada_Fase1.sql`.
2. Homologue o gate serial do pipeline e o tempo máximo para o lote corrente drenar antes de Parameters/Runner; a API/Bronze permanece disponível durante essas janelas.
3. Em banco DEV/Test, execute `database/Jornada_Seed_Dev.sql`.

O seed demonstra catálogo factual único, atributos transversais e três Entregas no envelope v2: atualização cadastral via SMS, Pessoas+Benefício Concedido AA01 e Pessoas+Serviço Prestado CRA1.

## Power BI

O projeto está em `bi/` e consome somente `serving.v_bi_*`. As tabelas semânticas `BeneficiosConcedidos`, `ServicosPrestados` e `Registros` continuam separadas, apesar da consolidação física do Serving.

## Documentação

- `docs/API.md` — contrato HTTP resumido.
- `docs/Bronze_Operacao.md` — integridade, 503/retry, GC seguro, ZIP determinístico e backup/restore da Bronze.
- `docs/README.md` — notas técnicas complementares.
- `config/security/README.md` — política das chaves sintéticas.
- `../Documentos/Especificacao_Tecnica_Jornada_v3.62.docx` — fonte normativa desta distribuição. A Referência Territorial permanece a fonte geográfica da visualização territorial; a v3.47 preserva a trava de consistência do CPF, os casos governados de identidade e a independência entre ocorrência factual e atribuição canônica.

## Compartilhamento municipal por padrão — vigente na v3.55

Credenciais `GESTOR`, `BENEFICIO` e `SERVICO` autorizadas acessam a Pessoa e não precisam comprovar vínculo, observação ou fato prévio para localizar o UUID ou consultar a projeção contratada. Credenciais técnicas `BENEFICIO`/`SERVICO` continuam limitadas ao próprio Tipo, scopes e schemas autorizados; a limitação é de recurso, não de população histórica.

Exceções de projeção são declarações negativas versionadas em `controle.restricao_projecao_jornada_versao`. A ausência de restrição `ATIVA` aplicável preserva a projeção municipal padrão. A função `controle.fn_projecao_jornada_permitida` reduz atributos transversais, Registros e Possibilidades conforme Gestor responsável, Gestor consumidor e alvo; ela não amplia scope, contrato ou permissão.

Gold continua materializada desde a primeira fonte. `BASELINE_FONTE_UNICA` é estado Gold e indica ausência de fonte independente corroborante; `fontes_distintas` contabiliza fontes observadas sem chamar a primeira fonte de “corroboração”.

## Integridade global de identidade — vigente na v3.55

- Um CPF que produz `CPF_COMPARTILHADO_SUSPEITO` ou `CPF_NUCLEO_EXISTENTE_INDISPONIVEL` passa a `identidade.identity_map.estado=EM_CONFLITO`; enquanto isso, CPF→UUID devolve `CONFLITO / CPF_EM_CONFLITO_IDENTIDADE`, sem UUID.
- `identidade.identity_map_estado_evento` preserva a trilha de transições do identificador.
- A correção é governada: `identidade.correcao_identidade`, `correcao_identidade_item` e `sp_aplicar_correcao_identidade` registram ato, justificativa, agrupamentos e reassociações/separações/fusões decididas institucionalmente. A Jornada não infere o titular.
- Versões ATIVAS de contratos guardam SHA-256 aprovado; API e Processor recalculam o digest dos bytes físicos e falham fechado se uma versão publicada tiver sido substituída in-place, inclusive após restart.
- O modelo semântico padrão do Power BI não importa `pessoa_uuid`; quantidades distintas de Pessoas são publicadas por views agregadas de Serving. Os avisos normativos de Possibilidades e Monetário estão no PBIR.
- `Solution/docs/HML_Parametros.md` centraliza os valores que precisam de calibração/decisão antes de Produção.

## Serviço de Divergências mínimo e metadados de Pessoa — vigente na v3.55

A Fase 1 expõe somente `GET /api/v1/divergencias?limit=` e `POST /api/v1/divergencias/{divergenciaId}/desfecho`. A tipologia automática da Solution é `DIVERGENCIA_IDENTIDADE`; o serviço informa e registra desfecho, sem editar Silver/Gold. Correções cadastrais retornam pela ingestão de nova observação de Pessoa, fatos por `ALTERACAO`/`RETIFICACAO`, e identidade pelo fluxo governado próprio. Comparação cadastral assistida, óbito e notificações externas ficam para Fase 2.

`GET /api/v1/pessoas/{uuid}` e `POST /api/v1/pessoas/consulta` preservam `dados` sob o schema contratado e passam a incluir `metadados` com `fontesDistintas`, `estadoConcordancia` e `atualizadoEm`, derivados da Gold.

## Implementação de referência v3.55

A API deixa de usar serviços `NotImplemented*` para dados: resolução existente de CPF, recepção idempotente, Bronze, projeção de Pessoa, Histórico e Possibilidades usam SQL Server. Fora de Development, apenas autenticação/política continuam deny-by-default até IdP/secret store corporativo.

O Processor implementa reserva atômica por lease com heartbeat, fencing e recuperação periódica de lease expirado, revalidação do ZIP e contratos, persistência Silver, resolução determinística CPF→UUID com trava de consistência do núcleo para CPF já conhecido, Gold/Serving, promoção SCD2 de atributos comprovados e QC de referência por Tipo/versão. `ingestao.v_entrega_completude` + `ingestao.sp_recalcular_entrega` tornam `entrega_completa` derivado do conjunto 1..N de lotes internos.

Regras reais de Possibilidades continuam externas. A territorialização é responsabilidade do Gestor/origem: o Processor valida e persiste `situacaoGeografia`, Distrito/Subprefeitura e `referenciaMalha`, sem enriquecimento geográfica Pessoa-a-Pessoa. `ACOLHIMENTO_INSTITUCIONAL` e `REFERENCIA_TERRITORIAL_DECLARADA` dependem de declaração explícita da fonte; não podem ser inferidos pela Jornada a partir de equipamento ou local de atendimento. Não existe submodelo autônomo nem API de Território de Referência.

### Integridade de CPF compartilhado — vigente na v3.55

CPF estruturalmente válido não é tratado como prova suficiente de que uma nova observação representa a mesma Pessoa quando o identificador já está associado a um UUID. O Processor compara o núcleo já conhecido antes de criar o vínculo da observação. A política `CPF_CORE_CONSISTENCY_V1` bloqueia somente divergência forte em dois sinais independentes: nome `LOW` e data de nascimento diferente. Variação de nome com a mesma data e diferença isolada de data não bastam, por si sós, para bloquear.

Quando a trava dispara, `identidade.vinculo_fonte` registra `status='CONFLITO'`, `pessoa_uuid=NULL` e `motivo='CPF_COMPARTILHADO_SUSPEITO'`. A observação permanece em Silver como evidência e o fato finalístico válido continua materializado em Gold/Serving com sua referência de sujeito declarada, `pessoa_uuid=NULL` e `estado_atribuicao_identidade='CONFLITO_IDENTIDADE'`. `serving.v_bi_pendencias_identidade` e a fila institucional de divergências expõem o motivo para tratamento finalístico/documental. O caso não é rebaixado automaticamente ao linkage probabilístico.

### Segurança vigente na v3.55
- A Fase 1 não exige `X-Jornada-Finalidade` e não mantém catálogo/allowlist de motivos declarados de consulta; autorização depende da credencial, scopes e recurso aplicável.
- `X-Jornada-Agente-CPF` é opcional e declarativo: a Jornada valida o CPF, calcula HMAC-SHA-256 com chave externa/versionada, remove o header do pipeline e não autentica o agente nem verifica a associação usuário↔CPF.
- Cardinalidade é propriedade do endpoint; rate limits são particionados pela credencial autenticada e segmentados por classe de endpoint.
- Restrições `ATIVA` usam Gestor responsável, Gestor consumidor e alvo referencialmente válido, evitando exceções fail-open por erro de código.
- `serving.v_bi_api` é minimizada e não expõe `pessoa_uuid`, HMAC do agente, `credencial_id` ou `api_evento_id`.

### Proxy confiável e cobertura de monitoramento v3.39

O rate limiter usa somente `HttpContext.Connection.RemoteIpAddress`. `X-Forwarded-For` não é lido diretamente pela aplicação. Quando `ReverseProxy:KnownProxies` está vazio, o `ForwardedHeadersMiddleware` não é habilitado; quando há proxies configurados, somente IPs individuais ou redes CIDR explicitamente configuradas são confiáveis; IP/CIDR inválido falha fechado no startup. Os testes unitários cobrem separação de buckets por classe de endpoint, rejeição de `X-Forwarded-For` bruto e configuração de proxies confiáveis. A observabilidade volumétrica deve ser feita por Gestor, credencial, rota e período, sem depender de rótulo de finalidade declarado pelo consumidor.


Na v3.52, replay/reprocessamento técnico é determinístico: a Entrega continua ligada ao `tipo_registro_versao_id` originalmente aceito, mesmo que uma versão posterior do Tipo altere janela/regime sem mudar o schema. Aplicação retroativa de norma é reavaliação governada distinta de replay. Quando existe janela configurada e `dataInicioConcessao` é nula, o QC estrutural registra `NAO_VERIFICAVEL / CONCESSAO_JANELA_NAO_VERIFICAVEL_V1`; o BI não conta esse caso como fora da janela e mede separadamente a cobertura verificável.

## Qualidade de dados e de envio — vigente na v3.55

O BI inclui `QualidadeBeneficios` e `QualidadeServicos`, alimentadas exclusivamente por `serving.v_bi_qualidade_beneficios_concedidos` e `serving.v_bi_qualidade_servicos_prestados`. A primeira permite Secretaria/Gestor × Benefício Concedido; a segunda, Secretaria/Gestor × Serviço Prestado. Os indicadores são flags auditáveis (entrega, geografia, QC e preenchimentos próprios de cada Natureza), evitando um score sintético único.

A trilha `ingestao.item_processado` ganhou índice por `(lote_id, classe_item)`. Por seu perfil de crescimento, a retenção granular deve ter janela específica aprovada na governança e preservar a última confirmação por origem; `VERSIONADO` é categoria operacional e ALTERACAO/RETIFICACAO continuam distinguíveis em `silver.registro_observacao.operacao`.

## Novidades v3.36 — coordenação serial do corpus

- A API e a persistência Ingestão/Bronze ficam fora do gate e continuam aceitando Entregas durante janelas analíticas.
- O Processor adquire `Jornada.Pipeline.Corpus` por lote e há no máximo uma materialização global do corpus por vez, mesmo com múltiplas instâncias.
- Parameters `GENERATE_DRAFT` e Linkage Runner usam uma conexão coordenadora dedicada, sem pooling, e mantêm `Jornada.Pipeline.ExclusiveRequest` + `Jornada.Pipeline.Corpus` em `LockOwner=Session` durante o job. A intenção exclusiva impede novos lotes e deixa apenas o lote corrente drenar, reduzindo fortemente o risco de starvation após a aquisição da intenção exclusiva.
- `ReadCommandTimeoutSeconds=900` e `FreezeUniverseCommandTimeoutSeconds=900` permanecem como limites finitos dos comandos SQL críticos; `PipelineCoordination:CurrentBatchDrainTimeoutSeconds=900` limita a espera pelo lote corrente. Esses valores devem ser calibrados em HML.
- A Fase 1 deixa de depender de `ALLOW_SNAPSHOT_ISOLATION` e de row-versioning para consistência do linkage; o script de habilitação foi removido.
- `identidade.linkage_run_item` continua materializando o universo auditável do run e o antigo `pessoa_observacao_id_max_snapshot` migra para `pessoa_observacao_id_high_watermark`.

## Novidades v3.33 — referência única para visualização territorial

- `ENDERECO_RESIDENCIAL` permanece endereço cadastral de residência; não é consumido diretamente como dimensão territorial do BI.
- `silver.referencia_territorial_observacao` passa a ser a única persistência geográfica da referência da Pessoa. O upgrade migra snapshots residenciais legados e remove `silver.endereco_residencial_geografia_observacao` e `silver.v_pessoa_geografia_residencial`.
- A camada de visualização usa exclusivamente a Referência Territorial selecionada: endereço de referência quando existir, ou Distrito/Subprefeitura em referências sem endereço postal.
- A precedência `REFERENCIA_TERRITORIAL explícita > evidência COMPROVADO > recência` ganhou teste SQL de integração.
- A v3.39 torna a territorialização responsabilidade do Gestor/origem; o Processor apenas valida e persiste `situacaoGeografia`, Distrito/Subprefeitura e `referenciaMalha`, sem serviço geográfico online no caminho normal.

## Novidades v3.31 — Referência Territorial implementada no modelo físico

- `gold.pessoa` possui índice coberto para o blocking exato por `data_nascimento`, eliminando scan completo por observação.
- Power BI ganhou **Qualidade dos Pacotes** e **Qualidade de Identidade por Origem**, com recorte por Gestor/Secretaria, Sistema de Origem e Tipo.
- O indicador `SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO` passa a ser observável por origem antes de qualquer relaxamento do blocking.
- CPF permanece em claro na Fase 1 somente nas camadas operacionais restritas necessárias à resolução/qualidade; não é exposto em BI, auditoria HTTP ou logs.
- Hash de versionamento passou a normalizar `null` opcional, Unicode NFC e representação numérica, e trata arrays-conjunto cadastrais como independentes de ordem. `atualizadoEmOrigem` não cria versão sozinho.
- Ingestão não mantém mais ZIP de até 250 MB em `byte[]`: usa arquivo temporário/stream; autorização genérica ocorre antes do recebimento e a autorização do Tipo antes da validação descompactada pesada.
- Rate limit tem duas camadas: borda por IP sem confiar em código público não autenticado e limite por `CredentialId` após autenticação.
- CI passa a executar testes SQL reais com SQL Server 2022, build com warnings como erro e auditoria de dependências vulneráveis.

## Resiliência do Processor (v3.39)

O Processor usa lease com fencing token por lote. `lease_id`/`lease_owner` identificam a posse corrente, `heartbeat_em` renova `lease_expira_em`, e toda publicação/finalização exige o mesmo lease. Falhas inesperadas são reagendadas com backoff exponencial configurável; após `MaxProcessingAttempts`, o lote entra em `POISON` e a Entrega é colocada em quarentena. Leases expirados são recuperados por varredura periódica e incrementam `recuperacao_count`. O BI de Qualidade de Envios expõe tentativas, recuperações e lotes poison.

Erros de contrato continuam determinísticos: pacote inválido é `REJEITADO` e contrato inexistente vai diretamente para `QUARENTENA`; esses casos não entram em retry automático.


## Consolidação v3.39

- territorialização da Pessoa é responsabilidade do Gestor; `situacaoGeografia` é explícita e não há chamada geográfica online no Processor;
- `IngestionStaging:RootPath` substitui o diretório temporário global do SO;
- GC da Bronze usa cursor persistente e publica métricas para o BI;
- `item_processado` possui consolidação/retensão configurável, desabilitada até decisão de governança;
- `modo_carga_inicial` suspende Linkage Runner e `GENERATE_DRAFT` enquanto mede Pessoas/hora;
- `Serializable` permanece; HML mede deadlocks/lock waits/Query Store antes de qualquer redução de isolamento; não existe rotina diária automática de REBUILD/REORGANIZE.


## Ajustes v3.39

Contratos JSON Schema podem ganhar novas versões `vN` sem restart: catálogo dinâmico, cache append-only por versão e proibição de edição in-place de schema já utilizado. O Processor aplica o limite descompactado de 2 GiB durante o próprio parse (uma única passagem de payload no Worker). Buffers Bronze de 1 MiB usam `ArrayPool<byte>` com limpeza na devolução. A retenção granular continua desligada e passa a exigir janela positiva explícita para habilitação. A auditoria permanece síncrona/durável; `AuditPersistenceMs` é emitido para medição em HML.

## Fechamento técnico de fato/identidade/BI (v3.46)

- BI factual único: `estado_atribuicao_identidade` pertence às próprias views de Benefícios, Serviços e Registros; `v_bi_fatos_identidade` e a tabela semântica paralela foram removidas. `Registros` passou a representar o universo factual, enquanto as métricas de Pessoas continuam exclusivamente no estado `ATRIBUIDA`.
- Retenção Bronze por Entrega implementada e desabilitada por default, com expurgo lógico auditável, app lock por SHA, preservação de objetos deduplicados ainda referenciados e métricas em `v_bi_retencao_bronze`.
- `Jornada.Bronze.Verify` valida existência, tamanho e SHA-256 de todas as referências Bronze `DISPONIVEL` após restore.
- Reverse proxy aceita `KnownProxies` e `KnownNetworks` CIDR, sempre fail-closed.
- Diagramas de sequência UML em `docs/diagrams/sequence` formalizam os fluxos normal, CPF em conflito, SEM_CPF+linkage e correção governada.


## Operação e scheduler corporativo - v3.55

`Jornada.Pipeline.Coordination` permanece biblioteca compartilhada pelos workers; não há coordenador executável. Agendamento, recorrência e encadeamento pertencem ao scheduler corporativo homologado pela PRODAM. O procedimento está em `docs/Runbook_Operacao.md`.

`Jornada.Operations.Maintenance.Worker` inclui `PipelineWatchdogWorker`, desabilitado por default e somente observacional. Ele alerta sobre runs/modelos potencialmente estagnados, leases vencidos, backlog antigo e carga inicial prolongada; não agenda, não cancela, não altera estado e não libera application locks.

Os fontes do BI (`bi/Jornada.pbip`, PBIR e TMDL) exigem validação e salvamento em Microsoft Power BI Desktop homologado antes da promoção.


## Harness técnico v3.55

A v3.55 acrescenta instrumentos reproduzíveis de engenharia sem dados reais: `database/Jornada_Dev_SyntheticScale.sql`, `scripts/local-scale.*`, `scripts/local-fault-injection.*` e `scripts/local-backup-restore-drill.*`. O harness de escala mede `GENERATE_DRAFT` e Runner `MODEL_VALIDATION` sobre massa sintética determinística; fault injection encerra deliberadamente a sessão SQL do gate e exige cancelamento fail-closed; o drill de restore combina backup SQL com uma referência Bronze controlada. Evidências locais ficam sob `.local/` e não entram no Git. Esses ensaios não substituem capacidade/RPO/RTO homologados em infraestrutura PRODAM.


## Harnesses técnicos reproduzíveis - v3.55

A v3.55 acrescenta instrumentos exclusivos de Development/HML: massa sintética determinística, harness de escala do Parameters Worker/Runner, fault injection do gate serial e ensaio local controlado de backup/restore SQL + Bronze. Consulte `docs/Runbook_Testes_Tecnicos.md`. Esses instrumentos não definem capacidade de Produção, thresholds de linkage, RPO/RTO ou configuração do scheduler corporativo.

## Release de engenharia v3.60 — sem alteração normativa

### Reprodutibilidade e supply chain

A v3.60 fixa o SDK .NET em `8.0.424`, as GitHub Actions por commit SHA e o SQL Server 2022 CU26 por digest OCI. `RestorePackagesWithLockFile` está habilitado; o job `dependency-lock` gera os `packages.lock.json` em restore limpo, valida `contentHash`, repete `dotnet restore --locked-mode` e distribui o mesmo conjunto de locks aos jobs que compilam/testam. O conjunto é publicado como evidência do CI para incorporação posterior ao Git no ambiente executável.

O workflow também aplica `permissions: contents: read`, `persist-credentials: false`, `concurrency` com cancelamento de execução obsoleta e `timeout-minutes` por job. O job unitário gera `sbom.cdx.json` CycloneDX 1.5 a partir das dependências diretas e transitivas resolvidas. Pins e política estão em `supply-chain/`.


Esta release fecha pendências técnicas implementáveis sem alterar a Especificação v3.55:

- `POST /api/v1/ingestao/entregas` eleva o limite de corpo do Kestrel somente nessa rota para o teto contratual de 250 MiB; reverse proxy/ingress deve admitir valor igual ou superior em HML.
- rate limits passaram para `ApiRateLimiting` em `appsettings` (`StandardPermitLimit=120`, `IdentityPermitLimit=30`, `IngestionPermitLimit=20`, `EdgeMultiplier=10`) e continuam sujeitos a calibração de HML.
- `ProbabilisticLinkage:CommandTimeoutSeconds` do Runner passou de infinito para 900 s e o código recusa `0` como forma de reintroduzir timeout infinito.
- liveness e readiness foram separados em `/health/live` e `/health/ready`, mantendo `/health` por compatibilidade.
- `openapi/jornada-v1.openapi.json` é verificado por `scripts/openapi-contract-gate.py`; o gate compara exatamente método+rota do Minimal API com o contrato e roda também no CI/local-test.
- `scripts/local-ddl-upgrade.*` aplica o baseline v3.55 em banco descartável, preserva uma sentinela, aplica o DDL atual duas vezes e exige fingerprint estável.
- `scripts/local-e2e.*` cobre HTTP → Bronze → Processor → Silver → Gold → Serving → retorno HTTP, replay da mesma `Idempotency-Key` e retransmissão dos mesmos bytes sob nova Entrega.
- `Jornada.Linkage.Evaluation` transforma as limitações metodológicas de blocking e transportabilidade de `m` em relatório reproduzível DEV/HML com `purpose=DEV_HML_ONLY_NO_PUBLICATION`; faz somente leitura nas tabelas operacionais e V2 permanece evidência experimental.
- o CI possui jobs próprios `ddl-upgrade` e `e2e`, ambos com evidências publicadas como artifacts do workflow.
- `harness-smoke` executa `scripts/linkage-evaluation-smoke.sh` sobre 100 rótulos sintéticos e exige fingerprint operacional idêntico antes/depois do Evaluation, além da presença das métricas V1/V2 e de transportabilidade.

Os documentos em `../Documentos/` foram atualizados para a base normativa v3.57 nos anexos afetados. A v3.61 identifica a Solution que implementa essa regra e preserva os hardenings de engenharia da v3.60.



### Endereço de casa-abrigo-sigilosa

`ENDERECO_CASA_ABRIGO_SIGILOSA` é um atributo reservado para o endereço de uma **casa-abrigo-sigilosa**. Ele não é sinônimo de `ENDERECO_RESIDENCIAL`, não cria uma categoria de “Pessoa protegida” e não é inferido pela Jornada. O atributo só é aceito em Entrega de `SERVICO` cujo `ref.tipo_registro_versao.origina_endereco_casa_abrigo_sigilosa=1`; portanto, o dado tem de vir do próprio serviço de casa-abrigo cadastrado para essa finalidade.

Por regra estrutural, esse atributo não é projetado pela API a Gestor diferente do Gestor responsável, independentemente da ausência de restrições manuais em `controle.restricao_projecao_jornada_versao`. Os demais dados da Pessoa seguem as regras normais de compartilhamento. O endereço sigiloso também não alimenta `REFERENCIA_TERRITORIAL`.


## Release v3.61 — endereço de casa-abrigo-sigilosa

A v3.61 não cria `PROTECAO_ESPECIAL` nem qualquer marcador geral na Pessoa. O sigilo fica localizado no dado `ENDERECO_CASA_ABRIGO_SIGILOSA`. A ingestão é aceita somente quando `Natureza=SERVICO` e a versão do Tipo possui `origina_endereco_casa_abrigo_sigilosa=1`; o Processor rejeita qualquer outra origem.

Na projeção da Pessoa, esse atributo recebe bloqueio estrutural quando o Gestor consumidor difere do Gestor responsável. A regra não depende de cadastro manual em `controle.restricao_projecao_jornada_versao`. O atributo não aceita geografia/referência territorial e não é inferido de `ACOLHIMENTO_INSTITUCIONAL`.


## Release v3.62 — atributos multivalorados e continuidade canônica de UUID

A base normativa v3.58 diferencia cardinalidade de atributos transversais. `TELEFONE_CONTATO` e `EMAIL_CONTATO` são `MULTI`: cada valor normalizado possui `atributo_instancia_chave` própria em Silver/Gold, permitindo múltiplos números/e-mails correntes. Atributos `SINGLE` continuam usando a instância única `#`. Nova evidência da mesma instância versiona aquela instância; a simples omissão em observação posterior não encerra outros valores MULTI.

A `FUSAO_HISTORICA` passa a preservar sucessão canônica em `identidade.pessoa.pessoa_uuid_sucessor`. `identidade.fn_pessoa_uuid_canonico` resolve cadeias de sucessão e as APIs de Pessoa/Registros/Benefícios/Serviços/Possibilidades aceitam UUID absorvido, consultam o UUID canônico e preservam no metadata o UUID originalmente solicitado. Identificadores ativos do UUID absorvido migram de forma auditável; CPFs ativos divergentes fazem a fusão falhar fechada.

Na projeção cadastral, ausência de atributo não prova inexistência na origem: o campo pode estar fora do JSON Schema autorizado ou ter sido suprimido por regra de projeção. A API não devolve marcador `RESTRITO`, pois isso revelaria a própria existência de dado não projetável — particularmente relevante para `ENDERECO_CASA_ABRIGO_SIGILOSA`.


## Release v3.63 — fusão exaustiva e telefone BR canônico

A base normativa v3.59 torna `FUSAO_HISTORICA` exaustiva por UUID absorvido. Antes de qualquer mutação da aplicação do caso, todos os vínculos correntes `RESOLVIDO` do UUID de origem que será absorvido devem estar incluídos no caso e apontar ao mesmo destino. Caso contrário, a procedure falha com `THROW 51119`, impedindo que uma fusão pedida termine apenas como reclassificação parcial.

`TELEFONE_CONTATO` passa a usar `TELEFONE_BR_CANONICO_V2`. Números brasileiros sem prefixo internacional com 10/11 dígitos são normalizados como `55 + DDD + número`; `+55`, `0055` e E.164 brasileiro sem `+` convergem à mesma instância. Números internacionais não brasileiros exigem prefixo explícito `+` ou `00`. A migração atualiza chaves legadas em Silver/Gold e fecha duplicatas lógicas correntes pela precedência, sem apagar histórico.


## Release v3.64 — preflight de fusão e migração telefônica V2 completa

A base normativa v3.60 antecipa para o preflight de `FUSAO_HISTORICA` as salvaguardas `51117`, `51118` e `51119`, antes do primeiro `UPDATE`/`INSERT` de aplicação. `51119` cobre tanto vínculo resolvido não incluído quanto dispersão da mesma origem para mais de um destino.

O upgrade de telefone passa a recomputar chaves legadas a partir do valor original usando `ref.fn_telefone_br_canonico_v2`. Isso preserva a informação de prefixo internacional `00` que a chave V1 havia reduzido a dígitos e faz Silver/Gold convergirem à mesma chave usada pelo Processor v3.64. Valores legados incompatíveis falham fechado no upgrade.

A v3.60 trata essa mudança como migração normativa explícita: reprocessamento posterior converge para V2; não se afirma replay histórico da normalização V1 apenas porque o helper legado permanece no código.


## Release v3.65 — fechamento técnico de atomicidade e conformidade

A base normativa permanece v3.60. Esta release não altera contratos, DDL lógico de tabelas, regras de acesso nem documentos normativos.

As procedures `identidade.sp_abrir_caso_conflito_identidade`, `identidade.sp_aplicar_caso_conflito_identidade` e `identidade.sp_aplicar_correcao_identidade` agora usam ownership transacional: se chamadas sem transação, abrem/commitam/rollbackam a própria; se o chamador já possui transação, não a commitam nem a rollbackam. O mesmo padrão foi aplicado a `identidade.sp_sincronizar_atribuicao_fatos` e `ingestao.sp_recalcular_entrega`. Isso preserva atomicidade também em execução administrativa direta, sem interferir na transação `Serializable` usada pelos serviços da API.

No Processor, `ScheduleRetryOrPoisonAsync` e `MarkFailedAsync` envolvem a alteração do lote e `sp_recalcular_entrega` na mesma transação `ReadCommitted`, eliminando janela de estado parcial entre lote e Entrega.

`tests/fixtures/phone/telefone-br-canonico-v2.json` passa a ser o conjunto único de vetores de conformidade para a regra `TELEFONE_BR_CANONICO_V2`. Teste unitário C# e teste de integração SQL consomem o mesmo arquivo, incluindo SPACE/TAB/CR/LF/NBSP nas extremidades e casos BR/internacionais válidos e inválidos. `scripts/technical-closure-gate.py` verifica estaticamente os invariantes transacionais e a presença/forma desses vetores.

O baseline imediato de upgrade da v3.68 é `database/baselines/Jornada_Fase1_v3.65.sql`, acompanhado de `Jornada_Seed_Dev_v3.65.sql`.


## Release v3.67 — fechamento do escopo de CTE e prova semântica SQL

- `identidade.sp_recompor_gold_pessoa` materializa a fonte em `@src`; nenhuma instrução posterior ao `MERGE` depende do CTE `obs`.
- A recomposição adota `XACT_ABORT` e ownership transacional, inclusive quando chamada diretamente.
- `database/Jornada_Runtime_Smoke.sql` força resolução dos módulos críticos e executa os caminhos com fonte e sem fonte, sob transação revertida.
- CI, `local-test` e `local-ddl-upgrade` executam o smoke SQL antes de considerar a engenharia fechada.
- O gate estático elimina a antiga exceção da recomposição e falha se `SELECT 1 FROM obs` reaparecer.
- Esta solução foi preservada como a linha vencedora para recomposição Gold e incorporada à convergência v3.68.


## Release v3.68 — convergência v3.66 + v3.67 e fechamento local

- Base normativa v3.62: `TELEFONE_CONTATO` usa `TELEFONE_BR_CANONICO_V2` e `EMAIL_CONTATO` usa `EMAIL_CANONICO_V2`.
- `EMAIL_CANONICO_V2` possui semântica idêntica em SQL/C#: trim apenas SPACE/TAB/CR/LF/NBSP nas extremidades, exatamente um `@`, lowercase somente ASCII A–Z, preservação dos demais codepoints e nenhuma NFC/NFD implícita.
- Upgrade de e-mail legado é fail-closed (`51087`); não existe fallback silencioso para chave V1 incompatível.
- `sp_aplicar_caso_conflito_identidade` mantém `51110`–`51119`; `51114` é validado antes da PK temporária, evitando erro genérico 2601/2627. A suíte contém testes diretos 51110–51118; 51119 permanece coberto diretamente na suíte governada.
- `/health/ready` exige extended properties `Jornada.BaseNormativa=3.62` e `Jornada.SolutionSchema=3.68`, além dos objetos essenciais.
- A materialização de atributos `MULTI` não cria `CONFLITO_EVIDENCIA` apenas por representações textuais diferentes da mesma chave canônica.
- `generate-sbom.py` deriva Base/Solution de `RELEASE_INFO.txt`; o baseline de upgrade é v3.65.
- `predecessor-integrity-gate.py` verifica SHA dos predecessores materializados e possui modo estrito para bloquear promoção se um predecessor declarado estiver ausente.
- O ambiente de montagem desta distribuição não possui .NET/SQL Server/Docker/PowerShell; build, testes de integração, upgrade real e E2E permanecem gates do CI/HML. `packages.lock.json` não são fabricados.
## Release v3.69 — fonte Git e promoção fail-closed

A v3.69 é uma release exclusivamente de engenharia/processo. Não altera a Base normativa v3.62 nem o schema funcional: `/health/ready` continua exigindo `Jornada.BaseNormativa=3.62` e `Jornada.SolutionSchema=3.68`.

- `../SOURCE_PROVENANCE.json` e `supply-chain/source/Jornada_Source_v3.68_v3.69.bundle` materializam uma cadeia Git recuperável com as tags `jornada-solution-v3.68` e `jornada-solution-v3.69`.
- `scripts/release-source-gate.py` valida SHA do bundle, commits/trees/tags e ancestralidade; em checkout Git real, a tag corrente deve apontar exatamente para `HEAD` e a tag predecessora deve ser ancestral.
- Em tags de release, o job `dependency-lock` deixa de gerar locks: `packages.lock.json` precisam estar versionados e o restore é executado em `--locked-mode`. Branches/PRs preservam o bootstrap para produzir evidência até o primeiro commit confiável dos locks.
- O job `release-promotion` só executa em tag e depende de unit, integration-sql, harness-smoke, ddl-upgrade e e2e; também valida proveniência Git antes de produzir o bundle de fonte como artefato.

O pacote montado neste ambiente não contém `packages.lock.json`, pois não há SDK/NuGet. Isso não é mascarado: uma tag criada antes de versionar locks confiáveis falhará deliberadamente.


## Release v3.70 — gates de evidência de execução

A v3.70 continua sendo exclusivamente de engenharia: não altera `Jornada.BaseNormativa=3.62` nem `Jornada.SolutionSchema=3.68`. O objetivo é transformar déficits de teste operacional em contratos executáveis de CI sem fingir que a execução ocorreu no runtime de montagem.

- `test-evidence-gate.py`: proíbe skip/não executado em FaultInjection de release.
- `local-backup-restore-drill.*` + `bronze-restore-evidence-gate.py`: origem/restore positivos, `MISSING`, `DIVERGENT` e PASS final.
- `local-scale.*` + `performance-evidence-gate.py`: evidência coerente de escala; thresholds permanecem HML.
- `powerbi-static-gate.py`: 22 páginas PBIR, ordem/visuais, TMDL e fontes serving essenciais.
- `release-promotion` passa a depender também de `bronze-restore-drill` e `scale-harness`.


## Release v3.71 — zero-skip para Unit, Integration e FaultInjection

A v3.71 é exclusivamente de engenharia e preserva `Jornada.BaseNormativa=3.62` e `Jornada.SolutionSchema=3.68`. O fechamento elimina skips determinísticos e generaliza a evidência TRX fail-closed para toda a suíte esperada em release.

- Unit gera `unit.trx` e exige `test-evidence-gate.py --forbid-skipped`.
- Integration completa gera `integration.trx` e exige zero skip/não executado/inconclusive.
- FaultInjection continua com TRX dedicado e gate separado.
- contrato Pessoa ausente e seed Bronze insuficiente agora falham explicitamente.
- `technical-closure-gate.py` inventaria `Assert.Ignore` e só admite as condições ambientais locais explicitamente autorizadas.


## Release v3.72 — contratos executáveis de HML e critérios pós-medição

A v3.72 é exclusivamente de engenharia e preserva `Jornada.BaseNormativa=3.62` / `Jornada.SolutionSchema=3.68`. Ela não inventa valores de HML: os novos arquivos `config/hml/*.json` nascem `PENDENTE`, mas transformam a futura homologação em um contrato auditável. `hml-config-gate.py` exige valor/evidência/SHA/data/responsável para qualquer item aprovado; `performance-evidence-gate.py` passa a aplicar limites quando um baseline for aprovado; `linkage-evaluation-evidence-gate.py` qualifica blocking V1/V2 candidato e transportabilidade de `m` contra política homologada, sem promover V2 automaticamente. `hml-readiness-gate.sh` reúne os três em modo estrito para uma passagem HML→Produção.


## Engenharia v3.73 — fechamento técnico sem inventar HML

A v3.73 adiciona `RELEASE_EVIDENCE.json` como consolidação hash-addressed das evidências de promoção, property tests determinísticos, invariantes/replay SQL, concorrência simultânea do gate do corpus e infraestrutura versionada de regras de Possibilidades com dry-run. O catálogo de Possibilidades distribuído fica `PENDENTE` e vazio até aprovação institucional.

### Engenharia v3.75 — governança/readiness executável

A v3.75 fecha o restante localmente implementável das pendências externas sem fabricar decisões: inventário criptográfico e rito machine-readable de aprovação dos schemas, retenção/DR, ciclo de vida de identidades pendentes, contrato do scheduler corporativo, preflight de ambiente e coletores/gates HML para SQL e consulta em lote de 1/10/100/1000 UUIDs. Todos permanecem fail-closed: estado `PENDENTE` é aceitável no pacote, mas não satisfaz o readiness HML estrito.

### Engenharia v3.76 — hardening estático, compatibilidade e supply chain

A v3.76 acrescenta compatibilidade retroativa de OpenAPI/JSON Schemas, gate de DDL destrutivo, drift governado de dependências, saneamento de fonte/configuração, ações GitHub pinadas por SHA, build determinístico em duplicidade, cobertura em modo ratchet, CodeQL/SARIF e auditoria NuGet estruturada, além de attestation OIDC/Sigstore da evidência de release. Nenhum desses mecanismos substitui a execução real: esta release prepara o CI para produzir e rejeitar evidência verificável.

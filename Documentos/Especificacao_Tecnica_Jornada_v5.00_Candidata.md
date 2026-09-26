# Especificação Técnica Jornada do Cidadão — Candidata v5.00

**Status:** CANDIDATA TÉCNICA — NÃO PUBLICADA  
**Identificador técnico citável:** `v5.00-candidata`  
**Versão normativa:** nenhuma; `candidate_specification_normative_version=null` até publicação formal  
**Baseline histórica publicada:** `Especificacao_Tecnica_Jornada_v3.62`  
**Base técnica de consolidação:** `master` corrente; o SHA exato somente é fixado no corte formal da candidata  
**SolutionSchema técnico corrente:** `3.70`  
**Issue de consolidação:** #141  
**Regra de versão:** `v5.00-candidata` identifica este baseline técnico para citação e rastreabilidade, sem convertê-lo em versão normativa publicada. A versão normativa continua inexistente até aprovação e publicação formal.

## 1. Finalidade e regra de precedência

Esta candidata consolida, sobre a Especificação Técnica v3.62, comportamentos, contratos e decisões já materializados e verificáveis no estado técnico corrente da Jornada. Ela não reescreve retroativamente a v3.62, não reconstrói por inferência uma suposta v3.64 ausente e não altera a proveniência da última release selada.

Até publicação formal desta candidata, a precedência permanece:

1. `RELEASE_INFO.txt` identifica a última release efetivamente selada e a Base Normativa declarada por ela;
2. `Especificacao_Tecnica_Jornada_v3.62.docx/.pdf` permanece o último texto de Especificação Técnica publicado;
3. a família de Requisitos v1.1, o DDL, OpenAPI, ADRs, documentação de arquitetura e testes descrevem o estado técnico candidato;
4. esta candidata é documento de consolidação e revisão, sem efeito de release por si só.

## 2. Arquitetura operacional SQL e Fabric

A Jornada possui uma única semântica funcional e uma única autoridade operacional relacional. Não existe bifurcação de regra de negócio por hospedagem.

- **Microsoft SQL Server é a tecnologia relacional normativa e o banco relacional operacional de HML/Produção**;
- SQL Server 2022 Developer/Testcontainers é a baseline obrigatória de desenvolvimento, CI e validação ordinária do DDL canônico, sem implicar uso da edição Developer em Produção;
- **SQL Database in Microsoft Fabric não é alvo operacional de Produção nem gate de release da candidata v5.00**; harnesses e evidências Fabric permanecem somente como histórico/compatibilidade técnica;
- Lakehouse e SQL Analytics Endpoint pertencem ao escopo analítico/compatibilidade e não substituem implicitamente o banco relacional operacional;
- a aplicação utiliza o mesmo contrato funcional e o mesmo adaptador SQL operacional, sem variantes de domínio específicas para Fabric.

A evidência histórica Fabric permanece válida como antecedente técnico, mas não precisa ser repetida contra o HEAD candidato para autorizar o corte v5.00 enquanto Fabric não for alvo operacional da release.

## 3. Identidade progressiva

A Jornada admite identidade progressiva e não condiciona a existência de uma observação válida à resolução canônica imediata da pessoa.

Cada **identidade persistente de origem** admitida recebe um `initial_uuid` estável, aleatório e não derivado de PII. O namespace persistente é definido por `(base_pessoa_origem_id, codigo_pessoa_origem)`, podendo a mesma Base ser utilizada por sistemas autorizados. `codigoPessoaOrigem` é opcional: uma observação sem código local continua válida, mas não recebe origem nem `initial_uuid` sintético. `idPessoaEntrega` é a ligação obrigatória entre Pessoa e fatos somente dentro da mesma remessa. O `initial_uuid` não é reciclado, não entra como evidência no score probabilístico e permanece distinto do vínculo canônico que possa ser posteriormente atribuído. Quando uma busca probabilística comprovadamente completa não encontra candidato para uma origem persistente, o próprio `initial_uuid` pode ser promovido a referência canônica com resultado `NOVA_IDENTIDADE`, mantendo modelo, política e universo auditáveis.

A persistência separa os conceitos:

- `identidade.pessoa`: entidade de identidade e autoridade de vínculo;
- `gold.pessoa`: projeção materializada para consumo analítico/serving;
- identidade de origem: referência estável do registro recebido;
- identidade canônica: vínculo eventualmente resolvido por CPF, retroalimentação interna governada, linkage ou correção governada.

### 3.1 Nome de referência e evidência nominal

Nome de referência é regra de **apresentação**, não atributo canônico de identidade. Quando houver `NOME_SOCIAL` declarado ou comprovado entre as observações correntemente vinculadas à Pessoa, ele possui precedência de apresentação sobre o nome civil; o nome civil permanece preservado no núcleo interno e não deve ser exibido em paralelo por padrão. A seleção deve ser única, derivada, auditável, com proveniência e instante de referência, e reversível sem sobrescrever a evidência de origem.

No Município de São Paulo, essa regra encontra base específica no **Decreto Municipal nº 58.228, de 16 de maio de 2018**, atualmente catalogado sem revogação expressa. O decreto estabelece a autodeclaração do nome social (art. 3º), restringe a identificação pelo registro civil aos sistemas internos de acesso restrito e aos casos absolutamente necessários (art. 4º, §§ 3º e 4º) e determina a incorporação do campo de nome social nos sistemas internos quando atualizados (art. 6º). Fonte oficial: `https://legislacao.prefeitura.sp.gov.br/decreto-58228-de-16-de-maio-de-2018/consolidado`.

`nome_referencia` pertence a serving/atendimento e **não** participa de blocking, features, LLR, posterior, margem ou calibração. Nome civil, nome social e variantes históricas permanecem evidências nominais independentes conforme o contrato de resolução homologado. O scorer corrente não deve trocar o nome técnico do candidato pela referência de apresentação; eventual comparação de conjuntos de variantes exige nova versão calibrada do modelo.

A correção de identidade é governada e auditável; a plataforma não deve inferir titularidade institucionalmente controversa nem apagar a trilha histórica de agrupamentos, separações ou fusões.

### 3.2 Autoria canônica das decisões governadas

Atos governados que alteram ou encerram uma decisão de identidade devem possuir um evento append-only em `auditoria.decisao_identidade_evento`, persistido na mesma transação da mutação. A autoria é a credencial institucional `GESTOR` autenticada e vinculada ao Gestor responsável; cada ato recebe `operacao_id` gerado pelo SQL Server. `correlation_id` serve somente para correlação técnica da requisição e não substitui a identidade do decisor.

`controle.api_evento` continua sendo trilha de acesso/telemetria HTTP. O CPF opcional de agente declarado pela origem e armazenado apenas como HMAC não é prova de autenticação individual e, portanto, não é usado como autoria canônica do ato de identidade. Se o ledger não puder ser persistido, a decisão governada deve falhar atomicamente.

Quando houver confirmação humana, ela deve ser estruturada em apenas duas formas: `DOCUMENTO_VERIFICADO` ou `CONFIRMACAO_INSTITUCIONAL_SEM_DOCUMENTO`; evidência documental exige tipo de documento estruturado. Eventos que não constituem confirmação usam `evidencia_tipo = NULL`, e `evento_tipo` expressa a natureza operacional do ato. Texto livre (`ato_referencia`/`justificativa`) permanece contexto e não substitui a classificação.

## 4. Âncora CPF → UUID e ausência de CPF

Quando um CPF válido e governado está disponível, a resolução determinística CPF→UUID é a âncora externa de maior autoridade do vínculo. Conflitos de consistência não devem ser silenciados por regras probabilísticas.

NIS/PIS/PASEP/NIT usam um tipo canônico `NIS`, preservando a procedência no namespace; RG permanece qualificado por emissor e UF. **NIS e RG são identificadores secundários, não âncoras.** A Jornada pode validar estrutura, preservar declaração/comprovação e detectar reutilização inconsistente, mas esses identificadores não constituem Pessoa, não criam `identity_map`, não selecionam UUID e não competem com CPF. Uma Pessoa pode manter múltiplos números sociais e documentos históricos. O mesmo NIS observado sob Pessoas distintas gera sinal de qualidade, nunca fusão automática. A decisão detalhada está na ADR-006.

A ausência de CPF, entretanto, não impede a ingestão de um fato válido. O sistema deve preservar `cpf_declarado` quando informado, registrar o motivo de ausência quando aplicável e manter o estado da atribuição de identidade separado do fato finalístico.

## 5. Fatos independentes da resolução de identidade

Benefícios concedidos, serviços prestados e demais fatos finalísticos validamente declarados pelo Gestor devem ser materializados mesmo quando a identidade canônica ainda estiver pendente ou em conflito.

O modelo factual deve preservar o vínculo obrigatório com a observação de Pessoa da remessa e, **quando existirem**, a origem persistente e o código local da Pessoa. Deve preservar também sistema de origem, CPF declarado quando existente e estado da atribuição de identidade. `pessoa_uuid`, `pessoa_origem_id` e `codigo_pessoa_origem` não podem ser usados como requisitos artificiais para a existência do fato. A obrigatoriedade de `codigoRegistroOrigem` é declarada pelo schema versionado de cada Tipo de Benefício/Serviço, e não por uma regra global paralela do Processor.

A camada analítica deve conseguir distinguir, de forma explícita, fatos com identidade atribuída, pendente ou em conflito.

## 6. Linkage probabilístico e blocking

O linkage probabilístico é complementar às âncoras determinísticas e opera de forma versionada, reproduzível e conservadora.

O blocking corrente é multi-passe. Para nascimento, a política V2 considera cinco passes: nascimento exato; mês/ano com inicial aplicável; dia/ano com inicial aplicável; transposição válida de dia/mês; e mesmo dia/mês com tolerância configurada de ano. Ausência de uma evidência desabilita apenas o passe dependente dela e não autoriza fabricar dados.

Comparadores fuzzy atuam sobre o universo candidato; não devem ser confundidos com colunas físicas artificiais de similaridade. O plano de blocking, seus diagnósticos e seus parâmetros precisam permanecer versionados.

Execução incompleta, evidência inconsistente, rótulo inconclusivo ou ausência de gate obrigatório não autoriza promoção automática de modelo.

## 7. Calibração, avaliação e promoção de modelos

Parâmetros de linkage possuem ciclo de vida governado e auditável. Um modelo novo deve passar explicitamente pelos estados aplicáveis de geração, rascunho, validação e ativação; um modelo não substitui o ativo por simples existência no banco.

A promoção `RASCUNHO → VALIDADO → ATIVO` deve ser explícita. O schema admite estados adicionais de operação, como geração, inativação e falha, mas apenas um modelo deve exercer a autoridade ativa prevista pelo contrato.

A avaliação independente deve separar amostra de treino e avaliação quando aplicável, rejeitar rótulos inconclusivos em métricas que exijam verdade-terreno e não assumir que UUIDs diferentes representam necessariamente pessoas diferentes.

A conferência independente de implementação é distinta da avaliação estatística representativa. A primeira versão governada, `JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1`, recebe estados comparativos e inputs de guard já pré-computados e recalcula independentemente LLR, agregação, posterior, ranking e política final; portanto não afirma independência dos comparadores de nome/data nem da derivação do flag de colisão demográfica.

O schema persiste somente evidência agregada da conferência em `auditoria.linkage_conferencia_evidencia`, de forma append-only e sem PII, identificadores de candidatos ou scores par-a-par. Cada registro fica amarrado por SHA-256 ao snapshot decisório corrente do modelo; o assert de promoção recomputa esse fingerprint e rejeita evidência obsoleta se parâmetros, estatísticas ou ruleset forem alterados depois da conferência.

O contrato de tolerância técnica da conferência foi congelado ex ante em `V1_2026-09-26` com diferença LLR de `0,01`, sem constituir medição abrangente do erro numérico ou valor aprovado para produção. O gate de promoção, porém, já integra `VALIDATE` e `ACTIVATE`: ambos leem o mesmo contrato governado e falham fechado enquanto ele não estiver `FROZEN`. Quando congelado/versionado, a promoção exige `auditoria.sp_assert_conferencia_linkage_conforme` para a mesma `modelo_id`, versão do método e versão/valor da tolerância. O fluxo efetivo é `GENERATE_DRAFT → CONFERENCIA → VALIDATE → ACTIVATE`; no estado corrente oficial ele para deliberadamente antes da conferência governada/promoção porque nenhuma tolerância de produção foi aprovada.

A conferência de implementação também não pode declarar a validação estatística representativa: o campo persistido correspondente fica restrito a `NOT_ASSESSED_ISSUE_31`. A validação estatística continua sendo gate separado da issue #31.


## 8. Referências IBGE de nomes

Frequências oficiais do IBGE podem ser usadas como evidência de blocking e calibração quando aplicável, sempre com proveniência e versão explícitas.

A referência oficial de nomes deve possuir snapshot local imutável, representação operacional determinística, manifesto/fingerprint verificável e vínculo ao run/modelo que a consumiu. Replay deve utilizar a referência efetivamente congelada para aquela execução, não a referência corrente por conveniência.

A semântica publicada do IBGE deve ser preservada. Da coluna `nome_completo`, somente o primeiro nome pode ser derivado com semântica equivalente à publicação oficial; sobrenomes não devem ser inferidos por heurística a partir do nome completo. Quando atributos de sobrenome forem utilizados, sua origem deve respeitar a semântica da fonte correspondente.

A ausência de frequência na referência não equivale a frequência observada igual a zero. Ausência, supressão/cobertura insuficiente e zero observado são estados semanticamente distintos e não podem ser colapsados em um mesmo valor técnico.

Críticas históricas equivalentes a P8 e P15 não constituem defeitos correntes se afirmarem inexistência dessa distinção ou ausência de fluxo governado de promoção.

## 9. Residência, endereço residencial e referência territorial

A Jornada distingue conceitos que não devem ser usados como sinônimos:

- `ENDERECO_RESIDENCIAL` é o atributo contratual de endereço residencial informado pela origem; a Jornada não o redefine automaticamente como “endereço de residência”;
- referência territorial é informação temporal própria selecionada para territorialização e constitui a superfície canônica da visualização analítica;
- residência, endereço residencial, endereço de correspondência e outras referências mantêm semânticas próprias e não devem ser convertidos uns nos outros por convenção técnica.

A superfície analítica territorial utiliza a Referência Territorial selecionada. Quando regra vigente permitir que `ENDERECO_RESIDENCIAL` participe como evidência candidata de uma referência de natureza `DOMICILIAR`, essa participação não altera a semântica do atributo de origem e a seleção permanece explícita e rastreável. A plataforma não deve escolher outro endereço institucional nem fabricar equivalência semântica apenas por conveniência técnica.

`SEM_ENDERECO_APTO` qualifica a ausência de endereço apto à resolução geográfica naquele registro e não equivale a “sem endereço fixo declarado”. Ausência/`null` significa informação não fornecida ou não disponível. A declaração explícita de ausência de endereço fixo é informação própria e deve receber estado contratual distinto em nova versão de schema; os schemas v1–v4 não devem ser alterados in-place nem reinterpretados retroativamente.

A próxima versão contratual de Referência Territorial deve admitir natureza tipada suficiente para distinguir, no mínimo, referência domiciliar, acolhimento institucional, institucional prisional, serviço de referência de atendimento e logradouro de pernoite, além do estado `SEM_ENDERECO_FIXO_DECLARADO`. Naturezas que revelem condição institucional sensível exigem restrição de projeção compatível; `ENDERECO_CASA_ABRIGO_SIGILOSA` nunca é projetado como referência territorial fina compartilhada.

Endereço e Referência Territorial permanecem inelegíveis no modelo de resolução de identidade corrente. Concordância de endereço institucional compartilhado não constitui evidência positiva; eventual uso futuro requer ruleset/modelo versionado com limite de bloco e ajuste explícito de frequência. `ENDERECO_CASA_ABRIGO_SIGILOSA` permanece fail-closed para resolução de identidade.

## 10. Contratos HTTP e aceite OpenAPI

OpenAPI é contrato funcional da superfície HTTP e não mera documentação textual.

O aceite deve incluir conformidade de caixa-preta entre contrato publicado e comportamento runtime: métodos/rotas, status permitidos e formatos de resposta relevantes devem ser exercitados contra a aplicação. Gates textuais ou snapshots congelados continuam úteis como regressão da implementação própria, mas não substituem o teste funcional de conformidade.

Mudanças incompatíveis de contrato devem falhar fechado nos gates previstos até que exista decisão de versionamento explícita.

## 11. Autorização, identidade corporativa e readiness

Ambientes HML e Produção permanecem `deny-by-default` enquanto identidade corporativa, secret store e autorização operacional reais não estiverem integrados.

Credenciais sintéticas são exclusivas de Development. `/health/live` representa liveness do processo; readiness deve refletir dependências necessárias à operação, inclusive integração de identidade corporativa e versão/objetos esperados do schema.

A falta de integração institucional não deve ser mascarada por configuração permissiva, fallback para chave de desenvolvimento ou readiness artificialmente verde.

## 12. Finalidade de acesso

A decisão sobre finalidade, necessidade e base legal do compartilhamento é gate institucional, não detalhe a ser inventado pela engenharia. Para encaminhamento administrativo, utilizar **Coordenação do Programa Reencontro (SEPE)**, sem confundir a interlocução com a competência jurídica de aprovação de políticas de compartilhamento. A estrutura formal do Programa Reencontro prevista nos arts. 7º–9º do Decreto municipal nº 62.149/2023 é o **Núcleo Gestor do Programa Reencontro**, coordenado por **SGM/SEPE**, e apoiado por Núcleo Técnico. O Decreto não basta, isoladamente, para declarar quem aprova a finalidade/base legal do compartilhamento da Jornada: identificar formalmente a instância institucional competente e sua deliberação antes de alterar políticas de acesso.

O contrato corrente autoriza por credencial autenticada, scopes e recurso aplicável. Não existe `X-Jornada-Finalidade` livre, catálogo escolhido pelo consumidor ou allowlist de finalidade enviada arbitrariamente em cada requisição.

Se a **instância institucional competente, após confirmação com SGM/SEPE**, exigir finalidade adicional, a solução deve ser incorporada por decisão formal e preferencialmente vinculada à credencial/contrato de projeção autorizado e à auditoria, evitando uma string livre declarada pelo consumidor sem governança.

## 13. Escopo da Fase 1

A Especificação deve refletir o escopo efetivamente implementado e homologável da Fase 1, sem generalizar capacidades não entregues.

Benefícios e serviços são modelados por fatos e atributos extensíveis, evitando alteração estrutural do banco para cada novo programa com semântica equivalente. Quando houver limitações de domínio — inclusive limitações monetárias já registradas nos requisitos correntes — elas devem permanecer explícitas em vez de serem convertidas em promessa genérica de plataforma.

## 14. Modelo físico e rastreabilidade

O modelo físico corrente deriva do **instalador executável canônico** `Solution/database/Jornada_Fase1_v3.70.sql` (wrapper SQLCMD gerado de `database/migrations/manifest.txt`). Esse wrapper inclui `Solution/database/Jornada_Fase1.sql`, que é a base DDL editável, seguida da identidade progressiva e das migrações manifestadas: executar o arquivo base isoladamente **não equivale** à instalação/atualização completa. O manifesto é a autoridade para ordem e fechamento de `Jornada.SolutionSchema` (atualmente 3.70; migrações 3.71 permanecem aditivas até o rebind correspondente). O modelo e os diagramas devem permanecer sincronizados por processo reprodutível. Contagens históricas de tabelas pertencem aos seus snapshots e não podem ser usadas para invalidar o inventário corrente sem considerar a versão correspondente.

Mudanças materiais desta candidata devem ser rastreáveis, conforme aplicável, à família de Requisitos v1.1, ADRs, DDL/migrações, contratos OpenAPI/JSON, testes e documentação operacional.

## 15. Nulabilidade de nome da mãe e evidência de identidade

O contrato cadastral `Pessoa v3` admite `nomeMae` ausente. A nulabilidade é preservada no contrato interno, persistência, leitura, projeções e candidatos de linkage; a plataforma não deve preencher sinteticamente esse atributo nem descartar a observação por sua ausência.

A ausência, indisponibilidade ou baixa qualidade do nome da mãe reduz ou neutraliza apenas a evidência correspondente na resolução de identidade, conforme política versionada. Ela não transforma por si só uma observação válida em registro inválido e não autoriza inferir o valor ausente.

Essa regra técnica não decide como cada sistema de origem deve governar ou qualificar seu próprio cadastro. Eventual obrigação administrativa de saneamento na origem é matéria institucional distinta do contrato de ingestão da Jornada.

## 16. Pendências externas que não podem ser resolvidas por texto normativo inventado

Permanecem explicitamente externas ao fechamento técnico desta candidata:

1. **Volumetria HML representativa:** ainda é necessária evidência legítima de capacidade e comportamento sob blocking multi-passe, concorrência e regiões/trechos serializados relevantes. Testes locais ou amostras pequenas não autorizam declarar capacidade de produção.
2. **Finalidade/base legal (articulação pela Coordenação do Programa Reencontro (SEPE); chave de máquina histórica `GTPR_PURPOSE_LEGAL_BASIS_DECISION`):** a decisão da instância institucional competente, a confirmar com SGM/SEPE, permanece pendente e não deve ser substituída por cabeçalho livre ou convenção local.
3. **Validação estatística representativa do linkage:** métricas de corpus sintético, smoke tests e validações locais protegem a engenharia, mas não substituem avaliação representativa necessária para concluir desempenho estatístico no universo operacional.

A homologação Fabric deixa de integrar esta lista porque SQL Database in Microsoft Fabric não é alvo operacional de Produção da candidata v5.00. A validação operacional deve ocorrer sobre o ambiente Microsoft SQL Server efetivamente previsto para HML/Produção.

## 17. Critérios para publicação formal

Esta candidata somente deve ser promovida a Especificação Técnica publicada quando, no mínimo:

- a revisão confirmar aderência entre texto, Requisitos v1.1, DDL, modelo físico, OpenAPI, ADRs e comportamento testado;
- não houver diagnóstico histórico superado descrito como defeito corrente;
- nenhuma decisão institucional pendente tiver sido artificialmente transformada em regra técnica;
- a identificação de versão formal for definida no ato de publicação;
- os derivados oficiais necessários forem gerados a partir da fonte aprovada;
- a v3.62 permanecer preservada como baseline histórica e a nova publicação registrar explicitamente sua linhagem.

## 18. Artefatos de evidência principais

A revisão desta candidata deve considerar, entre outros, os seguintes artefatos correntes:

- `Documentos/Requisitos/00_Indice_Mestre_Requisitos_Jornada_v1.1.md` e família v1.1;
- `Documentos/Anexo_Modelo_Fisico_Jornada_v1.40.md`;
- `Solution/database/Jornada_Fase1_v3.70.sql` (instalador SQLCMD canônico gerado do manifesto), incluindo `Jornada_Fase1.sql` e as migrações aplicáveis;
- `Solution/docs/Arquitetura_Identidade_Linkage.md`;
- `Solution/docs/Identidade_Progressiva_*.md`;
- `Solution/docs/Aceite_OpenAPI_BlackBox.md` e contratos em `Solution/openapi/`;
- `Solution/docs/Governanca_Tecnica_Readiness.md`;
- `Solution/docs/Governanca_Finalidade_Acesso.md`;
- `Solution/docs/Territorializacao_Fase1.md`;
- `Documentos/ADR/ADR-001-frequencias-nomes-sobrenomes-enriquecimento.md`;
- documentação e testes de avaliação independente, blocking dinâmico e proveniência de runs/modelos.

---

Esta candidata é deliberadamente conservadora: consolida o que o sistema já prova e mantém como pendência aquilo que depende de ambiente, governança ou decisão institucional externa.

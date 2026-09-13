# Especificação Técnica Jornada do Cidadão — Candidata de Consolidação

**Status:** CANDIDATA — NÃO PUBLICADA  
**Baseline histórica publicada:** `Especificacao_Tecnica_Jornada_v3.62`  
**Base técnica de consolidação:** `master` corrente; o SHA exato somente é fixado no corte formal da candidata  
**SolutionSchema técnico corrente:** `3.70`  
**Issue de consolidação:** #141  
**Regra de versão:** este documento não recebe número normativo enquanto não houver corte/publicação formal.

## 1. Finalidade e regra de precedência

Esta candidata consolida, sobre a Especificação Técnica v3.62, comportamentos, contratos e decisões já materializados e verificáveis no estado técnico corrente da Jornada. Ela não reescreve retroativamente a v3.62, não reconstrói por inferência uma suposta v3.64 ausente e não altera a proveniência da última release selada.

Até publicação formal desta candidata, a precedência permanece:

1. `RELEASE_INFO.txt` identifica a última release efetivamente selada e a Base Normativa declarada por ela;
2. `Especificacao_Tecnica_Jornada_v3.62.docx/.pdf` permanece o último texto de Especificação Técnica publicado;
3. a família de Requisitos v1.1, o DDL, OpenAPI, ADRs, documentação de arquitetura e testes descrevem o estado técnico candidato;
4. esta candidata é documento de consolidação e revisão, sem efeito de release por si só.

## 2. Arquitetura operacional SQL e Fabric

A Jornada possui uma única semântica funcional e uma única autoridade operacional relacional. Não existe bifurcação de regra de negócio por hospedagem.

- SQL Server 2022 Developer/Testcontainers é a baseline obrigatória de desenvolvimento, CI e validação ordinária do DDL canônico;
- SQL Database in Microsoft Fabric é o destino relacional operacional preferencial de HML/Produção, condicionado à homologação da release exata;
- Lakehouse e SQL Analytics Endpoint pertencem ao escopo analítico e não substituem implicitamente o banco relacional operacional;
- a aplicação utiliza o mesmo contrato funcional e o mesmo adaptador SQL operacional, sem variantes de domínio específicas para Fabric.

Evidência histórica de compatibilidade Fabric não homologa automaticamente o HEAD atual. A homologação precisa ser repetida contra o SHA exato candidato ao corte.

## 3. Identidade progressiva

A Jornada admite identidade progressiva e não condiciona a existência de uma observação válida à resolução canônica imediata da pessoa.

Cada identidade de origem admitida recebe um `initial_uuid` estável, aleatório e não derivado de PII. A chave de origem `(sistema_origem_id, codigo_pessoa_origem)` recupera idempotentemente a mesma referência inicial. O `initial_uuid` não é reciclado e permanece distinto do vínculo canônico que possa ser posteriormente atribuído.

A persistência separa os conceitos:

- `identidade.pessoa`: entidade de identidade e autoridade de vínculo;
- `gold.pessoa`: projeção materializada para consumo analítico/serving;
- identidade de origem: referência estável do registro recebido;
- identidade canônica: vínculo eventualmente resolvido por CPF, linkage ou correção governada.

A correção de identidade é governada e auditável; a plataforma não deve inferir titularidade institucionalmente controversa nem apagar a trilha histórica de agrupamentos, separações ou fusões.

## 4. Âncora CPF → UUID e ausência de CPF

Quando um CPF válido e governado está disponível, a resolução determinística CPF→UUID é a âncora de maior autoridade do vínculo. Conflitos de consistência não devem ser silenciados por regras probabilísticas.

A ausência de CPF, entretanto, não impede a ingestão de um fato válido. O sistema deve preservar `cpf_declarado` quando informado, registrar o motivo de ausência quando aplicável e manter o estado da atribuição de identidade separado do fato finalístico.

## 5. Fatos independentes da resolução de identidade

Benefícios concedidos, serviços prestados e demais fatos finalísticos validamente declarados pelo Gestor devem ser materializados mesmo quando a identidade canônica ainda estiver pendente ou em conflito.

O modelo factual deve preservar, conforme o contrato aplicável, a origem da pessoa, sistema de origem, código da pessoa na origem, CPF declarado quando existente e estado da atribuição de identidade. `pessoa_uuid` não pode ser usada como requisito artificial para a existência do fato.

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

## 10. Contratos HTTP e aceite OpenAPI

OpenAPI é contrato funcional da superfície HTTP e não mera documentação textual.

O aceite deve incluir conformidade de caixa-preta entre contrato publicado e comportamento runtime: métodos/rotas, status permitidos e formatos de resposta relevantes devem ser exercitados contra a aplicação. Gates textuais ou snapshots congelados continuam úteis como regressão da implementação própria, mas não substituem o teste funcional de conformidade.

Mudanças incompatíveis de contrato devem falhar fechado nos gates previstos até que exista decisão de versionamento explícita.

## 11. Autorização, identidade corporativa e readiness

Ambientes HML e Produção permanecem `deny-by-default` enquanto identidade corporativa, secret store e autorização operacional reais não estiverem integrados.

Credenciais sintéticas são exclusivas de Development. `/health/live` representa liveness do processo; readiness deve refletir dependências necessárias à operação, inclusive integração de identidade corporativa e versão/objetos esperados do schema.

A falta de integração institucional não deve ser mascarada por configuração permissiva, fallback para chave de desenvolvimento ou readiness artificialmente verde.

## 12. Finalidade de acesso

A decisão sobre finalidade, necessidade e base legal do compartilhamento é gate institucional, não detalhe a ser inventado pela engenharia.

O contrato corrente autoriza por credencial autenticada, scopes e recurso aplicável. Não existe `X-Jornada-Finalidade` livre, catálogo escolhido pelo consumidor ou allowlist de finalidade enviada arbitrariamente em cada requisição.

Se o CCGD exigir finalidade adicional, a solução deve ser incorporada por decisão formal e preferencialmente vinculada à credencial/contrato de projeção autorizado e à auditoria, evitando uma string livre declarada pelo consumidor sem governança.

## 13. Escopo da Fase 1

A Especificação deve refletir o escopo efetivamente implementado e homologável da Fase 1, sem generalizar capacidades não entregues.

Benefícios e serviços são modelados por fatos e atributos extensíveis, evitando alteração estrutural do banco para cada novo programa com semântica equivalente. Quando houver limitações de domínio — inclusive limitações monetárias já registradas nos requisitos correntes — elas devem permanecer explícitas em vez de serem convertidas em promessa genérica de plataforma.

## 14. Modelo físico e rastreabilidade

O modelo físico corrente é derivado do DDL canônico do SolutionSchema 3.70 e deve permanecer sincronizado por processo reprodutível. Contagens históricas de tabelas pertencem aos seus snapshots e não podem ser usadas para invalidar o inventário corrente sem considerar a versão correspondente.

Mudanças materiais desta candidata devem ser rastreáveis, conforme aplicável, à família de Requisitos v1.1, ADRs, DDL/migrações, contratos OpenAPI/JSON, testes e documentação operacional.

## 15. Nulabilidade de nome da mãe e evidência de identidade

O contrato cadastral `Pessoa v3` admite `nomeMae` ausente. A nulabilidade é preservada no contrato interno, persistência, leitura, projeções e candidatos de linkage; a plataforma não deve preencher sinteticamente esse atributo nem descartar a observação por sua ausência.

A ausência, indisponibilidade ou baixa qualidade do nome da mãe reduz ou neutraliza apenas a evidência correspondente na resolução de identidade, conforme política versionada. Ela não transforma por si só uma observação válida em registro inválido e não autoriza inferir o valor ausente.

Essa regra técnica não decide como cada sistema de origem deve governar ou qualificar seu próprio cadastro. Eventual obrigação administrativa de saneamento na origem é matéria institucional distinta do contrato de ingestão da Jornada.

## 16. Pendências externas que não podem ser resolvidas por texto normativo inventado

Permanecem explicitamente externas ao fechamento técnico desta candidata:

1. **Volumetria HML representativa:** ainda é necessária evidência legítima de capacidade e comportamento sob blocking multi-passe, concorrência e regiões/trechos serializados relevantes. Testes locais ou amostras pequenas não autorizam declarar capacidade de produção.
2. **CCGD/finalidade:** a decisão institucional sobre finalidade, necessidade e base legal permanece pendente e não deve ser substituída por cabeçalho livre ou convenção local.
3. **Homologação Fabric:** a release candidata precisa de evidência executada contra o HEAD exato que vier a ser cortado; a evidência histórica v4.00 não satisfaz esse gate sozinha.
4. **Validação estatística representativa do linkage:** métricas de corpus sintético, smoke tests e validações locais protegem a engenharia, mas não substituem avaliação representativa necessária para concluir desempenho estatístico no universo operacional.

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
- `Solution/database/Jornada_Fase1.sql` e migrações aplicáveis;
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
# Busca síncrona semicega — PR #532

> **Gate de ativação (issue #539):** a rota `POST /api/v1/identidade/candidatos` está integrada ao `master` como código, mas exige `SemiblindIdentitySearch:Enabled=true` **e** `ASPNETCORE_ENVIRONMENT=Development`. O valor versionado é `false`; HML/Produção retornam HTTP 503 mesmo que alguém configure `Enabled=true`. Ativar somente para ensaios com dados sintéticos em Development, sem acessar dados reais até a deliberação de finalidade/visibilidade institucional por atributo e a prova SQL/HTTP entre dois órgãos descritas na [issue #539](https://github.com/lucianox777/Jornada/issues/539). Nenhum token de opção serve como confirmação de identidade.


## Contrato e fronteiras

`POST /api/v1/identidade/candidatos` recebe `nome_completo`, `data_nascimento` e `nome_mae` como evidências opcionais. A suficiência não é definida por um campo universalmente obrigatório: o ruleset versionado executa os passes elegíveis para a combinação disponível; sem passe elegível, a busca não deve ser interpretada como varredura integral com zero candidatos. A resposta tipada contém `candidatos[]` com `opcaoId`, `nome_completo`, `data_nascimento` e `nome_mae`; o booleano `nenhumDestes` está sempre presente e true inclusive para lista vazia. Requer o escopo `jornada.identidade.busca.read`, com autenticação compartilhada DT-04 e política institucional. HML/Produção permanecem deny-by-default até a identidade corporativa.

A consulta utiliza o modelo ATIVO, o snapshot de ruleset, o loader de blocking e o ranking Fellegi–Sunter do Runner. Com nascimento e mãe, acrescenta os passes do `CombinedIdentityCandidatePlanner` aos passes dinâmicos publicados; sem nascimento executa apenas os passes dinâmicos elegíveis. O guard rail da busca síncrona é `SemiblindIdentitySearch:MaxCandidatesPerQuery` (padrão 10000), com recusa em excesso sem truncamento. Até 50 candidatos internos pontuados alimentam a autorização, até preencher cinco opções autorizadas. Seleciona internamente até cinco candidatos, randomiza a ordem de apresentação com gerador criptográfico e retorna somente nome, nascimento, nome da mãe e identificador aleatório de opção. O identificador de opção NÃO é um token de confirmação e não pode ser usado para constituir ou publicar vínculos. A opção «Nenhum destes» é sempre válida, inclusive com lista vazia.

Sem nascimento, o blocking legado retorna lista vazia. Rulesets dinâmicos só executam quando existe passe elegível para a observação. O endpoint não inventa data, não degrada para varredura irrestrita, não cria `linkage_run` e não altera o scorer.

## Pendências para retirar o rascunho

1. Reexecutar CI após registrar dependência API → Runner na política arquitetural e excluir appsettings/lock do Runner da publicação da API; validar Windows bundle, build e testes. O dependency-lock e o gate de política compartilhada passaram na rodada anterior.
2. Testes HTTP in-memory adicionados para 401/403, 200 com uma única auditoria e 503 sem divulgação quando o sink falha; executar no CI. Testar ausência de nascimento nos dois contratos de blocking.
3. Verificar integração SQL com 0/1/5 candidatos e modelo ATIVO, inclusive limite de fan-out.
4. Auditoria: a busca persiste o evento antes de devolver candidatos e responde 503 se falhar; o middleware evita duplicação quando a gravação antecipada foi bem-sucedida. Testes HTTP in-memory foram adicionados para falha do sink e persistência única; ainda precisam passar no CI e de validação SQL. Os demais endpoints mantêm a política anterior.
5. Validar a especificação OpenAPI com os gates DT-03 e compatibilidade do contrato.
6. Definir protocolo governado de confirmação separado; o opcaoId desta fase é exclusivamente identificador de interface. Não persistir nem expor UUID na resposta.
7. Bloqueio de liberação: revisar a autorização por domínio institucional. A política atual valida escopo e código de recurso, mas a elegibilidade dos candidatos por instituição deve ser confirmada antes da exposição em produção. Não promover para HML/Produção sem essa prova.

Arquivos protegidos na solicitação não foram alterados.

## Verificação adicional — autorização por candidato

O serviço agora chama `IPolicyEngine.IsAllowedAsync` com `PessoaUuid` de cada candidato antes de projetar nome, nascimento e nome da mãe. Um teste unitário verifica que um candidato negado não aparece na resposta. Isso não comprova, por si só, que a implementação concreta da política restringe corretamente a visibilidade institucional: a validação SQL/HTTP com dados de dois órgãos continua bloqueante para a promoção. A seleção por `OpcaoId` ainda não possui protocolo governado de confirmação.

O CI anterior confirmou o gate NuGet e a política compartilhada; a arquitetura e a publicação Windows exigiram correções posteriores, ainda sem execução completa confirmada no último HEAD.

### Alcance real da autorização por Pessoa

A chamada por `PessoaUuid` foi adicionada antes da projeção, com teste de política negando candidato. **A implementação `MunicipalAccessPolicyEngine` atual não discrimina por `PessoaUuid`**: aplica escopo e propriedade de recurso e considera a identidade Pessoa compartilhada no município. Assim, o teste de política simulada demonstra que o serviço respeita uma negativa, mas **não comprova restrição setorial em DEV**. Antes de permitir esta rota com dados pessoais reais, decidir explicitamente se nome/data/nome da mãe integram a identidade municipal compartilhável e comprovar a base de autorização; caso contrário, implementar filtro efetivo por pessoa/setor antes da resposta. A ausência de política corporativa mantém HML/Produção em deny-by-default.

### Teste de negativa integral

O teste `All_candidates_denied_returns_no_personal_data` cobre a resposta vazia quando a política rejeita todos os candidatos retornados pelo retriever. É um teste com política simulada, não substitui a prova de restrição efetiva da política municipal com dados de órgãos distintos. A promoção permanece bloqueada até a execução dos testes no HEAD do PR e a decisão documentada sobre visibilidade institucional.

### Reconciliação com master (27/09/2026)

A comparação GitHub registrou divergência de 47 commits novos em `master` contra 36 commits da branch no momento da consulta. O único caminho de arquivo modificado em ambas as pontas na comparação foi `Solution/config/release/nuget-lock-provenance.json`. Não resolver por `ours`/`theirs`: incorporar as alterações do master, regenerar os locks com SDK 8.0.424, recomputar os hashes do grafo e executar os gates de proveniência. O estado `mergeable=false` não deve ser atribuído exclusivamente a esse arquivo sem a tentativa de integração. Não há execução de Actions localizada para o HEAD recente. O teste `Policy_failure_never_returns_candidate_data` acrescenta cobertura para exceção da política; sua execução ainda não está confirmada.

### Indisponibilidade do modelo ativo

A rota captura `InvalidOperationException` do serviço (inclusive ausência de modelo probabilístico ATIVO) e retorna HTTP 503 sem detalhes internos. O teste HTTP `model_unavailable` verifica que a resposta não contém dados de candidatos nem a mensagem de erro do modelo. A captura não substitui telemetria operacional de indisponibilidade; os testes ainda precisam passar no CI do HEAD.

### Teste da política concreta de desenvolvimento

`AccessPolicyTests.Semiblind_search_current_policy_is_municipal_not_person_scoped` caracteriza a implementação real: Gestor com escopo consulta qualquer UUID; credencial BENEFICIO com escopo consulta qualquer UUID dentro do código de recurso autorizado, mas é negada para outro recurso. Isso registra o comportamento **atual**, não o homologa como regra de compartilhamento de dados da busca. O gate de liberação exige decisão expressa sobre os atributos de identidade visíveis por instituição e teste de integração correspondente. Não interpretar o teste de caracterização como prova de segregação por Pessoa.

### Validação de tamanho dos campos

Foram adicionados testes unitários para rejeitar `Nome` e `NomeMae` com 201 caracteres, conforme limite de 200 caracteres do serviço. Os testes exercitam o contrato do serviço e ainda dependem de execução do CI no HEAD. A integração da branch com `master` e a validação da política de visibilidade institucional permanecem bloqueios de merge.

### Caracterização de exposição entre instituições

O teste `Concrete_municipal_policy_exposes_same_candidate_to_two_authorized_resources` exercita o serviço com `MunicipalAccessPolicyEngine` real e dois contextos BENEFICIO, um SEHAB/AA01 e outro SMADS/BB02. A implementação atual retorna os mesmos atributos de candidato para ambos quando têm escopo e recurso autorizados, gerando `OpcaoId` distinto por consulta. O teste caracteriza um risco de exposição transversal, **não é aceite da regra de negócio nem prova de conformidade**. Antes de habilitar com dados reais, decidir se os atributos nominais da busca podem ser compartilhados entre esses órgãos e implementar a restrição de projeção, caso necessária. Não fazer merge apenas por esse teste passar.

### Deduplicação defensiva das opções

O serviço mantém um conjunto de `PessoaUuid` já vistos e ignora observações repetidas da mesma pessoa antes da autorização e projeção. O teste `Duplicate_person_is_returned_only_once` caracteriza o limite mesmo quando o retriever devolve duplicatas. A deduplicação não substitui a seleção governada nem a política de visibilidade institucional; execução do teste no CI ainda pendente.

## Complemento de proteção: gate estrito de banco sintético (PR #543)

O PR #532 está integrado ao master e o PR #544 acrescentou a configuração `SemiblindIdentitySearch:Enabled=false` por padrão, com bloqueio de HML/Produção. O PR #543 **preserva exatamente essa configuração**, sem criar uma flag paralela, e reforça a checagem de ativação. Mesmo com `Enabled=true`, o endpoint só executa em `Development` quando sua **própria conexão SQL** aponta para o banco `JornadaSyntheticDev`, cujo marcador residente `Jornada.EnvironmentProfile` precisa ser `Development`. O gate é registrado na DI e consultado após autenticação/autorização DT-04, mas antes da recuperação probabilística, e falha fechado (HTTP 503) sem dados de candidatos se as condições não forem comprovadas. O middleware continua auditando as consultas recusadas. O nome e o marcador do banco são controles complementares, não autorização para misturar cidadãos reais com o corpus sintético.

O procedimento de DEV sintético deve inicializar `JornadaSyntheticDev` pelo `scripts/local-db.ps1` ou `scripts/local-db.sh`, que grava o marcador residente. A verificação automatizada cobre flag desligada, banco de DEV comum, marcador ausente/incorreto e HML/Produção com a flag ligada. Os testes HTTP in-memory usam um gate simulado e validam autorização, auditoria e ausência de PII quando a busca é recusada; a política concreta do gate requer consulta SQL ao próprio banco.

O CI do PR #543 anterior à reconciliação concluiu com sucesso, incluindo integração SQL e instalador Windows. A reconciliação com o PR #544 exige nova execução para este HEAD. A issue #539 permanece aberta como bloqueio de ativação com dados reais; #378 mantém a dependência da identidade corporativa.


## Resultado de recuperação — #612

A resposta distingue explicitamente a execução integral da recuperação da impossibilidade de executar uma busca seletiva:

- `busca_completa=true` com `candidatos=[]`: os passes elegíveis foram executados dentro dos limites e nenhum candidato foi recuperado.
- `busca_completa=false`: a ausência de candidatos **não** significa pessoa inexistente e não autoriza `NOVA_IDENTIDADE`. `motivo_incompletude` usa taxonomia estável: `SEM_PASSE_ELEGIVEL`, `LIMITE_FANOUT_EXCEDIDO` ou `TIMEOUT_RECUPERACAO`.

O relatório de recuperação é anterior e separado do scorer FS. Fan-out/timeout não alteram score, thresholds ou decisão probabilística e não são mascarados por truncamento silencioso. A apresentação continua limitada e sujeita à autorização por Pessoa; UUID, CPF, LLR, posterior, score e posição de ranking não fazem parte do contrato externo.

A ativação em HML/Produção continua proibida enquanto os gates institucionais de visibilidade e identidade corporativa não forem satisfeitos. Evidência sintética/IBGE serve para regressão técnica e calibração de engenharia, não substitui medição em corpus representativo autorizado.

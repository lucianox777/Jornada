# Busca síncrona semicega — PR #532

## Contrato e fronteiras

`POST /api/v1/identidade/busca` recebe nome, nascimento opcional e nome da mãe opcional. Requer o escopo `jornada.identidade.busca.read`, com autenticação compartilhada DT-04 e política institucional. HML/Produção permanecem deny-by-default até a identidade corporativa.

A consulta utiliza o modelo ATIVO, o snapshot de ruleset, o loader de blocking e o ranking Fellegi–Sunter do Runner. Seleciona internamente até cinco candidatos, randomiza a ordem de apresentação com gerador criptográfico e retorna somente nome, nascimento, nome da mãe e identificador aleatório de opção. O identificador de opção NÃO é um token de confirmação e não pode ser usado para constituir ou publicar vínculos. A opção «Nenhum destes» é sempre válida, inclusive com lista vazia.

Sem nascimento, o blocking legado retorna lista vazia. Rulesets dinâmicos só executam quando existe passe elegível para a observação. O endpoint não inventa data, não degrada para varredura irrestrita e não altera o scorer.

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

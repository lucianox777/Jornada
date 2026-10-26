# DT-10 V9 — vínculo imutável de autorização DT-15 (em implementação)

**Estado em 26/10/2026:** o guard SQL foi adicionado à branch da PR #1199 no commit `12dadabff6d05ca81fc2cd1098f799defbcf0f71`. **Não testado fisicamente nem integrado à master**. A política continua desabilitada por padrão, sem registros de política ou autorização gerados pela migração. A aprovação de um modelo DT-15 isoladamente **não** autoriza a política inferior.

## Contrato de autorização

A tabela `auditoria.v9_dt10_lower_policy_autorizacao` vincula `aprovacao_id`, `politica_versao` e `politica_sha256` em registro separado, protegido contra UPDATE e DELETE por trigger. Somente a governança DT-15 pode criar a autorização depois de verificar a política proposta; nenhuma autorização é semeada automaticamente. A publicação exige simultaneamente a aprovação DT-15 ACTIVATE existente e o digest exato correspondente à linha da política.

**Serialização canônica V1:** `HASHBYTES('SHA2_256', CONCAT(...))`, com texto SQL Server Unicode e campos nesta ordem: prefixo literal `JORNADA_DT10_V9_POLICY_V1`, `politica_versao`, `modelo_id` (UUID), `modelo_versao`, `modelo_fingerprint` (hex), `fit_fingerprint` (hex), `snapshot_sha256` (hex), `limiar_inferior`, `limiar_superior`, separados por `|`. Os decimais são convertidos a NVARCHAR(40) a partir de DECIMAL(18,8), preservando oito casas. A verificação é realizada na procedure de publicação antes de escrever o ledger. Não aceitar digest enviado pelo produtor como substituto da recomputação SQL.

O snapshot é verificado por hash do conteúdo, schema, fit e threshold superior, além da aprovação do modelo e dossiê vigentes. Alterações na versão, no modelo, no fit, no snapshot ou nos limiares invalidam o digest. A trigger impede reescrever ou apagar a autorização existente; controles de permissão devem restringir INSERT a operadores DT-15 autorizados.

## Evidências ainda obrigatórias antes do merge

1. Teste SQL positivo em harness descartável: aprovação DT-15 válida, autorização do digest, candidato e posterior persistidos, publicação de nova identidade, ledger único e replay idempotente.
2. Testes negativos de mutação individual dos campos vinculados, digest ausente ou divergente, aprovação incorreta e tentativa de UPDATE/DELETE da autorização.
3. Validação do schema, testes de regressão e CI aplicável no HEAD final; atualização de matriz de gates e instruções operacionais antes da ativação.

**Não ativar em produção nem inferir aprovação de CI a partir deste documento.**

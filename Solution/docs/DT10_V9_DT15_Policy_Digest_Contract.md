# DT-10 V9 — vínculo imutável de autorização DT-15 (em implementação)

**Estado em 26/10/2026:** o guard SQL foi adicionado à branch da PR #1199 no commit `12dadabff6d05ca81fc2cd1098f799defbcf0f71`. **Teste físico negativo e replay legado aprovados em LocalDB descartável no HEAD 5a9e68e; a prova positiva governada permanece pendente; não integrado à master**. A política continua desabilitada por padrão, sem registros de política ou autorização gerados pela migração. A aprovação de um modelo DT-15 isoladamente **não** autoriza a política inferior.

## Contrato de autorização

A tabela `auditoria.v9_dt10_lower_policy_autorizacao` vincula `aprovacao_id`, `politica_versao` e `politica_sha256` em registro separado, protegido contra UPDATE e DELETE por trigger. Somente a governança DT-15 pode criar a autorização depois de verificar a política proposta; nenhuma autorização é semeada automaticamente. A publicação exige simultaneamente a aprovação DT-15 ACTIVATE existente e o digest exato correspondente à linha da política.

**Serialização canônica V1:** `HASHBYTES('SHA2_256', CONCAT(...))`, com texto SQL Server Unicode e campos nesta ordem: prefixo literal `JORNADA_DT10_V9_POLICY_V1`, `politica_versao`, `modelo_id` (UUID), `modelo_versao`, `modelo_fingerprint` (hex), `fit_fingerprint` (hex), `snapshot_sha256` (hex), `limiar_inferior`, `limiar_superior`, separados por `|`. Os decimais são convertidos a NVARCHAR(40) a partir de DECIMAL(18,8), preservando oito casas. A verificação é realizada na procedure de publicação antes de escrever o ledger. Não aceitar digest enviado pelo produtor como substituto da recomputação SQL.

A guarda de publicação canônica exige também `origem_evidencia` do dossiê DT-15 em `REPRESENTATIVA_HML` ou `REPRESENTATIVA_PROD`. `SINTETICA_DEV` é rejeitada mesmo com estado `COMPLETO`, digest correto e aprovação ACTIVATE. A origem declarada como representativa é **condição necessária, não prova de representatividade estatística, aprovação institucional ou liberação de TEST**. O ensaio positivo em LocalDB deve permanecer claramente técnico e não pode ser usado para declarar certificação.

O snapshot é verificado por hash do conteúdo, schema, fit e threshold superior, além da aprovação do modelo e dossiê vigentes. Alterações na versão, no modelo, no fit, no snapshot ou nos limiares invalidam o digest. A trigger impede reescrever ou apagar a autorização existente; controles de permissão devem restringir INSERT a operadores DT-15 autorizados.

## Evidências ainda obrigatórias antes do merge

1. Teste SQL positivo em harness descartável: aprovação DT-15 válida, autorização do digest, candidato e posterior persistidos, publicação de nova identidade, ledger único e replay idempotente.
2. Testes negativos de mutação individual dos campos vinculados, digest ausente ou divergente, aprovação incorreta e tentativa de UPDATE/DELETE da autorização.
3. Validação do schema, testes de regressão e CI aplicável no HEAD final; atualização de matriz de gates e instruções operacionais antes da ativação.

**Não ativar em produção nem inferir aprovação de CI a partir deste documento.**

## Critério de aceite verificável da onda positiva (bloqueante para retirar draft)

A prova deve ocorrer em **um único LocalDB descartável JornadaHarness**, usando o fluxo real de aprovação DT-15 (aprovação ACTIVATE e dossiê vigente), nunca um INSERT artificial de autorização tomado como prova de governança. O digest deve ser calculado e conferido pelo SQL a partir de modelo, versão, fit, snapshot imutável e ambos os limiares. O valor **0,05 é configuração da política aprovada na fixture**, não constante de modelo ou do procedimento.

Executar **na mesma onda**, sob a mesma política aprovada, e registrar PASS/FAIL por caso na PR #1199:

| Caso | Preparação | Asserções obrigatórias |
| --- | --- | --- |
| LOWER | Candidato persistido, posterior estritamente abaixo de 0,05 | Exatamente uma publicação `LIMIAR_INFERIOR_V9_GOVERNADO`, uma nova identidade e um evento no ledger, com vínculo à política e ao snapshot aprovados |
| INTERMEDIÁRIO | Candidato persistido, posterior >= 0,05 e abaixo do limiar superior aprovado | Continua pendente; zero identidades criadas, zero eventos de criação |
| SEM CANDIDATO | Busca completa sem candidato | Caminho existente `SEM_CANDIDATO_NO_RULESET_BLOCKING`, sem depender da autorização lower |
| REPLAY | Reexecutar a mesma onda sem alterar inputs | Zero publicações adicionais; mesmos UUIDs, estados e contagens de eventos |

O teste deve comparar **estado final de cada origem e contagem de eventos por origem/run** antes e depois do replay, não apenas ausência de exceções. Não marcar como aprovado quando a fixture contiver somente registros artificiais sem passar pela aprovação DT-15 real.

### Mutações negativas e atomicidade

Repetir com autorização ausente, `aprovacao_id` incorreto, digest divergente e alteração individual de versão, modelo, fit, snapshot, limiar inferior e superior. Em cada rejeição, exigir **zero eventos parciais, zero promoção de identidade e estado do run preservado**; UPDATE e DELETE de autorização devem falhar. Registrar o erro SQL esperado e observado para cada caso.

### Publicação de evidências e ordem de merge

Publicar na PR #1199 tabela por caso (PASS/FAIL), SHA do HEAD, identificador da execução física, digest/snapshot em formato não sensível e contagens antes/depois. **Não retirar de draft nem mesclar** enquanto qualquer caso não passar e todos os gates aplicáveis não estiverem verdes no mesmo HEAD. A PR #1202 do helper NUnit é independente; substituir os usos ambíguos existentes e validar build Release com warnings-as-errors antes de seu merge. Não ativar V9/DT-15 em ambiente persistente.

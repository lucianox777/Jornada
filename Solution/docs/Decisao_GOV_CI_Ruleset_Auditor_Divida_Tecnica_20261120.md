# GOV-CI-RULESET-AUDITOR-DEFER-20261120 — decisão do PO

## Contexto
A PR #1764 foi integrada à master (`f9b8d0779f6d29096d0c9d049024f95709390eaa`). O auditor somente leitura consulta primeiro a proteção clássica, recebe HTTP 404 na master protegida por ruleset e termina UNKNOWN sem consultar regras efetivamente aplicáveis. O PO recebeu relatório de inspeção autenticada de `/rules/branches/master` identificando ruleset **Proteção da master**, ID **26230392**, ativo, alvo `~DEFAULT_BRANCH`, sem bypass, PR exigida, exclusão e force-push bloqueados, e checks `unit-runtime`, `deterministic-build` e `security-guards` vinculados ao GitHub Actions (integration 15368). Esses resultados foram **relatados**, não reproduzidos pelo conector desta sessão.

## Decisão aprovada
O Product Owner decidiu **classificar a correção do auditor como dívida técnica e pular sua implementação neste momento**. Rastrear em issue #1765, sem abrir PR de código corretivo agora. A decisão não declara que o auditor passou, não dispensa os critérios científicos e não encerra automaticamente a P0-A #1734.

## Justificativa e impacto
Evitar bloquear outras prioridades por limitação do diagnóstico automatizado quando há evidência autenticada manual relatada da configuração efetiva. O script permanece fail-closed e pode produzir UNKNOWN/UNVERIFIED em proteção exclusiva por ruleset; isso não é prova de ausência de proteção. Não alterar o ruleset: `strict_required_status_checks_policy=false` e `required_approving_review_count=0` permanecem como riscos de governança conhecidos, sujeitos a futura decisão específica. Nenhuma mudança em LOCK, corpus, TRAIN, TEST, CI ou produção.

## Trabalho futuro e precedência
Issue #1765: distinguir HTTP 404 clássico de falha de autenticação; consultar `/rules/branches/master` e detalhes completos dos rulesets; validar aplicação, checks e controles; separar checks_verified de master_protection_verified; testes offline e fail-closed. Precedência: GOV-DOC-01, #1734, decisão GOV-CI-MASTER-CHECKS-AUDIT-20261120, PR #1764. A evidência de CI da PR não comprova por si só CI pós-merge da master nem os demais critérios de #1734; manter pendências independentes visíveis. O fluxo V9 científico permanece condicionado aos gates aplicáveis.

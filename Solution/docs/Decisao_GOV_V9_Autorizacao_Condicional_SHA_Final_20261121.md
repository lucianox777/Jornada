# GOV-V9-CAMPANHA-STANDARD-LINEAR-100K-AUTORIZACAO-SHA-FINAL-20261121

**Data:** 21/11/2026. **Autoridade:** Product Owner. **Estado:** AUTORIZAÇÃO CONDICIONAL APROVADA; PREFLIGHT NÃO ATESTADO; DESPACHO NÃO REALIZADO.

## Contexto e manifestação

A autorização anterior, registrada em [decisão de 21/11](Decisao_GOV_V9_Autorizacao_Condicional_Campanha_Standard_Linear_100k_20261121.md), referia-se exclusivamente ao SHA `ed5137daa484679bef7c8dd42bf77e61460c9189`. A PR #1825 integrou a documentação e levou `master` ao SHA `a74d56f0acd463cb9efcaee17f554cc2fc935d40`. O relatório de preparação do Work indicou diff limitado a quatro arquivos Markdown, CI pós-merge e regressões aprovadas, integridade científica verificada e preflight parcial. O assistente recomendou autorizar o novo SHA, **condicionado à confirmação efetiva do preflight**, mantendo separados autorização e atestação. O PO respondeu: **“Eu confirmo a sua recomendação.”**

## Decisão e alcance

Fica **autorizada condicionalmente** a campanha científica V9 Standard Linear 100k **somente** para o SHA `a74d56f0acd463cb9efcaee17f554cc2fc935d40`, no workflow `.github/workflows/v9-standard-linear-100k.yml`, rota IBGE congelada e parâmetros padrão do runbook. A aprovação da recomendação **não constitui atestação do preflight**, não permite assinalar `security_preflight_approved=true` antes da comprovação pelo proprietário e **não autoriza despacho imediato**.

## Justificativa, impactos e condições

A decisão evita repetir verificações técnicas já aprovadas e preserva vinculação do ato científico ao código e insumos efetivamente validados. Antes do despacho: (1) proprietário atesta preflight completo para esse SHA, inclusive isolamento SQL descartável, TLS/credenciais, capacidade, integridade LOCKED/IBGE, ausência de Gold/Serving e controles do runbook; (2) não há falhas bloqueantes nas verificações relevantes; (3) `master` e `expected_sha` correspondem exatamente ao SHA autorizado; (4) workflow é despachado manualmente pelo proprietário, na tentativa 1; (5) evidências são registradas.

**Cuidado de governança:** a integração desta decisão documental por PR altera o SHA da `master` e **não estende automaticamente a autorização a esse novo SHA**. Para evitar ciclo infinito de alteração do SHA por documentação, registrar recibos operacionais de autorização/preflight também em issue/PR do GitHub, sem alterar o commit científico, conforme mecanismos admitidos pela governança. Não despachar em SHA distinto sem autorização expressa.

## Precedência e rastreabilidade

Prevalecem GOV-DOC-01, manifests e seeds LOCKED, três coortes independentes, TEST obrigatório se houver vencedor, gates fail-closed e vedação de promoção/ativação automática. A issue #1584 continua aberta e deve ser avaliada por bloqueio real, não encerrada por inferência. Evidências: PR #1825 (squash `a74d56f0acd463cb9efcaee17f554cc2fc935d40`), CI #44922073440, regressão ampla #44922073460, probe #44922073382, Diagnostics #44923030906 e relato do Work apresentado pelo PO nesta conversa.

**Resultado:** autorização específica concedida; atestação do proprietário pendente; nenhuma campanha científica iniciada por este registro.

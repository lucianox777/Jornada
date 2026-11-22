# GOV-V9-CAMPANHA-STANDARD-LINEAR-100K-AUTORIZACAO-SHA-FINAL-20261121

## Autorização dinâmica vigente

**Decidida em 21/11/2026 pelo Product Owner**, no Mandato Executivo Definitivo da campanha V9 Standard Linear 100k. Aplica-se ao último SHA validado da `master` no momento do despacho, no workflow `.github/workflows/v9-standard-linear-100k.yml` e no escopo científico aprovado. Abrange correções pertinentes e reexecuções justificadas.

1. Imediatamente antes de cada despacho, confirmar o HEAD da `master`, as PRs integradas, os checks obrigatórios, a validade científica, a ausência de bloqueios e a validade do preflight. Passar aquele SHA integral em `expected_sha`.
2. O proprietário atesta efetivamente o preflight e faz o despacho manual, tentativa 1. A aprovação dinâmica do PO e a atestação formal são atos distintos. O campo `security_preflight_approved=true` decorre da atestação válida.
3. Mudanças de SHA dentro do escopo aprovado seguem verificação dos controles afetados e recibo operacional no GitHub. Use o recibo para registrar o SHA validado antes da execução e consolide a documentação ao fechar a campanha. Reexecuções exigem causa identificada, novo despacho e seed TEST inédita quando aplicável.
4. Preservar parâmetros e manifestos LOCKED, referência IBGE, coortes próprias e TEST obrigatório com vencedor. Gold, Serving, produção e promoção ficam fora do mandato. Mudanças científicas ou de segurança fora do escopo exigem decisão específica.

**Precedência.** A regra dinâmica substitui a exigência de nova autorização do PO por alteração de SHA em escopo nas decisões anteriores, inclusive as autorizações específicas para `ed5137daa484679bef7c8dd42bf77e61460c9189` e `a74d56f0acd463cb9efcaee17f554cc2fc935d40`. Preflight, verificações do SHA e controles de segurança e ciência continuam obrigatórios pelo [runbook](Runbook_Operacao.md#operação-do-calibrador-v9). Cada campanha conserva a identidade do SHA executado quando o HEAD da master avança.

**Evidência da aplicação em 22/11/2026.** O [run #44970012048](https://github.com/lucianox777/Jornada/actions/runs/44970012048) concluiu ADMISSION, PREPARE, TRAIN/AUDIT de 324 hipóteses, VALIDATION de três finalistas, TEST independente e CLOSEOUT `VERIFIED`. [Resultado e recibos](V9_Standard_Linear_100k_Resultado_Run_44970012048.md). Tentativa 1 no SHA `30c71a0db1bdeac6ba536db5e428ade4d5c70c9c`, com preflight atestado no despacho do proprietário. [Admissão e PREPARE](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7072044978) · [Recibo final](https://github.com/lucianox777/Jornada/pull/1837#issuecomment-7075300378)

## Registro histórico das autorizações específicas por SHA

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

## Ampliação do mandato operacional pelo PO — 21/11/2026

Após receber proposta de mandato Work para admissão final e execução científica, o PO determinou: **“Eu quero alterar esse mandato porque eu já autorizo a execução, as execuções que forem necessárias.”**

**Decisão:** o PO autoriza o Work a conduzir autonomamente a execução científica V9 Standard Linear 100k e as execuções técnicas/operacionais adicionais **necessárias** à sua conclusão, inclusive verificações, correções, reexecuções justificadas e acompanhamento, **sem solicitar aprovação repetitiva para cada execução**, desde que respeitados os protocolos oficiais, limites científicos, custo/recursos disponíveis, rastreabilidade e controles de segurança. Cada tentativa deve ter justificativa, SHA, parâmetros, run ID, resultado e causa de eventual reexecução. Não inclui autorização para campanhas de escopo diferente, alterações LOCKED, promoção/ativação de modelos, bypass ou repetição ilimitada sem diagnóstico.

**Condições que permanecem:** esta autorização de execução não equivale a atestação do preflight; o proprietário deve confirmar efetivamente os controles e o workflow deve cumprir o gate owner-only. O workflow exige SHA idêntico ao HEAD da `master`; quando o merge documental alterar o SHA, o Work deve **parar o despacho e solicitar autorização específica para o novo SHA**, salvo se houver decisão posterior expressa que o cubra. Não utilizar `security_preflight_approved=true` sem atestação real. A execução deve permanecer bloqueada enquanto houver falhas bloqueantes. A autorização é para executar quando os gates forem satisfeitos, **não** para ignorá-los.

**Precedência:** esta ampliação substitui apenas a necessidade de pedir permissão a cada execução operacional pertinente, mantendo as restrições técnicas e científicas das seções anteriores, GOV-DOC-01 e o runbook. Não houve despacho por este registro.

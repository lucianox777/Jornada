# Checkpoint operacional — 2026-11-20 — mandato IA

**Escopo:** consulta pontual via conector GitHub; não é inventário paginado de Actions. **Janela:** observações desta rodada em 20/11/2026 UTC; não foi obtido cursor temporal da API Actions. **Fonte:** GitHub PR metadata, diff e commit combined status. **SHA master observado:** `3a329e494d8d4feabd951181c29020bc12d61cd7` (busca de commits; revalidar via ref antes de mutações). **Saúde master: NÃO VERIFICADO.**

## Revisão de frentes e commits
- **P0-A #1734 / PR #1769:** draft aberta, HEAD `960d5ffc8c2e03c6d366d3521397a91bea59c1d8`, 260 commits e 34 arquivos alterados segundo metadata. O escopo efetivo excede o título de auditoria somente leitura: diff inclui gerador C#, projeto parametrizado, workflow de 100k, watchdog, status summary e testes. **Risco de revisão e precedência:** separar auditoria read-only de alterações científicas/operacionais; não presumir aprovação de mudanças LOCKED ou de campanha. Exigir revisão por arquivo, proveniência e gates do SHA atual antes de qualquer merge. A alteração observada no watchdog reforça validações de IDs, jobs, tipos e evidência de logs, mas isso não demonstra teste remoto nem autorização de rerun.
- **Pós-merge / PR #1759:** draft aberta, HEAD `193982db1b68356f11c71a068de29143b9d1b625`, mergeable=false na consulta. Diff adiciona monitor horário `jornada-ci-health-monitor` e verificador fail-closed para dois workflows; a própria documentação informa que não é inventário global e que a execução real não foi comprovada. Evitar criar outro monitor horário antes de reconciliar.
- **Checkpoint / PR #1770:** draft aberta, HEAD `2290aae057103a4f77470a3956dc063bfc88d0e8`; relata 6 runs/12 jobs e cobertura parcial, sem autorização de merge.
- **Mandato / PR #1771:** draft aberta, HEAD anterior `279c3d51fbb9e9a4e1449d2337c4eca71684caf0`; documentação e índice em proposta, sem integração.
- **Plano oficial:** prioridade aprovada P0-A #1734 → DT-10 #1706 → P0-B científica V9; não iniciar TRAIN/TEST, alterar LOCKs ou ativar V9 por inferência. Frentes independentes permitidas: revisão de CI, documentação e testes offline não destrutivos.

## Revisão de jobs e cobertura
- Consulta `get_commit_combined_status` nos SHAs `3a329e494d8d4feabd951181c29020bc12d61cd7`, `960d5ffc8c2e03c6d366d3521397a91bea59c1d8` e `279c3d51fbb9e9a4e1449d2337c4eca71684caf0`: **statuses=[]** para os três. Essa interface não é inventário de workflow runs/check runs e resultado vazio **não** prova ausência de jobs.
- **RUNS ÚNICOS:** não apurado. **Aprovadas / andamento / aguardando / reprovadas / outras:** indeterminadas. **DELTA:** não comparável, pois não há inventário compatível por ID e janela.
- **Gates pós-merge:** `jornada-ci` e `jornada-pr-required-fast` são exigidos pelo verificador proposto em #1759; aplicabilidade e execução no SHA master atual não verificadas nesta rodada.
- **Jobs/steps/skips/artefatos:** sem cobertura direta nesta consulta. Nenhum teste executado ou rerun solicitado nesta rodada.

## Próxima ação executável
1. Obter ref master diretamente e inventário paginado Actions por janela UTC, evento, workflow, SHA e run_attempt; inspecionar jobs e artefatos, preservando IDs e limites.
2. Revisar a decomposição da PR #1769 em incrementos isolados e confirmar testes por SHA; não mesclar enquanto o escopo efetivo conflitar com o título/autorizações.
3. Reconciliar #1759 com #1770 e o watchdog já existente; decidir implementação incremental do monitor cumulativo somente após identificar lacunas.
4. Prosseguir com DT-10 apenas em atividades independentes seguras; P0-B científica continua sujeita aos gates próprios.

**Persistência:** este checkpoint é proposto na branch da PR #1771; continuidade oficial ainda não garantida até integração. Nenhum segredo, teste científico ou alteração de produção.

## Atualização de investigação — jobs reais e correção proposta
Consulta de runs por SHA via conector (somente evento PR, primeira página; **não exaustiva**):
- PR #1759 HEAD `193982db1b68356f11c71a068de29143b9d1b625`: run 44663007640 `jornada-pr-required-fast` completed/success; quatro jobs success.
- PR #1769 HEAD anterior `960d5ffc8c2e03c6d366d3521397a91bea59c1d8`: seis runs retornados, quatro success e dois failure. Run 44789044996 (`jornada-pr-required-fast`): classify-impact, security-guards e deterministic-build success; unit-runtime failure (job 136471746935). Run 44789044891 (diagnóstico P0-A): job 136471513615 failure e job histórico success.
- PR #1771 HEAD anterior `4bf91f96238670c0cb2d112e5b432b7e76b7dbed`: run 44792515802 inicialmente queued; consulta subsequente de jobs mostrou classify-impact success e outros três in_progress. **Não extrapolar resultado final.**
- Jobs 136471513615 e 136471746935: logs recuperados. Ambos falharam no teste `test_required_fast_enforces_frozen_generator_before_impact_classification`. A asserção proibia `continue-on-error: true` em todo o job `classify-impact`, incluindo observador de ruleset explicitamente não bloqueante. O gate LOCKED propriamente dito não usa essa dispensa. Causa reproduzível por inspeção do teste e YAML; outras causas posteriores podem aparecer após esta correção.
- Correção mínima em #1769 commit `d97966033af2634a9381c688de1d66d41f5cf329`: restringe a asserção ao step `Enforce both historical TF LOCKED generator identities before classification` e exige `--require-pinned`. **Teste local não executado; CI do novo HEAD pendente de observação.**
- Steps skipped no job de diagnóstico após a falha inicial não constituem aprovação. Nenhum rerun ou merge solicitado.

**Cobertura:** runs consultados por SHA em primeira página, sem paginação total, sem push master, sem inventário histórico, sem comparação temporal de checkpoints. **Saúde master: NÃO VERIFICADO**. Próxima ação: acompanhar checks do commit `d979660...`, inspecionar nova falha se houver e ampliar coleta paginada por API autorizada.

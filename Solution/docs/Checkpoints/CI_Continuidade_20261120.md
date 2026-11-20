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

## Atualização — acompanhamento de CI e frente independente pós-merge
- PR #1769 SHA `d97966033af2634a9381c688de1d66d41f5cf329`: seis runs observados, todos `in_progress` na consulta por SHA; run obrigatório 44792630722 com `classify-impact` success e três jobs em progresso; auditor P0-A run 44792630873 com dois jobs em progresso. **Sem conclusão de gate**.
- PR #1771 SHA `15d9c7bfeb7978f929d7c1c661b0299a992587bd`: run obrigatório 44792657934 em progresso; `classify-impact` e `deterministic-build` success, `security-guards` e `unit-runtime` em progresso.
- PR #1759: revisão independente do verificador identificou que entrada de run não dicionário, ID ausente/inválido e duplicidade de ID poderiam causar exceção não controlada ou seleção ambígua. Correção fail-closed em commits `0ae02fd76be3a437109383ea655ee4e91412b6ed` e `dddee976e1a7f1d18c4b55ddbcade1f102f148df`, com testes de regressão para entradas malformadas e IDs duplicados. **Testes não executados nesta rodada; CI do novo HEAD pendente**.
- A coleta por SHA continua limitada à primeira página de runs `pull_request`; não constitui inventário global nem prova pós-merge `push`. Não houve rerun, merge ou execução científica.

**Próxima ação:** reconsultar os dois SHAs atualizados e respectivos jobs; obter logs de qualquer nova falha; validar testes da PR #1759 e separar observação periódica de inventário cumulativo antes de promover monitor.

## Atualização de acompanhamento — resultados posteriores dos mesmos SHAs
- PR #1769, HEAD `d97966033af2634a9381c688de1d66d41f5cf329`: run obrigatório `jornada-pr-required-fast` #44792630722 **completed/success**, com quatro jobs success (classify-impact #136483970648, deterministic-build #136484099544, security-guards #136484099563, unit-runtime #136484099837). Diagnóstico P0-A #44792630873 **completed/success**. Outros três runs observados completed/success: #44792630858, #44792631148 e #44792630791. Run #44792630803 (pin IBGE independente) permanecia **in_progress**; job #136483970457 in_progress. Portanto **5 success + 1 in_progress na amostra de 6**, não 6 aprovadas. Cobertura limitada à primeira página de runs de PR; sem certificação científica.
- PR #1759, HEAD `dddee976e1a7f1d18c4b55ddbcade1f102f148df`: consulta de runs por SHA retornou lista vazia; **não comprova ausência de CI** devido à limitação do conector. Nenhuma aprovação do novo HEAD comprovada.
- PR #1771, HEAD anterior `14e4a22496a75ffa2392ce3c979670a5daf6fd10`: run obrigatório #44792780913 **completed/success** com quatro jobs success (classify-impact #136484487542, security-guards #136484707860, deterministic-build #136484707929, unit-runtime #136484707969). É CI de PR, não prova de push pós-merge.
- **Classificação cumulativa:** cobertura parcial; sem paginação, run_attempt completo, inventário por janela UTC ou prova de `master` atual. **Master: NÃO VERIFICADO.**
- **Próxima ação:** reconsultar #44792630803 e HEAD de #1759; auditar logs se falharem; revisar escopo amplo da #1769 antes de merge. Nenhum rerun, merge ou operação científica nesta atualização.

## Atualização — observação do pin IBGE e reconciliação DT-10
- PR #1769 run #44792630803, job #136483970457 `independent-ibge-pin`: ainda `in_progress` na consulta. Steps concluídos com success: checkout, binding de commit, SQL descartável, setup .NET e build. Step `Materialize independent IBGE reference and verify complete provenance` ainda `in_progress`; preservação de artefatos e remoção de container pendentes. **Não é falha nem aprovação comprovada.** Aguardar conclusão e investigar se exceder timeout documentado; não interromper sem autorização.
- PR #1759 HEAD `dddee976e1a7f1d18c4b55ddbcade1f102f148df`: consulta por SHA continua sem runs retornados (API do conector limitada a PR/primeira página). **PENDENTE/NÃO VERIFICADO**, não VERMELHO comprovado.
- DT-10 PR #1706: **closed**, não uma PR aberta aguardando merge. Diff consultado altera apenas teste físico `DT10PublicationEvidenceSqlServerTests.cs`, zerando contadores coerentemente para simular run incompleto sem violar `ck_linkage_run_contagens`. A situação de integração, suficiência do teste e do conjunto DT-10 requer reconciliação de merge SHA e CI; a ordem aprovada P0-A → validação DT-10 → P0-B permanece.
- **Sem mutação de código nesta atualização**; somente leitura de steps/PR e persistência de evidências. Não há comprovação de push CI master nem inventário paginado.

## Retificação factual DT-10 e acompanhamento P0-A
**Retificação expressa da anotação anterior:** PR #1706 está `closed` **E `merged=true`**, em `2026-11-18T17:31:45Z`, merge commit `0d150c536ae518f5a1029e25e664e0d63cd23511`. Portanto a correção do teste DT-10 **foi integrada**; não se deve tratá-la como mera proposta fechada. Seu texto descreve falha anterior do job `dt10-evidence` na master, run #44356060893 (SHA `c5077e5...`), e exige verificação física pós-merge; essa aprovação pós-merge **não foi consultada/comprovada aqui**. Integração da PR isolada não equivale à validação integral de DT-10.
- PR #1769: job #136483970457 da run #44792630803 permanece `in_progress` em consulta repetida. Step `Materialize independent IBGE reference and verify complete provenance` permanece em progresso, após setup, SQL descartável e build aprovados. Sem evidência de conclusão ou de timeout.
- `master`: busca de commits recentes ainda lista `3a329e494d8d4feabd951181c29020bc12d61cd7` como mais recente, mas busca não substitui consulta autoritativa da ref. Sem inventário de jobs `push` para esse SHA.
- Escopo da #1769 continua com 34 arquivos e 261 commits; **revisão de escopo antes de integração** permanece obrigatória. #1759 tem base antiga `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`; verificar atualização de base e checks no SHA final.
**Governança:** este checkpoint corrige interpretação factual, não aprova decisão nova. Sem merge ou ação científica nesta rodada.

## Atualização — P0-A seis execuções concluídas com sucesso (escopo estrito)
- PR #1769 HEAD `d97966033af2634a9381c688de1d66d41f5cf329`: consulta por SHA retornou **seis runs PR completed/success**: #44792630858 (comparação bytes), #44792631148 (contrato), #44792630722 (required-fast), #44792630791 (smoke não publicador), #44792630873 (auditoria diagnóstica) e #44792630803 (pin IBGE).
- Run #44792630803: job `independent-ibge-pin` #136483970457 **completed/success**. Inspeção dos steps confirma success para materialização/verificação de proveniência IBGE, preservação apenas de pin/manifesto e remoção do SQL descartável, além de build/setup. Corrige a pendência anterior; **não significa aprovação de campanha científica ou saúde da master**.
- Evidência obtida via consulta de runs por SHA restrita à primeira página de eventos PR. Resultado **VERDE COMPROVADO apenas para as seis execuções observadas nesse SHA**, sem alegação de inventário completo, cobertura de todos os attempts ou push pós-merge.
- PR #1759 segue aberta/draft com base `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb` antiga; deve ser atualizada/reconciliada com master e checks antes de qualquer integração.
- Plano oficial relido: P0-A (#1734) → validação separada DT-10 (#1706, integrada) → P0-B. A aprovação de CI da PR #1769 **não substitui** os critérios P0-A de master nem a validação física pós-merge DT-10. Não iniciar TRAIN/TEST, alterar LOCK ou ativar V9.

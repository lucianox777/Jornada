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

## Investigação adicional — evidência física DT-10 e limites do conector
- Consulta por SHA do merge #1706 `0d150c536ae518f5a1029e25e664e0d63cd23511` retornou **zero runs**; o método do conector é restrito a `pull_request`/primeira página e **não permite inferir ausência de push CI**.
- Foi inspecionada a run histórica `jornada-ci` #44356060893, explicitamente citada na descrição da PR #1706 como **anterior à correção**. O job `dt10-evidence / dt10-evidence` #134989112336 consta `completed/failure`. Também constam falhas em `unit-runtime`, `harness-smoke` e `unit`; demais jobs mistos success/skipped. **Essa run não pode ser usada como prova pós-merge da correção #1706.**
- PR #1769 continua open/draft, mergeable, HEAD `d97966033af2634a9381c688de1d66d41f5cf329`, com 34 arquivos; PR #1771 continua open/draft, HEAD antes desta atualização `06097850cae8850e0b88bd68e9f247b104ff798a`.
- A referência `#1734` no plano é de frente/issue, não uma PR consultável: `get_pr_info(1734)` retornou 404. Não tratar como PR.
- **Próxima ação de evidência:** obter inventário autenticado de runs `push` no SHA exato do merge e da master atual, com paginação, tentativas e jobs; se indisponível, manter DT-10 e master `NÃO VERIFICADO`. Não promover P0-B com base em runs PR.

## Reconciliação de critérios de aceite P0-A — issue #1734
Issue oficial #1734 permanece **open** e exige cumulativamente: logs das falhas master e impacto #1721; comparação byte/semântica do gerador versus LOCKED; correção mínima sem atualizar manifests congelados por conveniência; comprovação de checks obrigatórios por branch protection/rulesets; CI e probes da master verdes; validação DT-10 separada; documentação GOV-DOC-01. A aprovação de seis runs de PR #1769 **não encerra** a issue nem prova rulesets/CI master.
- Run PR #44792630803 terminou success e publicou artifact `v9-seed42-independent-ibge-pin-44792630803`, id `14657271719`, 2256 bytes, digest `sha256:e7d8aa495fe0456160daa7fd51dfb46c52377b2bae0817feee6439b964791312`, não expirado na consulta, vinculado ao SHA `d97966033af2634a9381c688de1d66d41f5cf329`. **Metadados de artefato confirmados; conteúdo ainda não auditado**. Não confundir o digest do ZIP do artefato com hash do gerador ou pin IBGE interno.
- Próxima ação: inspecionar conteúdo do artefato, confrontar critérios #1734 com diffs e logs master, validar ruleset e evitar promover PR #1769 (261 commits/34 arquivos) sem delimitação de escopo.

## Correção efetiva do verificador pós-merge — PR #1759
- Inspeção direta do código `Solution/scripts/ci-master-postmerge-proof.py` e do teste `test_ci_master_postmerge_proof.py` encontrou incompatibilidade: `test_malformed_run_entries_fail_closed` exige `ValueError` para `{"name":"jornada-ci"}`, mas `evaluate()` anteriormente filtrava a entrada por SHA/branch antes de validar o ID, retornando `missing` em vez de rejeitar a entrada malformada.
- Correção mínima aplicada na branch `fix/ci-master-postmerge-proof-20261120`, commit `12f006434bc7a24fb07d19ff8767693c09e91336`: valida `id` inteiro positivo (sem bool) **antes** dos filtros por SHA, branch, evento e workflow. Preserva `fail-closed` e compatibilidade com o teste existente; não altera manifests LOCKED, workflows de produção ou execução científica.
- **Teste não executado localmente nesta rodada**; CI no novo HEAD precisa ser observada antes de declarar aprovação. O monitor ainda não foi integrado à master. Esta é correção de implementação, não nova decisão do PO.

## Revisão estática do monitor pós-merge — PR #1759 HEAD 12f0064
- A consulta de runs do commit `12f006434bc7a24fb07d19ff8767693c09e91336` não retornou execuções no conector (visibilidade parcial); **CI ainda NÃO VERIFICADA**. PR permanece `draft`, `mergeable=false` segundo metadata (não atribuir causa sem diagnóstico de conflitos/checks).
- Workflow `.github/workflows/jornada-ci-health-monitor.yml` inspecionado: agenda `23 * * * *` e `workflow_dispatch`; permissões somente `actions:read` e `contents:read`; obtém SHA exato de `refs/heads/master`; roda testes Python antes do verificador e publica `postmerge-health.json` como artefato com retenção de 14 dias. Não faz rerun/merge.
- Workflow `jornada-pr-required-fast.yml` inclui `test_ci_master_postmerge_proof.py` no step `Check classification contract`, mas esta inclusão **não comprova que o teste rodou no HEAD novo**.
- Revisão de código confirma que `evaluate` agora valida IDs antes dos filtros; ainda depende de teste efetivamente executado e de resolver condição `mergeable=false`. Não promover monitor até reconciliar com workflows/rulesets da master.

## Regressão de entrada malformada no verificador — PR #1759
- Novo teste `test_invalid_id_in_filtered_run_fails_closed` incluído em `Solution/tests/scripts/test_ci_master_postmerge_proof.py`, commit `674f1bb2eeac8fa6e196059afd2dc1fe4c8c7ef7`: rejeita IDs `None`, `True`, `0`, `-1` e string mesmo em entrada de SHA diferente e evento `pull_request`, ao lado de duas execuções `master/push` válidas. Garante que filtros não escondam resposta malformada.
- Na consulta anterior, HEAD `12f006434bc7a24fb07d19ff8767693c09e91336` sem runs retornados pelo conector, PR `draft` e `mergeable=false`. **Novo HEAD ainda sem resultado CI confirmado**; executar/observar testes antes de aprovar. Sem merge ou alteração científica.

## Reforço de integridade de IDs — PR #1759
- Detectada lacuna: `evaluate()` só rejeitava IDs duplicados **dentro de cada workflow relevante**; IDs globais de runs GitHub devem ser únicos também entre workflows e entradas filtradas. Duplicação entre `jornada-ci` e `jornada-pr-required-fast` poderia produzir falso `verified=true` a partir de resposta malformada.
- Corrigido `Solution/scripts/ci-master-postmerge-proof.py`: conjunto `seen_ids` validado antes de qualquer filtro, commit `48c4505965140c4b2bf6cf265973ad4520e85d4b`. Dois testes novos para duplicação entre workflows e com entrada filtrada, commit `0a9ba031ef15ba4dad221ab554d52071e872f3e1`.
- O HEAD anterior `674f1bb2eeac8fa6e196059afd2dc1fe4c8c7ef7` não retornou runs pelo conector; PR draft/mergeable=false. **CI do HEAD novo ainda não comprovada.** Tentativa de acesso Git remoto no ambiente de execução falhou por DNS, portanto testes não foram executados localmente. Sem merges, reruns ou operações científicas.

## Revisão integral do diff e reviews da PR #1759
- Diff da PR consultado: **6 arquivos**, exclusivamente monitor `.github/workflows/jornada-ci-health-monitor.yml`, inclusão de testes em `.github/workflows/jornada-pr-required-fast.yml`, proposta `GOV_CI_Postmerge_Proof_20261120.md`, índice de decisões, verificador Python e testes Python. A governança interna classifica a decisão como **PROPOSTA técnica, não aprovada para integração**; não confundir com decisão já ratificada.
- Consulta a reviews retornou **zero reviews**; PR ainda `draft`, `mergeable=false`, base `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb` enquanto PR #1769 usa base `3a329e494d8d4feabd951181c29020bc12d61cd7`. Divergência de base **sugere** necessidade de reconciliação, mas não prova causa específica de `mergeable=false`.
- HEAD da PR #1759 `0a9ba031ef15ba4dad221ab554d52071e872f3e1` sem runs retornados pela consulta limitada; nenhuma revisão formal. **Não pronta para merge**; requer CI, avaliação de conflitos, aprovação e validação de rulesets.

## Comparação Git autenticada — divergência da PR #1759
- `compare_commits(1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb, master)`: `status=ahead`, **master avançou 5 commits**, merge-base exatamente `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`. SHA atual da master na comparação: `3a329e494d8d4feabd951181c29020bc12d61cd7`.
- `compare_commits(master, fix/ci-master-postmerge-proof-20261120)`: `status=diverged`, PR **20 commits à frente e 5 atrás**, merge-base `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`. Isso **comprova divergência de histórico**, não necessariamente conflito textual; `mergeable=false` permanece sem diagnóstico conclusivo de conflito.
- Entre os arquivos alterados na master desde a base da #1759 constam `.github/workflows/jornada-pr-required-fast.yml` e `Solution/docs/Indice_Decisoes_Vigentes.md`; ambos também são alterados pela PR #1759. **Pontos de reconciliação prioritários**, sem sobrescrever gates nem decisões.
- `compare_commits(master, fix/p0-locked-generator-diagnostic-20261120)`: `status=ahead`, PR #1769 **261 commits à frente e zero atrás**; 34 arquivos no diff. Comparação mostra base alinhada à master, mas não resolve revisão de escopo ou comprovação física.
- Ação recomendada: reconciliar #1759 sobre a master atual por PR/rebase controlado, preservando simultaneamente as alterações do fast gate e índice. Não forçar merge nem atualizar branch de forma destrutiva sem revisar os dois diffs.

## Reconciliação concreta do fast gate — PR #1759
- Comparação direta de `.github/workflows/jornada-pr-required-fast.yml` na master e na #1759 revelou regressão material na branch: o step `Check classification contract` omitia `test_audit_master_required_checks.py`; o job `unit-runtime` omitia `test_v9_testonly_failclosed_contract.py` e `test_v9_runv9_dispatch_failclosed.py`, com comentário protetor do TEST. A PR adicionava apenas `test_ci_master_postmerge_proof.py`.
- **Correção aplicada:** substituído somente o conteúdo do workflow na branch #1759 pelo conteúdo vigente da master **mais** o teste novo `test_ci_master_postmerge_proof.py` no step de classificação. Commit `ff46e282a541b146c61087c9bb0a5d52d781e32c`. Preservados os três testes de master e guardas TEST, sem alterar LOCK ou disparar campanha.
- `Solution/docs/Indice_Decisoes_Vigentes.md` ainda diverge entre master e branch; **não sobrescrever** índice aprovado da master com versão antiga da PR. É necessário merge/reconciliação documental antes de considerar PR integrável. CI do novo HEAD e status `mergeable` ainda não reavaliados nesta etapa.

## Reconciliação do índice GOV-DOC-01 na PR #1759
- Lidos os arquivos integrais `Solution/docs/Indice_Decisoes_Vigentes.md` em `master` (blob `cf60dee9fc0da11b7be8b1ba4001659f7e3dbb1a`) e branch #1759 (blob `2df6ad53fd7561802d7aaa6b3a75c13f5815aca8`). A branch não incorporava as entradas supervenientes da master, incluindo decisões aprovadas de auditoria de checks e adiamento do auditor de rulesets.
- Reconciliação **não destrutiva** aplicada na branch #1759: preservar integralmente o conteúdo atual da master e acrescentar as duas entradas exclusivas da proposta #1759 (`GOV-CI-POSTMERGE-PROOF-20261120` e `GOV-CI-MONITOR-POSTMERGE-20261120`), ambas explicitamente **não aprovadas/não integradas**. Commit `85f0af876d147ff2a1e22ffeaf0438f8efc7241a`.
- Novo HEAD permanece `draft`, `mergeable=false`; consulta limitada de workflow runs retornou zero, **não equivale a ausência de CI**. A reconciliação do conteúdo dos dois arquivos sobrepostos está implementada, mas a divergência de histórico Git e a revisão/CI continuam pendentes.

## Cobertura offline adicional do verificador pós-merge
- Comparação autenticada após reconciliação de workflow/índice: branch #1759 ainda `diverged`, **22 commits à frente e 5 atrás** da master; PR `draft` e `mergeable=false`. Os dois arquivos de sobreposição foram reconciliados por conteúdo, mas a ancestralidade Git não foi atualizada.
- Acrescentados testes unitários com resposta HTTP simulada (sem rede nem disparar workflows) para rejeitar `total_count` maior que a página retornada e `total_count=True` (booleano indevido). Commit `1b7bbfe518a375a1a07d9a71cbb1fcb6f97d102e` na PR #1759. Cobertura adicional **implementada, execução de CI pendente**.
- Consulta de runs do HEAD anterior `85f0af876d147ff2a1e22ffeaf0438f8efc7241a` retornou lista vazia pelo conector limitado a PR; isso não comprova ausência de execução. Sem merge, rebase, rerun, LOCK, TRAIN ou TEST.

## Teste positivo de escopo da consulta REST — PR #1759
- Revisão do código `Solution/scripts/ci-master-postmerge-proof.py` e testes da branch #1759 confirmou consulta `head_sha=<SHA>`, `branch=master`, `event=push`, `per_page=100`, com validação de `total_count`.
- Acrescentado teste positivo com resposta HTTP simulada de página completa (dois workflows aprovados), incluindo asserções sobre todos os filtros de URL e a avaliação `verified=true`. Commit `5430f23d22ffaa9656c2580943cc64e2703301f6`. Complementa os testes negativos de paginação, sem credenciais reais ou execução de CI.
- Testes implementados, **execução não observada**; PR segue sem aprovação para integração. A comprovação física pós-merge permanece não verificada.

## Correção de proveniência do código do monitor — PR #1759
- Revisão do workflow `.github/workflows/jornada-ci-health-monitor.yml` encontrou divergência potencial em `workflow_dispatch` na branch: o checkout padrão obtinha o ref do evento, enquanto a prova consultava SHA da `master`. Isso permitiria executar o verificador de código diferente daquele cuja CI se pretendia comprovar.
- Corrigido para **resolver primeiro** `refs/heads/master` e fazer `actions/checkout` com `ref: ${{ steps.master.outputs.sha }}`, garantindo que script e testes executem no mesmo commit de master consultado. Commit `1be091b225ac7f1bc809b73e4115305c7a0078db` na PR #1759.
- Limite preservado: esse workflow só poderá executar o código do monitor **depois de integrado na master**; dispatch em branch de proposta não é evidência de implantação. Consulta do HEAD anterior `5430f23d22ffaa9656c2580943cc64e2703301f6` retornou zero runs pelo conector PR-only; CI continua não verificada. Sem merge ou campanhas.

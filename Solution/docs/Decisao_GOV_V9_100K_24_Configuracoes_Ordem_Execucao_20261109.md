# GOV-V9-100K-24-CFG-EXEC-20261109 — determinação de execução técnica pelo PO

**Autoridade:** Product Owner, 09/11/2026 (15h11 BRT). **Referência:** [#1349](https://github.com/lucianox777/Jornada/issues/1349), [#1253](https://github.com/lucianox777/Jornada/issues/1253), GOV-DOC-01.

**Decisão:** dar continuidade e **executar as 24 configurações oficiais V9 multiparâmetro com 100.000 identidades sintéticas** como prova física **técnica**, sem ampliar nem redefinir o plano científico. A determinação de execução **não declara preflight de segurança aprovado nem autoriza contornar bloqueios fail-closed**. Estado em 09/11/2026: **DETERMINADO / NÃO EXECUTADO / BLOQUEADO PELO PREFLIGHT #1253 E PELO DISPARO MANUAL**.

## Contexto e justificativa
O [SCALE medium 100k](https://github.com/lucianox777/Jornada/actions/runs/42534643000) atestou operação com **motor V8**, não a busca multiparâmetro V9. O [perfil do gerador 100k](https://github.com/lucianox777/Jornada/actions/runs/42664056609) provou somente geração e perfil de recursos do gerador, sem SQL, fitting, TEST ou Gold. Nenhum desses resultados substitui as **24 avaliações físicas V9 em 100k**. A execução pedida visa preencher a lacuna técnica #1349.

## Protocolo vigente (sem modificação da ciência)
1. Utilizar o workflow [v9-multiparameter-100k-technical.yml](../../.github/workflows/v9-multiparameter-100k-technical.yml), **somente `workflow_dispatch` no `master`** e **por proprietário**; `expected_sha` igual ao SHA completo do `master` no instante do disparo. Base observada ao registrar esta decisão: `f5fe37e21eceafb59c78d6822adedf6413793b13`; ela não é autorização de rodar contra HEAD posterior sem conferir os gates do novo SHA.
2. Preservar o plano oficial PRIMARY J e seu artifact pinado, referência TF/IBGE histórica 30k, corpus **TRAIN sintético 100.000 / seed42**, V2 WITNESS/ANCHOR, V3 e opções TF/M/U/misturas. Manter **24/24 hipóteses**: 16 testemunhas/diagnósticos, 8 candidatas na correção de múltiplas comparações, sem qualquer exclusão por conveniência de recursos. 81 células; mesmo recorte de dados para comparação.
3. Usar SQL Server descartável, runner GitHub padrão conforme [decisão superveniente #1379](Decisao_V9_GitHub_Standard_100k_Otimizacao_20261109.md), limites de recursos e *fail closed* já implementados; sem alteração de FDR, limiares, paralelismo científico, planos, seeds ou algoritmo.
4. **ANTES de marcar `security_preflight_approved=true`**, comprovar a ausência de credenciais persistentes ativas/reutilizadas, controle dos logs históricos da #1253, bootstrap sem exposição de segredo, descarte SQL e exportação segura de artifacts; registrar evidências sem reproduzir qualquer segredo. [Preflight vigente](Relatorio_V9_Preflight_Seguranca_20261109.md) conclui **PARCIAL / NÃO AUTORIZA CAMPANHA 100K**. A autorização geral para executar não substitui essa verificação específica.
5. Se o preflight e a CI do SHA não estiverem íntegros, **não disparar**. A operação é manual; não mudar para `push`/`pull_request`, não criar exceções de segurança e não declarar execução a partir de abertura de PR ou mero perfil de gerador.
6. Quando houver execução efetiva, conservar ID do run, SHA, plano e *binding receipt*, análise por opção com TP/FP/FN, conflitos, recall, FDR observado (não certificado), P95 condicional, duração, pico de RAM e métrica de disco com seu escopo/limitações; registrar 24/24/16/8 e falhas de capacidade como **FAIL**, nunca como PASS. Publicar **somente** evidências agregadas. Sem tocar TEST cego, Gold, DT-15, HML/PROD ou ativação.

## Resultado verificável deste registro
- `master` consultada em 09/11/2026: `f5fe37e21eceafb59c78d6822adedf6413793b13`.
- Workflow manual existente, configuração das 24 hipóteses e runner padrão já presentes no repositório. **Nenhum workflow V9 multiparâmetro 100k foi disparado por este registro.**
- #1349 permanece **NÃO COBERTO**; #1253 permanece **ABERTA**; o relatório [perfil somente gerador](Relatorio_V9_GitHub_Standard_100k_Perfil_Gerador_20261109.md) não é substituído.
- O conector GitHub disponível neste contexto permite consulta e alterações de repositório, **mas não oferece ação de `workflow_dispatch`**. Não usar edição de workflow para criar gatilho alternativo e burlar o contrato exclusivamente manual.
- A CI do `master` no SHA observado [run 42669297610](https://github.com/lucianox777/Jornada/actions/runs/42669297610) concluiu **failure**; isso não é validação de execução 100k e deve ser reconciliado antes da campanha.

## Impactos, precedência e rastreabilidade
**Impacto:** autorização de prioridade para uma prova técnica futura condicionada a controles operacionais, sem mudança de código/algoritmo, orçamento estatístico, segurança, gestão de custos ou modelo. **Precedência:** complementa [decisão 100k multiparâmetro](Decisao_V9_Escala_100k_Multiparametro_Tecnica_20261109.md) e [preferência por GitHub padrão](Decisao_V9_GitHub_Standard_100k_Otimizacao_20261109.md); **não revoga** GOV-DOC-01, #1253, a política de CI por SHA, a separação TRAIN/TEST e o LOCK científico. Encerrar #1349 somente após prova física efetiva, auditoria integral e documentação integrada.

**Estado final desta atualização:** determinação documentada em PR; **nenhuma prova 100k V9 realizada**.

## Incidente de validação documental na PR #1382

O [primeiro run do check rápido #42672501553](https://github.com/lucianox777/Jornada/actions/runs/42672501553) terminou **FAIL**, com **zero jobs reportados** pela API do GitHub. Uma tentativa de `rerun_failed_jobs` foi recusada pelo GitHub (HTTP 403, *workflow run cannot be retried*). Não há execução V9 100k associada. O registro deste incidente é documental e a atualização de HEAD requer novo check; **não fazer merge com gate vermelho nem inferir causa não observada**.


## Adenda — diagnóstico verificado de CI e segurança (09/11/2026, corte UTC 18h20)

**PR documental #1382 sem testes executados:** os runs rápidos [42672501553](https://github.com/lucianox777/Jornada/actions/runs/42672501553) (HEAD `7aa9c9aaccf1ba01238439fc0ac3ae760554b570`) e [42672884700](https://github.com/lucianox777/Jornada/actions/runs/42672884700) (HEAD `9e37f994e59d83c3d327c4f97b9d7296d36523f4`) terminaram **FAILURE com `total_count=0` jobs**, conforme API `/actions/runs/{id}/jobs?per_page=100`. O segundo não contém qualquer step, log de execução ou artifact; portanto **não há prova de falha de teste de código nem PASS**. O GitHub recusou `rerun_failed_jobs` do primeiro (403: *This workflow run cannot be retried*). Causa-raiz da falha anterior à criação de jobs **INDETERMINADA**; não alterar políticas, exigir inspeção da página/ações/configurações da conta e novo run de checks realmente executados.

**Master CI no mesmo corte:** [run 42669297610](https://github.com/lucianox777/Jornada/actions/runs/42669297610), SHA `f5fe37e21eceafb59c78d6822adedf6413793b13`, informa `conclusion=failure`; API de jobs reportou **17 jobs** (15 `success`, 2 `skipped`, nenhum `failure` individual). Essa divergência exige diagnóstico de orquestração/resultado agregado **sem reinterpretar workflow vermelho como verde**. O [probe 30k no mesmo SHA](https://github.com/lucianox777/Jornada/actions/runs/42669297803) passou, mas não cobre 100k V9.

**Segurança:** inspeção estática do script `Solution/scripts/v9-tf-start-disposable-sql.sh` confirma para o caminho 100k geração de senha aleatória e máscara antes do Docker, arquivo privado `0600` com `--env-file`, limpeza `trap` e SQL em loopback. **Isso não prova saneamento histórico:** o [run 41535062165](https://github.com/lucianox777/Jornada/actions/runs/41535062165) permanece consultável, com exposição registrada na #1253. Foram identificados ainda serviços SQL via `services:` na `ci.yml` (múltiplos jobs), `dt10-evidence.yml` e `schema-consolidation-370.yml`; o GitHub prepara `services` antes de steps, exigindo auditoria própria de máscara, retenção e não reutilização de credenciais. **Achado de risco potencial, não alegação de vazamento novo.**

**Encaminhamento fail-closed:** finalizar segurança #1253 com comprovação de descarte/não reutilização e decisão autorizada de retenção/expurgo dos logs, reconciliar resultado de CI, obter check de PR realmente executado e somente então autorizar o disparo manual owner-only com SHA master atual. Nenhum segredo, corpus, TEST, Gold ou modelo foi publicado/ativado. **#1349 permanece NÃO COBERTO**. GOV-DOC-01.

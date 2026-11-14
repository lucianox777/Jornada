# GOV-V9-TF4-324-TIMEOUT-FISICO-340-20261113 — ampliar exclusivamente a etapa física TRAIN

**Autoridade:** Product Owner, 13/11/2026, horário de Brasília. **Estado:** APROVADO pelo PO após falha observada, implementação condicionada a CI rápida e merge. **Política:** GOV-DOC-01.

## Contexto, incidente e justificativa

A campanha [#43499315560](https://github.com/lucianox777/Jornada/actions/runs/43499315560), SHA `18bfe33050a0dbba81c9e514fdebd895f350d2e5`, concluiu **308/324 hipóteses (95,1%)** e falhou no passo `Run physical V9 CSharp on 324 V3-only four-name hypotheses` porque o GitHub Actions impôs **315 minutos exatos** de timeout da etapa. A auditoria científica e a VALIDATION independente não ocorreram, e a campanha **não é SUCCESS**, nem validação completa. À hipótese 308, o log operacional estimava ~16,3 min adicionais; a projeção de conclusão próxima a 331 min não era uma garantia.

O PO confirmou que prefere **cancelar a próxima tentativa #43523989957**, já iniciada na versão com progresso simplificado, e **preparar nova configuração com 340 minutos** para não arriscar outro timeout de 315 min. O cancelamento da execução pertence ao usuário/GitHub; uma solicitação de cancelamento não equivale a `completed/cancelled` até estado terminal ser observado. A campanha antiga não herda alterações novas; o código deve ser integrado antes de iniciar uma nova campanha manual.

A versão original de 324/324 sem progresso individual concluiu em ~234 min no run #43105369099. A diferença para campanhas com instrumentação individual é observacional e não comprova causalidade; a próxima campanha deve medir a duração com o formato minimalista, sem atribuir ganho antecipado.

## Decisão operacional vigente

**Alterar somente o limite hard do passo físico TRAIN de `timeout-minutes: 315` para `timeout-minutes: 340`.** O **job TRAIN inteiro permanece `timeout-minutes: 360`**, conforme teto aplicável ao runner GitHub-hosted. Os jobs independentes **VALIDATION e TEST continuam com timeout de 360 min cada**, e os seus passos internos de 330 min permanecem. Não criar runner self-hosted, repartição de hipótese, subjob de auditoria ou alterações no método científico nesta intervenção.

Sincronizar o prazo informativo do console de cada hipótese em `V9NestedCalibrationCoordinator.cs`: o relógio UTC de início do passo `V9_PHYSICAL_STEP_START_UTC` continua sendo a referência operacional aproximada, mas a data limite informada passa a ser `início + 340 min`, e **não** `+315`. Na ausência de variável de ambiente, manter fallback a `UTC atual + 340 min` para uso fora do workflow. O GitHub impõe o limite autoritativo, que começa antes de eventuais trechos do wrapper ou C#; não apresentar margem como prazo garantido para todo o job.

Manter os sete campos multilinha por hipótese (`[V9 PROGRESS]`, timestamp, SHA, AVALIADA/REJEITADA, restante, limite e status), uma única escrita, um `lock` e um `Flush`. `replay_timing_diagnostics` continua **false** por padrão; preservar métricas científicas P95, SQL, TRAIN, fronteira FDR/CP, contabilidade 324/324, evidências, regras de segurança e handoff auditável para VALIDATION; `test_required=false`, `prepare_sealed_test=false`, certificado TEST vazio para a campanha TRAIN/VALIDATION atual. `disable_python_sql_budget=true` permanece o padrão experimental explícito (sem alterar as proteções restantes).

## Risco, precedência e verificação

**Folga residual:** 340 min de etapa física dentro de 360 min totais do TRAIN deixa **no máximo 20 minutos de tempo de job** para preparação, auditoria, handoff, upload e limpeza. Essa folga inclui o tempo **antes e depois** do passo; logo não é uma reserva inteira pós-calibração. Qualquer demora nessas fases pode disparar o timeout do job mesmo que o passo físico conclua. O ajuste amplia até 25 min o tempo permitido da etapa, mas **não garante sucesso**; se a etapa demorar mais de 340 min ou o restante exceder 360 min, deve falhar sem contornar gates.

**Precedência:** substitui **exclusivamente** o limite de 315 min e a correspondente data limite operacional da [GOV-V9-324-TIMEOUT-FISICO-315-20261113](Decisao_GOV_V9_324_Timeout_Fisico_315_20261113.md). As decisões de uma mensagem multilinha por hipótese, jobs independentes TRAIN/VALIDATION/TEST, TEST selado e política de validação científica permanecem vigentes nos demais aspectos. Não executar nem expor TEST para simplificar a campanha. A regra de manual `workflow_dispatch`, actor proprietário, SHA igual ao `master`, ambiente DEV e preflight de segurança não é flexibilizada.

**Aceite:** atualizar o YAML do passo TRAIN, o coordenador e todos os testes de contrato em uma PR; exigir **`jornada-pr-required-fast` 3/3 sucesso**, incluindo compilação Release, testes de timeout e segurança; integrar por squash à `master` e obter SHA exato; fornecer ao PO link do workflow manual e os **valores ativos/desativados**. Não iniciar a campanha manual em nome do proprietário. A CI completa `jornada-ci` continua separada da execução manual 100k, sem ser substituída ou declarada automaticamente aprovada no novo SHA.

**Rastreabilidade:** arquivos `.github/workflows/v9-v3-four-name-100k.yml`, `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`, testes `test_v9_tf4_three_job_timeouts.py`, `test_v9_100k_frozen_ibge_admission.py`, `test_v9_v3_four_name_100k.py`, `test_v9_calibrator_safe_failure_stage.py`, `Solution/docs/Indice_Decisoes_Vigentes.md`. Histórico dos runs #43499315560, #43523989957; nova prova física ainda pendente. PR e SHA de merge serão vinculados ao final da integração.

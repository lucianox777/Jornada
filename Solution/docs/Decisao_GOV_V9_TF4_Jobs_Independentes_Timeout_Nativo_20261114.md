# GOV-V9-TF4-JOBS-INDEPENDENTES-TIMEOUT-NATIVO-20261114

**Autoridade:** Product Owner, 14/11/2026, diretriz explícita: “Separe a calibração em auditorim jobs independentes e retire esses limites, porque o Git já faz. Se houver problema e não conseguir ser executado, a gente tem que verificar o Git, ver em que sessão deu problema e fazer otimizações. Continue com essa decisão.”

**Estado:** APROVADO pelo PO; implementação proposta na [PR #1526](https://github.com/lucianox777/Jornada/pull/1526), branch `feat/v9-physical-audit-separated-jobs-github-timeout-20261114`; integração, CI final e execução científica física ainda **não atestadas** neste registro. **Política:** GOV-DOC-01.

## Contexto, justificativa e problema corrigido

A campanha manual [#43551194008](https://github.com/lucianox777/Jornada/actions/runs/43551194008), SHA `8ce35420fbb015fb281d8edcbc99213252aac183`, concluiu 324/324 TRAIN e 3 finalistas, mas a VALIDATION e o TEST foram SKIPPED. O defeito de herança da condição `success()` da VALIDATION, quando `prepare-test-100k` opcional era SKIPPED, foi corrigido separadamente e integrado via [PR #1525](https://github.com/lucianox777/Jornada/pull/1525), squash `398f820945cc81730bee74030af5f68636bf43cd`, com CI 3/3 [#43613590000](https://github.com/lucianox777/Jornada/actions/runs/43613590000) SUCCESS.

Antes desta decisão, o job TRAIN continha *calibração, auditoria e handoff* e possuía `timeout-minutes: 360`; a etapa física possuía `timeout-minutes: 340`. VALIDATION e TEST tinham cada um job `360` e etapa principal `330`; preparação TEST `60`. O código C# usava `AddMinutes(340)` para estimar um prazo que não era o relógio de início real do job, criando uma margem de forecast equivocada. **O PO decidiu remover esses timeouts explícitos deste workflow**, confiar no limite nativo do GitHub-hosted runner e investigar o step/log efetivamente responsável por futuras falhas em vez de impor tetos artificiais.

## Decisão aprovada e desenho implementado na branch

1. **Jobs independentes e sequenciais:** `prepare-test-100k` (opcional, sempre segregado) → `physical-100k` (**somente geração/SQL/calibração das 324 hipóteses e produção de evidências**) → `audit-100k` (**novo job independente, execução do mesmo auditor Python e handoff congelado**) → `validation-100k` (**holdout independente**) → `test-100k` (**TEST único, só se todos os gates científicos e de segurança forem satisfeitos**).
2. **Sem `timeout-minutes` explícito** nos jobs nem nos steps deste workflow. Limites nativos e interrupção são de responsabilidade do GitHub Actions/runner. Isso não cria horas ilimitadas: o job físico ainda precisa **finalizar a serialização e o upload do artefato** antes do limite nativo do próprio job, senão a auditoria não pode começar.
3. **Transferência de auditoria, não de aprovação:** o job físico prepara e publica artefato `v9-tf4-calibration-preaudit-<run>-<attempt>` com allowlist de `plan.json`, `binding-receipt.json`, `mu-statistical-analysis.json`, `runtime-diagnostics.json`, `selection-reference.json`, envelope congelado e até 3 fitted snapshots; manifesto SHA-256 exato, código SHA, run, 324/81 e flags TRAIN-only. Nunca transferir coorte TRAIN, CSV, banco SQL, corpus TEST, chave, credencial, PII ou arquivos não permitidos. Este artefato **não é científico/auditado** e não habilita VALIDATION.
4. **Job AUDIT fail-closed:** condição positiva de `physical-100k.result == success`, owner, preflight, `always() && !cancelled()` para tolerar ancestral TEST-prep SKIPPED, SHA/ref/attempt=1 idênticos, *download* e verificação independente byte a byte antes de aceitar qualquer dado. Executa o script científico original `v9-v3-four-name-100k-audit.py`, sem alterar seus cálculos, políticas FDR/CP/P95, 324/81 ou seleção. Só com auditoria PASS e finalistas existentes publica o handoff originalmente usado pela VALIDATION, que volta a verificar manifesto, auditoria e hashes.
5. **VALIDATION depende de `audit-100k`**, não do TRAIN físico diretamente, e lê `needs.audit-100k.outputs.has_finalists`. Sem finalistas: auditoria ainda documenta o resultado técnico; VALIDATION fica SKIPPED; não presumir sucesso científico.
6. **Remover deadline artificial do progresso C#:** o `[V9 PROGRESS]` continua uma linha por hipótese e mantém ETA da vazão física observada, mas informa `limite=GITHUB_JOB_NATIVO`, `margem_prevista_min=NAO_APLICAVEL` e status informativo, sem `340`, sem calcular risco de uma margem fictícia e sem interromper hipóteses. O relógio físico UTC é diagnóstico, **não timeout**.
7. **Diagnóstico de falhas:** verificar separadamente `prepare-test-100k`, `physical-100k`, o step de calibração, o step de preparo/upload do artefato, `audit-100k`, `validation-100k`, `test-100k` e seus logs; falha, SKIPPED, timeout nativo, artefato ausente, SHA divergente e resultado científico inelegível são estados distintos. Corrigir gargalo/problema real com otimização rastreável antes de reexecutar. Nenhum `skipped` deve ser contado como PASS.

## Garantias não alteradas

- A campanha permanece **manual em master SHA exato**, owner-only e preflight positivo; não executar automaticamente 100k a partir de PR.
- Preservar **324 hipóteses V3 TF4, 81 células, FDR/CP/P95, SQL, parâmetros e paralelismo AUTO**. Auditoria continua a mesma rotina científica; só muda o runner em que ocorre e a transferência de dados.
- Todos os gates de TEST permanecem **fail-closed**: `test_required=true` ainda exige paridade operacional total `ProbabilisticLinkageBatchRunner.RunAsync`, segredo/certificado, corpus CMS lacrado, reserva global one-use, SHA do vencedor e a governança das [issues #1488](https://github.com/lucianox777/Jornada/issues/1488) e [#1478](https://github.com/lucianox777/Jornada/issues/1478). **Esta decisão não autoriza remover o guard `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` nem consumir TEST.**
- O `V9_PREAUDIT` não transfere base ou gabarito; apenas metadados e arquivos científicos imutáveis já transferíveis mediante auditoria. Falhas devem abortar dependentes, não pular a auditoria.

## Impactos e riscos explicitamente aceitos

**Benefícios:** não desperdiçar o relógio de 6 horas do job TRAIN com auditoria posterior; auditoria tem seu próprio prazo nativo; remover a interrupção de 340 minutos e o ETA enganoso; cada job/step evidencia onde houve falha.

**Custos:** nova fronteira de artefato (upload/download, potencialmente centenas de MB), maior armazenamento e tempo; o TRAIN ainda deve conseguir publicar seu artefato dentro do prazo nativo. Se o GitHub encerrar o processo antes do upload, não há handoff íntegro nem auditoria, exigindo otimização real ou checkpoint futuro sujeito à decisão específica. O TEST continua sujeito ao risco de coorte one-use quando eventualmente autorizado: nenhum rerun automático de TEST, sem exposição antecipada. A retirada de timeouts GitHub **não** remove timeouts técnicos de SQL/IO e proteção de recursos de outros componentes; esta decisão não altera tais mecanismos.

## Precedência, substituição e rastreabilidade

**Substitui operacionalmente** `GOV-V9-TF4-324-TIMEOUT-FISICO-340-20261113` apenas na regra de limite explícito do workflow; mantém intactos os fatos históricos e evidências da execução anterior. **Substitui a semântica de deadline e risco** da `GOV-V9-PROGRESS-PREVISAO-RISCO-TIMEOUT-20261114`, mantendo estimativa de ETA como diagnóstico e inalterando ciência. **Preserva** GOV-V9-TF4 jobs independentes e GOV-V9-TEST-Paridade Formato/RunAsync; GOV-DOC-01.

**Aceite técnico:** (a) nenhuma linha `timeout-minutes` no workflow manual; (b) quarto job audit independente e `needs` correto; (c) teste do manifesto contra alterações/extra files/erro de SHA e fluxo sem finalistas; (d) testes de contrato e compilação C#; (e) PR com CI 3/3 e integração; (f) **execução real física** posterior para observar job AUDIT e VALIDATION; nenhum run de PR substitui essa prova.

**Observação de estado:** nenhuma nova campanha foi iniciada por esta implementação; a execução completa TEST exige gates de produção/one-use independentes. Atualizar o recibo PR/CI/merge nesta decisão e nos índices quando evidenciados.

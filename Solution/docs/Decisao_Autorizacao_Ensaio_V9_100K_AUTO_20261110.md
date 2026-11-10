# GOV-V9-100K-AUTO-RUN-20261110 — retomada autorizada de calibração V9 100k

**Autoridade:** Product Owner em 10/11/2026 (horário local de São Paulo). **Política:** GOV-DOC-01. **Estado:** solicitação de execução aprovada; **execução física ainda não comprovada**. Sem certificação, TEST, Gold ou ativação.

## Contexto, decisão e precedência

O PO autorizou expressamente **retomar agora** a calibração técnica V9 completa de **100.000 pessoas sintéticas** com os parâmetros aprovados e implementados na [PR #1396](https://github.com/lucianox777/Jornada/pull/1396). Esta decisão **supera apenas a suspensão operacional** de 100k registrada anteriormente em `GOV-V9-SQL-BOOTSTRAP-COST-FIRST-20261109`. Os respectivos requisitos técnicos, estatísticos e de segurança permanecem integralmente vigentes.

A campanha aprovada deve seguir o workflow `.github/workflows/v9-multiparameter-100k-technical.yml` **exclusivamente via `workflow_dispatch`**, após merge da PR #1396 em master e comprovação dos checks no SHA exato. Configuração:

- `expected_sha` = **SHA de 40 caracteres da master já integrada**, não HEAD da PR, não `latest` flutuante, não branch experimental.
- `disable_python_sql_budget=true` (**padrão**): não calcular nem impor teto de memória SQL pelo Python; o script `v9-tf-start-disposable-sql.sh` aplica admissão Bash de RAM/cgroup/disco e levanta SQL descartável de uso único com credencial mascarada. `false` é a alternativa explícita, em que o estimador Python roda **dentro do próprio bootstrap SQL**, antes do Docker, com recibo de memória e aplicação conjunta.
- `Calibration.MaximumParallelRuleSets=0` (**AUTO**) no binding operacional do plano técnico congelado, governador C# completo, pilotagem e admissão por CPU/RAM/disco/SQL. Não utilizar V9-Lite.
- Grupos físicos SQL da calibração aninhada em ordem de amplitude estrutural estimada decrescente (a consulta possivelmente mais pesada primeiro). **Não é medição de custo SQL real**.
- Corpus limpo de 100.000 pessoas sintéticas, seed42, plano histórico J vinculado ao corpus sem mudar hipóteses científicas; **24 avaliações físicas oficiais, 16 testemunhas/diagnósticos e 8 candidatas CP**, TF, M/U, V2/V3, FDR e independência TRAIN preservados; nenhum TEST/Gold operacional.
- Logs integrais **somente em desenvolvimento local privado**; GitHub Actions mantém códigos sanitizados e evidências agregadas sem segredos/corpus cru.

## Pré-condições de execução, segurança e recibos

A solicitação **não equivale** a afirmar `security_preflight_approved=true`. O proprietário deve confirmar de verdade as salvaguardas do workflow — conta/ator autorizado, SQL efêmero isolado, senha nova mascarada antes do container, credenciais históricas da [issue #1253](https://github.com/lucianox777/Jornada/issues/1253) não reutilizadas, limites da infraestrutura e artefatos sem segredos. A #1253 continua aberta e não será falsamente declarada saneada. Não remover o gate ou criar um gatilho por `push` para contornar `workflow_dispatch`.

Se o conector GitHub disponível não expuser `workflow_dispatch`, **não é permitido simular aprovação**, reutilizar runs de outro SHA ou alterar o workflow para execução automática. Nesse caso o estado permanece **AUTORIZADA, AGUARDANDO DISPARO MANUAL PELO PROPRIETÁRIO**. Isso não é execução concluída; registrar run ID, SHA/ref/evento e artifact depois da execução real.

**Critérios de resultado:** recibos `capacity-before-sql.json`, `capacity-before-calibration.json`, `bound/binding-receipt.json`, `mu-statistical-analysis.json`, `runtime-diagnostics.json`, `calibration-runtime-scheduling.json`, `technical-audit.json` e eventuais `interrupted.json` com status, SHA e plano. Reportar tempo, RSS/memória, paralelismo observado, contagem física 24/16/8, TP/FP/FN, FDR, condições de falha e divergências de hashes. **Não presumir PASS antes dos artefatos.**

## Riscos, impacto, precedência e histórico

A política padrão SQL sem cap Python pode deixar o SQL consumir RAM e reduzir o paralelismo efetivo; controlador C# continua com admissões e eventual fail-closed. Priorizar por amplitude estrutural pode **não** escolher a consulta real mais demorada; o teste 100k deverá medir, não assumir benefício. A retomada é apenas uma decisão de execução técnica TRAIN, não de homologação nem ativação.

**Precedência:** revoga a marca de **pausa** de 100k de `GOV-V9-SQL-BOOTSTRAP-COST-FIRST-20261109` mas **não** os seus requisitos; mantém `GOV-V9-100K-SQL-PY-OPTOUT-20261109`, GOV-DOC-01, governança de segredo e ci/30k auto por impacto na master. **Rastreabilidade:** PR #1396, merge no master (a registrar), workflow manual, eventual run e artifact. Decisão anterior de pausa permanece histórica.

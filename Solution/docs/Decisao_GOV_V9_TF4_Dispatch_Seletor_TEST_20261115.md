# GOV-V9-TF4-DISPATCH-SELETOR-TEST-20261115 — novo disparo manual com opção explícita de TEST

**Autoridade e data:** Product Owner, 15/11/2026 (America/Sao_Paulo): «um novo workflow pra eu executar, inclusive com a opção dos testes». **Estado:** DECIDIDO e IMPLEMENTADO/INTEGRADO pela PR #1554 (squash `7de4f3d5966ab0ccf6fb9801d748893667949d0e`), CI rápida 3/3 e smoke cross-job sintético aprovados; registro do novo workflow na `master` confirmado, execução física V9/100k e TEST operacional ainda NÃO REALIZADOS. **Regra:** GOV-DOC-01.

## Contexto e motivação

O fluxo físico canônico `.github/workflows/v9-v3-four-name-100k.yml` já continha uma opção booleana `test_required` e uma variável `test_database`, mas a seleção não apresentava claramente quais alternativas de TEST estão efetivamente disponíveis. O PO solicita um **novo formulário de execução manual** sem repetir o enorme pipeline científico de 324 hipóteses nem confundir TEST científico sintético (bloqueado por falta de paridade operacional completa) com diagnóstico em banco já existente.

O pedido é operacional: disponibilizar um novo workflow selecionável na aba Actions, **sem mudar a semântica científica e sem dispensar testes de código/guardas que o fluxo canônico já executa**.

## Decisão técnica/operacional

1. Adicionar `.github/workflows/v9-tf4-execucao-testes.yml` com apenas `workflow_dispatch`, bloqueio por proprietário, `expected_sha` **idêntico ao commit selecionado da `master`**, `security_preflight_approved=true` e veto a `rerun` por `GITHUB_RUN_ATTEMPT != 1`. Executar um job de admissão que valida os parâmetros **antes** de iniciar a campanha 100k.
2. Apresentar `test_mode` como seletor fechado de **duas opções atualmente executáveis**:
   - `sem_test` (padrão): executa TRAIN físico 100k com **324 hipóteses TF4**, AUDIT independente, VALIDATION independente e closeout E2E; o TEST final é omitido. Os testes unitários/contratuais de pré-voo do pipeline original continuam obrigatórios.
   - `banco_existente_readonly`: exige `test_database` nomeado e já existente, preflight SQL SELECT-only antes do TRAIN, `secrets.V9_EXTERNAL_TEST_READONLY_CONNECTION` e runner **privado** com etiqueta `v9-private-readonly`. Após TRAIN→AUDIT→VALIDATION, aplica somente o modelo vencedor congelado aos registros existentes, sem gerar corpus sintético, escrever SQL, publicar/ativar ou alegar TEST científico (sem ground truth, TP/FP/FDR).
3. O TEST científico sintético independente 100k **não é opção executável do novo seletor**, pois continua **bloqueado** por `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` até comprovação exigida nas issues #1488/#1478. Não liberar essa opção por UI, renomeação, bypass ou flag; o workflow canônico preserva o bloqueio.
4. Expor o pipeline canônico como `workflow_call` adicional ao seu `workflow_dispatch` atual. O novo workflow apenas invoca esse reutilizável com as entradas existentes `test_required`, `test_database`, `expected_sha`, `security_preflight_approved` e `disable_python_sql_budget`, herdando segredos para os **jobs já isolados**. Manter as validações internas de owner/SHA/master/one-use, cadeia, artefatos e os timeouts **nativos do GitHub**. O limite de permissão `contents: write` no job de chamada é necessário apenas para o caminho preexistente de reserva da tag one-use do TEST científico; o pipeline canônico conserva `contents: read` por padrão e restringe o write ao job científico. O novo seletor não habilita esse caminho.
5. A opção `disable_python_sql_budget` continua configurável e mantém o default canônico `true`. Alterá-la não modifica TF4, FDR, P95, M/U, bloqueio, seed, corpus, plano, avaliação do TEST ou artefatos congelados.

## Por que não duplicar o workflow

Copiar mais de mil linhas de geração, SQL, fitting, pré-auditoria, auditoria, transferência de finalistas, validação, TEST e closeout criaria divergência e uma segunda superfície de autorização. O `workflow_call` é deliberadamente um **adaptador de formulário**, não novo algoritmo nem campanha de testes paralela. A entrada manual original continua disponível e não é substituída.

## Impacto, riscos e critérios de verificação

- **Invariantes:** 81 opções TF4 × duas misturas × duas margens = 324 hipóteses V3; plan/seed/IBGE pin original; CPU/RAM/SQL/disco e segurança; tratamento de erros por registro/hipótese; SHA/allowlist; TRAIN→AUDIT→VALIDATION; TEST opcional apenas no caminho autorizado; auditoria e status de conclusão fail-closed. Nenhum novo job físico fora do pipeline canônico.
- **Riscos operacionais:** a opção de DB existente ficará indisponível sem runner privado e segredo SELECT-only; executar 324 hipóteses pode consumir toda a janela nativa de job; `workflow_call` precisa de verificação sintática e de permissões/segredos na CI antes do merge; o uso de `contents: write` no chamador é teto delegado, não autorização para publicar modelos. A validação física requer disparo real posterior na master.
- **Aceite:** testes focados do novo formulário e testes canônicos devem passar; CI obrigatória na PR (`security-guards`, `unit-runtime`, `deterministic-build`); merge por processo governado; verificar aparecimento do novo workflow em Actions/master. **Não** declarar campanha 100k executada nem TEST científico certificado por mero merge.

## Utilização esperada após integração na master

Em Actions, selecionar **V9 TF4 100k - Execucao com opcao de TEST** → **Run workflow** → branch `master`. Informar `expected_sha` (SHA integral da master exibida), marcar conscientemente o preflight de segurança e escolher `test_mode`. Deixar `test_database` **vazio** no modo `sem_test`; no modo `banco_existente_readonly`, indicar somente um banco SQL existente admitido e confirmar o runner/segredo privado. Manter `disable_python_sql_budget=true` salvo escolha experimental explícita. Não reutilizar tentativa de TEST ou criar workflow alternativo para contornar bloqueios.

## Precedência e rastreabilidade

**Complementa**, sem revogar, [GOV-V9-TEST-DATABASE-EXISTENTE-OPCIONAL-20261114](Decisao_GOV_V9_TEST_Database_Existente_Opcional_20261114.md), [GOV-V9-CALIBRADOR-LOG-MINIMO-20261115](Decisao_GOV_V9_Calibrador_Log_Minimo_20261115.md), [GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114](Decisao_GOV_V9_Cadeia_Obrigatoria_E2E_Preflight_20261114.md) e a política GOV-DOC-01. Mantém os bloqueios de paridade #1488/#1478 e o TEST científico global one-use. **Arquivos de mudança:** `.github/workflows/v9-tf4-execucao-testes.yml`, `.github/workflows/v9-v3-four-name-100k.yml`, `Solution/tests/scripts/test_v9_tf4_dispatch_test_modes.py`, este documento e os dois índices. Registrar PR, SHA e evidências CI após obtenção; antes disso, status de implementação física é **NÃO VERIFICADO**.

## Recibo de integração — 15/11/2026

- **PR:** [#1554](https://github.com/lucianox777/Jornada/pull/1554), branch `feat/v9-tf4-dispatch-test-mode-20261115`, head `8840e314dcc63318d4d947141e6f91f95c22f46c`, merge por squash na master `7de4f3d5966ab0ccf6fb9801d748893667949d0e`.
- **CI obrigatória:** [jornada-pr-required-fast #43735403883](https://github.com/lucianox777/Jornada/actions/runs/43735403883): `security-guards`, `unit-runtime`, `deterministic-build` **SUCCESS (3/3)**.
- **Smoke de integração entre jobs (sem 100k físico):** [#43735403970](https://github.com/lucianox777/Jornada/actions/runs/43735403970) **SUCCESS**, com TRAIN/AUDIT/VALIDATION/closeout por fixtures e TEST opcional `skipped`.
- **Workflow versionado na master:** [v9-tf4-execucao-testes.yml](../../.github/workflows/v9-tf4-execucao-testes.yml) verificado por leitura da árvore mestre. A disponibilização/execução real em Actions e as escolhas do formulário exigem confirmação no próprio serviço no próximo disparo.
- **Limites da prova:** CI e merge NÃO mediram 324×100k, não exercitaram TEST com banco existente e não desbloquearam TEST científico sintético. Manter #1488/#1478 e o status de prova física pendente. GOV-DOC-01.

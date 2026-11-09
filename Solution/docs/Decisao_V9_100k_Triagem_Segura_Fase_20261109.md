# GOV-V9-100K-TRIAGEM-FASE-20261109 — localização segura do INVALID_DATA físico

**Data:** 09/11/2026. **Mandato:** continuidade da investigação técnica do V9 100k, sob #1349 e #1386. **Autoridade:** PO — aprovação permanente de implementação rastreada; **nenhuma mudança científica nova autorizada**. **Governança:** GOV-DOC-01. **Situação:** proposta de instrumentação em PR isolada, ainda não integrada.

## Fatos e motivação

O [workflow manual 100k #42678462930](https://github.com/lucianox777/Jornada/actions/runs/42678462930), no SHA `dc3db33dffd6ddba6f65a57e1bfc07f4b7dc65b9`, construiu o Calibrador, gerou o corpus seed42 100k, vinculou o `plan.json` oficial com 24 hipóteses, aprovou dois gates de recursos reais e iniciou SQL descartável. Na execução de `--v9-calibrate`, encerrou **FAILURE** com `V9_CALIBRATOR_FAILED:INVALID_DATA` (33s). A evidência agregada artifact #13700734282 contém plano, binding e orçamento externo, mas não o relatório de fitting nem `interrupted.json`. **Não se demonstrou OOM, e 24×100k NÃO PASSOU**.

O contrato anterior de proteção de mensagens livres/segredos é correto e não deve ser afrouxado. O código de erro genérico, contudo, não permite distinguir em qual subsistema uma entrada ou orçamento foi recusado. Repetir campanha manual sem essa evidência desperdiça recursos e não identifica a causa. A remediação adequada é um **código de etapa finito e estático**, não a impressão de exceção, SQL, token, senha SA, caminho privado ou amostra de pessoas.

## Implementação instrumental

- O `--v9-calibrate` possui um estágio de diagnóstico iniciado em `CLI_ARGUMENTS_AND_PLAN`, avançando por `PLAN_VALIDATED_AND_OUTPUT_ADMISSION`, `TRAIN_HASH_AND_MANIFEST`, `TRAIN_CSV_LOAD`, `TRAIN_SPLIT_AND_PHYSICAL_SQL_FIT`, `MU_VARIANT_EXPANSION`, `TRAIN_SPLIT`, `DECLARED_REQUESTS_AND_CP`, `PHYSICAL_SQL_BATCHES` e `FIT_EVIDENCE_EXPORT`.
- Essas fases são **constantes fixas no código**, atualizadas antes do processamento correspondente. No erro o `interrupted.json` já existente passa a carregar o `safeFailureStage` e o **tipo** de exceção; o stderr emite `V9_CALIBRATOR_SAFE_STAGE:<FASE_FIXA>` sem mensagem. O arquivo de reserva protegida `last-error` continua excluído de artifacts.
- O workflow 100k, ainda `workflow_dispatch`, owner-only e com preflight explícito, acrescenta somente `results/interrupted.json` ao artifact **agregado** em `if: always()`. Não baixa, reexecuta ou exporta massa sintética, CSV, TEST ou credenciais em logs.
- Gate rápido de testes de contrato verifica enumeração hardcoded, nenhuma exceção `.Message` / `.ToString()` publicada, upload e proteções manuais. A versão C# e a ciência do ensaio não são modificadas; chamadas sem callback mantêm o comportamento histórico. A instrumentação **não corrige nem determina o erro** sozinha: a fase permitirá diagnóstico verificável da próxima reprodução.
- O novo relatório de recursos de calibração física está isolado na [PR #1385](https://github.com/lucianox777/Jornada/pull/1385); esta decisão tem outra responsabilidade. Dependência operacional: integrar a #1385 primeiro, por SHA exato e após concluir regressões físicas pendentes, depois reconciliar esta PR com a master. Não modificar a master durante processos que revalidam `HEAD` em runtime.

## Riscos, limites, precedência e aceite

1. **Precedência:** mantém política de erro público seguro existente, ausência de exceção livre e proteção de senha #1253; estágio não constitui certificação/autoridade. Não introduz nova classe de erro nem ignora logs confidenciais.
2. **Integridade científica:** não altera o plano oficial, seed42, quantidade 100k, a referência IBGE histórica de 30k, as 24 hipóteses, FDR, TF, M/U, regras de paralelismo, CP, P95, TEST/Gold, DT15 ou ativação.
3. **Prova negativa:** não se deve atribuir `INVALID_DATA` a memória, Docker ou cap de SQL sem evidência; `safeFailureStage` descreve localização aproximada da falha e não presume sua causa.
4. **Segurança:** somente literais fixos e tipo de exceção saem no artifact. Nunca exportar `last-error.log`, `observacoes.csv`, connection strings nem dados de teste. O risco histórico [#1253](https://github.com/lucianox777/Jornada/issues/1253) permanece aberto até auditoria de logs.
5. **Aceite:** testes estáticos e build .NET verdes no HEAD exato; teste negativo de vazamento; merge após #1385 e regressões master concluídas; #1386 rastreia fase/predicado, #1349 fica aberta até prova física 24×100k e auditor.

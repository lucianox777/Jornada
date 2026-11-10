# GOV-REV-V9-LITE-20261109 — retirada da V9-Lite da master

**Autoridade:** Product Owner, 09/11/2026. **Política:** GOV-DOC-01. **Estado:** retirada aprovada pelo PO; integração deve ocorrer exclusivamente por PR de reversão, com gates. **Precedência:** esta decisão REVOGA a autorização de manter o executor V9-Lite e os workflows experimentais em master, anteriormente registrada em `EXP-V9-LITE-100K-20261109`. Não apaga nem invalida evidências históricas.

## Contexto e justificativa

A V9-Lite foi incorporada pela [PR #1391](https://github.com/lucianox777/Jornada/pull/1391), merge `ba5d4b3f0372c210d494c97a88c2c2532fdc30e9`. Ela era uma alternativa serial de 1 trabalhador sem governador C# nem scheduler, com o mesmo núcleo científico V9, e workflows manuais próprios para 30k/100k. O PO esclareceu que a implantação contará com máquina de data center em que é possível aumentar vCPUs; portanto, decidiu **retirar a variante Lite do produto e do repositório ativo**, preservando a V9 governada como arquitetura única. A evidência prévia 1k de paridade científica e economia de RAM com maior tempo na Lite permanece histórica e não equivale a uma certificação 30k/100k. A decisão é de simplificação operacional/arquitetural, não uma afirmação de que a Lite seria cientificamente incorreta.

## Escopo técnico exato da reversão

Partindo da master `ba5d4b3f0372c210d494c97a88c2c2532fdc30e9` (sem alterações posteriores na inspeção anterior):

1. Restaurar aos blobs da master anterior à #1391, SHA `d4b4ac174355f8b9e97675b2342839ceb076b1ef`, **somente**: `.github/workflows/jornada-pr-required-fast.yml`, `Solution/src/Jornada.Linkage.Parameters.Worker/Program.cs`, `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs` e `Solution/tests/scripts/test_v9_worker_public_cli_process_canaries.py`.
**Exceção de segurança obrigatória:** a CI real da reversão (#1394, canário A2) detectou no caminho restaurado da V9 que falhas de aridade do `--v9-calibrate` produziam duas linhas no stderr (`V9_CALIBRATOR_SAFE_STAGE` além do código finito), violando o contrato de stderr seguro. Manter **apenas** a guarda `if (output is not null)` antes de escrever `V9_CALIBRATOR_SAFE_STAGE` em `V9NestedCalibrationCli.cs`. A correção existia na #1391, mas é independente da Lite e não autoriza nem introduz `--v9-lite-benchmark`. Registro da causa: [run 42738061453](https://github.com/lucianox777/Jornada/actions/runs/42738061453). Deve passar o canário real no SHA corrigido.\n\n2. Remover os quatro arquivos introduzidos pela #1391: `.github/workflows/v9-lite-serial-1k-physical-parity.yml`, `.github/workflows/v9-lite-serial-30k-matched-benchmark.yml`, `.github/workflows/v9-lite-serial-100k-experiment.yml` e `Solution/tests/scripts/test_v9_lite_experiment_contract.py`.
3. **Preservar** todos os demais recursos prévios: V9 governada, scheduler C#, orçamento conjunto CPU/RAM/cgroup/SQL/disco, TF, M/U, V2/V3, misturas, 81 células, DT-15, isolamento TRAIN/TEST e o script SQL descartável compartilhado `Solution/scripts/v9-tf-start-disposable-sql.sh` (não foi criado pela #1391 e é usado por outros workflows).
4. Preservar o arquivo histórico `Decisao_V9_Lite_Experimento_Execucao_Serial_100k_20261109.md` e os recibos do GitHub com anotação **REVOGADA — SOMENTE ACERVO**. Não eliminar decisões anteriores sem atualizar o índice.
5. A V9 ativa continua com o único parâmetro `MaximumParallelRuleSets`: `0`=AUTO; `1`=1 trabalhador **com** governador e scheduler; `N` explícito conforme as regras já vigentes. **Não alterar o default legado, plano científico ou métricas.**

## Impactos, segurança e gates

- Retirada da entrada experimental `--v9-lite-benchmark`, da variável de autorização `JORNADA_V9_LITE_EXPERIMENT` como feature e dos benchmarks V9-Lite 1k/30k/100k. **Não modificar nem disparar calibração operacional, Gold, TEST ou ativação de modelos.**
- Preservar o workflow de regressão física V9 30k **já existente** para pushes impactantes da master e seus critérios. Sua eventual execução é independente de benchmark Lite. A [issue #1393](https://github.com/lucianox777/Jornada/issues/1393) monitora evidência dos runs de push, cuja leitura exige fonte distinta da consulta PR-only do conector.
- A [issue histórica de segurança SQL #1253](https://github.com/lucianox777/Jornada/issues/1253) permanece aberta. Não usar a reversão como declaração de saneamento retroativo dos logs.
- Integrar por **PR de reversão**, revisar diff exato, CI e não contornar falhas. Nenhuma instrução envolve `git reset --hard` ou `push --force` em master.
- A restauração dos arquivos ao estado anterior à #1391, **com a exceção estritamente necessária da guarda de stderr seguro**, garante que nenhuma funcionalidade Lite seja carregada como efeito colateral; **nenhum commit posterior à #1391 existia** na master durante a análise inicial. Verificar novamente antes do merge.

## Precedência e rastreabilidade

**Decisão revogada:** `EXP-V9-LITE-100K-20261109`; **decisão vigente:** `GOV-REV-V9-LITE-20261109`. Preservadas as decisões `GOV-V9-RECURSOS-CONJUNTOS-20261109` e `GOV-V9-CALIBRADOR-RECURSOS-INTERNOS-20261109` e demais decisões científicas V9. Referências: [PR #1391](https://github.com/lucianox777/Jornada/pull/1391), [merge de origem](https://github.com/lucianox777/Jornada/commit/ba5d4b3f0372c210d494c97a88c2c2532fdc30e9), [arquivo do experimento](Decisao_V9_Lite_Experimento_Execucao_Serial_100k_20261109.md) e PR de reversão. O histórico das conversas não substitui estes arquivos.

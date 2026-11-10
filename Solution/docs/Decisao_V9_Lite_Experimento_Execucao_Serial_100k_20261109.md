# EXP-V9-LITE-100K-20261109 — executor serial experimental sem orçamento Python de memória

**Autoridade:** Product Owner, 09/11/2026. **Política:** GOV-DOC-01. **Estado:** proposta experimental aprovada; implementação em branch/PR; `100k` **NÃO EXECUTADO** e nenhum ganho comparativo certificado.

## Decisão, contexto e motivação

O PO autorizou investigar uma arquitetura de execução V9-Lite com **o mesmo motor científico multiparâmetro C# da V9** — V2/V3, TF, M/U, misturas, 81 células, CP/FDR, blocking e seleção — mas **sem usar o Python de orçamento adaptativo de recursos**, nem o governador/scheduler C# no braço experimental. O objetivo é determinar se o custo de governança operacional é justificável para atingir 100 mil identidades sintéticas com menor complexidade; não é aprovação de uma segunda V8 operacional.

O OOM histórico dos 30k (run 42283035832) foi mitigado pela otimização RAM-first da PR #1300, com prova 160/160 no run 42337308236. O ensaio V9 de 100k (run 42678462930) passou os gates externos e falhou `INVALID_DATA`, **sem OOM comprovado**. As PRs #1385/#1388 acrescentaram mecanismos posteriores ainda sem benchmark comparativo com executor simples. O SQL SCALE 100k com motor V8 (run 42534643000) utilizou amostra de treino 10k e não é trabalho multiparâmetro equivalente.

## Implementação experimental isolada

- Nova entrada `--v9-lite-benchmark <plan.json> <observacoes.csv> <diretório novo>`, com **autorização explícita de ensaio** `JORNADA_V9_LITE_EXPERIMENT=EXPLICIT_SYNTHETIC_TRAIN_ONLY`.
- Mantém validação do plano, verificação de manifesto/CSV sintético TRAIN, SQL efêmero `JornadaHarness`, SHA científico, proteção de diretório novo, logs seguros, diagnósticos e ausência de publicação/ativação; não cria novo estimador científico nem altera `plan.json`.
- No modo experimental, executa **um único worker fixo**, sem `V9CalibrationResourceGovernor.Capture` ou `V9CalibrationRuntimeScheduler`. O recibo operacional `calibration-resource-budget.json` identifica claramente `V9_LITE_SERIAL_BENCHMARK_V1`, com `EXPERIMENTAL_NO_ADAPTIVE_GOVERNOR`. A lista `calibration-runtime-scheduling.json` fica vazia — **não alegar que o scheduler mediu trabalhadores**. Permanece telemetria informativa de RSS, disco lógico e tempos.
- Workflow manual separado `v9-lite-serial-100k-experiment.yml`: mesmos 100k sintéticos, seed42, plano oficial 24/16/8, pin de referência IBGE, SHA master exato, proprietário e preflight de segurança obrigatório. Usa SQL descartável com **teto fixo explícito** `MSSQL_MEMORY_LIMIT_MB=2048` e contêiner `--memory 3072m`; o valor não é calculado por `v9-100k-execution-resource-budget.py`. Preserva o script bash existente de inicialização SQL mascarada e `--env-file` protegido. Python **permanece apenas** na preparação/verificação dos planos científicos e nos auditores independentes — não controla orçamento de memória.
- Preflight mínimo em bash verifica pelo menos 10 GiB de RAM disponível e 6 GiB de espaço no workspace antes do SQL. É **admissão pontual**, não garantia contra OOM. Registros nunca reutilizam credenciais, corpus de TEST, Gold, HML/produção ou modelos ativados.
- Workflow original 100k, `--v9-calibrate`, `--v9-adaptive-study`, governadores, planos científicos e índices históricos permanecem intocados fora deste experimento.

## Protocolo de comparação e critérios de aceite

1. Testes estáticos de isolamento, falha sem token experimental, serialismo, flags, preflight/manual-only e ausência de Python de orçamento; build .NET e testes de segurança/ci do HEAD exato.
2. Antes da campanha integral 100k, reproduzir o perfil 30k com o **mesmo plano/corpus/SQL/SHA de ciência**, comparando executor normal vs simplificado em 1 worker, com hashes de seleção, todos os fits, TP/FP/FN, FDR, cobertura, P95, RAM RSS, GC, CPU, pico de arquivos lógicos, disco livre e tempo por fase. Resultados que faltarem são `NÃO MEDIDO`.
3. Executar 100k **apenas após** CI verde, preflight #1253 realmente satisfeito e autorização manual do proprietário; 24 hipóteses completas, 16 testemunhas/diagnósticos, 8 CP elegíveis, sem reduzir população para fazer passar.
4. Se ocorrer `INVALID_DATA`, preservar `interrupted.json` e `safeFailureStage`, não alterar ciência ou reportar OOM sem evidência. Artefatos públicos: apenas agregados, hashes e recibos sem dados por identidade.
5. Só é lícito afirmar superioridade de desempenho mediante comparação pareada e repetida em ambientes equivalentes, com intervalos/variação; custo por hipótese e custo total devem ser separados.

## Riscos, precedência e limites

O serialismo pode diminuir pico de RAM e **aumentar o tempo**. SQL fixo com menos cache pode aumentar I/O/P95. Sem governador, recursos podem se esgotar após a admissão; por isso a via é **estritamente experimental**, não substitui a configuração governada e não permite ativação. Os parâmetros científicos e o digest permanecem a fonte comum de verdade, mas a equivalência física ainda precisa ser provada.

**Precedência:** experimento isolado, complementar — não revoga `GOV-V9-RECURSOS-CONJUNTOS-20261109`, `GOV-V9-CALIBRADOR-RECURSOS-INTERNOS-20261109`, `DC-V9-PAR-DET-20261107` ou a decisão do **Calibrador único** de 14/10/2026. Sem merge antes dos gates exigidos; sem decisão de substituir o motor atual. Rastrear PR/HEAD e novos resultados em adenda posterior, sem converter pré-requisitos em provas realizadas.

## Continuidade da implementação e teste de isolamento (09/11 — PR #1391)

- PR em rascunho [#1391](https://github.com/lucianox777/Jornada/pull/1391), sem merge ou autorização para publicar modelos.
- O primeiro HEAD `d4f2e754...` teve build/.NET e CI rápida verdes, mas o canário de stderr `--v9-calibrate` falhou porque recebeu `V9_CALIBRATOR_SAFE_STAGE` em tentativa de argumento inválido, além do código fixo. Correção faz o diagnóstico de fase ser emitido somente após existir diretório de execução e o novo modo Lite foi incluído no canário. Falha histórica e correção são rastreadas; **não declarar gates do HEAD novo como aprovados até a conclusão das Actions**.
- O teste estático `test_v9_lite_experiment_contract.py` foi conectado à CI rápida de PR, além do preflight do workflow manual.
- Adicionado `v9-lite-serial-1k-physical-parity.yml` acionado em PR, somente corpus técnico sintético de **1.000 identidades**, SQL descartável, mesmo plano de calibração e motor científico para a via governada e a via Lite. Compara SHA de envelope científico, snapshots ajustados e finalistas; registra RSS/tempo com recibo agregado. Não executa TEST/Gold, não autoriza 100k nem demonstra performance equivalente em escala.
- O workflow 100k permanece **manual-only**, proprietário, SHA da master exato, preflight de segurança; até a PR ser integrada validamente, não pode rodar nela como prova da master. O teste 1k tampouco substitui a regressão física 30k pareada.

## Primeiro ensaio físico 1k e correção da auditoria de paridade

**Action:** [42727045172](https://github.com/lucianox777/Jornada/actions/runs/42727045172), SHA `0de0cd595a160684fb74ad22a34b633b558bb065`, **FAILURE na auditoria**, após **ambas as execuções físicas concluírem** no mesmo banco SQL efêmero/corpus sintético/plan. O erro exato: `SCIENTIFIC_PARITY_FAILED:envelopeSha256`; não foi OOM nem falha do fitting.

O recibo agregado [artifact 13722094841](https://github.com/lucianox777/Jornada/actions/runs/42727045172/artifacts/13722094841) registrou V9 governada `totalMilliseconds=50415`, `peakWorkingSetBytes=193167360`, `effectiveParallelism=4`; V9-Lite `totalMilliseconds=56690`, `peakWorkingSetBytes=177463296`, `effectiveParallelism=1`. No único run, houve 8,13% menos pico RSS e 12,45% mais tempo no braço Lite; **não é prova causal, nem benefício de V8/V9 em 100k**. O relatório do teste não contém os dados científicos necessários para concluir se o envelope diferente representa apenas telemetria/progresso ou resultados distintos.

**Correção de método:** o SHA do envelope inteiro não é um digest exclusivamente científico: o envelope contém `result.Progress` e potencialmente P95 de candidatos, sujeitos à execução. A nova auditoria exige igualdade de finalistas e razão de parada, comparação de todas as avaliações pelo `RequestSha256` (métricas, suporte, prior, opções, margens, seleção, paridade SQL), além dos arquivos `.fitted.json` endereçados por SHA; não compara P95 nem progresso como se fossem resultados científicos. **Essa equivalência não está comprovada até novo teste físico.** É proibido relativizar uma divergência de métricas ou fits para obter PASS.

## Segunda prova física 1k — paridade científica PASS, recursos observados

**Action [42728199302](https://github.com/lucianox777/Jornada/actions/runs/42728199302), HEAD `cd7b47c96e1ff382c972b66046a0fd5d9771f655`: SUCCESS.** Artifact oficial [13724290146](https://github.com/lucianox777/Jornada/actions/runs/42728199302/artifacts/13724290146), recibo `parity-receipt.json`: `scienceIdentical=true`, 22 fitted snapshots com nomes SHA iguais, todas as linhas de avaliações comparadas por request SHA (métricas, suporte, prior, SQL pairs, elegibilidade, finalistas), TEST/ativação false. Esta prova é de 1k sintético e não certifica FDR populacional ou escala 100k.

Métricas físicas reportadas no mesmo runner Ubuntu 24.04: V9 governada `44021 ms` / pico RSS `260227072 B` / teto efetivo `4`; V9-Lite `50323 ms` / pico RSS `243974144 B` / worker `1`. Lite registrou ~6,25% menos RSS e ~14,32% mais tempo; as medições são de uma execução, não de um A/B com paralelismo igual, nem prova causal da vantagem do modo simples. Disco de saída lógico amostrado: governada `2865640 B`; Lite `2863338 B`; exclui SQL/temporários externos/disco físico e não comprova economia global.

**Próximo controle implementado na PR:** no teste físico 1k, derivar o mesmo plano experimental fixando **somente** `Calibration.MaximumParallelRuleSets=1`, usado em ambos os braços e verificado pela auditoria como configurado/efetivo 1, com `scienceIdentical` e hashes dos fits obrigatórios. O objetivo é estimar melhor o custo do governador sem confundi-lo com 4×1 workers. Esta alteração no workflow **não constitui execução concluída**: os novos resultados devem ser vinculados ao SHA exato e auditados separadamente antes de permitir comparação 30k.

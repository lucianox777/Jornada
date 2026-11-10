> **PRECEDÊNCIA EXPERIMENTAL CONFIRMADA PELO PO — 09/11/2026:** A comparação **principal** mantém a **V9 normal com paralelismo AUTO e governador** contra a **V9-Lite exclusivamente serial, sem governador**. Os dois braços executam o **mesmo plano científico, mesmo corpus e mesmas 160 avaliações (30k)**; o valor de plano `MaximumParallelRuleSets=0` não concede paralelismo à Lite. A comparação 1×1 é apenas diagnóstico secundário, **não bloqueia** o estudo principal por sua relevância científica, mas os gates de segurança e integridade continuam obrigatórios. Qualquer trecho histórico abaixo que descreva a comparação 1×1 como requisito para a principal 30k fica **subordinado** a esta decisão. A ciência multiparâmetro, TF, M/U, V2/V3, misturas e 81 células permanecem iguais; nenhuma prova 30k ou 100k é presumida.

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

## Terceiro ensaio pareado 1×1 e preparação 30k — acompanhamento

No SHA `c4a58168d365bef37b0565f9531f5fa4c0904858`, [Action 42729028733](https://github.com/lucianox777/Jornada/actions/runs/42729028733) foi iniciada, com **`MaximumParallelRuleSets=1` no mesmo plano para ambos os modos**. Na leitura desta etapa, o job saiu da fila e estava em execução; nenhum PASS de paridade 1×1 ou benchmark de desempenho deve ser inferido antes do recibo de conclusão e da conferência do SHA.

Foi preparado na branch experimental o workflow `.github/workflows/v9-lite-serial-30k-matched-benchmark.yml`, **exclusivamente `workflow_dispatch` na master e pelo proprietário**, com `expected_sha` e autorização de segurança. Ele reutiliza a geração/pin do protocolo físico V3 broad surname seed42: **30k corpus sintético, 8 políticas de suporte V3 × duas hipóteses TF × cinco margens × dois rulesets = 160 avaliações em cada braço**. A versão governada e a V9-Lite usarão literalmente o mesmo `plan.json`, alterado somente para concorrência operacional 1 antes da execução, SQL efêmero com orçamento fixo 2048/3072 MiB, e auditoria de todas as 160 avaliações, hashes de fit e seleção. Não invoca o Python de orçamento adaptativo de RAM; Python de composição científica e auditoria continua permitido.

O benchmark 30k **NÃO FOI EXECUTADO**; não confundir workflow preparado com medição física. Seu futuro uso exige: resultado de paridade 1×1 PASS no SHA exato; CI/segurança e revisão técnica no SHA final; liberação do preflight #1253 aplicável; execução manual autorizada; conferência do artifact. Não altera o gate técnico seed42 da master nem a campanha 100k, não utiliza TEST/Gold e não publica modelos.

A comparação entre dois executores no mesmo host compartilha cache SQL aquecido pelo primeiro; o resultado isolado de duração pode conter efeito de ordem. Para inferências de performance será necessário repetir e alternar a ordem dos braços sob o mesmo plano, com controles de cache/temperatura e de pressão de recursos. O teste já compara os resultados científicos estritamente, mas **ainda não certifica eficiência nem qualidade estatística populacional**.

## Revisão de precedência do protocolo — PO 09/11/2026

**Contexto:** o PO rejeitou transformar o ensaio principal em dois executores seriais ou acrescentar paralelismo à V9-Lite. A pergunta é se a implementação V9 completa, com paralelismo, governador e recursos adaptativos, supera a V9-Lite simplificada serial em RAM, disco, CPU e duração ao realizar **o mesmo trabalho científico**.

**Implementação:** workflow principal `v9-lite-serial-30k-matched-benchmark.yml` agora mantém `Calibration.MaximumParallelRuleSets=0` no único `plan.json` produzido pelo protocolo broad seed42, usado por ambos os comandos; no modo `--v9-calibrate`, 0 passa ao governador AUTO; no `--v9-lite-benchmark`, o modo isolado executa sempre 1 worker (sem scheduler). O auditor exige configuração normal 0 no relatório da V9 e 0 solicitado/1 efetivo no relatório da Lite, mesmas 160 avaliações, métricas e hashes científicos, além dos limites de TEST/Gold e recibo. A comparação 1×1 permanece opcional/diagnóstica e **não é pré-requisito** do benchmark principal.

**Justificativa e limites:** isolar só o custo do governador exigiria paralelismo igual; o objetivo aprovado é **comparar arquiteturas completas**. Os ganhos/perdas não poderão ser atribuídos causalmente a um componente isolado. O efeito da ordem sequencial das execuções sobre cache SQL precisa ser medido em ensaios repetidos/invertidos antes de generalizar tempo ou I/O. Não há autorização de ativação, publicação, uso de dados reais ou comparação 100k sem segurança; o benchmark 30k segue workflow manual-only com SHA da master e aprovação do proprietário. Os resultados do 1k com V9 AUTO × Lite serial permanecem válidos somente no seu escopo.

**Rastreabilidade:** PR [#1391](https://github.com/lucianox777/Jornada/pull/1391) (branch de experimento); arquivo de workflow, teste `test_v9_lite_experiment_contract.py` e índices GOV-DOC-01. Nenhum PASS de 30k ou 100k declarado por esta mudança.

## Recibo concluído de diagnóstico 1×1 — NÃO é comparação principal

[Action 42729686525](https://github.com/lucianox777/Jornada/actions/runs/42729686525) **SUCCESS**, SHA `d8341dacaf1b805c347ee9c6be9adc9ddffe3ada`, [artifact 13723738907](https://github.com/lucianox777/Jornada/actions/runs/42729686525/artifacts/13723738907) efetivamente inspecionado: `schema=V9_LITE_1K_MATCHED_SERIAL_PARITY_V2`, `scienceIdentical=true`, 22 snapshots ajustados, `bothRequestedWorkers=1`, `testUsed=false`, `certified=false`, `activated=false`. V9 governada: `36891 ms`, `177618944 B` RSS pico; Lite: `36175 ms`, `178601984 B` RSS pico. Nesta única rodada Lite foi ~1,94% mais rápida com ~0,55% mais pico RSS, sem evidência de vantagem estatística robusta. O ensaio apenas ajuda a dimensionar overhead da governança quando ambos usam 1 worker; não substitui nem bloqueia a **comparação principal V9 AUTO × Lite serial** para 30k e 100k. Não foi feito 30k pareado nem 100k.

**Precedência do PO:** prevalece o protocolo de comparação principal V9 governada AUTO × Lite serial já documentado. A V9-Lite **não receberá implementação de paralelismo**. O 30k manual foi preparado para preservar `MaximumParallelRuleSets=0` no mesmo plano: a V9 aplica AUTO por meio do governador; a Lite força 1 worker sem governador. A versão paralela da V9 nunca é silenciosamente substituída por 1 no teste principal. 

## Acompanhamento de 09/11/2026 — benchmark principal instrumentado, bloqueio de segurança

A PR #1391 continua experimental e sem merge. O benchmark principal `v9-lite-serial-30k-matched-benchmark.yml` preserva a arquitetura aprovada **V9 governada AUTO (plano `MaximumParallelRuleSets=0`) × V9-Lite serial (1 worker efetivo)**, sempre com plano científico idêntico, 160 avaliações e corpus 30k seed42. A comparação secundária 1×1 não é requisito de desempenho nem substitui o ensaio principal.

O workflow passou a coletar **por braço**, com `/usr/bin/time` do processo: CPU usuário/sistema, tempo de parede, RSS máximo, contadores de entrada/saída do SO; também coleta `du -sk` do diretório de saída. Esses números são instrumentação de processo e **diretório lógico apenas**, não medem CPU consumida pelo processo SQL separado nem uso físico total de disco por Docker/SQL. O recibo `parity-receipt.json` vincula métricas ao SHA; não declarar capacidade/eficiência 30k até a execução física válida. Os checks contratuais exigem campos de CPU e disco.

**Preflight de segurança:** a [issue #1253](https://github.com/lucianox777/Jornada/issues/1253) de credenciais SQL no probe histórico está **aberta** na consulta. Os workflows experimentais usam SQL efêmero criado após mascaramento de senha e autenticação por arquivo 0600, mas o fechamento formal de #1253 e a aprovação explícita da segurança aplicável não podem ser presumidos. O workflow 30k segue `workflow_dispatch`, proprietário, `security_preflight_approved`, SHA exato na master. **Não foi despachado** nessa etapa. A execução 100k continua não coberta.

**Status do HEAD anterior (`8fc094d2`):** CI rápida e canários de segurança concluídos com SUCCESS; TF, validação independente, Windows e 1k estavam pendentes/em andamento na consulta inicial. Após novos commits, todos os checks precisam ser reavaliados no SHA final. Ciência, TEST/Gold e ativação continuam inalterados.

## Continuidade — recibo no HEAD b3b4daac e alinhamento do teste automático

O ensaio [42730782585](https://github.com/lucianox777/Jornada/actions/runs/42730782585), SHA `b3b4daac06d46202032516a7c948306c15f89ad6`, terminou **SUCCESS**, com [artifact 13724088917](https://github.com/lucianox777/Jornada/actions/runs/42730782585/artifacts/13724088917) auditado: `scienceIdentical=true`, 22 snapshots, `certified=false`, `activated=false`, `testUsed=false`. Era ainda o diagnóstico **1×1**: V9 governada 57.828 ms / 180.768.768 B RSS pico; V9-Lite 57.385 ms / 175.988.736 B RSS pico. Resultado indicativo de 0,77% menos tempo e 2,64% menos pico RSS para Lite na única execução, sem inferência causal de vantagem.

Para obedecer à **precedência explícita do PO**, o gatilho físico automático de PR de 1k foi realinhado: no mesmo plano original `MaximumParallelRuleSets=0`, a V9 utiliza governador e AUTO e a Lite continua serial; o auditor exige as duas arquiteturas (V9 configurada 0; Lite configurada 0/efetiva 1), igualdade das avaliações científicas, dos hashes dos ajustes e ausência de ativação. O teste contratual `test_v9_lite_experiment_contract.py` passou a impedir futuras alterações que promovam o 1×1 a comparação principal. O resultado desta alteração **ainda depende da execução dos checks no novo SHA**, sem transferir o PASS de um SHA anterior.

O [preflight histórico de credenciais #1253](https://github.com/lucianox777/Jornada/issues/1253) permanece aberto na consulta e não pode ser declarado encerrado pelo modo SQL descartável da PR. O benchmark principal de 30k e a execução 100k **não foram disparados**. PR #1391 continua em rascunho; master sem alteração.

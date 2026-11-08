# Jornada V9 — estudo opt-in de regularização U nas células raras (06/11/2026)

**Estado:** implementação de três estimadores explícitos (V2, V3 histórico e V3 com mistura U) + testes unitários e de montagem do plano; **não** é execução física, escolha de parâmetros, certificação estatística, proposta de ativação nem substituição da V2. Frente independente das PRs #1298 e #1299, sem stacking.

## Hipótese pré-declarada

A V3 original estima U de células fora do suporte conjunto usando o produto das duas marginais condicionais do TRAIN. No experimento físico pareado da #1299, TF0/3 passes perdeu 469 resoluções corretas da V2 e recuperou 17 (saldo -452), com 450 mudanças do melhor candidato; todas as 469 perdas envolveram células com fallback U, mas 6.248 resoluções preservadas também envolveram fallback. A associação **não prova** efeito causal isolado de U.

Propomos um **único novo eixo experimental**: a fração `lambda = UBackoffJointMixtureWeight` da massa rara U que vem do estimador conjunto Dirichlet do mesmo TRAIN, em lugar do fallback por produto de marginais.

Para toda célula U com suporte conjunto preservado: `U_new(c) = U_V3(c)` (invariável).

Para as células U raras, fora do conjunto preservado:

```text
U_product(c) = R * P_person(c) * P_mother(c) / sum_rare(P_person * P_mother)
U_joint(c)   = (observed_U(c) + alpha) / (total_U + 81*alpha)
U_lambda(c)  = (1-lambda)*U_product(c) + lambda*U_joint(c)
```

Aqui `R = sum_rare(U_joint(c))`, calculado uma só vez a partir do mesmo TRAIN. Os dois termos somam **a mesma massa residual R**, de modo que a combinação convexa conserva U normalizado, M inteiro e probabilidade positiva em todas as 81 células, sem truncar probabilidades isoladas. A hipótese não pressupõe independência completa nas células sem suporte; `lambda=1` usa U conjunto Dirichlet nessas células, mas **não transforma toda a V3 na V2** porque M e as outras regras da V3 permanecem.

## Contracto no `plan.json`

**Margem de conflito (decisão de 07/11/2026):** não há arquivo
de ambiente nem `experiment.json` para determinar margem, pesos ou política
de seleção do estudo U raro. O gerador científico versionado
(`v9-u-rare-mixture-plan.py`) congela diretamente no **`plan.json`
executável** `Calibration.ConflictMargins`, `MuEstimationMethods` com
os pesos U `[0, 0.25, 0.5, 1]` e `SelectionDuplicatePolicies`
`[COUNT_ALL, COUNT_UNIQUE]`. São dois planos imutáveis, com SHA próprio:
`PRIMARY=0.5` e `PREDECLARED_SENSITIVITY=0.25`, cada um em SQL descartável.
O workflow informa somente `--role`; `--margin` e `--weights` são
proibidos com `--role` (fail-closed), e o recibo documenta papel, margens
pré-declaradas e hashes. A documentação e os testes integram a
pré-declaração versionada, sem reinterpretar campanhas passadas.
`.env` de infraestrutura, quando necessário para conexão SQL/API, **não**
deve ser apagado: não fornece parâmetros científicos ao plano.

Na especificação `MuEstimationMethods` do `plan.json`, declarar **três famílias** no mesmo array: `DEFAULT` (V2), `CLASSWISE_SUPPORT_BACKOFF` (V3 histórico sem peso) e `CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE` (V3 experimental com array explícito). A sintaxe anterior com pesos no método `CLASSWISE_SUPPORT_BACKOFF` isolado ainda é aceita para compatibilidade, mas os dois formatos misturados na mesma grade são recusados:

```json
{
  "MuEstimationMethods": [
    { "Method": "DEFAULT" },
    {
      "Method": "CLASSWISE_SUPPORT_BACKOFF",
      "Parameters": {
        "ClassMassCoverage": [0.99],
        "MinimumObservedMPerCell": [5],
        "MinimumObservedUPerCell": [5],
        "RetainSupportedTail": [true]
      }
    },
    {
      "Method": "CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE",
      "Parameters": {
        "ClassMassCoverage": [0.99],
        "MinimumObservedMPerCell": [5],
        "MinimumObservedUPerCell": [5],
        "RetainSupportedTail": [true],
        "UBackoffJointMixtureWeights": [0, 0.25, 0.5, 1]
      }
    }
  ]
}
```

- Se **ausente**, cálculo, versão, `OptionId`, serialização e conteúdo histórico de V3 permanecem os originais (compatibilidade exata).
- Se presente, os valores explícitos são finitos, únicos e entre 0 e 1; eles multiplicam o orçamento declarado, sem truncar grade.
- `lambda=0` é a **testemunha de paridade numérica** da V3, mas tem `OptionId`/proveniência experimental exclusivos. Quando V3 padrão e `lambda=0` coexistirem, contar os **dois braços** no orçamento e na multiplicidade, sem interpretá-los como resultados independentes.
- Se presente, a proveniência do snapshot usa `V9_CLASSWISE_U_RARE_MIXED_JOINT_BACKOFF_V1`, o `FallbackAssumption` e `MethodU` específicos. O replay recalcula exatamente todas as probabilidades e recusa metadata adulterada.
- Este parâmetro é **U**, não margem de decisão e não TF. M, prior, regras de blocking, referências fixadas, seleção de FDR 1% e isolamento de TEST/Gold permanecem inalterados.

O plano congelado de comparação controlada contém **24 avaliações declaradas**: (1 V2 + 1 V3 padrão + 4 alternativas V3 U misto) × (TF0, ONE_MF) × (3, 19 passes), margem 0,5. As versões e os IDs da V2 e da V3 padrão permanecem originais; o novo método tem identificadores distintos. Os três são candidatos de avaliação TRAIN, **não** três modelos operacionais ativos.


## Otimização de custo físico para as três famílias

Na busca aninhada, os braços `DEFAULT`, `CLASSWISE_SUPPORT_BACKOFF` e
`CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE` compartilham **a mesma materialização
de candidatos SQL**, dentro do mesmo corpus TRAIN, conjunto de passes de
blocking e margem declarada. O coordenador forma grupos pela identidade do
frame (passes + margem), e não pelo estimador M/U. Cada opção continua sendo
ajustada, pontuada, auditada e identificada **independentemente**, usando seu
próprio `OptionId`, fingerprint e snapshot; nenhum score ou desfecho é
copiado da V2 ou da V3 para outro método. Se há mais de 64 opções, o lote
é dividido para respeitar o limite de modelos do executor.

Para a grade congelada de 24 avaliações (2 rule sets, 1 margem,
2 braços TF, 6 variantes de M/U), a organização dos lotes prevê
**uma materialização inicial por conjunto de passes**, em vez de seis
materializações iniciais por conjunto de passes, sem contar replays e
medidas posteriores executadas por opção. Este é um ganho potencial
em leituras/joins SQL, **não** uma afirmação de redução de tempo medida.
É obrigatório conferir SHA dos candidatos e métricas individuais no
experimento físico; sem evidência pareada, não reportar velocidade obtida
nem equivalência inferencial de `lambda=0`.

A grade, orçamento de FDR e multiplicidade estatística permanecem com as
24 avaliações; compartilhar o SQL não reduz o número de hipóteses.

## Gate físico ainda exigido

**Não executar automaticamente a campanha científica confirmatória.** Para testar esta hipótese, congelar antes um `plan.json` pequeno, no mesmo TRAIN, corpus, seed42/43/57 e snapshots de referência, comparar os quatro pesos em TF0 e ONE_MF com 3 passes e 19 passes; margem operacionalmente elegível `0.5` fixa. Usar o próprio Calibrador SQL descartável e guardar hashes do plano, pares, fit, 81 células e resultados. Comparar o efeito no melhor e no segundo candidato, alterações de ranking/agrupamento e conflitos; não selecionar `lambda` pelos dados de TEST/Gold nem mascarar a regressão com margem zero.

Condições de aceitação: testes C# e Python completos em HEAD exato, reexecução física fechando TP/FP/identidades base, métricas independentes de fusão/fragmentação, fanout/custo e controle de FDR dependente por identidade-base. Sem melhora inequívoca e validação independente, manter V2 como referência e V3 desativada. A autorização do usuário para *merge de código validado* não autoriza DT-15 nem ativação operacional.

### Diagnóstico e otimização de RAM sem spill (06/11/2026)

O run #42283035832 de `surname/seed42` completou o batch de 160
configurações TRAIN e terminou em **Out of memory** (exit 134), antes do
recibo final. Não há prova de que apenas reduzir workers solucione a
retenção acumulativa; nenhuma execução foi certificada por esse run.

O construtor de `V9FittedSnapshot` armazenava duas representações
inteiras por fit: o `Payload` com até quatro distribuições TF congeladas,
**mais** o `byte[] utf8` canônico retido pelo tempo de vida do snapshot.
Como `V9NestedCalibrationCoordinator` guarda todos os snapshots de todos
os lotes até publicar o envelope, esse segundo buffer acumulava RAM
proporcional ao número de configurações. O snapshot agora mantém o
`Payload` imutável e o SHA canônico, mas cria bytes JSON **sob demanda**
ao publicar ou carregar, verificando SHA/limite; não mantém o buffer.
O custo potencial é CPU extra ao serializar, medido no benchmark físico.
Não mudou fit, scoring, M/U, FDR, famílias, hashes ou output JSON.

O `--v9-calibrate` registra `memory-progress.jsonl` ao fim de cada
batch (tempo acumulado, nº de configurações, heap/commit GC, memória RAM
residente e pico RSS). Esse arquivo é diagnóstico e não pertence ao
fingerprint/fit/seleção. O workflow V3 anexa o traço mesmo em falhas,
sem nomes nem identificadores pessoais. `GC.Collect()` não é chamado;
não foi adicionado limitador de CPU/RAM ou modo disco. A paridade
byte/SHA dos snapshots foi coberta por teste determinístico, inclusive
múltiplas leituras independentes e defesa contra mutação do buffer.

**Resultado ainda aberto:** só um novo teste físico com as mesmas 160
configurações e o mesmo corpus poderá medir economia real de RAM,
possíveis diferenças de tempo e se o OOM deixou de ocorrer.

### Serialização do envelope final sem duplicação (compatibilidade #1298)

A persistência de `<sha>.selection.json` usa a mesma saída UTF8 de
`JsonSerializer.SerializeAsync`, sem alocar todo o documento como
`SerializeToUtf8Bytes`. O arquivo temporário é criado dentro do diretório
novo da execução, seu SHA é calculado dos bytes escritos e é publicado por
rename que nunca sobrescreve artefatos anteriores. Testes comparam bytes e
SHA contra o formato antigo. **Não** se trata de um modo disk-backed de
calibração: todos os cálculos e avaliações permanecem em RAM; o SSD recebe
somente o relatório final. Essa alteração é idêntica à correção na #1298,
para que os resultados do mesmo caso de OOM sejam comparáveis.

### Paralelismo nativo do .NET: CPUs disponíveis, sem percentual

A propriedade **já existente** `Calibration.MaximumParallelRuleSets` aceita
**0 = automático (novo padrão)**: no momento de iniciar cada grupo de
trabalho, o calibrador usa `Environment.ProcessorCount` — as vCPUs lógicas
visíveis ao processo pelo runtime .NET, inclusive restrições de contêiner.
Nenhum percentual nem regra de “CPUs menos um” foi introduzido.
Valores positivos continuam sendo um override explícito (por exemplo,
`1` quando a API recebe somente uma conexão SQL), não o padrão. Cada
worker paralelo tem sua própria conexão SQL; `Parallel.ForEachAsync` e o
SO administram o escalonamento. Número de grupos disponíveis, SQL e RAM
podem limitar a aceleração. Esse cálculo não é controle de uso real da CPU.

O gerador de novos planos da busca aninhada declara explicitamente `0`.
Planos históricos que contêm `MaximumParallelRuleSets: 1` ou `2`
continuam respeitando o valor declarado. O campo 0 é apenas
**configuração de execução**; não altera cardinalidade de modelos,
seleção, FDR, TF ou política operacional. Não há alteração no tratamento
RAM-first: a otimização remove cópias redundantes; nenhum spill, limite de
RAM, `GC.Collect` forçado ou reinício em disco.

Na comparação física do OOM, documentar o valor efetivo de CPUs e a
concorrência, pois estudos executados com 1, 2 ou todas as vCPUs não são
benchmarks de performance diretamente comparáveis. Reproduzir a falha com
o mesmo corpus/seed e registrar resultados físicos no SHA exato.

### Compatibilidade dos planos físicos V2 × V3 com CPU automática (07/11/2026 UTC)

O commit que introduziu `MaximumParallelRuleSets=0` para CPUs visíveis
falhou nos gates `v9-mu-seed42-technical-regression` (run
`42317187489`) e `v9-mu-tf0-controlled-same-frame` (run
`42317187465`): o builder Python `v9-mu-historical-plan.py` ainda
exigia `MaximumParallelRuleSets >= 1` e abortou antes da execução
física com `Original train plan lacks required physical execution budget`.

O contrato corrigido reconhece **0 como autodetecção nativa do .NET**
nesses planos, preserva os valores positivos explícitos (e o cap de
diagnóstico histórico de 2 quando houver override) e recusa negativos,
booleanos e tipos incorretos. Há regressões offline tanto no plano
histórico quanto na comparação TF0 × ONE_MF. Não são resultados físicos
nem alteração de FDR, seed, M/U, TF, TEST ou DT-15. Os dois gates precisam
ser reexecutados no SHA atualizado antes de merge.

### Detecção automática fail-closed (decisão definitiva)

Para `MaximumParallelRuleSets=0`, o Calibrador usa **exatamente** o valor de
`Environment.ProcessorCount`, sem `Math.Max(1,...)`, sem reduzir para 1,
sem tentativa de detectar CPU por fonte alternativa e sem alteração
silenciosa do plano. Um retorno inválido (`<=0`) ou erro de detecção
**interrompe o processo**, com erro explícito indicando que é necessário
examinar a quantidade de vCPUs visível ao processo (Windows/Linux,
afinidade e cotas do contêiner) e, se for o caso, configurar manualmente
`MaximumParallelRuleSets=1` ou `2` e executar novamente.

Valores históricos positivos são preservados literalmente, inclusive quando
a autodetecção falhar, pois um override explícito não a consulta. Essa
política não contém fallback automático. O tempo, o número de workers
efetivo e a RAM devem ser observados no mesmo ambiente físico, sem
atribuir a detecção de CPUs a responsabilidade por todo possível OOM.

### Aviso no relatório estatístico quando workers manuais < CPUs detectadas

O relatório existente `mu-statistical-analysis.json` e sua apresentação
`mu-statistical-analysis.md` incluem agora o diagnóstico **não inferencial**
`cpuCapacity`: `ConfiguredMaximumParallelRuleSets`,
`DotNetVisibleLogicalProcessors` (vCPUs lógicas vistas pelo processo C#),
`EffectiveMaximumParallelRuleSets` e o código de `Notice`.
**Somente se** o valor manual for positivo e menor que a contagem detectada,
o relatório exibe **AVISO** de diferença de capacidade. No automático
(`0`) o limite é exatamente o número detectado, sem aviso.

É uma observação de configuração, **não** uma alteração automática do
paralelismo, nem prova de uso real de 100% da CPU: o trabalho disponível,
o SQL e a RAM ainda condicionam o uso. A detecção inválida em modo
`0` continua **fail-closed**, e o override manual positivo continua
válido. Se o contador .NET estiver indisponível no modo manual, o relatório
não inventa um número e registra essa indisponibilidade.
A comparação não muda plano, decisão científica, hashes de snapshots,
M/U, TF, FDR, TEST ou publicação; apenas acrescenta campos ao relatório
estatístico já gerado, sem criar arquivo de trabalho adicional.

### Diagnóstico de capacidade de CPU **e** RAM no relatório estatístico (07/11/2026)

As decisões mais recentes do responsável substituem instruções anteriores:
`MaximumParallelRuleSets=0` continua padrão; utiliza **exatamente**
`Environment.ProcessorCount`, sem fallback, sem `CPU-1`, sem percentuais.
Planos legados com `1`, `2` etc. preservam o valor explícito.
A autodetecção inválida interrompe a calibração, com erro claro e sem
ajustar automaticamente trabalhadores. O relatório já avisa quando os
workers *configurados explicitamente* são inferiores às CPUs lógicas
visíveis para o processo C#; não altera o plano.

Acrescentado `ramCapacity` em **ambos** os arquivos existentes
`mu-statistical-analysis.json` e `mu-statistical-analysis.md`:
`ResidentWorkingSetBytes` (RSS do processo naquele instante),
`PeakResidentWorkingSetBytes` (pico do processo desde o início),
`ManagedLiveBytesEstimate` (sem forçar coleta), `GcHeapSizeBytes`,
`GcCommittedBytes`, `GcTotalAvailableMemoryBytes`,
`GcMemoryLoadBytes`, `GcHighMemoryLoadThresholdBytes`,
`GcReportsHighMemoryLoad` e `Notice`. São medições feitas no momento
da publicação, complementar ao **arquivo já existente**
`memory-progress.jsonl` com amostras ao final de cada lote. O orçamento
do GC **não é a RAM física livre do host**; heap GC e RSS também não são
intercambiáveis. Não afirmar consumo da máquina toda a partir do RSS.

O **AVISO RAM** é emitido quando o próprio .NET informa carga no seu
limiar alto de memória, ou quando a API não fornece dados comparáveis
para essa avaliação. Não existe limite ou percentual de RAM inventado
pela Jornada, nem degradação silenciosa para SSD. Sem novas gravações
de resultados intermediários, sem `GC.Collect()` forçado e sem alterar
FDR, TF/M/U, snapshots, seleção ou etapas TRAIN/VALIDATION/TEST.
O aviso é **observacional**: ausência de aviso em uma amostra não prova
ausência de OOM ou de pressão anterior, e não evita falha de memória.
A prova física seed42 permanece obrigatória.

### Última exceção de erro do Calibrador — `calibrador.log`

Para cada execução de `--v9-calibrate` ou `--v9-validate-finalists`,
uma falha de C# **não é recuperada silenciosamente**: a exceção original
continua sendo propagada e o processo retorna erro. Quando o diretório
de resultados já existe, `calibrador.log` recebe **somente a exceção mais
recente** (`Exception.ToString()`, com tipo, mensagem, stack trace e
exceções internas), escrita com substituição do conteúdo e **sem append**.
O recibo existente `interrupted.json` permanece separado e mínimo.
O workflow físico V3 inclui `results/calibrador.log` entre os
artefatos de falha; nenhuma linha individual de TRAIN, nomes ou pares
é adicionada propositalmente ao diagnóstico.

**Limite físico inevitável:** se a falha for disco cheio (`ENOSPC`) ou
ocorrer antes da criação do diretório, o próprio log pode não ser
persistido. Nesse caso a exceção é impressa no `stderr` para os logs
do processo e do CI; a falha ao escrever `interrupted.json` ou o
`calibrador.log` não encobre a exceção original. Não há retry,
limpeza automática, troca de volume nem fallback operacional. Em OOM
fatal do runtime ou encerramento pelo sistema operacional, o `catch`
pode nem executar: não declarar garantia absoluta de arquivo em tais
condições. Nenhuma alteração em RAM/CPU, FDR, M/U/TF ou seleção de modelo.

Uma tentativa rejeitada por **diretório de saída já existente** não altera
os arquivos dessa execução anterior: o log só pode ser gravado em um
diretório criado pela própria tentativa atual. Em falhas antes disso,
o canal confiável de diagnóstico é o stderr do processo/CI.

### Reserva antecipada de 10 KiB para último erro (disc-full / ENOSPC)

**Decisão vigente:** antes de iniciar a execução física do Calibrador, e
depois de criar o **diretório novo** desta execução, abrir o próprio
`calibrador.log` com `CreateNew`, gravar integralmente **10.240 bytes**
(de fato, não apenas `SetLength`, que pode resultar em arquivo esparso),
e forçar `Flush(flushToDisk: true)`. O handle fica aberto durante o cálculo.

Se ocorrer uma exceção gerenciada, inclusive por falta de espaço, o Calibrador
imprime `Exception.ToString()` no stderr, reescreve **os mesmos 10 KiB**
do arquivo já aberto **em posição zero**, preenche o restante com espaços,
e força `Flush` sem aumentar o tamanho do arquivo. Erros maiores que
10 KiB são cortados em fronteira UTF-8 válida com marcador explícito de
truncamento. **Somente o último erro é mantido, sem append.**

Se o processo terminar com sucesso, fecha o handle e remove o arquivo
de reserva vazio. Caso contrário, preserva o arquivo do erro. Se a
reserva inicial não puder ser gravada por falta de espaço, **não inicia
a calibração** (fail-closed). Diretórios de resultados anteriores não são
alterados. Uma falha ao escrever `interrupted.json` ou a própria reserva
não encobre a exceção original.

**Não é garantia absoluta:** em sistemas copy-on-write, compressão,
quotas, encerramento abrupto do processo, falhas do runtime ou SO,
mesmo uma área pré-gravada pode não garantir I/O posterior; a saída
`stderr` do processo/CI é a evidência adicional. A reserva existe
exclusivamente para relatório de erro: **não** é spill, checkpoint,
redução de RAM, fallback ou mudança estatística.

### Regressão OOM com cobertura física original restaurada (07/11/2026 UTC)

**Correção da interpretação anterior:** o sucesso do job
`surname/seed42` no SHA `68bff2bef7f0fc0966c260cc9e7d4c15b83c89c5`,
run `42322540698`, mediu somente **64 configurações**,
porque o workflow deixou de fixar as cinco margens de conflito,
passando de `[0, 0.25, 0.5, 2, 4]` a apenas duas margens do
plano de origem. Essa redução foi introduzida no desenho experimental,
**não demonstrou a correção do OOM das 160 configurações**.
O job passou com `Batch=1`, `TotalConfigurations=64`, memória
residente amostrada de 2.114.019.328 bytes, pico RSS de 3.673.632.768
bytes, commit GC de 1.982.185.472 bytes e 561.797 ms de cálculo.
O pico do processo não é sinônimo de memória do host.

O incidente original é o run `42283035832`, commit
`e05043bab749ea222e598e811e4b8bbe814defe1`,
onde `surname/seed42` reportou `Batch=1` e
`TotalConfigurations=160` antes de `Out of memory.` (exit 134).
Para comparação física válida, o workflow `v9-v3-broad-train-regression`
volta a fixar **explicitamente as cinco margens**, mantém
8 políticas V3 × 2 opções de TF × 2 regras SQL em seed42
= **160 configurações**, e 1 política × 2 TF × 5 margens ×
2 regras em seeds 43 e 57 = **20 configurações**.
O auditor `v9-v3-broad-train-audit.py` falha fechado se faltarem
margens ou linhas, inclusive os 81 estados/células M/U exigidos.
Regressão offline adicional rejeita expressamente apresentar 64 como
se fossem 160. Não muda opções, margens, fórmulas ou dados de TEST.

**Gate ainda obrigatório:** executar o novo commit completo e
comparar corpus, seed, opções, CPUs/workers e memória medida.
Sucesso em 64 não é certificado de 160; sucesso técnico no TRAIN
não é evidência de validação independente ou autorização DT-15.
As alterações de CPU, RAM e pré-reserva de 10 KiB do
`calibrador.log` continuam ativas, sem fallback ou spill.

### Diagnóstico do gate físico 160 — margem zero recusada pelo contrato atual (07/11/2026)

O commit `2b13cfe4` restaurou as **160 configurações**, mas os seis
jobs do workflow `42325219539` falharam **antes de calibrar** com
`InvalidDataException: Plano V9 incompleto, duplicado ou fora do contrato`,
em `V9AdaptiveCalibrationCoordinator.ValidatePlan` (linha 115).
O motivo foi a margem `0`: a versão **atual** da master proíbe
`ConflictMargins <= 0`; já o commit histórico `e05043b` permitia
margem zero. Não houve OOM nesse run, tampouco reserva/log porque o
plano foi recusado **antes** da criação do diretório de resultados.

**Reteste de carga conforme contrato atual:** manter 160 combinações
e cinco margens, mas usar `[0.0001, 0.25, 0.5, 2, 4]` no lugar de
`[0, 0.25, 0.5, 2, 4]`. **Não** relaxar a validação atual nem aceitar
margem zero por exceção de workflow; isso contrariaria o contrato da
master e o teste unitário `More_compute_cannot_relax_risk_or_substitute_projection_or_zero_margin`.
A menor margem positiva funciona como *aproximação*, **não como reprodução
exata das métricas de zero**. O objetivo deste reteste é demonstrar
que o Calibrador com otimização RAM-first suporta **160 avaliações
físicas** no mesmo corpus/família/seed, e medir RSS/pico. A comparação
numérica histórica exata exigiria outra campanha sob o contrato antigo,
devidamente segregada; não afirmar equivalência estatística entre os
dois protocolos. Auditor independente e regressão offline rejeitam
ausência de margem e aceitação indevida de zero.

O erro foi inspecionado diretamente nos logs dos jobs; as mudanças deste
registro não alteram o algoritmo FS, M/U, TF, FDR, snapshot, execução
em RAM, uso automático de CPUs ou política de reserva do log.

### Reconciliação com master após merge #1298 (07/11/2026)

O PR #1298 foi integrado à master no commit `f1bd50f`, e a regra
mais recente da master **aceita margem zero exclusivamente como
diagnóstico medido**. Ela não pode ser candidata de Pareto nem
finalista operacional. A PR #1300 deve preservar os guardas
`V9AdaptiveCalibrationCoordinator`, `V9NestedCalibrationCoordinator`,
`V9TrainingParetoSelection` e a regra de elegibilidade do relatório,
bem como o streaming e o catálogo completo de regras do #1298.

A campanha RAM-first de `ee70f19` mantém o braço `0.0001` como
**regressão de carga de 160 configurações** já comprovada. O auditor
agora verifica expressamente um dos dois protocolos declarados:
`[0, 0.25, 0.5, 2, 4]` (margem zero diagnóstica da master)
ou `[0.0001, 0.25, 0.5, 2, 4]` (prova RAM-first).
Não agrega os dois, não os considera estatisticamente equivalentes e
não diminui a cobertura por conveniência. O paralelismo automático da
#1300 permanece `0 = Environment.ProcessorCount`, sem o override
temporário de 1 worker no workflow experimental #1298.

### Consolidação pré-merge com #1299 — diagnóstico pareado em RAM (07/11/2026)

A instrumentação V2/V3 da PR #1299 foi reunida à branch desta PR,
sem merge na master. A captura privada de `SourceRankInternal` é
habilitada somente para plano que declare V2 `DEFAULT` **e** V3
`CLASSWISE_SUPPORT_BACKOFF`, preservando invariavelmente os
`IdentityOutcomes` necessários para limites de FDR.

A regressão somente V3 de 160 configurações mantém RAM-first sem
retenção adicional de ranking top-two por configuração. O estudo pareado
continua reduzindo em memória os rankings individuais para contagens
agregadas, preservando `JsonIgnore` em fontes/IDs. Os novos auditores
`v9-v3-source-gap-audit.py` e `v9-v3-source-paired-audit.py`
exigem SHA/frames idênticos e vedam causalidade ou certificação
indevida. Conciliação estatística no mesmo SHA não substitui a
execução física de 160 casos no SHA combinado. Nenhuma ativação DT-15.

### Protocolo físico reprodutível de 24 braços e sensibilidade pré-declarada (07/11/2026)

**Objetivo:** tentar explicar a regressão da V3 em TF0/3 passes sem
confundir uma redução da margem com uma correção do estimador U.
A primeira campanha utiliza **exatamente 24 avaliações físicas**:
`DEFAULT` (V2), `CLASSWISE_SUPPORT_BACKOFF` (V3 histórica) e as
quatro versões explícitas de `CLASSWISE_SUPPORT_BACKOFF_U_MIXTURE`
com `lambda=0,0.25,0.5,1`; cruzar com TF0/ONE_MF e 3/19 passes
de blocking, sob a mesma seed 42, mesmos 30 mil registros sintéticos
e SQL descartável. Margem primária fixa `0.5`, limite FDR ponto e
superior de 1%, mesmas observações, corpus e pin IBGE.

**Sensibilidade**, antecipadamente declarada **antes de ver resultados**:
a grade completa de 24 braços é repetida separadamente na margem
`0.25`, com manifesto e fingerprint de estudo diferentes e
multiplicidade contabilizada para os dois estudos. Não considerar
margem 0 selecionável; margem 0.25 não substitui 0.5 como referência.

**Critérios de auditoria antes da execução:** exatamente 24 linhas,
24 digests de requisição únicos, 81 células por método e dois quadros
SQL estáveis por braço; variantes com `lambda=0` devem reproduzir
literalmente TP, FP, recall, conflitos, pendências, motivo Resolve,
prior e limite FDR da V3 histórica dentro da mesma margem.
Comparações por estimador exigem o mesmo SHA de pares SQL, o mesmo
prior, o mesmo conjunto de fontes elegíveis, os mesmos TF e blocking.
TP/FP, recall e limites de confiança condicionais são relatados
para *todas* as alternativas, inclusive piores. O limite superior
condicional é **não certificado**; não usar TRAIN para ativar
operacionalmente U ou selecionar a melhor mistura como se estivesse
validada em holdout independente.

**Interpretação estatística:** se `lambda=1` recuperar recall, isso
será evidência do papel da intervenção em U, mas **não** prova causal
isolada do fallback; ainda é necessário separar o efeito sobre
pontuação do melhor/segundo candidato e agrupamentos blind.
As 19 regras continuam diagnóstico porque o fanout do controle
histórico ultrapassa o orçamento; não podem tornar-se finalistas
automaticamente. A coorte sintética `clean` conhecida é apenas
TRAIN_SELECTION, e FP=0 observado não implica FDR verdadeiro zero.

**Execução e privacidade:** o workflow
`.github/workflows/v9-u-rare-mixture-physical.yml` usa dois jobs
independentes (margens 0.5/0.25), cada qual com SQL descartável,
pin IBGE, SHA exato, plano congelado e auditor separado
`v9-u-rare-mixture-physical-audit.py`. Publica apenas métricas
agregadas, planos, hashes, `memory-progress.jsonl` e recibos
imutáveis. Nunca publica envelope com resultados por identidade,
ranking top-two privado, nomes de pessoas, CPF ou conteúdo de
`calibrador.log`. Nenhuma escrita em master, uso de TEST/Gold,
certificação automática de 30k ou ativação DT-15.

**Gate para considerar a evidência física:** os dois jobs precisam
concluir no SHA exato, com `lambda=0` numericamente idêntico à
V3 histórica. Se o teste falhar, registrar a causa antes de mudar
o método ou a auditoria; nunca diminuir hipóteses para passar CI.

### Evidência física da mistura rara — seed42, 07/11/2026 (HEAD 0f180021)

A campanha física pré-declarada executou **48 avaliações**, em dois jobs
independentes de 24 configurações cada: margem primária `0.5` e
sensibilidade `0.25`, ambas com TF0 e ONE_MF, 3/19 passes,
V2, V3 e `lambda=0,.25,.5,1`. Workflow
`v9-u-rare-mixture-physical-train`, run `42343493885`, SHA
`0f180021263c48d46c00c445bc400ba14882b89c`.
Artefatos `13562219158` (0.5) e `13563570659` (0.25).
`24/24` avaliações e `0` falsos aceitos observados por margem;
`lambda=0` foi numericamente idêntico à V3 histórica.
As 81 células M/U, hashes SQL, identidade das hipóteses e limites
condicionais foram auditados, sem TEST, Gold ou ativação.

**Três passes, 7.011 fontes elegíveis — TP por modelo:**

| Margem / TF | V2 | V3 | λ=0 | λ=0,25 | λ=0,5 | λ=1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 0,5 / TF0 | 6758 | 6306 | 6306 | **6771** | 6758 | 6758 |
| 0,5 / ONE_MF | 6702 | **6771** | **6771** | 6715 | 6707 | 6702 |
| 0,25 / TF0 | 6759 | 6704 | 6704 | **6774** | 6767 | 6759 |
| 0,25 / ONE_MF | 6761 | 6772 | 6772 | 6718 | **6775** | 6761 |

**Critério primário pré-declarado:** não basta recuperar TF0;
a alternativa não deve piorar ONE_MF. `lambda=0.25` restaura
`+465 TP` de TF0 na margem 0.5 (6306→6771), mas **perde
56 TP de ONE_MF** ante V3 original (6771→6715). `lambda=0.5`
restaura TF0 até a V2 (6758), mas perde 64 TP na ONE_MF
ante V3 (6771→6707). `lambda=1` replica o TP da V2 nos
dois braços e não oferece vantagem na ONE_MF. **Nenhum peso
comum satisfaz simultaneamente os dois critérios na margem
primária**, de modo que nenhuma mistura fica eleita.
A sensibilidade 0.25 tem `lambda=0.5` melhorando ambos os
braços contra V3 (TF0 +63 TP, ONE_MF +3 TP), mas não pode
substituir a hipótese primária após observar o resultado.

Todas as **19-pass** foram rejeitadas pelo limite de fanout,
apesar do recall alto e FP=0. O zero de FP em corpus sintético
conhecido **não prova** FDR verdadeiro abaixo de 1%; os limites
condicionais não são independentes e não certificam nenhuma opção.
A resposta de TF0 a λ acrescenta evidência do papel da
**intervenção em U**, mas ainda não demonstra a causa isolada do
fallback porque os scores, rankings e agrupamentos também mudam.
É indispensável separar esses efeitos em protocolo independente
antes de escolher parâmetro ou autorizar DT-15.

**Correção de proveniência posterior à campanha:** cada recibo
`receipt.json` já continha o digest `fileSha256.sourcePlan`,
mas o ZIP publicado não incluía `source-nested-plan.json`.
O novo upload inclui **somente esse plano sintético sem PII**,
permitindo conferir os bytes deste elo no SHA de novos jobs; foi
adicionado teste de não regressão. A evidência anterior continua
válida quanto aos arquivos efetivamente publicados, mas não
ganha retroativamente a verificação independente desse elo.
Nenhuma modificação de score, calibração, regra de decisão ou
certificação estatística decorre dessa correção documental.

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

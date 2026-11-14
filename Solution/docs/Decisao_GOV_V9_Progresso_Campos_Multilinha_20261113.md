# GOV-V9-PROGRESS-CAMPOS-MULTILINHA-20261113 — uma mensagem por hipótese com um campo por linha

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo). **Estado:** APROVADO pelo PO e proposto em PR técnica; CI/merge são condições para marcar como INTEGRADO. **Política:** GOV-DOC-01.

## Esclarecimento explícito do Product Owner

Após a [decisão de formato anterior](Decisao_GOV_V9_Progresso_Uma_Linha_Por_Hipotese_20261113.md), o PO esclareceu: **“quero uma informação por linha. Pode tirar o pipeline?”**. A palavra “pipeline” designa aqui o caractere de texto `|` (**pipe**), não o workflow/pipeline do GitHub Actions, que é preservado. O caractere `|` **não é necessário para a mensagem de progresso** e deve ser retirado.

**Interpretação aprovada:** conservar **uma mensagem/registro lógico por hipótese** (324 registros de progresso), mas renderizar **cada campo em uma linha física própria**. São 7 linhas de texto por hipótese no modo padrão, montadas previamente como uma única string com separadores newline (`\n`), emitida por **um único `Console.WriteLine`**, sob **um único `lock (Console.Out)`** e **um único `Console.Out.Flush()`**. Não trocar por 7 chamadas individuais de escrita nem criar sincronização global por grupos de cinco.

Exemplo **ilustrativo** (campos do MESMO resultado `38/324`):

```text
[V9 PROGRESS] 38/324
timestamp=2026-11-13T18:35:22Z
hipotese=047649776a35
resultado=AVALIADA
restante=219min
limite=2026-11-13T22:15:00Z
status=DENTRO_DO_PRAZO
```

A data e o fuso devem estar explícitos para impedir ambiguidades ao cruzar meia-noite. O `restante` é **até o prazo da etapa física**, jamais ETA estimada com base no rendimento. O passo real da campanha continua controlado por `timeout-minutes: 315` no GitHub Actions e o job TRAIN por 360 minutos. O workflow exporta um timestamp UTC imediatamente antes da execução do wrapper como referência **aproximada** do início do passo; o relógio autoritativo do timeout é interno ao GitHub e não deve ser alegado como conhecido exatamente. Em outros chamadores sem a variável, a referência passa a ser o início do coordenador.

## Delimitação técnica e governança

- **Preservar:** 324 hipóteses TF4, contagem atômica existente, resultados individuais `AVALIADA/REJEITADA`, hashes de requests, callback de auditoria quando presente, SQL e replay, resultados TRAIN, validação independente e TEST isolado, P95 científico, segurança/recursos, timeouts efetivos e persistência agregada.
- **Simplificar:** remover do console de progresso a formatação `|`, estimativa móvel/cumulativa de ETA, margem derivada de ETA e tempo individual pós-SQL que não integra os sete campos aprovados. A medição individual já existente no código não é revogada nesta alteração, em linha com orientação anterior do PO.
- **Diagnóstico:** `V9_REPLAY_TIMING_DIAGNOSTICS` permanece **opt-in OFF no workflow padrão**; com progresso ligado e diagnóstico explicitamente ligado, `replay_elapsed_ms` é um campo adicional dentro da mesma mensagem, sem duplicar lock/escrita; com progresso desligado e timing ligado, o histórico `[V9 PERF DIAG]` isolado continua para investigação. O `index_build` diagnóstico é por grupo, não altera ciência. A mensagem operacional por grupo `[V9 SQL GROUP]` não se confunde com o progresso individual e fica fora do escopo desta revisão.
- **Concorrência:** imprimir o bloco sob o `Console.Out` lock já existente, sem acrescentar buffer compartilhado ou novo `lock`. Os resultados podem chegar fora da ordem dos SHAs por conta de grupos concorrentes; o contador indica conclusões observadas, não índice preordenado no plano.
- **Risco de desempenho:** agora são sete linhas físicas por hipótese, embora uma única invocação de escrita/flush; não foi medido impacto causal relativo ao run [#43105369099](https://github.com/lucianox777/Jornada/actions/runs/43105369099), 324/324 em ~234 min sem progresso individual. Não prometer recuperar ~69 min por alteração de formato.

**Precedência:** substitui **somente a apresentação em linha única com pipes** da decisão [GOV-V9-PROGRESS-UMA-LINHA-POR-HIPOTESE-20261113](Decisao_GOV_V9_Progresso_Uma_Linha_Por_Hipotese_20261113.md) e o formato de log da PR #1493. Mantém o requisito superior **um registro por hipótese, um lock, um WriteLine, um Flush**, a ciência e os limites. Nada é alterado retroativamente na campanha [#43499315560](https://github.com/lucianox777/Jornada/actions/runs/43499315560); não disparar 100k sem workflow_dispatch manual pelo PO.

**Aceite:** inspeção de código e build .NET, testes de contrato dos sete campos separados por newline sem pipes, um `Console.WriteLine` por conclusão (normal), `Flush()` único sob lock, fallback de DIAG, rejeições, 315 minutos da etapa versus 360 minutos do job e CI `unit-runtime`/`security-guards`/`deterministic-build` aprovadas. Validar futuramente desempenho físico com mesma configuração, sem imputar causalidade à instrumentação sem A/B pareado.

**Rastreabilidade:** branch `fix/v9-progress-multiline-fields-20261113`; [índice de decisões vigentes](Indice_Decisoes_Vigentes.md), [análise de baseline](Baseline_Tempo_Decorrido_V9_100k_20261112.md), código `V9NestedCalibrationCoordinator.cs`, workflow `.github/workflows/v9-v3-four-name-100k.yml`, testes estáticos `test_v9_calibrator_safe_failure_stage.py` e `test_v9_v3_four_name_100k.py`. PR e SHA de integração são acrescentados em recibo após CI/merge.

## Recibo de integração (13/11/2026)

A implementação foi integrada à `master` na [PR #1497](https://github.com/lucianox777/Jornada/pull/1497), squash SHA `951b84090d79285fcb41624f275f89aa6c5506ae`. Os checks obrigatórios [#43522292974](https://github.com/lucianox777/Jornada/actions/runs/43522292974) aprovaram **unit-runtime**, **security-guards** e **deterministic-build** (3/3). O [preflight offline #43522292969](https://github.com/lucianox777/Jornada/actions/runs/43522292969) também foi aprovado.

**Estado:** FORMATO IMPLEMENTADO e INTEGRADO; validação física 100k sob a nova instrumentação **não executada** neste recibo, ganho de desempenho **NÃO MEDIDO**. A campanha histórica #43499315560 permanece independente do novo SHA, sem cancelamento nem migração retroativa. Continuidade de documentação GOV-DOC-01 pelo índice atualizado em PR de recibo.

## Esclarecimento posterior do PO — apresentação de sete linhas físicas substituída

O PO apresentou em 13/11/2026 uma captura do GitHub Actions móvel na qual cada hipótese tinha **um único número de linha do GitHub**, mas o texto longo com pipes quebrava visualmente em várias linhas. Sua intenção final é **preservar a apresentação de uma única entrada numerada por hipótese**, **remover os pipes** e apresentar os **campos novos**, com quebra visual automática conforme largura. Assim, o formato de sete linhas físicas (`\\n`) definido acima **passa a ser histórico e não é a especificação visual vigente**. A nova diretriz e as limitações de quebra visual estão em [GOV-V9-PROGRESS-ENTRADA-UNICA-SEM-PIPE-20261113](Decisao_GOV_V9_Progresso_Entrada_Unica_Console_Sem_Pipe_20261113.md). A nova apresentação foi implementada e integrada na [PR #1504](https://github.com/lucianox777/Jornada/pull/1504), squash SHA `ba645606` e CI obrigatória [#43546556584](https://github.com/lucianox777/Jornada/actions/runs/43546556584) 3/3 verde; execução física já iniciada não recebe mudança retroativa. Validação visual futura e ganho físico não medido. GOV-DOC-01.

# GOV-V9-PROGRESS-PREVISAO-RISCO-TIMEOUT-20261114 — previsão operacional para 324 hipóteses

**Autoridade:** Product Owner, 14/11/2026 (America/Sao_Paulo), aprovação explícita: “Pode implementar agora isso.” **Estado:** DECIDIDO / IMPLEMENTADO / INTEGRADO à `master` na [PR #1506](https://github.com/lucianox777/Jornada/pull/1506), squash SHA `fe6b49a6a2e772b1da149ebab16944cca009f0bd`. [CI rápida #43550125833](https://github.com/lucianox777/Jornada/actions/runs/43550125833): 3/3 SUCCESS (`unit-runtime`, `security-guards`, `deterministic-build`) no HEAD `d75dce689d5b011bf51951245e62355e69bdcdb7`. Validação da projeção em execução física 100k e ganho de performance **NÃO medidos**. **Política:** GOV-DOC-01.

## Contexto, evidência e necessidade

Na campanha manual [#43548040615](https://github.com/lucianox777/Jornada/actions/runs/43548040615), SHA `562e22b7504004635a31ddbe2a71f040c896393e`, a captura do GitHub móvel registrou `6/324`, `restante=330min`, `limite=2026-11-14T08:29:46Z`, `status=DENTRO_DO_PRAZO`. O PO perguntou como o status era calculado. Inspeção do código comprovou que `DENTRO_DO_PRAZO` significava **somente relógio anterior ao timeout de 340 min**, sem prever a conclusão de 324 hipóteses. Isso poderia gerar falsa sensação de segurança quando a vazão observada já indicasse prazo insuficiente.

O PO aprovou a implementação imediata da proposta de **previsão de tempo restante pela velocidade das conclusões**, classificando risco de timeout. A mudança é **exclusivamente de apresentação diagnóstica operacional**, não científica. Não cancelar, alterar ou reiniciar automaticamente a campanha #43548040615: seu SHA foi fixado no início e continuará a exibir o formato anterior.

## Decisão, fórmula e semântica

No relatório `[V9 PROGRESS]` de **cada hipótese concluída**, calcular os seguintes valores com o relógio UTC do início da etapa física `V9_PHYSICAL_STEP_START_UTC`, previamente capturado pelo workflow; se ausente, usar o instante inicial do coordenador C#:

- **`concluidas`** = contador atômico de todas as hipóteses finalizadas, incluindo avaliadas e rejeitadas, como já contabilizado pelo callback de auditoria; **`total`** = multiplicidade física declarada no plano (`324` nesta campanha).
- **`decorrido_min`** = minutos desde o início do passo físico até o instante da mensagem, incorporando a sobrecarga SQL/runner observada nesse intervalo.
- **`eta_min`** = `decorrido_min / concluidas × (total − concluidas)`, desde que `concluidas > 0`; indicador heurístico de tempo até completar as hipóteses **no ritmo médio observado**, não promessa nem medição real da duração futura. Em `total/total`, `eta_min=0`.
- **`restante`** = `max(0, floor(limite_fisico_utc − agora_utc))` em minutos; permanece orçamento de relógio, **não ETA**.
- **`margem_prevista_min`** = minutos restantes **até o timeout do passo físico** menos `eta_min`, pode ser negativa. Reserva preventiva operacional de **15 minutos** para sinalização de risco, não prolongamento de limite.
- **`status`**: `PRAZO_EXCEDIDO` se o relógio de 340 min já expirou; `PREVISAO_PRELIMINAR` antes de **8 hipóteses concluídas** (ou sem observações); a partir da 8ª, `RISCO_TIMEOUT` quando a margem prevista for **menor ou igual a 15 minutos**, inclusive negativa; `DENTRO_DA_PREVISAO` quando a margem for superior a 15 minutos. Se todas as hipóteses terminaram antes do prazo, `DENTRO_DA_PREVISAO`. A condição de prazo efetivamente excedido prevalece sobre as demais.

**Exemplo sintético**, não log medido, com tempo limite físico de 340 min, 8/324 avaliadas em 8 minutos: ETA 316 min, orçamento restante 332 min, margem +16 min → `DENTRO_DA_PREVISAO`. Com 8/324 em 8,1 min: ETA 319,95 min, restante 331,9 min, margem +11,95 min → `RISCO_TIMEOUT`. Com 1/324 em 3 min a ETA projetada é 969 min, mas o status permanece `PREVISAO_PRELIMINAR` por insuficiência amostral.

O progresso padrão passa a incluir `eta_min`, `margem_prevista_min` e um `status` com a nova semântica. Mantém `[V9 PROGRESS] n/324 (p%) timestamp=... hipotese=... resultado=... restante=... limite=...`. As novas informações ficam **na MESMA linha física numerada do GitHub**, separadas por espaços, **sem pipes `|` e sem newlines internos**. Uma única aquisição de `lock(Console.Out)`, uma única chamada `Console.WriteLine` e um único `Flush` por hipótese. Sem mensagens extra de heartbeat e sem timeouts extras.

```text
[V9 PROGRESS] 8/324 (2.5%) timestamp=... hipotese=... resultado=AVALIADA restante=332min limite=... eta_min=316.0 margem_prevista_min=+16.0 status=DENTRO_DA_PREVISAO
```

O diagnóstico opcional `replay_elapsed_ms` **continua OFF por padrão**. Não reativar nem confundir esta ETA operacional com P95 científico ou com tempo individual de replay. A projeção não utiliza nem modifica as métricas científicas.

## Ressalvas obrigatórias, riscos e alternativas

O ritmo físico não é necessariamente constante: diferentes frames SQL, agrupamentos, margens, FDR/CP, rejeições instantâneas, memória/CPU e paralelismo podem tornar a projeção otimista ou pessimista. Mesmo `DENTRO_DA_PREVISAO` **não garante** concluir dentro do limite. A estimativa observa somente o **teto físico 340 min**, não o orçamento adicional para auditoria, upload, handoff e limpeza dentro do **job TRAIN 360 min**; sucesso de TRAIN/VALIDATION não está garantido pelo status. O limite físico real é aplicado pelo GitHub e a referência do relógio UTC é apenas aproximada.

Alternativas avaliadas: (a) manter o status antigo só de relógio — **rejeitada**, não responde ao risco de conclusão; (b) estimar pelo tempo de um único replay — **rejeitada**, confunde fases e é pouco representativo; (c) reestimar pela taxa histórica acumulada com ressalva inicial — **aprovada** como heurística simples e transparente. Reserva de risco de 15 minutos e mínimo de 8 conclusões são parâmetros operacionais conservadores, não garantias de segurança nem limiares estatísticos.

## Precedência, limites e não alteração de campanha

Esta decisão **complementa** a [GOV-V9-PROGRESS-ENTRADA-UNICA-SEM-PIPE-20261113](Decisao_GOV_V9_Progresso_Entrada_Unica_Console_Sem_Pipe_20261113.md) e **substitui somente** a antiga semântica `DENTRO_DO_PRAZO/FORA_DO_PRAZO` e a exclusão anterior de qualquer ETA; a ETA reintroduzida é explicitamente nova, derivada da vazão física medida, e não o diagnóstico de replay/estimativa baseada no tempo individual. O formato de uma entrada GitHub numerada por hipótese sem pipe continua vigente.

Permanecem inalterados os 324 TF4/81 opções, sementes/corpus, SQL/replay físico, estatística FDR/CP/P95, auditoria, contagem de rejeições, paralelismo AUTO, TEST selado, VALIDATION independente, SHA imutável, preflight de segurança, etapa física 340 min, TRAIN 360 min e execução 100k exclusivamente manual pelo proprietário. Não usar o status para desligar, abortar, reduzir ou repriorizar hipóteses, nem para publicar/certificar modelos.

## Implementação e critérios de aceite

Arquivos: `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`, testes de contrato em `Solution/tests/scripts/test_v9_calibrator_safe_failure_stage.py`, testes NUnit determinísticos em `Solution/tests/Jornada.Tests/Unit/V9NestedCalibrationTests.cs`, documentos e índices. O método puro de previsão deve ser testado para: sem amostra; 1–7 preliminares; fronteira de 8; margem >15, <=15 e negativa; exatamente no prazo e depois; 324/324; parâmetros inválidos. Testes de contrato exigem uma única linha sem pipes e um único lock/escrita/flush por progresso, diagnóstico opt-in preservado.

**Aceite de integração:** CI rápida `unit-runtime`, `security-guards` e `deterministic-build` 3/3 verde no SHA HEAD da PR; squash merge para `master`. A implementação somente afetará **novas execuções iniciadas pelo PO com o SHA atualizado**, não o run #43548040615. `IMPLEMENTADO`, `INTEGRADO`, `VALIDADO EM 100K` e `GANHO DE DESEMPENHO` são estados separados; sem nova prova física não alegar os dois últimos.

**Rastreabilidade:** branch `feat/v9-physical-progress-deadline-forecast-20261114`; [run #43548040615](https://github.com/lucianox777/Jornada/actions/runs/43548040615); GOV-DOC-01. [PR #1506](https://github.com/lucianox777/Jornada/pull/1506), [CI rápida #43550125833](https://github.com/lucianox777/Jornada/actions/runs/43550125833), squash `fe6b49a6a2e772b1da149ebab16944cca009f0bd`. GOV-DOC-01.

## Recibo de integração — 14/11/2026

A [PR #1506](https://github.com/lucianox777/Jornada/pull/1506) foi **integrada por squash merge** à `master` em `fe6b49a6a2e772b1da149ebab16944cca009f0bd`. O HEAD de PR `d75dce689d5b011bf51951245e62355e69bdcdb7` passou em **3/3** checks obrigatórios da [CI #43550125833](https://github.com/lucianox777/Jornada/actions/runs/43550125833). O build anterior #43549965920 detectou CA1512 no uso de exceções explícitas; o código foi corrigido via `ThrowIf...` em commit `0324d7dc`, revalidado. A verificação determinística NUnit da própria previsão foi adicionada ao gate rápido, sem executar SQL ou 100k. A integração não altera o run manual [#43548040615](https://github.com/lucianox777/Jornada/actions/runs/43548040615), iniciado no SHA `562e22b`; previsão nova só aparece em **futura execução manual**. CI completa e 30k pós-merge são provas independentes, não substituídas pelos três checks rápidos, e não são declaradas concluídas neste recibo.

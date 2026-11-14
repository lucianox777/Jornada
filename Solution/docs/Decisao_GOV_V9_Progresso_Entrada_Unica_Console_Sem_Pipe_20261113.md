# GOV-V9-PROGRESS-ENTRADA-UNICA-SEM-PIPE-20261113 — uma entrada numerada do GitHub por hipótese

**Autoridade:** Product Owner, 13/11/2026 (America/Sao_Paulo), esclarecimento com captura de tela de log anterior. **Estado:** DECIDIDO e IMPLEMENTADO/INTEGRADO à master pela [PR #1504](https://github.com/lucianox777/Jornada/pull/1504), squash SHA `ba64560669929be42062b0c383202a2dbd0a9383`; [CI rápida #43546556584](https://github.com/lucianox777/Jornada/actions/runs/43546556584) 3/3 SUCCESS. Validação visual em nova campanha 100k e ganho de desempenho físico NÃO MEDIDOS. **Política:** GOV-DOC-01.

## Contexto, evidência e interpretação

O PO apontou visualmente no console móvel do GitHub Actions a apresentação anterior das hipóteses `306/324` e `307/324`, cada uma com **um único número de linha do GitHub** (`642` e `644` no exemplo), mas cujo conteúdo longo **quebrava visualmente** em várias linhas na tela estreita. O formato anterior usava separadores `|`, incluía `ETA_min` e outros campos antigos e podia emitir `[V9 PERF DIAG]` em uma entrada adicional. O PO especificou: **“quero que apareça a informação do mesmo jeito que aparecia nessa imagem aí, só que com as informações novas e sem o pipeline”**. “Pipeline” refere-se ao separador textual *pipe* `|`, e não ao workflow do GitHub.

A revisão intermediária [GOV-V9-PROGRESS-CAMPOS-MULTILINHA-20261113](Decisao_GOV_V9_Progresso_Campos_Multilinha_20261113.md) implementou **um `Console.WriteLine` por hipótese contendo `\\n` entre os campos**. Isso gera sete **linhas físicas numeradas separadamente** no GitHub Actions, diferentemente da captura apresentada. A apresentação antiga de uma entrada numerada por hipótese tem precedência nesta decisão.

**Limitação explícita da interface:** uma única linha física impressa no console permanece uma única entrada numerada, mas o GitHub faz quebra de linha **automática e dependente da largura disponível**. Não é possível garantir “exatamente um campo por linha visual” em todos os dispositivos e, ao mesmo tempo, exigir uma única linha física numerada por hipótese no visualizador nativo. O PO priorizou a aparência da captura e a continuidade de uma entrada numerada por hipótese, sem `|`.

## Decisão vigente

Para cada conclusão da campanha V9 TF4 324, emitir **um único `Console.WriteLine` com uma única linha física de texto**, separando campos por **espaços** (sem `|`, `\\n`, `\\r` nem várias chamadas de `WriteLine`); permitir a quebra visual automática do GitHub na tela estreita. O conteúdo normal tem os **sete campos novos preservados** e exibe também o **percentual** anterior, como auxílio de leitura:

```text
[V9 PROGRESS] 38/324 (11.7%) timestamp=2026-11-13T18:35:22Z hipotese=047649776a35 resultado=AVALIADA restante=219min limite=2026-11-13T22:15:00Z status=DENTRO_DO_PRAZO
```

Exemplo ilustrativo, não evidência de execução. Em um smartphone, a linha acima pode aparecer em várias linhas **visuais**, mas com um só número de linha do GitHub. A posição de quebra varia conforme dispositivo e zoom.

**Manter exatamente um contador `Interlocked.Increment`, um `lock (Console.Out)`, um `Console.WriteLine` e um `Flush()` por hipótese concluída.** Manter `V9_PHYSICAL_PROGRESS_LOG=1` no workflow 324. Quando `V9_REPLAY_TIMING_DIAGNOSTICS=1` com progresso ativo, acrescentar `replay_elapsed_ms` **na mesma linha**, sem segundo log por hipótese; quando só o diagnóstico estiver ativo, manter o fallback `[V9 PERF DIAG]` separado. Diagnóstico permanece OFF por padrão. Mensagens operacionais por grupo SQL ou indexação diagnóstica não mudam.

**Campos e semântica:** `timestamp` e `limite` em UTC explícito; `restante` é a estimativa de minutos até o prazo físico, **não ETA de produtividade**; `status` é o estado relativo ao prazo. O limite informativo continua baseado em `V9_PHYSICAL_STEP_START_UTC + 340 min`, enquanto o GitHub aplica o timeout real do passo de 340 min e o job TRAIN permanece em 360 min.

## Escopo, riscos e precedência

- **Substitui exclusivamente** a apresentação textual em **sete linhas físicas por hipótese** da GOV-V9-PROGRESS-CAMPOS-MULTILINHA-20261113 e a proposta anterior de linha com pipes. Esses registros permanecem como histórico da evolução do requisito.
- **Preserva** 324 hipóteses TF4, 81 opções, misturas, margens, avaliação física SQL/replay, FDR/CP/P95, rejeições, audit callback, contagem e evidências, isolamento TRAIN/VALIDATION/TEST, segurança, hashes, parâmetros científicos, paralelismo e publicação.
- **Sem ETA antiga:** não restaurar `ETA_min`, `margem_etapa_315min` ou `tempo_individual_pos_SQL` só porque estavam na captura; usar os campos novos aprovados, mais o percentual de avanço.
- **Sem alteração retroativa:** a campanha manual [#43544864254](https://github.com/lucianox777/Jornada/actions/runs/43544864254), iniciada no SHA `61030c07cccad584863e8e7512e56379287cf0b1`, não muda com commits posteriores. Não cancelar nem reiniciar automaticamente.
- **Riscos:** quebra visual não é controlada pelo C#; diferentes larguras podem juntar dois campos em uma linha visual ou quebrar um valor longo; o ganho/desempenho físico não está medido. A mudança reduz quebras físicas no log e não altera a ciência.

## Implementação e critérios de aceite

**Arquivos:** `Solution/src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCoordinator.cs`, `Solution/tests/scripts/test_v9_calibrator_safe_failure_stage.py`, descrição da flag em `.github/workflows/v9-v3-four-name-100k.yml`, este documento e `Solution/docs/Indice_Decisoes_Vigentes.md`.

**Aceite:** prova estática/compilação de uma linha física por hipótese, sem pipes e sem `\\n` embutidos; campos novos e percentual; replay diagnóstico opt-in inline; um lock/WriteLine/Flush em progresso normal; contador consistente inclusive em rejeições; gate rápido obrigatório (`unit-runtime`, `security-guards`, `deterministic-build`) 3/3 verde; **squash merge**. Prova visual definitiva em GitHub Actions é futura, após campanha explicitamente disparada pelo PO; não alegar já validado em execução física.

**Rastreabilidade:** branch `fix/v9-progress-single-github-row-no-pipe-20261113`; screenshot informado pelo PO na discussão em 13/11/2026; [PR #1497 histórica](https://github.com/lucianox777/Jornada/pull/1497); [campanha #43544864254](https://github.com/lucianox777/Jornada/actions/runs/43544864254). [PR #1504](https://github.com/lucianox777/Jornada/pull/1504) e [CI rápida #43546556584](https://github.com/lucianox777/Jornada/actions/runs/43546556584). GOV-DOC-01.

## Recibo de integração — 13/11/2026

A [PR #1504](https://github.com/lucianox777/Jornada/pull/1504) foi integrada à `master` por **squash merge** no commit `ba64560669929be42062b0c383202a2dbd0a9383`. A [CI obrigatória #43546556584](https://github.com/lucianox777/Jornada/actions/runs/43546556584) concluiu **unit-runtime, security-guards e deterministic-build: 3/3 SUCCESS** no HEAD da PR `b1813c3777b837a795e0e5cc93ab7e9ff12c63c2`; o [preflight offline #43546556605](https://github.com/lucianox777/Jornada/actions/runs/43546556605) também foi aprovado. A integração é de **formato do log**, não medição de desempenho, certificação estatística ou TEST. A campanha #43544864254 usa o SHA original `61030c07` e **não** herda alteração visual; um futuro run só terá o novo formato se explicitamente iniciado pelo PO com SHA atualizado da `master`. GOV-DOC-01.

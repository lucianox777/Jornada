# V9 #1362 — duração de etapas no relatório analítico estatístico

**Decisão PO 09/11/2026; estado: IMPLEMENTAÇÃO INTEGRADA via PR #1376 (merge master 06705cc5798e6fe0c46b60abd10249c7e4f0f907), SEM PROVA FÍSICA PÓS-MERGE AINDA.** GOV-DOC-01.

## Motivo, definição de medição e limites
Até este PR, o relatório `mu-statistical-analysis.json/.md` tinha CPU/RAM, métricas e decisões, mas a duração só aparecia como `physicalCalibration`, `evidenceExport` e total no sidecar `runtime-diagnostics.json`. O PO decidiu exibir no próprio relatório a duração das etapas **realmente medidas**, não extrapoladas, sem reabrir escolhas científicas nem ativar modelo.

## Escopo implementado
- `input_preparation`: Stopwatch iniciado antes de ler CSV/manifesto; SHA, validação do manifesto e materialização de TRAIN.
- `physical_sql_blocking_fit_selection`: cronômetro do coordenador existente, que contém SQL, blocking, fit M/U/TF, avaliação e seleção **em fase composta**. Subetapas internas não são desmembradas: **NÃO MEDIDO** individualmente; não inventar particionamento ou somar etapas paralelas.
- `evidence_export_before_analysis`: tempo de escrita das saídas já existentes antes do relatório.
- `statistical_report_assembly`: tempo de montagem em memória das estruturas e Markdown; **não** inclui escrita final do próprio relatório. Total parcial é a soma de quatro estágios **sequenciais**, com escopo `ENTRADA_ATE_RELATORIO_EM_MEMORIA`. Tempos de persistência final, runtime JSON e SQL externo não são inferidos. `runtime-diagnostics.json` mantém `totalMilliseconds` histórico intacto e acrescenta `totalWallClockIncludingInputPreparationMilliseconds` e `stageDurations` para a execução abrangente.
- Tanto o relatório Markdown como o JSON mostram identificador, unidade milissegundo, estado `MEASURED`/`NOT_MEASURED`, escopo e ressalvas. A montagem do relatório não gera um novo fit, não altera snapshots/planos/contratos anteriores ou consumo de TEST.
- Tempos são informativos e dependem de máquina/concorrência. Não usados para ranking, limiar, TF, M/U, FDR, Gold, TEST ou DT-15.

## Testes e aceite
Teste unitário de totalização, ordem, estado não medido e negativos; CI obrigatória no SHA exato da PR e após o merge na master. **Até CI verde e prova física concreta, não declarar resultado observado nem encerrar #1362.** O formato mantém os campos existentes de análise e runtime (campos adicionais sem remover os anteriores). [Issue #1362](https://github.com/lucianox777/Jornada/issues/1362).

## Precedência
Complementa observabilidade de RAM da V9 sem revogar política de calibração, ausência de teto de registros, separação TRAIN/VALIDATION/TEST ou bloqueio de segurança #1253. O ítem de disco #1363 é separado; este PR não declara pico de disco.

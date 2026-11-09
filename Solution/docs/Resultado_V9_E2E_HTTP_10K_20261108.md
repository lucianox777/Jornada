# V9 — resultado real E2E HTTP 10.000 por 10 remessas (08/11/2026 BRT)

**Aceite técnico pontual: PASS** — ensaio [GitHub Action 42542220115](https://github.com/lucianox777/Jornada/actions/runs/42542220115), concluído em 09/11/2026 00:58 UTC, **head físico exato `2435b9c9c8cb08ed60b9008e108b65b0d954754a`**, branch `test/v9-e2e-http-10k-20261108`. Artifact oficial [13641174597](https://github.com/lucianox777/Jornada/actions/runs/42542220115/artifacts/13641174597) **SHA-256 ZIP `429d19eb69980083f263986ad4f05e3c31d387c53effe247d3c5b91501d63fd1`**, arquivo `http-volume-10k-summary.json` lido e conciliado. Os commits posteriores deste relatório não alteram nem retroativamente substituem o SHA do teste físico.

## Protocolo/escopo efetivamente realizado

**10.000 pessoas sintéticas, 10.000 fatos AA01, seed355**, organizados em **10 remessas HTTP sequenciais de 1.000 pessoas por ZIP**, uma chave de idempotência própria por remessa, SHA de ZIP diferente em cada lote, dentro do banco DEV `JornadaE2E` descartável. API real + armazenamento Bronze + Processor + Silver + Gold + Serving. Worker e banco apagados ao final. Código de produto e algoritmo de linkage científico não foram alterados. A prova é **de ingestão funcional volumétrica**, não da busca multiparâmetro V9.

| Métrica auditada em JSON agregado | Valor |
| --- | ---: |
| Status | **PASS** |
| Lotes HTTP/entregas independentes | **10/10** |
| Pessoas diferentes por CPF sintético | **10.000/10.000** |
| Arquivos Bronze | **10** |
| Pessoas Silver | **10.000** |
| Registros Silver | **10.000** |
| Itens processados | **20.000** |
| Pessoas Gold | **10.000** |
| Registros Serving | **10.000** |
| Replays HTTP com mesma chave e ZIP | **2/2 PASS**, sem duplicações |
| Tempo de parede total (10 lotes + overhead) | **414.815ms (6min54,815s)** |
| Throughput observado end-to-end | **24,107 pessoas/s** |
| p95 nearest-rank entre **10 durações de POST** | **76ms** |
| Maior POST | **76ms** |
| p95 nearest-rank entre **10 durações de entrega/processamento** | **48.589ms** |
| Maior duração de entrega | **48.589ms** |
| p95 individual de cidadão | **NÃO MEDIDO** (`null`) |

Latências por lote `elapsedMs` (milissegundos): **36510, 36477, 40504, 40558, 44552, 48589, 36463, 36502, 40490, 40497**. Latências de POST por lote: **26, 28, 26, 29, 28, 28, 33, 76, 29, 28ms**. O p95 nearest-rank com apenas dez observações é **estatística de pequena amostra e corresponde aqui ao máximo**, sem inferência de SLO operacional. Tempos de processamento incluem o polling configurado a cada quatro segundos; não interpretar como latência de serviço pura.

`commitSha` do JSON corresponde ao HEAD físico acima; `scientificValidation=false`, `activated=false` e `perPersonHttpP95Status=NOT_MEASURED_BULK_10_HTTP_REQUESTS`. Prova com dados sintéticos, sem publicação real. A repetição da mesma chave/ZIP no primeiro e último lote demonstrou idempotência; não há prova de replay em todos os dez lotes, nem de execução paralela sob concorrência intensa.

## Consequências e lacunas

**PASS técnico da rampa 10k HTTP→Serving**: a cadeia processou dez remessas sequenciais com reconciliação de todas as camadas e das identidades artificiais. Comparar diretamente throughput de 10k com 1k não caracteriza regressão: são execuções distintas com overhead e polling. **NÃO COBERTO:** 100k HTTP com múltiplas remessas, p95 individual, teste de carga concorrente, FDR/TP/FP de linkage V9 multiparâmetro em 100k, validação TEST/Gold científica, HML/PROD, ativação DT-15 e metas SLO oficiais. [Issue #1348](https://github.com/lucianox777/Jornada/issues/1348) segue aberta até a rampa 100k; [#1349](https://github.com/lucianox777/Jornada/issues/1349) permanece separada.

**Precedência GOV-DOC-01:** [decisão temática da rampa 10k](Decisao_V9_E2E_HTTP_10K_20261108.md), [relatório 1k](Resultado_V9_E2E_HTTP_1K_20261108.md), [SCALE médio SQL 100k (algoritmo V8)](Resultado_V9_Scale_Medium_100k_20261108.md), [índice vigente](Indice_Decisoes_Vigentes.md), [teste geral a–m](Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md).

**Reexecução pós-integração:** workflow e guard de shell passam de `push` da branch de teste para **somente `workflow_dispatch` manual na master**, sem agendar carga 10k ao atualizar documentos, sem alterar CI master ou probe 30k. A prova física anterior vincula-se ao SHA da branch, não à PR posterior; depois do merge, CI e probe 30k da master devem ser reavaliados no novo SHA antes de classificar o estado da master como verde.

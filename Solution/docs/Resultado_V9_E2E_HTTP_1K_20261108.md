# Resultado técnico — primeiro E2E HTTP real de 1.000 pessoas sintéticas (08/11/2026)

**GOV-DOC-01 / #1348, status: PASS técnico restrito.** O workflow [run #42541173969](https://github.com/lucianox777/Jornada/actions/runs/42541173969) foi **COMPLETED/SUCCESS**, tentativa 1, em 08/11/2026 (UTC 09/11 00h40) sobre o **SHA físico exato `655e340215e21d822288f995849061f664f5685a`**, branch de ensaio `test/v9-e2e-http-1k-20261108`. O commit que posteriormente integrar este relatório na master **não é o SHA deste teste**. Artifact oficial [13641457473](https://github.com/lucianox777/Jornada/actions/runs/42541173969/artifacts/13641457473), ZIP 655 bytes, **SHA-256 `b8e0983148f8992e57a2d19d06b0a90880676f33c6601b2ece33072758cf6f63`** confirmado independentemente. ZIP continha somente `http-volume-1k-summary.json`, CRC íntegro; não contém CPFs, ZIP de ingestão, JSONL, nomes ou segredos.

## Configuração e cobertura realizada

- **1.000 pessoas e 1.000 fatos AA01 exclusivamente sintéticos**, gerados deterministicamente a partir da fixture SEHAB/AA01 v2 e seed355, CPFs de teste distintos com dígitos verificadores. O hash do ZIP de ingestão sintético (`zipSha256`, não arquivado) foi `1e87f7bd3cdd076fb43d11ee80b1c5525ee8757358fac0dbcba4a15c9fe846fe`.
- **Caminho integrado físico realizado:** POST HTTP autenticado `202` → Bronze imutável → Processor → Silver → Gold → Serving; estado da Entrega `PROCESSADA`. O teste pequeno preexistente também passou antes da rampa.
- **Reexecução HTTP com a MESMA chave e o MESMO ZIP** preservou o `entregaId`, contagem de entregas=1, 1.000 Silver e 1.000 Serving, sem duplicação. Negativos de autenticação e payload seguem cobertos pelo E2E funcional preexistente, não por cem mil solicitações negativas.
- Banco `JornadaE2E`, Docker/SQL de desenvolvimento isolado e descartável. Cleanup após validação **SUCCESS**. `scientificValidation=false`, `activated=false`.

| Métrica conciliada por Entrega | Valor efetivamente observado |
| --- | ---: |
| Pessoas sintéticas do ZIP HTTP | **1.000** |
| Arquivos Bronze | **1** |
| Pessoas Silver | **1.000** |
| Fatos Silver | **1.000** |
| Itens processados na ingestão | **2.000** |
| Pessoas Gold com os CPFs da entrega | **1.000** |
| Registros Serving daquela Entrega | **1.000** |
| Replay HTTP com mesma chave/bytes | **PASS — sem duplicidade** |
| Duração POST + processamento até PROCESSADA | **24.316 ms = 24,316 s** |
| Vazão derivada dessa entrega, ponta a ponta | **41,125 pessoas/s** |
| Latência HTTP p95/p99 por requisição | **NÃO MEDIDA (uma remessa)** |

As contagens foram verificadas por consultas SQL amarradas ao `entrega_id` e `lote_id`, não por total global, e guardadas como JSON agregado com `commitSha` idêntico ao commit ensaiado.

## Resultado, limitações e próxima rampa

**PASS exclusivamente para 1k HTTP→Serving com uma remessa e replay idempotente** no ambiente DEV sintético. O resultado não cobre throughput sob 10k/100k com múltiplas remessas, p95 HTTP, 100k multiparâmetro V9, inferência TP/FP/FDR por identidade, performance de HML/PROD ou segurança operacional corporativa. A execução 100k de Calibrador/Runner SQL do [relatório SCALE medium](Resultado_V9_Scale_Medium_100k_20261108.md) é outra prova, com algoritmo operacional V8. Não substituir uma pela outra.

**Escalonamento aprovado somente como procedimento de engenharia e condicionado à prova:** [issue #1348](https://github.com/lucianox777/Jornada/issues/1348) — próxima etapa **10.000 via HTTP**, dimensionada em múltiplos ZIPs limitados a 1.000 cada, com reconciliação de lote a lote, backpressure, tempo/p95 de múltiplas entregas e gates fail-closed. 100k só após 10k e revisão de custo/tempo, não inferir prontidão automática. Manter `plan.json` oficial, contrato de ciência, TEST/Gold, FDR, HML/PROD e ativação intocados.

## Precedência operacional do workflow

A branch de stage usou trigger por `push` somente para obter esta prova. Após a execução, a PR de integração substitui o trigger por **`workflow_dispatch` manual e exclusivamente `refs/heads/master`**, sem execução automática de 1k por mudança documental ou alteração de motor. A CI obrigatória de master e gate físico 30k `push-only` seguem sem modificação. Este documento e [decisão temática](Decisao_V9_E2E_HTTP_1K_20261108.md) são integrados por PR protegida, com checks rápidos, CI completa pós-merge e 30k por impacto. A conclusão do **E2E 1k** não fecha a matriz [#1345](https://github.com/lucianox777/Jornada/issues/1345) nem as issues #1348/#1349 de 10k/100k e V9 científico.

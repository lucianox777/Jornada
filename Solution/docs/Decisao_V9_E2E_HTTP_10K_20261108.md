> **RESULTADO FÍSICO DE 10K (08/11 BRT, 09/11 00:58 UTC):** [Action #42542220115](https://github.com/lucianox777/Jornada/actions/runs/42542220115) **SUCCESS** em `2435b9c9c8cb08ed60b9008e108b65b0d954754a`, artifact `13641174597` SHA256 `429d19eb69980083f263986ad4f05e3c31d387c53effe247d3c5b91501d63fd1`; 10 remessas HTTP de 1k por ZIP, **10.000 Silver/Gold/Serving, 20.000 itens, 10 Bronze, 2 replays idempotentes**, total **414.815ms**, **24,107 pessoas/s**, p95 entre 10 lotes **48.589ms** e p95 POST **76ms**. **Não** mensura p95 individual, 100k HTTP nem V9 multiparâmetro 100k. [Relatório detalhado](Resultado_V9_E2E_HTTP_10K_20261108.md). Workflow passará de push na branch de ensaio a `workflow_dispatch` manual master-only; nenhum disparo automático deste volume por atualização. GOV-DOC-01.

# GOV-V9-E2E-HTTP-10K-20261108 — rampa 10.000 HTTP, lotes limitados

**PO e governança:** continuidade da [issue #1348](https://github.com/lucianox777/Jornada/issues/1348) — ensaiar a V9 pós-merge em escala crescente end-to-end. Etapa **1k comprovada PASS** em [run 42541173969](https://github.com/lucianox777/Jornada/actions/runs/42541173969), com relatório [por SHA](Resultado_V9_E2E_HTTP_1K_20261108.md). Rampa 10k implementada como **proposta técnica, resultado físico ainda PENDENTE**, auditável conforme GOV-DOC-01.

## Decisão operacional e justificativa

Para evitar que um único POST de 10.000 indivíduos contorne limites de payload, backpressure e contratos de negócio, manter o **limite imutável de 1.000 pessoas e 1.000 fatos por ZIP**, e executar **10 remessas sequenciais reais via HTTP** de uma mesma instalação efêmera SQL DEV. Cada remessa possui **chave idempotente própria**, ZIP com SHA único, 1.000 CPFs artificiais sem sobreposição e controles de reconciliação individual. O gerador de massa aceita índices 1..10.000, seed355 congelada e recusa contagens >1000 por ZIP. A massa é sintética, não toca HML/PROD, modelo de produção, Gold/TEST científico ou DT15.

**Justificativa de segurança e desempenho:** por lotes, é possível isolar erros e contabilizar 202/PROCESSADA, deduplicação, tempo de requisição e processamento por lote, latências p95 de **10 solicitações agregadas** e vazão total. O teste de 1k com um único envio **não mede p95**; esta rampa mede p95 do tamanho de lote (1k), **não latência por pessoa nem serviço sob alto paralelismo**. Não interpretar como SLO de HML.

## Contrato e fail-closed

1. Executar somente com `JORNADA_E2E_HTTP_VOLUME_COUNT=10000`, GitHub Actions `CI=true`, SHA exato da branch de ensaio `test/v9-e2e-http-10k-20261108`, banco `JornadaE2E` descartável, Actions pin SHA e SDK10/NuGet locked. Testar geração offline de **10×1.000 CPFs distintos e relações Pessoa/Registro** antes de subir SQL.
2. Cada remessa exige `HTTP 202`, entrega `PROCESSADA`, um lote, **1 arquivo Bronze, 1.000 Silver pessoas, 1.000 Silver registros, 2.000 itens processados, 1.000 Gold pessoas e 1.000 Serving registros**, vinculados à própria `entregaId/loteId`. Verificar replay de primeira e última remessa (mesma chave e ZIP) sem duplicação.
3. A série completa exige **10 entregas únicas, 10.000 CPFs exclusivos, 10 Bronze, 10.000 pessoas Silver/Gold/Serving, 10.000 fatos Silver e 20.000 itens processados**. Nenhuma contagem global de origem desconhecida pode substituir a conciliação por lote.
4. Reportar **tempo por POST e tempo por entrega**, p95 nearest-rank de 10 POSTs, p95 de 10 durações ponta a ponta e throughput total medidos. Guardar campo `perPersonHttpP95Ms=null` para evitar inventar p95 individual. Sem TP/FP/FDR do modelo V9 multiparâmetro.
5. Guardar artifact somente com JSON de métricas e SHA por remessa, sem CPF, pessoa, ZIP ou JSONL. `Docker Compose down -v` mesmo em falha. Reprovação de contrato, cardinalidade, bloqueio, autenticação, schema, idempotência ou timeout implica **FAIL**, sem bypass. Não disparar 100k automaticamente.
6. Rodar pelo workflow de stage `.github/workflows/v9-http-volume-10k-e2e-technical.yml`; se aprovado, congelar relatório no repo, trocar gatilho para manual e integrar via PR protegida, sempre respeitando CI/30k pós-merge no SHA exato da master. O status só muda para PASS após artifact real.

## Precedência e limitações

Complementa [GOV-V9-E2E-HTTP-1K](Decisao_V9_E2E_HTTP_1K_20261108.md), [teste geral a–m](Decisao_V9_Teste_Geral_E2E_Pos_Merge_20261108.md) e [issue 1345](https://github.com/lucianox777/Jornada/issues/1345). O teste 10k não substitui [100k SCALE SQL V8](Resultado_V9_Scale_Medium_100k_20261108.md) ou investigação do [espaço multiparâmetro V9 em 100k](https://github.com/lucianox777/Jornada/issues/1349). **A corrida 100k HTTP permanece NÃO COBERTA** até prova específica. Corpo técnico do motor, estatística, política FDR, seleção, LOCK e DT-15 não mudam.

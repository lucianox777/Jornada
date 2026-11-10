# Evidência V9 100k — tentativa física não concluída, 10/11/2026

**Estado:** diagnóstico, NÃO certificação e NÃO mudança científica. **GOV-DOC-01**. Master original `9540424ad3bc4721eab4d2635b44e320e04d73a7`. Esta evidência sucede [GOV-V9-100K-AUTO-RUN-20261110](Decisao_Autorizacao_Ensaio_V9_100K_AUTO_20261110.md), sem revogar SQL nativo como padrão, AUTO ou ordem de consultas estruturais.

## Resultados verificáveis dos logs e ZIPs dos Actions

- [Run #42753955131](https://github.com/lucianox777/Jornada/actions/runs/42753955131), [artifact #13735191738](https://github.com/lucianox777/Jornada/actions/runs/42753955131/artifacts/13735191738): validações pré-físicas, corpus 100k TRAIN seed42, SQL efêmero, referência congelada e preflight RAM/disco PASS; `MaximumParallelRuleSets=0` declarado. Iniciou fitting em `PHYSICAL_SQL_BATCHES`, mas falhou com `V9_CALIBRATOR_FAILED:INVALID_DATA`; `results/interrupted.json` informa `exceptionType=InvalidDataException`, `completed=false`. Causa raiz não revelada por opção de segurança.
- [Run #42754580416](https://github.com/lucianox777/Jornada/actions/runs/42754580416), [artifact #13735940258](https://github.com/lucianox777/Jornada/actions/runs/42754580416/artifacts/13735940258): mesmas etapas de preparação PASS; fitting cancelado em andamento, sem resultado final; não atribuir causa sem auditoria de cancelamento.
- Dois recibos apresentam `people=100000`, `knownTrainSeed=42`, `runtimeParallelismMode=AUTO_0`, `physicalEvaluations=24`, `witnessAndDiagnostics=16`, `candidateCpMultiplicity=8` e SHA de observações `6a2cb2ffd48d14b705faeb14ec4dd517bef94ced730e20a062e0dd5403c832b1`. **Esses números são previstos, não 24 avaliações concluídas**.
- O `calibration-resource-budget.json` mostra 4 vCPUs visíveis e `EffectiveParallelism=3` como **teto calculado**, não concorrência real comprovada. RAM host ≈15,61 GiB, ≈9,4–9,5 GiB disponíveis antes do fitting, disco ≈72,5 GiB. Padrão `disable_python_sql_budget=true`, `sqlMemoryLimitMb=null`; segurança e capacidade medidas antes da campanha não substituem prova final.
- Artefatos **não contêm auditoria final** das 24 configurações, FDR real, TP/FP/FN/latências nem prova de 3 workers simultâneos. Não houve TEST/Gold nem ativação.
- Consulta aos runs atuais identificou **zero em execução** e **zero na fila**; não houve cancelamento praticado nesta conversa. Issues [#1349](https://github.com/lucianox777/Jornada/issues/1349) e [#1253](https://github.com/lucianox777/Jornada/issues/1253) permanecem abertas.

## Encaminhamento técnico e limites

O log público foi corretamente sanitizado, mas `PHYSICAL_SQL_BATCHES` cobre várias subfases. Para identificar o ponto sem publicar segredos, a PR de triagem passa a anexar `safeFailureCategory` a `interrupted.json` usando **valores predefinidos em código** (`PHYSICAL_SQL_FRAME`, `PHYSICAL_FRAME_ORDER`, `FROZEN_TF_REFERENCE`, alguns códigos `RESOURCE_...`), e `UNCLASSIFIED` em todos os demais casos. A fonte não copia texto/stack/SQL de exceções de origem externa; o log local Development permanece integral conforme decisão anterior e o log CI/produção permanece sanitizado. Testes com canário sensível devem passar. **Esse diagnóstico não corrige por si só a InvalidDataException, não prova hipótese de memória nem autoriza disparar múltiplos jobs.**

**Risco e precedência:** falha técnica genuína ainda não localizada; novo run só deve ser feito após gate e preflight do proprietário. Nenhum teto FDR, TF, M/U, conjunto de opções ou governador é alterado; o estudo de 100k continua NÃO COBERTO.

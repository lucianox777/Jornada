# GOV-SEC-1253-RETENCAO-20261110 / GOV-V9-100K-PROBE-20261110 — retenção autorizada, risco aceito e diagnóstico seguro

**Data:** 10/11/2026. **Autoridade:** Product Owner. **Política:** GOV-DOC-01. **Incidente:** [#1253](https://github.com/lucianox777/Jornada/issues/1253). **Ensaio:** [#42863124223](https://github.com/lucianox777/Jornada/actions/runs/42863124223). **Rastreabilidade operacional:** [#1349](https://github.com/lucianox777/Jornada/issues/1349).

## 1. Decisão vigente do PO — retenção e encerramento por aceitação de risco

O Product Owner, em 10/11/2026, **revogou explicitamente a autorização anterior de expurgo** e determinou **manter os logs históricos como estão** e **encerrar a issue #1253**. A decisão anterior de exclusão, registrada nesta mesma data, foi **substituída antes de executada**. Nenhum log deve ser excluído por conta da autorização superada. Essa decisão documenta o risco residual aceito pelo PO, sem negar que houve exposição.

**Objeto:** execução pública [#41535062165](https://github.com/lucianox777/Jornada/actions/runs/41535062165), job `125584814018`, com oito ocorrências legíveis da credencial SQL SA de um serviço de teste efêmero; o log continuava acessível na última verificação. O caráter descartável do SQL reduz a probabilidade de reutilização útil da senha, mas **não demonstra risco zero**, tampouco prova por si só a exclusão do container ou a inexistência de reutilização em serviços persistentes. **Não foi constatada exposição de dados reais nem comprometimento de PROD**, mas a ausência de constatação não constitui auditoria completa.

**Fundamentos e impactos:** a falha geradora já recebeu correções preventivas nas PRs #1262/#1368 (credencial aleatória, mascaramento anterior ao uso, transmissão protegida por env-file); a PR #1403 implementou logs integrais somente nos sinks privados DEV/HML e códigos seguros em PROD/CI. Manter a evidência histórica auxilia a rastreabilidade, mas preserva um segredo antigo em log público. A aceitação expressa do risco residual é responsabilidade de governança do PO; não autoriza que credenciais sejam expostas em novas execuções.

**Conclusão autorizada:** fechar #1253 por **remediação preventiva concluída e risco histórico residual aceito pelo PO**, com essas ressalvas explícitas. **Não afirmar como fatos comprovados:** que nenhum terceiro acessou o segredo, que a senha jamais foi reutilizada, ou que a efemeridade garante risco nulo. Caso surja evidência de reutilização, serviço persistente, exposição de dados ou nova credencial pública, reabrir #1253 ou criar incidente vinculante.

**Precedência:** esta decisão **supera** a autorização anterior de exclusão da #1253 e a seção de expurgo deste documento que foi proposta na PR #1405 antes do merge. Preserva as correções de sigilo, os requisitos de preflight para novas campanhas e a política privada DEV/HML. Não altera `security_preflight_approved` de qualquer run anterior.

## 2. Resultado físico 100k após correção de diretório

O run manual [#42863124223](https://github.com/lucianox777/Jornada/actions/runs/42863124223), SHA `8dae6591393cf74e25f4ff47d13528b54104a32d`, atingiu a fase `PHYSICAL_SQL_BATCHES`, demonstrando que a correção #1404 eliminou o bloqueio anterior de `PLAN_VALIDATED_AND_OUTPUT_ADMISSION`. Falhou **antes da conclusão da primeira unidade de progresso** com `InvalidDataException`, categoria sanitizada `PHYSICAL_SQL_PROBE`. Isso não identifica ainda a condição exata de dados/SQL que rejeitou o frame, e **24/24 não foram concluídos**.

Artefato `#13786417961` reporta 15 amostras, pico SQL Docker `6472859 KiB`, cgroup SQL `6477228 KiB`, pico worker process tree `4629620 KiB`, mínimo `MemAvailable=3930172 KiB`; 0 novos eventos `oom` e `oom_kill` observados em SQL e runner, `OOMKilled=false`, `workerExitCode=1`. Nenhum sinal de OOM observado é prova de ausência universal de OOM; **não atribuir a falha a RAM** sem evidência causal. O modo SQL nativo sem orçamento Python e `AUTO_0` permaneceram válidos.

## 3. Decisão técnica — subclassificação permitida, sem vazar exceções

O agrupamento `PHYSICAL_SQL_PROBE` não diferencia 21 validações físicas SQL de mensagens fixas internas. Preservar a categoria ampla e adicionar ao recibo interrompido `safeFailureReasonCode` com **somente códigos enumerados em fonte**, obtidos por correspondência **exata** com mensagens estáticas já existentes; qualquer mensagem livre/contaminada cai em `UNCLASSIFIED_PHYSICAL_SQL_PROBE`. Fora desse escopo, `NOT_PHYSICAL_SQL_PROBE`. Não publicar `error.Message`, SQL, variáveis, connection string, identidade ou stack trace. Em DEV/HML privado, aplica-se separadamente a política de logs integrais; em PROD e CI, apenas código seguro.

**Testes e aceitação:** validar código de motivo conhecido, mensagem parcialmente semelhante com canário sensível, e exceção sem marcação; checks de build/security do HEAD exato. Não modificar algoritmo de blocking, TF, M/U, FDR, limites SQL/Docker, parâmetros 24/16/8, corpus, TEST/Gold ou ativação. Nova execução 100k continua manual owner-only, requer SHA correto, preflight real e será analisada após gates apropriados. Uma classificação mais precisa **não é correção causal** do probe.

## 4. Impactos, precedência e governança

A retenção substitui a autorização anterior de expurgo antes de qualquer exclusão; o encerramento da issue decorre de decisão de governança do PO com risco histórico residual expressamente aceito. O campo diagnóstico preserva o contrato de sigilo já vigente, complementa as decisões sobre triagem SQL, memória e [GOV-V9-100K-MONITOR-OUTPUT-20261110](Decisao_V9_100k_Monitor_Diretorio_Imutavel_20261110.md); não substitui a necessidade de ensaio físico nem sua auditoria. Rastreabilidade e decisões ficam registradas nos índices oficiais GOV-DOC-01.

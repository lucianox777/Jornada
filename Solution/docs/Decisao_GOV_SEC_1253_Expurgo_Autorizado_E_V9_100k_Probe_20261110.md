# GOV-SEC-1253-EXPURGO-20261110 / GOV-V9-100K-PROBE-20261110 — expurgo autorizado e diagnóstico seguro

**Data:** 10/11/2026. **Autoridade:** Product Owner. **Política:** GOV-DOC-01. **Incidente:** [#1253](https://github.com/lucianox777/Jornada/issues/1253). **Ensaio:** [#42863124223](https://github.com/lucianox777/Jornada/actions/runs/42863124223). **Rastreabilidade operacional:** [#1349](https://github.com/lucianox777/Jornada/issues/1349).

## 1. Decisão operacional do PO — exclusão dos logs problemáticos

O PO **autorizou expressamente a exclusão dos logs que expõem credenciais** da #1253. Alvo confirmado: logs completos do run histórico [#41535062165](https://github.com/lucianox777/Jornada/actions/runs/41535062165), job `125584814018`, com oito linhas em que a variável sensível de senha SQL ficou legível. **Excluir apenas logs do run, não a execução, seus metadados, histórico de resultados, nem evidência agregada saneada de outras campanhas.** Outras execuções somente após confirmação de exposição, sem exclusão indiscriminada. Não copiar, imprimir, republicar ou arquivar os segredos expostos.

**Estado:** autorização registrada, **EXCLUSÃO AINDA NÃO EXECUTADA**. A integração GitHub disponível nesta sessão não possui a operação mutante de exclusão de logs de workflows. O proprietário pode abrir a página do run e usar o menu superior `...` → **Delete all logs** → confirmar, ou usar a API com permissão Actions:write: `DELETE /repos/lucianox777/Jornada/actions/runs/41535062165/logs` (HTTP `204`). O endpoint elimina os logs, preservando os metadados; verificar resultado e registrar recibo na issue. **Nunca usar a operação que apaga o run inteiro por engano.** Referência: documentação oficial GitHub de exclusão de logs de workflow.

**Precedência e segurança:** autoriza o expurgo anteriormente pendente, sem declarar que já ocorreu, sem revogar o requisito de determinar eventual reutilização da credencial em ambiente persistente. Não encerra automaticamente #1253, não concede `security_preflight_approved=true` retroativo e não transforma logs DEV públicos em armazenamento privado. A política GOV-SEC-1253-LOGS-DEV-HML-PROD-20261110 continua válida.

## 2. Resultado físico 100k após correção de diretório

O run manual [#42863124223](https://github.com/lucianox777/Jornada/actions/runs/42863124223), SHA `8dae6591393cf74e25f4ff47d13528b54104a32d`, atingiu a fase `PHYSICAL_SQL_BATCHES`, demonstrando que a correção #1404 eliminou o bloqueio anterior de `PLAN_VALIDATED_AND_OUTPUT_ADMISSION`. Falhou **antes da conclusão da primeira unidade de progresso** com `InvalidDataException`, categoria sanitizada `PHYSICAL_SQL_PROBE`. Isso não identifica ainda a condição exata de dados/SQL que rejeitou o frame, e **24/24 não foram concluídos**.

Artefato `#13786417961` reporta 15 amostras, pico SQL Docker `6472859 KiB`, cgroup SQL `6477228 KiB`, pico worker process tree `4629620 KiB`, mínimo `MemAvailable=3930172 KiB`; 0 novos eventos `oom` e `oom_kill` observados em SQL e runner, `OOMKilled=false`, `workerExitCode=1`. Nenhum sinal de OOM observado é prova de ausência universal de OOM; **não atribuir a falha a RAM** sem evidência causal. O modo SQL nativo sem orçamento Python e `AUTO_0` permaneceram válidos.

## 3. Decisão técnica — subclassificação permitida, sem vazar exceções

O agrupamento `PHYSICAL_SQL_PROBE` não diferencia 21 validações físicas SQL de mensagens fixas internas. Preservar a categoria ampla e adicionar ao recibo interrompido `safeFailureReasonCode` com **somente códigos enumerados em fonte**, obtidos por correspondência **exata** com mensagens estáticas já existentes; qualquer mensagem livre/contaminada cai em `UNCLASSIFIED_PHYSICAL_SQL_PROBE`. Fora desse escopo, `NOT_PHYSICAL_SQL_PROBE`. Não publicar `error.Message`, SQL, variáveis, connection string, identidade ou stack trace. Em DEV/HML privado, aplica-se separadamente a política de logs integrais; em PROD e CI, apenas código seguro.

**Testes e aceitação:** validar código de motivo conhecido, mensagem parcialmente semelhante com canário sensível, e exceção sem marcação; checks de build/security do HEAD exato. Não modificar algoritmo de blocking, TF, M/U, FDR, limites SQL/Docker, parâmetros 24/16/8, corpus, TEST/Gold ou ativação. Nova execução 100k continua manual owner-only, requer SHA correto, preflight real e será analisada após gates apropriados. Uma classificação mais precisa **não é correção causal** do probe.

## 4. Impactos, precedência e governança

O expurgo é autorizado, mas executável somente por mecanismo com permissão Actions:write. O campo diagnóstico preserva o contrato de sigilo já vigente, complementa as decisões sobre triagem SQL, memória e [GOV-V9-100K-MONITOR-OUTPUT-20261110](Decisao_V9_100k_Monitor_Diretorio_Imutavel_20261110.md); não substitui a necessidade de ensaio físico nem sua auditoria. Rastreabilidade e decisões ficam registradas nos índices oficiais GOV-DOC-01.

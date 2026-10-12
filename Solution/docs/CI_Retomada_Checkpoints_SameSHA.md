# CI — Retomada por checkpoint de jobs (mesma execução)

**Decisão de engenharia (12/10/2026):** antes de eliminar outros builds, corrigir o maior gargalo operacional: quando uma execução falha, **não iniciar outra execução completa do mesmo commit**. O GitHub Actions já suporta `rerun-failed-jobs`, que conserva a identidade da execução e do SHA, reutilizando os jobs bem-sucedidos que não precisam ser reexecutados. Usar o recurso nativo, com supervisão e limites, em vez de criar um orquestrador paralelo de jobs.

## Estado durável

O checkpoint é o próprio `run_id` do GitHub Actions, incluindo a HEAD exata, conclusões de jobs e artefatos publicados. O watchdog consulta o registro do GitHub, não um timeout de conversa ou de conexão de API. Ele registra no `GITHUB_STEP_SUMMARY` as contagens de jobs `success`, falhos/bloqueados e `skipped`. Com `rerun-failed-jobs` os jobs que passaram ficam preservados (sujeito à semântica do grafo `needs`), enquanto falhos e seus dependentes são candidatos a reexecução. Artefatos reaproveitados devem permanecer sob o mesmo `run_id` e SHA.

## Gatilhos

- `workflow_run: completed` em `jornada-ci`: ao terminar em `failure`, dispara imediatamente o watchdog da **master confiável**; não faz checkout nem executa o código do PR analisado.
- `schedule` horário: reconcilia falhas que não tenham sido processadas imediatamente ou em caso de perda/atraso do evento.
- `workflow_dispatch`: diagnóstico por padrão; `apply=true` permite a mesma política restrita de reexecução para a HEAD vigente.

Permissão temporária: apenas `actions: write` para solicitar rerun e `contents: read` para consultar o agente versionado. Não criar nem guardar PAT. Apenas o watchdog tem direito de reexecutar; a pipeline de produto não recebe permissões elevadas.

## Decisão fail-closed

A reexecução é **automática apenas para falhas comprovadas de runner/infraestrutura** em `completed/failure`, `run_attempt == 1`, dentro de 24h, SHA ainda atual de PR aberto ou master e logs evidenciando problema de runner para cada job com `failure`. Não reexecutar `cancelled`, `timed_out`, `action_required`, falha de compilação, assertiva de teste, segurança ou erro desconhecido. O agente não cancela, não ignora gate, não altera PR e não realiza merge nessa rotina. `concurrency.cancel-in-progress: false` evita matar sua própria verificação.

Para falhas determinísticas, o agente externo poderá criar **correção testada em uma nova HEAD**, mas isto altera o SHA: todos os gates exigidos precisam provar o novo commit. Não é correto reutilizar automaticamente a aprovação de outro SHA. Um futuro cache por módulo poderá compartilhar trabalho **somente após provar equivalência integral do input (fontes, transitive deps, lockfiles, toolchain, geração, regras de segurança)**, permanecendo os gates visíveis e fail-closed no SHA novo. Não implementar bypass oportunista de gates.

## Critérios de aceitação

1. Testes do `ci-watchdog.py` afirmam que jobs bem-sucedidos fazem parte do checkpoint, runs antigos e falhas de programa não são reexecutados e o endpoint invocado é **somente** `/rerun-failed-jobs`.
2. Verificar gatilho `workflow_run` em execução real após merge, inclusive ausência de loop em `run_attempt > 1`.
3. CI integral e checks de segurança verdes na HEAD exata do PR, sem mudar os contratos SQL/E2E/DT10.
4. Produzir as métricas: **quantos jobs foram preservados, minutos de runner poupados por reexecução parcial, tempo de recuperação**. Não prometer speedup em reparos que criaram um novo SHA.

**Isolamento:** nenhum acesso a `JornadaLocal`, dados reais, referência IBGE, NAS, NODE, HML/PROD ou volumes locais. Só GitHub Actions.

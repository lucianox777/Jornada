# GOV-V9-100K-MEMORIA-FISICA-20261110 — evidência de pressão de memória SQL/Docker/C#

**Data:** 10/11/2026. **Origem:** PO orientou priorizar o erro físico atual e sua possível relação com memória SQL/Docker, em vez de perseguir o antigo INVALID_DATA genérico. **Estado:** instrumentação em revisão. **Governança:** GOV-DOC-01.

## Contexto e evidência

A execução manual #42804299288 (SHA a1e0c1e) falhou no ajuste físico após inicialização de SQL e referência histórica; o marcador PHYSICAL_SQL_FRAME não comprova OOM. O pré-SQL automático #42841413291 (master b48f193e) concluiu PRE_SQL_CAPACITY_ADMITTED_NOT_FITTED para o corpus 100k, excluindo somente rejeição dos limites pré-SQL verificados, não erros posteriores.

Na configuração vigente, `disable_python_sql_budget=true` preserva o padrão nativo do SQL: sem `MSSQL_MEMORY_LIMIT_MB` e sem limite Docker específico. O governador AUTO do C# decide admissões por RAM/CPU, mas o SQL pode competir por memória do mesmo runner durante as avaliações. Isso é hipótese causal, não prova.

## Decisão e implementação

Medir **sem mudar limites** durante o fitting físico manual: pico de memória do container SQL via `docker stats`, RSS agregado da árvore do worker C#, menor RAM disponível do host, diferenças de contadores cgroup `oom`/`oom_kill` e estado `OOMKilled` do container. Salvar somente valores agregados e sinal fixo, sem linhas do corpus, SQL, credenciais, exceções ou argumentos. Preservar exatamente exit code do worker, falhar sem suprimir diagnóstico do calibrador, e anexar JSON mesmo após falha. O monitor é leitura somente, não certifica nem muda FDR, 24/16/8, TF, M/U, TEST, Gold, governor, SQL default ou disparo manual.

**Precedência:** decisões de 09/11 SQL nativo como padrão e governador AUTO permanecem; a medição não revoga nenhuma delas. Se evento OOM for observado, avaliar política de limites SQL/Docker e reserva de memória C# com comparação no mesmo corpus; exigir nova decisão/documentação antes de alterar padrão. Sem OOM observado não exclui pressão de memória. Preflight de segurança #1253 permanece obrigatório para nova execução owner-only. Nada é autorizado automaticamente.

## Aceite

CI rápida e canários de máscara/fluxo; PR integrável somente com gates no HEAD. Nunca chamar o workflow físico automaticamente. O próximo run manual, se autorizado e com preflight real, deverá produzir `results/physical-memory-evidence.json` junto ao `interrupted.json` ou relatório final; comparar uso agregado e rejeições de orçamento antes de mudar dimensionamento.

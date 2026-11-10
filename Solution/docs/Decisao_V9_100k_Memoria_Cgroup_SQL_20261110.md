# GOV-V9-100K-CGROUP-SQL-20261110 — evidência específica do cgroup SQL

**Data:** 10/11/2026. **Origem:** prioridade do PO de investigar consumo concorrente de memória SQL/Docker e calibrador C# após a falha física 100k. **Governança:** GOV-DOC-01. **Estado:** implementação instrumental em PR.

## Contexto
A calibração física #42804299288 falhou; não há prova conclusiva de OOM. O pré-SQL do run #42841413291 terminou `PRE_SQL_CAPACITY_ADMITTED_NOT_FITTED`. PRs #1400 e #1401 integraram amostragem de memória SQL/Docker e retenção da flag `State.OOMKilled`. Porém, o contador de eventos em `/sys/fs/cgroup/memory.events` do runner pode não refletir o cgroup específico do SQL. O Docker efêmero `--rm` ainda pode desaparecer antes de qualquer inspeção conclusiva de `OOMKilled`.

## Decisão técnica, justificativa e implementação
Complementar a medição durante o fitting **manual** com a leitura somente de `/sys/fs/cgroup/memory.events` e `memory.peak` **dentro do container SQL**, usando comando fixo sem argumentos pessoais ou credenciais. Agregar exclusivamente amostras válidas, pico em KiB e diferenças observadas de `oom` e `oom_kill`, relativas à primeira amostra válida. Preservar o máximo observado mesmo que o container depois desapareça. Se o cgroup v2 não estiver disponível, registrar `null` e zero amostras, **nunca inventar ausência de OOM**. A amostragem não capta eventos ocorridos antes da primeira leitura nem necessariamente eventos entre última leitura e remoção do container; não apresentar como certificação causal.

## Precedência, impactos e limites
Complementa GOV-V9-100K-MEMORIA-FISICA-20261110 (#1400) e GOV-V9-100K-RETENCAO-OOM-20261110 (#1401), sem revogar decisões anteriores. Acrescenta pequena carga de leitura de arquivos cgroup e chamada `docker exec` durante execução manual; não impõe novos limites de memória, não muda governador AUTO, SQL nativo, TF/M/U/FDR, 24/16/8, TEST/Gold, segurança ou ativação. Nenhuma campanha é disparada automaticamente. Segurança #1253 e provas SHA-bound continuam pré-condições.

## Rastreabilidade e aceite
Regressão de parser com dados sintéticos/indisponíveis, gates de PR no HEAD exato, validação de fluxo sanitizado e registro no índice de decisões. Somente nova campanha owner-only, autorizada e com preflight verdadeiro, poderá produzir evidência real de consumo SQL; resultado não valida 24×100k por si só.

## Revisão consolidada da mesma PR — monitoramento do runner e memória do SQL

**Contexto:** avaliação técnica do PO constatou que a leitura de `/sys/fs/cgroup/memory.events` sem resolver a hierarquia do processo poderia observar o cgroup errado; e que `/proc/PID/task/PID/children` poderia não incluir processos filhos criados por threads diferentes. Os dados incompletos poderiam ocultar pressão de memória mesmo quando o SQL já estivesse monitorado. A revisão amplia esta mesma PR #1402, sem criar trabalho paralelo.

**Decisões técnicas complementares aprovadas para implementação:**

1. Resolver o diretório cgroup v2 do **próprio processo** pela relação entre `/proc/self/cgroup` e a raiz/mountpoint de `/proc/self/mountinfo`. Ler `memory.events` apenas quando o arquivo efetivo estiver disponível; caso contrário produzir `null`. Registrar `runnerCgroupV2Resolved`, sem publicar caminhos de sistema.
2. Construir o conjunto de descendentes do calibrador por **PPID em `/proc/PID/stat`** para todos os PIDs visíveis, em vez de consultar apenas a lista `children` da thread principal. Somar RSS por processo, sem duplicação; tratar processos encerrados durante a amostra como observação incompleta, nunca como consumo zero comprovado.
3. Acrescentar snapshot numérico e estritamente limitado dos limites de memória que podem afetar SQL Server: `memory.memorylimitmb` explícito lido por `mssql-conf get`, override numérico `MSSQL_MEMORY_LIMIT_MB` quando existir, `Docker HostConfig.Memory` e `memory.max` do cgroup SQL. Nenhuma saída bruta, credencial, argv de processo ou texto de exceção compõe o recibo. Valores não configurados/indisponíveis permanecem **`null`**, sem inventar um limite padrão de 80%. Diferenciar limites declarados de pico efetivamente observado.
4. Adicionar regressões determinísticas para cgroup montado com raiz distinta, PPID de descendentes, memória explícita de SQL/Docker e valores desconhecidos. O intervalo de amostra padrão permanece 10 s: o kernel `memory.peak` ajuda a observar picos de cgroup, mas não garante captura de todos os instantes.

**Precedência:** estas mudanças complementam apenas observabilidade da #1402 e as decisões #1400/#1401. Não há autorização para mudar orçamento SQL, AUTO, 24/16/8, estatística, segurança ou modalidade manual. A hipótese de competição por memória continua não comprovada. **Rastreabilidade:** PR #1402, issue #1349, segurança #1253 (aberta), run pré-SQL #42841413291 e falha física #42804299288.

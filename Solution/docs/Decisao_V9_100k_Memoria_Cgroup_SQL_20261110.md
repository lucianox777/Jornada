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

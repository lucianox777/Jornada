# GOV-V9-100K-SQL-PY-OPTOUT-20261109 — opção manual de dispensar dimensionamento Python de memória SQL

**Autoridade:** Product Owner, 09/11/2026. **Estado:** autorizado como opção a implementar em PR; **sem execução 100k comprovada**. **Política:** GOV-DOC-01.

## Decisão, contexto e justificativa

O PO deseja executar o Calibrador V9 com 100.000 identidades sintéticas sem obrigar que o script Python `Solution/scripts/v9-100k-execution-resource-budget.py` determine o limite de memória do SQL Server. Essa decisão surge após a retirada da V9-Lite: a V9 completa, com governador C# e `MaximumParallelRuleSets` (0 AUTO, valores positivos explícitos), permanece como única arquitetura. A capacidade futura de aumentar vCPUs/RAM no data center motiva testar opções de provisionamento de SQL sem associar essa decisão a qualquer alteração na ciência, no corpus ou no motor de calibração.

**Fronteira técnica:** o Python **não altera a memória do SQL Server diretamente**. No workflow manual `.github/workflows/v9-multiparameter-100k-technical.yml`, calcula dois limites e exporta `V9_SQL_MEMORY_LIMIT_MB` e `V9_SQL_CONTAINER_MEMORY_MB` via `GITHUB_ENV`. O `Solution/scripts/v9-tf-start-disposable-sql.sh` então aplica `MSSQL_MEMORY_LIMIT_MB` e `docker run --memory`. Se tais variáveis não forem definidas, o bootstrap SQL já inicia sem teto imposto por esse workflow. No uso local direto de `--v9-calibrate`, o Worker C# **não invoca esse Python**.

## Comportamento configurável (somente workflow manual 24×100k)

Adicionar entrada booleana de `workflow_dispatch` **`disable_python_sql_budget`**, padrão `false`, espelhada na variável do job `V9_100K_DISABLE_PYTHON_SQL_BUDGET`.

- **`false` (padrão, sem mudança):** executar `v9-100k-execution-resource-budget.py` nas fases `plan-sql` e `before-fit`, selecionar o teto SQL pelo orçamento real (2048/3072/4096 MiB), aplicar limites Docker/SQL já existentes, registrar recibos originais e executar o C# normalmente.
- **`true` (opt-in explícito):** **não executar esse Python em nenhuma das duas fases** e **não exportar valores** para `V9_SQL_MEMORY_LIMIT_MB`/`V9_SQL_CONTAINER_MEMORY_MB`. O bootstrap SQL efêmero mantém suas proteções de senha/loopback, porém deixa o SQL e Docker com os limites de memória padrão do runtime; não interpretar isso como memória infinita nem como performance garantida.
- **Segurança do opt-out:** substitui somente a admissão de recursos do Python por verificações mínimas em Bash sobre `MemTotal`, `MemAvailable`, eventual teto/uso cgroup v2 e disco: antes do SQL, host >=12 GiB, RAM efetiva livre >=10 GiB e disco >=6 GiB; antes do fitting, RAM efetiva livre >=6 GiB e disco >=3 GiB. Valores ilegíveis/insuficientes **falham fechados**, não fazem fitting e geram evidência agregada `V9_100K_SQL_MEMORY_NATIVE_PREFLIGHT_V1`, sem segredos. O Bash rejeita variáveis SQL limitadoras presentes para não mascarar falsa desativação.
- **Invariantes:** SHA exato da master, `workflow_dispatch`, ator proprietário, confirmação de preflight de segurança, `JornadaHarness` descartável, credential mascarada e limpa, corpus clean 100k seed42, pin oficial, 24/16/8 opções, V2/V3/misturas/TF/M/U/FDR/81 células, TEST e Gold ausentes, nenhuma certificação, publicação ou ativação. Governador **interno C# permanece ativo**; o trabalho ainda pode falhar por pressão de memória ou tempo.

## Precedência, riscos e rastreabilidade

Esta opção **especializa somente a política de SQL externo do 100k** em [GOV-V9-100K-RECORRECURSOS-20261109](Decisao_V9_100K_Orcamento_Adaptativo_Execucao_20261109.md), cujo dimensionamento Python segue obrigatório no modo padrão. **Não revoga** [GOV-V9-CALIBRADOR-RECURSOS-INTERNOS-20261109](Decisao_V9_Orcamento_Adaptativo_Interno_Modo_Calibracao_20261109.md), nem os gates científicos e a política de segurança. A [issue #1253](https://github.com/lucianox777/Jornada/issues/1253) permanece aberta; `security_preflight_approved=true` só pode ser fornecido após a verificação competente, não automaticamente por este opt-out. Esse registro não autoriza executar campanha agora.

**Risco operacional explícito:** sem teto `MSSQL_MEMORY_LIMIT_MB`/Docker, SQL pode consumir mais memória e prejudicar o Worker; cgroup, kernel, SQL e Docker ainda impõem seus próprios limites. Monitore RSS, memória, disponibilidade SQL e tempos. O preflight é limiar de **admissão**, não garantia de conclusão 100k; execução manual deve registrar resultados e FAIL de forma íntegra.

**Implementação proposta:** branch `feature/v9-100k-disable-python-sql-budget-20261109`, a integrar somente com validação dos gates e aprovação da mudança. Nenhum benchmark Lite, TEST/Gold ou modelo de produção é alterado.

# Jornada — Runbook de Volumetria HML

## Objetivo

Este runbook produz as evidências técnicas da issue #93 sobre infraestrutura **já carregada com massa representativa em HML**. Ele não define capacidade de Produção, não escolhe thresholds por conta própria e não substitui decisão institucional.

A coleta é separada em quatro superfícies:

1. Parameters Worker + Linkage Runner em escala real, sem ativação/publicação;
2. SQL Server: Query Store, bloqueios, waits e deadlocks em janela observada;
3. API de projeção de Pessoas em lotes 1/10/100/1000;
4. passagem integrada dos gates HML depois que políticas/baselines forem formalmente aprovados.

## 1. Pré-condições

Antes da coleta:

- ambiente identificado como HML, nunca Produção;
- banco marcado pelo provisionamento com `Jornada.EnvironmentProfile=HML`;
- banco com `Jornada.SolutionSchema=3.70`;
- exatamente uma referência nominal `ATIVA` com fingerprint;
- massa de Pessoas/observações já carregada pelo procedimento institucional de HML;
- build/commit exato conhecido;
- conexão SQL injetada por secret store/variável de ambiente;
- Parameters Worker e Runner correspondentes ao mesmo commit disponíveis;
- nenhum operador concorrente executando calibração.

O harness de escala HML **não executa reset, seed, carga de corpus sintético, ACTIVATE nem publicação probabilística**. Ele cria somente um modelo `RASCUNHO -> VALIDADO` e um `linkage_run` de `MODEL_VALIDATION` com `publish=false`.


### 1.1. Marcador residente do ambiente

O runner **não cria nem altera** o marcador de ambiente. Ele deve ser definido pelo provisionamento de HML, fora do DDL canônico e preferencialmente com uma credencial administrativa diferente da credencial usada pelo ensaio.

Execute uma vez no banco HML:

```sql
IF EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.EnvironmentProfile'
)
    EXEC sys.sp_updateextendedproperty
        @name=N'Jornada.EnvironmentProfile',
        @value=N'HML';
ELSE
    EXEC sys.sp_addextendedproperty
        @name=N'Jornada.EnvironmentProfile',
        @value=N'HML';
```

Valide antes da coleta:

```sql
SELECT CONVERT(nvarchar(32), value) AS environment_profile
FROM sys.extended_properties
WHERE class=0 AND name=N'Jornada.EnvironmentProfile';
```

O resultado deve ser exatamente `HML`. Ausência, valor diferente ou marcador de outro ambiente faz o `HML_SCALE_EVIDENCE` falhar fechado. O nome do banco continua sendo uma guarda adicional, não a fonte de verdade do ambiente.

## 2. Evidência Parameters/Runner

### 2.1. Execução a partir da árvore de fontes

No diretório `Solution`, configure a conexão e o SHA exato da build.

PowerShell:

```powershell
$env:ConnectionStrings__Jornada = '<connection string HML>'
$env:Ensaio__Mode = 'HML_SCALE_EVIDENCE'
$env:Ensaio__BaselineSha = '<sha git de 40 caracteres>'
$env:Ensaio__SaidaDir = '.local\hml-volumetria'
$env:Ensaio__HmlScale__EnvironmentProfile = 'HML'
$env:Ensaio__HmlScale__AllowNonProductionWrites = 'true'
$env:Ensaio__HmlScale__Profile = 'hml-representative'
$env:Ensaio__HmlScale__MaxRecords = '100000'
$env:Ensaio__HmlScale__BatchSize = '10000'
$env:Ensaio__HmlScale__MaxParallelism = '4'
$env:Ensaio__Calibrador__Arguments = 'run --project src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj --configuration Release'
$env:Ensaio__Runner__Arguments = 'run --project src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj --configuration Release --'

dotnet run --project src/Jornada.Ensaio/Jornada.Ensaio.csproj --configuration Release
```

Shell POSIX:

```bash
export ConnectionStrings__Jornada='<connection string HML>'
export Ensaio__Mode=HML_SCALE_EVIDENCE
export Ensaio__BaselineSha='<sha git de 40 caracteres>'
export Ensaio__SaidaDir='.local/hml-volumetria'
export Ensaio__HmlScale__EnvironmentProfile=HML
export Ensaio__HmlScale__AllowNonProductionWrites=true
export Ensaio__HmlScale__Profile=hml-representative
export Ensaio__HmlScale__MaxRecords=100000
export Ensaio__HmlScale__BatchSize=10000
export Ensaio__HmlScale__MaxParallelism=4
export Ensaio__Calibrador__Arguments='run --project src/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.csproj --configuration Release'
export Ensaio__Runner__Arguments='run --project src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj --configuration Release --'

dotnet run --project src/Jornada.Ensaio/Jornada.Ensaio.csproj --configuration Release
```

Para restringir a janela temporal do corpus, defina `Ensaio__HmlScale__Since` com timestamp ISO-8601. O harness recusa valor inválido.

### 2.2. Guardas automáticas

A execução falha se:

- o perfil não for explicitamente `HML`;
- `AllowNonProductionWrites` não estiver `true`;
- o SHA da build não tiver 40 caracteres hexadecimais;
- o nome do banco indicar Produção (`prod`/`production`);
- o banco não possuir exatamente `Jornada.EnvironmentProfile=HML`;
- SolutionSchema não for 3.70;
- a referência nominal ativa não possuir fingerprint;
- houver mais de um novo modelo da janela de calibração;
- o modelo não terminar `VALIDADO`;
- o Runner não terminar `CONCLUIDO_SEM_PUBLICACAO`;
- `publicado_em` for preenchido;
- o modelo ativo mudar durante a coleta;
- faltar proveniência de ruleset/projeção;
- não houver Pessoas de referência ou registros elegíveis.

### 2.3. Artefatos

O diretório de saída recebe:

```text
hml-scale-<perfil>-<utc>.json
hml-scale-<perfil>-<utc>.json.sha256
```

O JSON usa `LINKAGE_SCALE_EVIDENCE_V1` e preserva:

- commit exercitado;
- perfil HML;
- tamanho da Gold de referência;
- tamanho materializado do run;
- tamanhos configurados de amostra/pool e tamanhos efetivamente observados pela calibração, sem tratá-los como a mesma métrica;
- versão/id/algoritmo do modelo;
- fingerprint do ruleset e da projeção;
- duração de `GENERATE_DRAFT`;
- duração do `MODEL_VALIDATION`;
- contagens do run;
- política de captura demonstrando `activateModel=false` e `publishLinkage=false`.

Valide a coerência imediatamente:

```bash
python3 scripts/performance-evidence-gate.py   .local/hml-volumetria/hml-scale-<perfil>-<utc>.json   --baseline config/hml/performance-baseline.json   --summary .local/hml-volumetria/performance-summary.json
```

Enquanto `performance-baseline.json` permanecer `PENDENTE`, esse gate prova estrutura, proveniência e coerência — **não aprovação de capacidade**.

## 3. Evidência SQL Server

Use uma janela durante carga representativa. O coletor não retorna texto SQL, parâmetros, Pessoa ou payload.

```bash
export SQLCMD_SERVER='<servidor HML>'
export SQLCMD_USER='<usuário técnico>'
export SQLCMDPASSWORD='<secret>'
export SQLCMD_DATABASE='<banco HML>'
export JORNADA_SQL_PERFORMANCE_WINDOW_SECONDS=300
export JORNADA_SQL_PERFORMANCE_OUT='.local/hml-volumetria/sql'

./scripts/local-sql-performance-evidence.sh
```

O relatório final deve conter Query Store, bloqueios correntes e deltas de lock waits/deadlocks na janela. A avaliação usa:

```bash
python3 scripts/sql-performance-evidence-gate.py   .local/hml-volumetria/sql/report.json   --policy config/hml/sql-performance-policy.json   --summary .local/hml-volumetria/sql/summary.json
```

A política permanece `PENDENTE` até que HML produza base suficiente para decisão de Plataforma/Operação.

## 4. Evidência API de projeção

Prepare um arquivo temporário com pelo menos 1.000 UUIDs autorizados para a credencial do Gestor. O harness usa o arquivo apenas como entrada e não persiste UUIDs, payloads ou respostas no artefato; somente o SHA-256 da entrada e métricas agregadas são gravados.

```bash
export JORNADA_HML_ACCESS_KEY='<secret>'

python3 scripts/api-projection-load-harness.py   --base-url 'https://<api-hml>'   --uuids-file '/caminho/uuids-hml.txt'   --gestor '<CODIGO_GESTOR>'   --iterations 10   --output '.local/hml-volumetria/api-projection.json'
```

Valide:

```bash
python3 scripts/api-projection-evidence-gate.py   .local/hml-volumetria/api-projection.json   --policy config/hml/api-projection-load-policy.json   --summary .local/hml-volumetria/api-projection-summary.json
```

## 5. Aprovação de baseline — somente após medição

Não copie automaticamente tempos observados para `config/hml/performance-baseline.json`.

A aprovação deve registrar explicitamente:

- perfil/corpus representativo;
- limites aprovados para geração de parâmetros e Runner;
- mínimos de Pessoas Gold e pendentes sem CPF;
- responsável e data;
- artefato de evidência + SHA-256;
- `approvalContext` compatível com commit/schema/ambiente/corpus.

O mesmo princípio vale para `sql-performance-policy.json`, `api-projection-load-policy.json` e `parameters.json`.

## 6. Passagem integrada

Depois de todos os contratos aplicáveis estarem realmente aprovados e os relatórios reais disponíveis:

```bash
export JORNADA_HML_PERFORMANCE_REPORT='/evidencia/hml-scale.json'
export JORNADA_HML_LINKAGE_REPORT='/evidencia/linkage-evaluation.json'
export JORNADA_HML_SQL_PERFORMANCE_REPORT='/evidencia/sql-performance.json'
export JORNADA_HML_API_PROJECTION_REPORT='/evidencia/api-projection.json'

./scripts/hml-readiness-gate.sh
```

O modo estrito é deliberadamente fail-closed. Enquanto #31 ou decisões institucionais da #93 permanecerem pendentes, o gate deve continuar falhando nos contratos correspondentes.

## 7. Critério técnico da parte de volumetria da #93

A parte de volumetria pode ser considerada evidenciada quando existir, para o mesmo contexto técnico:

- relatório Parameters/Runner com fingerprint e gate estrutural PASS;
- relatório SQL com janela representativa e gate estrutural PASS;
- relatório API 1/10/100/1000 com gate estrutural PASS;
- registro explícito do corpus, infraestrutura e duração da janela;
- thresholds/baselines aprovados pelos responsáveis, sem inferência automática;
- execução estrita dos gates aplicáveis sem usar massa sintética como substituto de HML.

Isso não encerra a decisão institucional de finalidade/compartilhamento da #93 e não encerra a homologação estatística do Linkage da #31.

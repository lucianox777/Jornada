# Diagnósticos DEV/HML: isolamento de ondas e custo do Linkage

## Guarda do ensaio sintético (#449)

O `local-synthetic-calibration.ps1` usa Bronze temporária acessível apenas
à própria API e ao próprio Processor. Se NODE1/NODE2 consumirem o mesmo banco,
um deles pode reservar uma entrega cujo ZIP está apenas nessa Bronze local.
O wrapper agora recusa o banco original ANTES de `local-db up` e da limpeza.

Use um banco descartável isolado, por exemplo:

```powershell
Copy-Item .env .env.synthetic.local
```

Edite `JORNADA_SQL_DATABASE=JornadaSyntheticDev` nessa cópia, preservando
a senha/porta da instância SQL existente. Dentro de `Solution`:

```powershell
$env:JORNADA_LOCAL_ENV_FILE=(Resolve-Path .\.env.synthetic.local).Path; $env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY=[Guid]::NewGuid().ToString('N'); .\scripts\local-synthetic-calibration.ps1 -People 20000 -Waves 3 -Seed 42
```

`-AllowSharedDatabase` autoriza explicitamente a exceção DEV, mas NÃO
desativa o preflight SQL que bloqueia Processors concorrentes. Para um
ensaio distribuído não destrutivo no banco original, com Bronze NAS,
idempotência por onda e auditoria de leases, a implementação integral
de #449 ainda é necessária.

## Crescimento do histórico de Linkage (#424)

```powershell
.\scripts\local-linkage-operational-metrics.ps1 -DatabaseName JornadaLocal
```

Consulta somente SELECT em Development/HML, sem reset, limpeza ou publicação.
Mostra estoque corrente por status, últimos 25 runs (separando PUBLICADO e
MODEL_VALIDATION), duração, linhas gravadas por run, crescimento diário,
distribuição de avaliações por observação e espaço utilizado pela tabela.
A série adicional mostra as 16 semanas UTC mais recentes com dados,
iniciadas na segunda-feira (independentemente de `DATEFIRST`), diferença
absoluta e variação percentual **somente entre semanas contíguas**.
Se houve semana sem registros, a comparação fica NULL, não é confundida
com crescimento zero. A semana corrente é parcial: para comparações
de taxa com períodos completos, excluí-la da interpretação.
As datas diária e semanal são calculadas convertendo `calculado_em`
(`DATETIMEOFFSET`) para UTC **antes** de extrair o dia. Isso evita que
registros próximos da meia-noite em fusos diferentes sejam atribuídos a
semanas distintas. Para exercitar a prova com instantes equivalentes e
viradas domingo/segunda, use o autoteste somente leitura:

```powershell
.\scripts\local-linkage-operational-metrics.ps1 -DatabaseName JornadaLocal -ValidateUtcBoundary
```

O workflow `jornada-powershell-local-gates` executa essa prova em
SQL Server descartável, sem criar dados de teste nas tabelas operacionais.

A migração aditiva `20260926_Linkage_Run_Incremental_Metrics_371.sql`
persiste `fresh_pending` e `reavaliados` como `BIGINT NULL` em
`identidade.linkage_run`. Para runs **INCREMENTAL novos**, o Runner grava
ambos na mesma transação curta que congela `linkage_run_item` e
`registros_elegiveis`. O banco exige valores não negativos,
`fresh_pending + reavaliados = registros_elegiveis` e impede
contadores preenchidos em outros modos. O relatório exibe as contagens
exatas e `100 * reavaliados / elegiveis`; sem elegíveis, a proporção
fica `NULL` (não há denominador).

**Runs históricos e modos FULL/REPLAY/ON_DEMAND/MODEL_VALIDATION ficam
`NULL` nos dois contadores.** Não inferir o estado da reserva histórica
por consultas atuais ou pela primeira avaliação. Essa etapa do trem 3.71
é aditiva e não muda sozinha o marcador `Jornada.SolutionSchema=3.70`;
o rebind final permanece condicionado à coordenação de #411.

As contagens permitem acompanhar a participação do estoque reavaliado,
mas não autorizam automaticamente uma fila causal de Fase 2 nem política
de retenção; essa decisão depende de medidas DEV/HML e da governança.

Ambos os diagnósticos são engenharia DEV; nenhum substitui o corpus real
independente e a homologação estatística da issue #31.

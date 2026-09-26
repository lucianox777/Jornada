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

Os contadores exatos `FreshPending` e `Reavaliados` são calculados pelo
Runner mas NÃO são persistidos em `linkage_run` no schema 3.70.
Consequentemente, as respectivas colunas do relatório são NULL. Não
confundir primeira avaliação encontrada no histórico com o estado da
reserva original. Persistência exata requer migração versionada no trem
3.71; #424 permanece parcialmente aberta.

Ambos os diagnósticos são engenharia DEV; nenhum substitui o corpus real
independente e a homologação estatística da issue #31.

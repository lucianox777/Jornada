param(
    [ValidateSet('up','reset','down','clean','status','logs','calibrate','linkage','linkage-diagnose')]
    [string]$Action = 'up',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$LocalDb = Join-Path $PSScriptRoot 'local-db.ps1'
$ClusterConfig = Join-Path $Root 'install\windows-production\Jornada.Cluster.Test.json'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
if (-not (Test-Path -LiteralPath $EnvFile)) {
    Copy-Item -LiteralPath $Example -Destination $EnvFile
    Write-Host 'Criado .env local com as credenciais sintéticas padrão de teste.'
}

function Format-CommandArgument {
    param([Parameter(Mandatory=$true)][AllowEmptyString()][string]$Value)
    if ($Value -notmatch '[\s''"`$&|<>]') { return $Value }
    return "'" + $Value.Replace("'", "''") + "'"
}

function Write-CommandLine {
    param(
        [Parameter(Mandatory=$true)][string]$Executable,
        [string[]]$Arguments = @()
    )
    $tokens = @((Format-CommandArgument $Executable))
    $tokens += @($Arguments | ForEach-Object { Format-CommandArgument ([string]$_) })
    Write-Host ("# " + ($tokens -join ' ')) -ForegroundColor DarkGray
}

function Get-EnvValue([string]$Name) {
    foreach ($line in Get-Content -LiteralPath $EnvFile) {
        if ($line -match '^\s*#' -or [string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '=', 2
        if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) { return $parts[1].Trim() }
    }
    return $null
}

function Invoke-Compose {
    param([Parameter(Mandatory=$true)][string[]]$ComposeArgs)
    Push-Location $Root
    try {
        Write-CommandLine 'docker' (@('compose','--env-file',$EnvFile) + $ComposeArgs)
        & docker compose --env-file $EnvFile @ComposeArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Get-SqlScalar([string]$Query) {
    $password = Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
    Push-Location $Root
    try {
        Write-CommandLine 'docker' @('compose','--env-file',$EnvFile,'exec','-T','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-P','<redacted>','-C','-d','JornadaLocal','-W','-h','-1','-b','-Q',"SET NOCOUNT ON; $Query")
        $lines = @(& docker compose --env-file $EnvFile exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -P $password -C -d JornadaLocal -W -h -1 -b -Q "SET NOCOUNT ON; $Query")
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
        $value = @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ }) | Select-Object -Last 1
        if ($null -eq $value) { return '' }
        return [string]$value
    }
    finally { Pop-Location }
}

function Invoke-SqlReport([string]$Query) {
    $password = Get-EnvValue 'JORNADA_SQL_SA_PASSWORD'
    if ([string]::IsNullOrWhiteSpace($password)) { throw 'JORNADA_SQL_SA_PASSWORD ausente do .env.' }
    Push-Location $Root
    try {
        Write-CommandLine 'docker' @('compose','--env-file',$EnvFile,'exec','-T','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-P','<redacted>','-C','-d','JornadaLocal','-W','-s','|','-b','-Q',"SET NOCOUNT ON; $Query")
        & docker compose --env-file $EnvFile exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd `
            -S localhost -U sa -P $password -C -d JornadaLocal -W -s '|' -b -Q "SET NOCOUNT ON; $Query"
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
}

function Invoke-Node2 {
    param([Parameter(Mandatory=$true)][string[]]$Command)
    Invoke-Compose -ComposeArgs (@('exec','-T','jornada-node2') + $Command)
}

function Ensure-LocalBlockingProjection {
    Write-Host 'Verificando projeção de blocking da massa sintética local (contadores do worker mostram apenas reconstruções/chaves novas desta chamada)...'
    Invoke-Node2 -Command @('env','Processor__Operation=REBUILD_LOCAL_BLOCKING','dotnet','/opt/jornada/apps/Jornada.Processor.Worker/Jornada.Processor.Worker.dll')
}

function Wait-NodeReady([string]$Name, [string]$Url) {
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
            if ([int]$response.StatusCode -eq 200) { Write-Host "$Name ready: $Url"; return }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    Invoke-Compose -ComposeArgs @('logs','--tail','120',$Name)
    throw "$Name não ficou ready: $Url"
}

function Show-Endpoints {
    $config = Get-Content -Raw -Encoding UTF8 $ClusterConfig | ConvertFrom-Json
    Write-Host ''
    Write-Host 'Cluster local pronto (4 containers canônicos):'
    Write-Host '  NODE1: http://127.0.0.1:5080'
    Write-Host '  NODE2: http://127.0.0.1:5180'
    Write-Host '  SQL:   localhost:14333'
    Write-Host '  NAS:   jornada-nas:445 / share bronze (host: localhost:1445)'
    Write-Host "  Config bundle: $($config.configurationBundleVersion) / SolutionSchema $($config.solutionSchema)"
    Write-Host ''
    Write-Host 'Monitor operacional:'
    Write-Host '  NODE1: http://127.0.0.1:5080/monitor'
    Write-Host '  NODE2: http://127.0.0.1:5180/monitor'
    Write-Host '  Com VIP/LB externo, use /monitor no endereço do balanceador.'
    Write-Host ''
    Write-Host 'Execuções únicas recomendadas no NODE2 (não ficam em background e não são agendadas):'
    Write-Host '  Calibrador completo: .\scripts\local-cluster.ps1 calibrate'
    Write-Host '    /opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll'
    Write-Host '  Linkage:             .\scripts\local-cluster.ps1 linkage'
    Write-Host '    /opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll'
    Write-Host '  Diagnóstico Linkage: .\scripts\local-cluster.ps1 linkage-diagnose'
    Write-Host '  Bronze Verify:'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Bronze.Verify/Jornada.Bronze.Verify.dll --help'
    Write-Host '  Linkage Evaluation (DEV/HML only):'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/tools/Jornada.Linkage.Evaluation/Jornada.Linkage.Evaluation.dll --help'
    Write-Host '  Integrador C#:'
    Write-Host '    docker compose --env-file .env exec -T jornada-node2 dotnet /opt/jornada/clients/Jornada.Integrador/Jornada.Integrador.CSharp.dll --help'
}

function Start-Nodes([switch]$Build) {
    $args = @('up','-d')
    if ($Build) { $args += '--build' }
    $args += @('jornada-node1','jornada-node2')
    Invoke-Compose -ComposeArgs $args
    Wait-NodeReady 'jornada-node1' 'http://127.0.0.1:5080/health/ready'
    Wait-NodeReady 'jornada-node2' 'http://127.0.0.1:5180/health/ready'
    Ensure-LocalBlockingProjection
    Show-Endpoints
}

function Invoke-Calibration {
    Ensure-LocalBlockingProjection
    $beforeText = Get-SqlScalar "SELECT ISNULL(MAX(versao),0) FROM identidade.modelo_linkage;"
    $before = [int]$beforeText
    Write-Host "Calibração iniciando após modelo v$before."
    Write-Host 'Referência IBGE canônica é materializada no bootstrap do ambiente; GENERATE_DRAFT usa Monte Carlo nominal para NOME/NOME_MAE e mantém nascimento condicionado ao blocking.' -ForegroundColor DarkYellow
    $ibgeMcPairCount = if ($env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT) { [int]$env:JORNADA_LINKAGE_IBGE_MC_PAIR_COUNT } else { 1000000 }
    $ibgeMcSeed = if ($env:JORNADA_LINKAGE_IBGE_MC_SEED) { [int]$env:JORNADA_LINKAGE_IBGE_MC_SEED } else { 20260917 }
    Write-Host "IBGE Monte Carlo nominal: pares_por_campo=$ibgeMcPairCount seed_pessoa=$ibgeMcSeed seed_mae=$($ibgeMcSeed+1)."
    Invoke-Node2 -Command @(
        'env',
        'LinkageParameters__Operation=GENERATE_DRAFT',
        'LinkageParameters__RunOnce=true',
        "LinkageParameters__IbgeNominalU__PairCount=$ibgeMcPairCount",
        "LinkageParameters__IbgeNominalU__Seed=$ibgeMcSeed",
        'dotnet',
        '/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    $count = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")
    if ($count -ne 1) { throw "Esperado exatamente um novo RASCUNHO; encontrados=$count." }
    $version = [int](Get-SqlScalar "SELECT MAX(versao) FROM identidade.modelo_linkage WHERE versao>$before AND status='RASCUNHO';")
    Invoke-Node2 -Command @('env','LinkageParameters__Operation=VALIDATE',"LinkageParameters__TargetVersion=$version",'LinkageParameters__RunOnce=true','dotnet','/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    Invoke-Node2 -Command @('env','LinkageParameters__Operation=ACTIVATE',"LinkageParameters__TargetVersion=$version",'LinkageParameters__RunOnce=true','dotnet','/opt/jornada/apps/Jornada.Linkage.Parameters.Worker/Jornada.Linkage.Parameters.Worker.dll')
    $active = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE versao=$version AND status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")
    if ($active -ne 1) { throw "Modelo v$version não ficou ATIVO como modelo calibrado." }
    $modelId = Get-SqlScalar "SELECT CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE versao=$version;"
    Write-Host "Calibração concluída: modelo calibrado v$version / ModeloId=$modelId ATIVO."
}

function Invoke-Linkage {
    Ensure-LocalBlockingProjection
    $active = [int](Get-SqlScalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO';")
    if ($active -ne 1) {
        throw "Linkage bloqueado: encontrados $active modelos calibrados ATIVOS. O seed sintético não libera execução. Execute primeiro '.\scripts\local-cluster.ps1 calibrate'."
    }
    $version = Get-SqlScalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
    $modelId = Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
    Write-Host "Executando linkage com modelo calibrado ATIVO v$version / ModeloId=$modelId."
    Invoke-Node2 -Command @('dotnet','/opt/jornada/apps/Jornada.Linkage.Runner/Jornada.Linkage.Runner.dll','--mode','ON_DEMAND','--publish','true','--requested-by','LOCAL_CLUSTER','--reason','manual-local-cluster')
}

function Show-LinkageDiagnosis {
    $activeModelId = Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status='ATIVO' AND ISNULL(amostra_metodo,'') <> 'SEED_DEV_FIXO_NAO_TREINADO' ORDER BY versao DESC;"
    if ([string]::IsNullOrWhiteSpace($activeModelId)) {
        throw "Diagnóstico bloqueado: nenhum modelo calibrado ATIVO. Execute primeiro '.\scripts\local-cluster.ps1 calibrate'."
    }

    $runId = Get-SqlScalar "SELECT TOP(1) CONVERT(varchar(36),linkage_run_id) FROM identidade.linkage_run WHERE status='PUBLICADO' AND tipo_run='ON_DEMAND' AND modelo_id='$activeModelId' ORDER BY publicado_em DESC,iniciado_em DESC,linkage_run_id DESC;"
    if ([string]::IsNullOrWhiteSpace($runId)) {
        throw "Nenhum linkage ON_DEMAND PUBLICADO para o modelo calibrado ATIVO $activeModelId. Execute primeiro '.\scripts\local-cluster.ps1 linkage'."
    }

    Write-Host "Diagnóstico do último linkage ON_DEMAND PUBLICADO do modelo ATIVO $activeModelId`: $runId"
    Write-Host 'Nota: modelo_versao é monotônica somente dentro da base corrente; clean/reset recria a base. Para A/B entre bases, compare modelo_id + fingerprints.'

    Write-Host ''
    Write-Host 'Resumo do run:'
    Invoke-SqlReport "SELECT CONVERT(varchar(36),linkage_run_id) AS run_id,CONVERT(varchar(36),modelo_id) AS modelo_id,modelo_versao,tipo_run,status,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,publicado_em FROM identidade.linkage_run WHERE linkage_run_id='$runId';"

    Write-Host ''
    Write-Host 'Proveniência do modelo, ruleset, projeção e thresholds efetivos:'
    Invoke-SqlReport "SELECT CONVERT(varchar(36),lr.modelo_id) AS modelo_id,lr.modelo_versao,m.algoritmo_versao,m.status AS modelo_status,m.amostra_metodo,m.amostra_pool_tamanho,m.amostra_m_tamanho,m.amostra_u_tamanho,rs.ruleset_versao,rs.fingerprint_sha256 AS ruleset_fingerprint,rs.projection_schema_version,rs.projection_fingerprint_sha256 AS projection_fingerprint,MAX(CASE WHEN p.nome='T_LINKAGE' THEN p.valor END) AS t_linkage,COALESCE(MAX(CASE WHEN p.nome='CONFLICT_MARGIN_LOG_ODDS' THEN p.valor END),MAX(CASE WHEN p.nome='CONFLICT_MARGIN' THEN p.valor END)) AS t_margem_efetivo,CASE WHEN m.algoritmo_versao='FELLEGI_SUNTER_DECISION_EVIDENCE_V6' THEN 'LOG_ODDS' ELSE 'POSTERIOR' END AS margem_espaco FROM identidade.linkage_run lr JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id LEFT JOIN identidade.linkage_ruleset rs ON rs.modelo_id=lr.modelo_id LEFT JOIN identidade.parametro_linkage p ON p.modelo_id=lr.modelo_id WHERE lr.linkage_run_id='$runId' GROUP BY lr.modelo_id,lr.modelo_versao,m.algoritmo_versao,m.status,m.amostra_metodo,m.amostra_pool_tamanho,m.amostra_m_tamanho,m.amostra_u_tamanho,rs.ruleset_versao,rs.fingerprint_sha256,rs.projection_schema_version,rs.projection_fingerprint_sha256;"

    Write-Host ''
    Write-Host 'Cobertura da fronteira de decisão (mostra se os thresholds foram realmente exercitados):'
    Invoke-SqlReport "DECLARE @modelo_id uniqueidentifier=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$runId'); DECLARE @alg nvarchar(100)=(SELECT algoritmo_versao FROM identidade.modelo_linkage WHERE modelo_id=@modelo_id); DECLARE @t decimal(18,8)=(SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome='T_LINKAGE'); DECLARE @tm decimal(18,8)=COALESCE((SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND nome=CASE WHEN @alg='FELLEGI_SUNTER_DECISION_EVIDENCE_V6' THEN 'CONFLICT_MARGIN_LOG_ODDS' ELSE 'CONFLICT_MARGIN' END),0); SELECT @t AS t_linkage,MAX(CASE WHEN score_melhor<@t THEN score_melhor END) AS maior_score_abaixo,MIN(CASE WHEN score_melhor>=@t THEN score_melhor END) AS menor_score_acima,SUM(CASE WHEN ABS(score_melhor-@t)<=0.02 THEN 1 ELSE 0 END) AS qtd_score_em_mais_menos_002,@tm AS t_margem,MAX(CASE WHEN margem IS NOT NULL AND margem<@tm THEN margem END) AS maior_margem_abaixo,MIN(CASE WHEN margem IS NOT NULL AND margem>=@tm THEN margem END) AS menor_margem_acima,SUM(CASE WHEN margem IS NOT NULL AND margem<@tm THEN 1 ELSE 0 END) AS qtd_margem_abaixo FROM identidade.linkage_resultado WHERE linkage_run_id='$runId';"

    Write-Host ''
    Write-Host 'Empates e saturação de apresentação:'
    Invoke-SqlReport "SELECT status,COUNT_BIG(*) AS qtd,SUM(CASE WHEN score_segundo IS NOT NULL AND margem=0 THEN 1 ELSE 0 END) AS empate_log_odds_exato,SUM(CASE WHEN score_segundo IS NOT NULL AND score_melhor=score_segundo AND ISNULL(margem,0)<>0 THEN 1 ELSE 0 END) AS posterior_igual_mas_log_odds_distinto,SUM(CASE WHEN score_segundo IS NOT NULL AND score_melhor=score_segundo AND margem=0 THEN 1 ELSE 0 END) AS posterior_e_log_odds_empatados FROM identidade.linkage_resultado WHERE linkage_run_id='$runId' GROUP BY status ORDER BY status;"
    Write-Host 'Em empate_log_odds_exato, UUID ordena apenas a representação determinística do empate; não constitui evidência de desempate.'

    Write-Host ''
    Write-Host 'u nominal usado no scoring de nomes (Monte Carlo IBGE):'
    Invoke-SqlReport "DECLARE @modelo_id uniqueidentifier=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$runId'); SELECT nome,valor FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND (nome IN('IBGE_MC_NOMINAL_U_ENABLED','IBGE_MC_NOMINAL_U_PAIR_COUNT','IBGE_MC_NOMINAL_U_SEED_PERSON','IBGE_MC_NOMINAL_U_SEED_MOTHER','IBGE_NAME_REFERENCE_ID','IBGE_MC_PERSON_EXACT_ANALYTIC','IBGE_MC_MOTHER_EXACT_ANALYTIC') OR nome LIKE 'U_NOME[_]%' OR nome LIKE 'U_NOME_MAE[_]%' OR nome LIKE 'IBGE_MC_SUPPORT_U_NOME[_]%' OR nome LIKE 'IBGE_MC_SUPPORT_U_NOME_MAE[_]%') ORDER BY nome;"

    Write-Host ''
    Write-Host 'Suporte condicionado ao blocking preservado para diagnóstico e nascimento:'
    Invoke-SqlReport "DECLARE @modelo_id uniqueidentifier=(SELECT modelo_id FROM identidade.linkage_run WHERE linkage_run_id='$runId'); SELECT nome,valor AS suporte FROM identidade.parametro_linkage WHERE modelo_id=@modelo_id AND (nome LIKE 'BLOCKING_SUPPORT_U_NOME[_]%' OR nome LIKE 'BLOCKING_SUPPORT_U_NOME_MAE[_]%' OR nome LIKE 'SUPPORT_U_NASCIMENTO_SEMANTICO[_]%' OR nome LIKE 'POOL_SUPPORT_U_NASCIMENTO_SEMANTICO[_]%') ORDER BY nome;"

    Write-Host ''
    Write-Host 'Composição atual do corpus Gold (explica SCALE versus seed/outros):'
    Invoke-SqlReport "WITH scale_gold AS (SELECT DISTINCT vc.pessoa_uuid FROM silver.pessoa_observacao po JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id WHERE vc.status='RESOLVIDO' AND po.codigo_pessoa_origem LIKE 'SCALE-SEHAB-%') SELECT COUNT_BIG(*) AS gold_total,SUM(CASE WHEN sg.pessoa_uuid IS NOT NULL THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END) AS gold_scale,SUM(CASE WHEN sg.pessoa_uuid IS NULL THEN CAST(1 AS bigint) ELSE CAST(0 AS bigint) END) AS gold_seed_ou_outros FROM gold.pessoa g LEFT JOIN scale_gold sg ON sg.pessoa_uuid=g.pessoa_uuid;"

    Write-Host ''
    Write-Host 'Qualidade contra ground truth sintético SCALE (verdade derivada do vínculo CPF da observação SEHAB correspondente):'
    Invoke-SqlReport "WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id='$runId' AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT COUNT_BIG(*) AS total_scale,SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END) AS resolvidos,SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END) AS resolvidos_corretos,SUM(CASE WHEN status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) THEN 1 ELSE 0 END) AS falsos_positivos,SUM(CASE WHEN status='CONFLITO' THEN 1 ELSE 0 END) AS conflitos,SUM(CASE WHEN status='CONFLITO' AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS conflitos_verdade_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' THEN 1 ELSE 0 END) AS nao_resolvidos,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND melhor_candidato_uuid=truth_uuid THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_primeiro_sem_empate,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 AND (melhor_candidato_uuid=truth_uuid OR segundo_candidato_uuid=truth_uuid) THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_empate_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND NOT(segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001) AND segundo_candidato_uuid=truth_uuid AND (melhor_candidato_uuid IS NULL OR melhor_candidato_uuid<>truth_uuid) THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_segundo_sem_empate,SUM(CASE WHEN status='NAO_RESOLVIDO' AND ISNULL(melhor_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid AND ISNULL(segundo_candidato_uuid,'00000000-0000-0000-0000-000000000000')<>truth_uuid THEN 1 ELSE 0 END) AS nao_resolvidos_verdade_fora_top2,SUM(CASE WHEN status='NAO_RESOLVIDO' AND segundo_candidato_uuid IS NOT NULL AND margem IS NOT NULL AND ABS(margem)<=0.000000001 THEN 1 ELSE 0 END) AS nao_resolvidos_empate_top2,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(SUM(CASE WHEN status='RESOLVIDO' THEN 1 ELSE 0 END),0) AS decimal(9,4)) AS ppv_sintetico_pct,CAST(100.0*SUM(CASE WHEN status='RESOLVIDO' AND pessoa_uuid_resolvido=truth_uuid THEN 1 ELSE 0 END)/NULLIF(COUNT_BIG(*),0) AS decimal(9,4)) AS sensibilidade_sintetica_pct FROM truth;"

    Write-Host ''
    Write-Host 'Falsos positivos resolvidos no corpus SCALE (deve ficar vazio em um ensaio conservador):'
    Invoke-SqlReport "WITH truth AS (SELECT r.*,po.codigo_pessoa_origem,tv.pessoa_uuid AS truth_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN silver.pessoa_observacao tpo ON tpo.codigo_pessoa_origem=REPLACE(po.codigo_pessoa_origem,'SCALE-PEND-','SCALE-SEHAB-') JOIN ref.gestor tg ON tg.gestor_id=tpo.gestor_id AND tg.codigo='SEHAB' JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=tpo.pessoa_observacao_id AND tv.status='RESOLVIDO' WHERE r.linkage_run_id='$runId' AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%') SELECT pessoa_observacao_id,codigo_pessoa_origem,score_melhor,score_segundo,margem,CONVERT(varchar(36),truth_uuid) AS truth_uuid,CONVERT(varchar(36),pessoa_uuid_resolvido) AS resolvido_uuid,CONVERT(varchar(36),melhor_candidato_uuid) AS melhor_candidato_uuid,CONVERT(varchar(36),segundo_candidato_uuid) AS segundo_candidato_uuid FROM truth WHERE status='RESOLVIDO' AND (pessoa_uuid_resolvido IS NULL OR pessoa_uuid_resolvido<>truth_uuid) ORDER BY codigo_pessoa_origem;"

    Write-Host ''
    Write-Host 'Não resolvidos/conflitos por motivo:'
    Invoke-SqlReport "SELECT r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,COUNT_BIG(*) AS qtd,MIN(r.score_melhor) AS score_min,AVG(r.score_melhor) AS score_medio,MAX(r.score_melhor) AS score_max FROM identidade.linkage_resultado r WHERE r.linkage_run_id='$runId' AND r.status<>'RESOLVIDO' GROUP BY r.status,r.motivo ORDER BY qtd DESC,r.status,r.motivo;"

    Write-Host ''
    Write-Host 'Detalhe dos não resolvidos/conflitos:'
    Invoke-SqlReport "SELECT r.pessoa_observacao_id,po.codigo_pessoa_origem,g.codigo AS gestor,r.status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,r.score_melhor,r.score_segundo,r.margem,CASE WHEN r.score_segundo IS NOT NULL AND r.margem=0 THEN 'EMPATE_EVIDENCIAL_UUID_APENAS_DETERMINISTICO' ELSE 'ORDEM_EVIDENCIAL' END AS ranking_interpretacao,CONVERT(varchar(36),r.melhor_candidato_uuid) AS melhor_candidato_uuid,CONVERT(varchar(36),r.segundo_candidato_uuid) AS segundo_candidato_uuid FROM identidade.linkage_resultado r JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id JOIN ref.gestor g ON g.gestor_id=po.gestor_id WHERE r.linkage_run_id='$runId' AND r.status<>'RESOLVIDO' ORDER BY po.codigo_pessoa_origem,r.pessoa_observacao_id;"

    Write-Host ''
    Write-Host 'Itens do universo fora de SCALE-PEND-* (explicam avaliados adicionais ao corpus de 1000 pendentes):'
    Invoke-SqlReport "SELECT ri.pessoa_observacao_id,po.codigo_pessoa_origem,g.codigo AS gestor,COALESCE(r.status,'SEM_RESULTADO') AS status,COALESCE(r.motivo,'SEM_MOTIVO') AS motivo,r.score_melhor FROM identidade.linkage_run_item ri JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ri.pessoa_observacao_id JOIN ref.gestor g ON g.gestor_id=po.gestor_id LEFT JOIN identidade.linkage_resultado r ON r.linkage_run_id=ri.linkage_run_id AND r.pessoa_observacao_id=ri.pessoa_observacao_id WHERE ri.linkage_run_id='$runId' AND po.codigo_pessoa_origem NOT LIKE 'SCALE-PEND-%' ORDER BY po.codigo_pessoa_origem,ri.pessoa_observacao_id;"
}

switch ($Action) {
    'up' {
        Write-CommandLine $LocalDb @('-Action','up')
        & $LocalDb -Action up
        if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 up falhou ($LASTEXITCODE)." }
        Start-Nodes -Build:(-not $NoBuild)
    }
    'reset' {
        Invoke-Compose -ComposeArgs @('stop','jornada-node1','jornada-node2')
        Write-CommandLine $LocalDb @('-Action','reset')
        & $LocalDb -Action reset
        if ($LASTEXITCODE -ne 0) { throw "local-db.ps1 reset falhou ($LASTEXITCODE)." }
        Start-Nodes
    }
    'down' { Invoke-Compose -ComposeArgs @('down') }
    'clean' { Invoke-Compose -ComposeArgs @('down','-v','--remove-orphans') }
    'status' { Invoke-Compose -ComposeArgs @('ps') }
    'logs' { Invoke-Compose -ComposeArgs @('logs','-f','jornada-node1','jornada-node2','jornada-nas') }
    'calibrate' { Invoke-Calibration }
    'linkage' { Invoke-Linkage }
    'linkage-diagnose' { Show-LinkageDiagnosis }
}
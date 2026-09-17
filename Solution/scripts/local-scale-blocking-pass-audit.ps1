param([int]$LabelCount = 500)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'

function Resolve-Python3 {
    foreach ($candidate in @(@{Name='python3';Prefix=@()},@{Name='python';Prefix=@()},@{Name='py';Prefix=@('-3')})) {
        $command = Get-Command $candidate.Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $command) { continue }
        $prefix = @($candidate.Prefix)
        & $command.Source @prefix -c 'import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)' 2>$null
        if ($LASTEXITCODE -eq 0) { return @{Exe=$command.Source;Prefix=$prefix} }
    }
    throw 'Python 3 não encontrado.'
}
function Get-EnvValues {
    $vars=@{}; Get-Content $EnvFile | ForEach-Object { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }; return $vars
}
function Invoke-SqlLines([string]$Query) {
    Push-Location $Root
    try {
        $lines=@(& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -w 65535 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE-ne 0){throw 'sqlcmd falhou.'}
        return @($lines | ForEach-Object {$_.Trim()} | Where-Object {$_})
    } finally { Pop-Location }
}

if($LabelCount-le 0){throw 'LabelCount deve ser > 0.'}
$Python3=Resolve-Python3; $vars=Get-EnvValues
$password=$vars['JORNADA_SQL_SA_PASSWORD']; $port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}; $db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD não definido.'}
$conn="Server=localhost,$port;Database=$db;User Id=sa;Password=$password;TrustServerCertificate=true;Encrypt=false"
$outDir=Join-Path $Root '.local/performance'; New-Item -ItemType Directory -Force $outDir|Out-Null
$labelsPath=Join-Path $outDir 'scale-blocking-pass-labels.csv'; $auditPath=Join-Path $outDir 'scale-blocking-pass-audit.json'
$goldPopulation=[int64](Invoke-SqlLines 'SELECT COUNT_BIG(*) FROM gold.pessoa;')[0]

$rows=Invoke-SqlLines @"
WITH pend AS (
 SELECT TOP ($LabelCount) po.pessoa_observacao_id,po.codigo_pessoa_origem
 FROM silver.pessoa_observacao po
 WHERE po.cpf IS NULL AND po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
 ORDER BY po.codigo_pessoa_origem,po.pessoa_observacao_id
)
SELECT CONCAT(p.pessoa_observacao_id,N',',CONVERT(nvarchar(36),tv.pessoa_uuid))
FROM pend p
JOIN silver.pessoa_observacao truth_po ON truth_po.codigo_pessoa_origem=REPLACE(p.codigo_pessoa_origem,N'SCALE-PEND-',N'SCALE-SEHAB-')
JOIN ref.gestor g ON g.gestor_id=truth_po.gestor_id AND g.codigo=N'SEHAB'
JOIN identidade.v_vinculo_corrente tv ON tv.pessoa_observacao_id=truth_po.pessoa_observacao_id AND tv.status=N'RESOLVIDO' AND tv.pessoa_uuid IS NOT NULL
ORDER BY p.codigo_pessoa_origem,p.pessoa_observacao_id;
"@
if($rows.Count-ne $LabelCount){throw "Amostra SCALE incompleta: esperados=$LabelCount obtidos=$($rows.Count)."}
$content=(@('pessoa_observacao_id,pessoa_uuid_verdade')+$rows)-join [Environment]::NewLine
[IO.File]::WriteAllText($labelsPath,$content+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))

$old=$env:ConnectionStrings__Jornada
try {
 $env:ConnectionStrings__Jornada=$conn; Push-Location $Root
 try {
  Write-Host ">>> dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --blocking-pass-audit-labels $labelsPath --blocking-pass-audit-output $auditPath"
  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- --blocking-pass-audit-labels $labelsPath --blocking-pass-audit-output $auditPath --ProbabilisticLinkage:CommandTimeoutSeconds 300
  if($LASTEXITCODE-ne 0){throw 'Blocking pass audit falhou.'}
  $args=@($Python3.Prefix)+@('scripts/blocking-pass-fanout-gate.py',$auditPath,'--population',"$goldPopulation")
  Write-Host ">>> $($Python3.Exe) $($args -join ' ')"
  & $Python3.Exe @args; if($LASTEXITCODE-ne 0){throw 'Gate de fan-out SCALE falhou.'}
 } finally { Pop-Location }
} finally { $env:ConnectionStrings__Jornada=$old }
Write-Host "SCALE BLOCKING PASS AUDIT: OK (labels=$LabelCount; goldPopulation=$goldPopulation)" -ForegroundColor Green
Write-Host "Evidência: $auditPath"

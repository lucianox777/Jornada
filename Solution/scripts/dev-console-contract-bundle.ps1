$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile=$DevConsoleEnvFile
$OutDir=Join-Path $Root '.local/dev-console'
$Stage=Join-Path $OutDir 'contract-config-bundle'
$ZipPath=Join-Path $OutDir 'contract-config-bundle.zip'
New-Item -ItemType Directory -Force $OutDir | Out-Null

if(-not(Test-Path $EnvFile)){throw '.env ausente. Suba a infraestrutura DEV primeiro.'}
$vars=@{}
Get-Content $EnvFile | ForEach-Object {
  $line=$_.Trim()
  if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){
    $p=$line.Split('=',2)
    $vars[$p[0].Trim()]=$p[1].Trim()
  }
}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

function Scalar([string]$Query){
  $old=$env:SQLCMDPASSWORD
  $env:SQLCMDPASSWORD=$password
  Push-Location $Root
  try {
    $lines=@(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
    if($LASTEXITCODE -ne 0){throw "sqlcmd falhou ($LASTEXITCODE)."}
    return [string](@($lines | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ }) | Select-Object -Last 1)
  } finally {
    if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
    Pop-Location
  }
}

$activeCount=[int](Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO';")
if($activeCount -le 0){throw 'Nenhum modelo ATIVO. Execute primeiro Calibração inicial a partir da Gold.'}

$modelId=Scalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;"
$version=[int](Scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;"
)
$bundleVersion=Scalar "SELECT TOP(1) ISNULL(model_config_bundle_version,N'') FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;"
$fingerprint=Scalar "SELECT TOP(1) ISNULL(model_config_bundle_fingerprint_sha256,N'') FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC;"

if(Test-Path $Stage){Remove-Item -Recurse -Force $Stage}
if(Test-Path $ZipPath){Remove-Item -Force $ZipPath}
New-Item -ItemType Directory -Force $Stage | Out-Null

Write-Host 'Gerando bundle de contratos e configurações...'
$copies=@(
  @{Source='config/contracts';Destination='config/contracts'},
  @{Source='config/governance';Destination='config/governance'},
  @{Source='config/linkage';Destination='config/linkage'},
  @{Source='config/observability';Destination='config/observability'},
  @{Source='config/operations';Destination='config/operations'},
  @{Source='config/possibilities';Destination='config/possibilities'},
  @{Source='openapi/jornada-v1.openapi.json';Destination='openapi/jornada-v1.openapi.json'},
  @{Source='install/windows-production/Jornada.Cluster.Test.json';Destination='install/windows-production/Jornada.Cluster.Test.json'},
  @{Source='database/migrations/manifest.txt';Destination='database/migrations/manifest.txt'},
  @{Source='database/Jornada_Fase1_v3.70.sql';Destination='database/Jornada_Fase1_v3.70.sql'}
)

foreach($item in $copies){
  $source=Join-Path $Root $item.Source
  if(-not(Test-Path $source)){throw "Fonte obrigatória do bundle ausente: $($item.Source)"}
  $destination=Join-Path $Stage $item.Destination
  New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
  if((Get-Item $source).PSIsContainer){
    Copy-Item -Recurse -Force $source $destination
  } else {
    Copy-Item -Force $source $destination
  }
  Write-Host " + $($item.Source)"
}

$info=[ordered]@{
  bundleType='JORNADA_DEV_CONTRACT_CONFIG_V1'
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  database=$db
  activeModel=[ordered]@{
    modelId=$modelId
    version=$version
    status='ATIVO'
    modelConfigBundleVersion=$bundleVersion
    modelConfigBundleFingerprintSha256=$fingerprint
  }
  contents=@(
    'OpenAPI v1',
    'JSON contracts',
    'governance/linkage/observability/operations/possibilities config',
    'cluster test configuration',
    'schema migration manifest',
    'canonical schema entrypoint'
  )
  excludes=@('config/security','test access keys','application binaries')
}
$infoPath=Join-Path $Stage 'BUNDLE_INFO.json'
$infoHtmlPath=Join-Path $Stage 'BUNDLE_INFO.html'
$infoJson=$info | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($infoPath,$infoJson,[Text.UTF8Encoding]::new($false))
$escapedInfo=[System.Net.WebUtility]::HtmlEncode($infoJson)
$infoHtml=@"
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Jornada DEV - Bundle de contratos e configurações</title><style>body{font-family:system-ui;margin:24px;color:#17202a}pre{background:#0b0f14;color:#d7e0ea;padding:16px;border-radius:8px;white-space:pre-wrap}</style></head><body><h1>Bundle de contratos e configurações</h1><p><b>Modelo ATIVO:</b> v$version ($modelId)</p><pre>$escapedInfo</pre></body></html>
"@
[IO.File]::WriteAllText($infoHtmlPath,$infoHtml,[Text.UTF8Encoding]::new($false))

$manifestLines=Get-ChildItem $Stage -Recurse -File |
  Sort-Object FullName |
  ForEach-Object {
    $relative=$_.FullName.Substring($Stage.Length).TrimStart('\','/').Replace('\','/')
    $hash=(Get-FileHash -Algorithm SHA256 $_.FullName).Hash.ToLowerInvariant()
    "$hash  $relative"
  }
$manifestPath=Join-Path $Stage 'MANIFEST.sha256'
$manifestLines | Set-Content -Encoding UTF8 $manifestPath

Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $ZipPath -CompressionLevel Optimal -Force

$zipHash=(Get-FileHash -Algorithm SHA256 $ZipPath).Hash.ToLowerInvariant()
Write-Host "Modelo ATIVO: v$version ($modelId)"
Write-Host "Fingerprint do model config bundle: $fingerprint"
Write-Host "Arquivos no bundle: $((Get-ChildItem $Stage -Recurse -File).Count)"
Write-Host "SHA-256 ZIP: $zipHash"
Write-Host "Resultado salvo em: $ZipPath"
Write-Host "BUNDLE_INFO JSON: $infoPath"
Write-Host "BUNDLE_INFO HTML: $infoHtmlPath"
Write-Host "Manifesto SHA-256: $manifestPath"
Write-Host "ARTEFATO: $ZipPath"
Write-Host "ARTEFATO: $infoPath"
Write-Host "ARTEFATO: $infoHtmlPath"
Write-Host "ARTEFATO: $manifestPath"

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
. (Join-Path $PSScriptRoot 'dev-console-html.ps1')
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

$eligible="status=N'ATIVO' AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO'"
$activeCount=[int](Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE $eligible;")
if($activeCount -eq 0){throw 'Nenhum modelo ATIVO. Execute novamente Subir infraestrutura, referências e bootstrap para garantir o BOOTSTRAP inicial.'}
if($activeCount -ne 1){throw "Quantidade inválida de modelos calibrados ATIVOS: $activeCount."}

$modelId=Scalar "SELECT TOP(1) CONVERT(varchar(36),modelo_id) FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
$version=[int](Scalar "SELECT TOP(1) versao FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
)
$bundleVersion=Scalar "SELECT TOP(1) ISNULL(model_config_bundle_version,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"
$fingerprint=Scalar "SELECT TOP(1) ISNULL(model_config_bundle_fingerprint_sha256,N'') FROM identidade.modelo_linkage WHERE $eligible ORDER BY versao DESC;"

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
$contentItems=($info.contents | ForEach-Object {"<li>$(ConvertTo-DevConsoleHtmlText $_)</li>"}) -join ''
$excludeItems=($info.excludes | ForEach-Object {"<li>$(ConvertTo-DevConsoleHtmlText $_)</li>"}) -join ''
$infoHtml=@"
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Jornada DEV - Bundle de contratos e configurações</title>
<style>
:root{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;color:#17202a;background:#f4f6f8}*{box-sizing:border-box}body{margin:0}
.page{max-width:1100px;margin:0 auto;padding:28px}.header,.section{background:white;border:1px solid #dce2e8;border-radius:12px;padding:20px 22px;margin-bottom:16px}
h1{margin:0 0 5px;font-size:1.55rem}h2{font-size:1.08rem;margin:0 0 14px}.muted{color:#697581}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}.field{border:1px solid #e1e6eb;border-radius:9px;padding:12px 14px;overflow-wrap:anywhere}
.field span{display:block;font-size:.74rem;text-transform:uppercase;letter-spacing:.04em;color:#697581;font-weight:700;margin-bottom:5px}.wide{grid-column:1/-1}
code{font-family:ui-monospace,Consolas,monospace;font-size:.88em}ul{margin:0;padding-left:22px}li{margin:6px 0}
@media(max-width:700px){.page{padding:14px}.grid{grid-template-columns:1fr}}
</style></head><body><main class="page">
<section class="header"><h1>Bundle de contratos e configurações</h1><div class="muted">Gerado em $(ConvertTo-DevConsoleHtmlText $info.generatedAt)</div></section>
<section class="section"><h2>Identificação</h2><div class="grid">
  <div class="field"><span>Tipo do bundle</span>$(ConvertTo-DevConsoleHtmlText $info.bundleType)</div>
  <div class="field"><span>Banco de dados</span>$(ConvertTo-DevConsoleHtmlText $info.database)</div>
  <div class="field"><span>Status do modelo</span><b>$(ConvertTo-DevConsoleHtmlText $info.activeModel.status)</b></div>
  <div class="field"><span>Versão do modelo</span><b>$(ConvertTo-DevConsoleHtmlText $info.activeModel.version)</b></div>
  <div class="field wide"><span>ID do modelo</span><code>$(ConvertTo-DevConsoleHtmlText $info.activeModel.modelId)</code></div>
  <div class="field"><span>Versão do model config bundle</span>$(ConvertTo-DevConsoleHtmlText $info.activeModel.modelConfigBundleVersion)</div>
  <div class="field wide"><span>Fingerprint SHA-256</span><code>$(ConvertTo-DevConsoleHtmlText $info.activeModel.modelConfigBundleFingerprintSha256)</code></div>
</div></section>
<section class="section"><h2>Conteúdo incluído</h2><ul>$contentItems</ul></section>
<section class="section"><h2>Conteúdo excluído</h2><ul>$excludeItems</ul></section>
<section class="section"><p class="muted">Arquivo JSON correspondente: <code>$(ConvertTo-DevConsoleHtmlText $infoPath)</code></p></section>
</main></body></html>
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

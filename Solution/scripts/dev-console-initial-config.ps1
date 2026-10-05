$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runtimeMode=if([string]::IsNullOrWhiteSpace($env:JORNADA_RUNTIME_MODE)){'HML'}else{$env:JORNADA_RUNTIME_MODE.Trim().ToUpperInvariant()}
$residentProfile=switch($runtimeMode){'DEV'{'Development'}'PROD'{'Production'}default{'Homologation'}}
. (Join-Path $PSScriptRoot 'dev-console-html.ps1')
$OutDir=Join-Path $Root '.local/dev-console/initial-config'
New-Item -ItemType Directory -Force $OutDir | Out-Null

$clusterPath=Join-Path $Root 'install/windows-production/Jornada.Cluster.Test.json'
$openApiPath=Join-Path $Root 'openapi/jornada-v1.openapi.json'
$contractsPath=Join-Path $Root 'config/contracts'
$governancePath=Join-Path $Root 'config/governance'
$linkagePath=Join-Path $Root 'config/linkage'
$operationsPath=Join-Path $Root 'config/operations'
$possibilitiesPath=Join-Path $Root 'config/possibilities'

foreach($required in @($clusterPath,$openApiPath,$contractsPath,$governancePath,$linkagePath,$operationsPath,$possibilitiesPath)){
  if(-not(Test-Path $required)){throw "Fonte de configuração ausente: $required"}
}

$cluster=Get-Content $clusterPath -Raw -Encoding UTF8 | ConvertFrom-Json

$modelStatePath=Join-Path $Root '.local/dev-console/initial-calibration.json'
$modelState=$null
if(Test-Path $modelStatePath){
  try{$modelState=Get-Content $modelStatePath -Raw -Encoding UTF8 | ConvertFrom-Json}
  catch{$modelState=$null}
}
$modelGoldPeople=$null
if($null -ne $modelState){
  $bootstrapGoldProperty=$modelState.PSObject.Properties['bootstrapGoldPeople']
  $legacyGoldProperty=$modelState.PSObject.Properties['goldPeople']
  if($null -ne $bootstrapGoldProperty){$modelGoldPeople=$bootstrapGoldProperty.Value}
  elseif($null -ne $legacyGoldProperty){$modelGoldPeople=$legacyGoldProperty.Value}
}

$health=@()
$envFile=Join-Path $Root '.env.devconsole'
if(-not(Test-Path $envFile)){$envFile=Join-Path $Root '.env'}
if(Test-Path $envFile){
  Write-Host 'Consultando status/health atual dos serviços Docker...'
  Push-Location $Root
  try{
    $raw=@(& docker compose --env-file $envFile ps --format json -a 2>$null)
    if($LASTEXITCODE -eq 0 -and $raw.Count -gt 0){
      $health=@(($raw -join "`n")|ConvertFrom-Json|ForEach-Object{
        [ordered]@{
          service=[string]$_.Service
          name=[string]$_.Name
          state=[string]$_.State
          health=[string]$_.Health
          status=[string]$_.Status
          exitCode=if($_.ExitCode -ne $null){[int]$_.ExitCode}else{$null}
        }
      })
    }
  } finally {Pop-Location}
}

$config=[ordered]@{
  schemaVersion=1
  kind='JORNADA_DEV_INITIAL_CONFIGURATION'
  generatedAt=(Get-Date).ToUniversalTime().ToString('o')
  solutionSchema=[string]$cluster.solutionSchema
  environment=$residentProfile
  runtimeMode=$runtimeMode
  clusterConfiguration=[ordered]@{
    source=$clusterPath
    configurationBundleVersion=[string]$cluster.configurationBundleVersion
  }
  contracts=[ordered]@{
    openApi=$openApiPath
    jsonSchemas=$contractsPath
  }
  governedConfiguration=[ordered]@{
    governance=$governancePath
    linkage=$linkagePath
    operations=$operationsPath
    possibilities=$possibilitiesPath
  }
  runtime=[ordered]@{
    node1='http://127.0.0.1:5080'
    node2='http://127.0.0.1:5180'
    sql='localhost:14333'
    nas='localhost:1445'
  }
  runtimeHealth=$health
  activeLinkageModel=$modelState
  note='Configuração inicial gerada após a garantia do modelo BOOTSTRAP inicial ATIVO. O bundle operacional de contratos/configurações é gerado separadamente e se vincula ao modelo ATIVO corrente.'
}

$jsonPath=Join-Path $OutDir 'configuration.json'
$htmlPath=Join-Path $OutDir 'configuration.html'
$manifestPath=Join-Path $OutDir 'files.json'

$json=$config | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($jsonPath,$json,[Text.UTF8Encoding]::new($false))

$modelHtml=if($null -ne $modelState){
@"
<div class="grid">
  <div class="field"><span>ID do modelo</span><code>$(ConvertTo-DevConsoleHtmlText $modelState.modelId)</code></div>
  <div class="field"><span>Versão</span><b>$(ConvertTo-DevConsoleHtmlText $modelState.version)</b></div>
  <div class="field"><span>Status</span><b>$(ConvertTo-DevConsoleHtmlText $modelState.status)</b></div>
  <div class="field"><span>Papel</span>$(ConvertTo-DevConsoleHtmlText $modelState.modelRole)</div>
  <div class="field"><span>Referência bootstrap</span>$(ConvertTo-DevConsoleHtmlText $modelState.bootstrapReference)</div>
  <div class="field"><span>Pessoas Gold</span>$(ConvertTo-DevConsoleHtmlText $modelGoldPeople)</div>
  <div class="field wide"><span>Método da amostra</span>$(ConvertTo-DevConsoleHtmlText $modelState.sampleMethod)</div>
</div>
"@
}else{'<p class="muted">Modelo ativo não disponível neste snapshot.</p>'}

$healthRows=if($health.Count -gt 0){
  ($health | ForEach-Object {
    $healthText=if([string]::IsNullOrWhiteSpace($_.health)){'n/a'}else{[string]$_.health}
    "<tr><td>$(ConvertTo-DevConsoleHtmlText $_.service)</td><td>$(ConvertTo-DevConsoleHtmlText $_.state)</td><td>$(ConvertTo-DevConsoleHtmlText $healthText)</td><td>$(ConvertTo-DevConsoleHtmlText $_.status)</td><td>$(ConvertTo-DevConsoleHtmlText $_.exitCode)</td></tr>"
  }) -join ''
}else{'<tr><td colspan="5" class="muted">Nenhum serviço Docker encontrado no momento da geração.</td></tr>'}

$html=@"
<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Jornada DEV - Configuração inicial</title>
<style>
:root{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;color:#17202a;background:#f4f6f8}*{box-sizing:border-box}
body{margin:0}.page{max-width:1300px;margin:0 auto;padding:28px}h1{margin:0 0 5px;font-size:1.6rem}h2{font-size:1.08rem;margin:0 0 14px}
.header,.section{background:white;border:1px solid #dce2e8;border-radius:12px;padding:20px 22px;margin-bottom:16px}.muted{color:#697581}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px}.field{border:1px solid #e1e6eb;border-radius:9px;padding:12px 14px;overflow-wrap:anywhere}
.field span{display:block;font-size:.74rem;text-transform:uppercase;letter-spacing:.04em;color:#697581;font-weight:700;margin-bottom:5px}.wide{grid-column:1/-1}
code{font-family:ui-monospace,Consolas,monospace;font-size:.88em}table{width:100%;border-collapse:collapse;font-size:.9rem}th,td{text-align:left;padding:9px 10px;border-bottom:1px solid #e7ebef}th{background:#f2f5f7}
@media(max-width:700px){.page{padding:14px}.grid{grid-template-columns:1fr}}
</style></head>
<body><main class="page">
<section class="header"><h1>Jornada DEV - Configuração inicial</h1><div class="muted">Gerado em $(ConvertTo-DevConsoleHtmlText $config.generatedAt)</div></section>
<section class="section"><h2>Identificação</h2><div class="grid">
  <div class="field"><span>Ambiente</span>$(ConvertTo-DevConsoleHtmlText $config.environment)</div>
  <div class="field"><span>Schema da solução</span>$(ConvertTo-DevConsoleHtmlText $config.solutionSchema)</div>
  <div class="field"><span>Versão do bundle</span>$(ConvertTo-DevConsoleHtmlText $config.clusterConfiguration.configurationBundleVersion)</div>
  <div class="field"><span>Schema do documento</span>$(ConvertTo-DevConsoleHtmlText $config.schemaVersion)</div>
</div></section>
<section class="section"><h2>Serviços</h2><div class="grid">
  <div class="field"><span>NODE1</span><code>$(ConvertTo-DevConsoleHtmlText $config.runtime.node1)</code></div>
  <div class="field"><span>NODE2</span><code>$(ConvertTo-DevConsoleHtmlText $config.runtime.node2)</code></div>
  <div class="field"><span>SQL Server</span><code>$(ConvertTo-DevConsoleHtmlText $config.runtime.sql)</code></div>
  <div class="field"><span>NAS</span><code>$(ConvertTo-DevConsoleHtmlText $config.runtime.nas)</code></div>
</div></section>
<section class="section"><h2>Modelo de linkage ativo</h2>$modelHtml</section>
<section class="section"><h2>Status dos serviços no momento da geração</h2><div style="overflow:auto"><table><thead><tr><th>Serviço</th><th>Estado</th><th>Health</th><th>Status</th><th>Exit code</th></tr></thead><tbody>$healthRows</tbody></table></div></section>
<section class="section"><h2>Fontes de configuração</h2><div class="grid">
  <div class="field wide"><span>Cluster</span><code>$(ConvertTo-DevConsoleHtmlText $config.clusterConfiguration.source)</code></div>
  <div class="field wide"><span>OpenAPI</span><code>$(ConvertTo-DevConsoleHtmlText $config.contracts.openApi)</code></div>
  <div class="field wide"><span>JSON Schemas</span><code>$(ConvertTo-DevConsoleHtmlText $config.contracts.jsonSchemas)</code></div>
  <div class="field"><span>Governança</span><code>$(ConvertTo-DevConsoleHtmlText $config.governedConfiguration.governance)</code></div>
  <div class="field"><span>Linkage</span><code>$(ConvertTo-DevConsoleHtmlText $config.governedConfiguration.linkage)</code></div>
  <div class="field"><span>Operações</span><code>$(ConvertTo-DevConsoleHtmlText $config.governedConfiguration.operations)</code></div>
  <div class="field"><span>Possibilidades</span><code>$(ConvertTo-DevConsoleHtmlText $config.governedConfiguration.possibilities)</code></div>
</div></section>
<section class="section"><h2>Observação</h2><p>$(ConvertTo-DevConsoleHtmlText $config.note)</p><p class="muted">Arquivo JSON correspondente: <code>$(ConvertTo-DevConsoleHtmlText $jsonPath)</code></p></section>
</main></body></html>
"@
[IO.File]::WriteAllText($htmlPath,$html,[Text.UTF8Encoding]::new($false))

$files=[ordered]@{
  generatedAt=$config.generatedAt
  json=$jsonPath
  html=$htmlPath
  sources=@($clusterPath,$openApiPath,$contractsPath,$governancePath,$linkagePath,$operationsPath,$possibilitiesPath)
}
[IO.File]::WriteAllText($manifestPath,($files|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))

if($health.Count -gt 0){
  Write-Host 'Resumo de status/health:'
  foreach($item in $health){
    $healthText=if([string]::IsNullOrWhiteSpace($item.health)){'n/a'}else{$item.health}
    Write-Host (" - {0}: state={1}; health={2}; status={3}; exit={4}" -f $item.service,$item.state,$healthText,$item.status,$item.exitCode)
  }
}
Write-Host 'Configuração inicial gerada.'
Write-Host "JSON: $jsonPath"
Write-Host "HTML: $htmlPath"
Write-Host "Manifesto de caminhos: $manifestPath"
Write-Host "ARTEFATO: $jsonPath"
Write-Host "ARTEFATO: $htmlPath"
Write-Host "ARTEFATO: $manifestPath"

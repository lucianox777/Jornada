$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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
  environment='Development'
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
  note='Configuração inicial gerada na subida da infraestrutura. Não contém modelo de linkage ATIVO; após a calibração gere o bundle operacional de contratos/configurações.'
}

$jsonPath=Join-Path $OutDir 'configuration.json'
$htmlPath=Join-Path $OutDir 'configuration.html'
$manifestPath=Join-Path $OutDir 'files.json'

$json=$config | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($jsonPath,$json,[Text.UTF8Encoding]::new($false))

$escaped=[System.Net.WebUtility]::HtmlEncode($json)
$html=@"
<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><title>Jornada DEV - Configuração inicial</title>
<style>body{font-family:system-ui;margin:24px;color:#17202a}pre{background:#0b0f14;color:#d7e0ea;padding:16px;border-radius:8px;white-space:pre-wrap}code{font-family:ui-monospace,Consolas,monospace}</style></head>
<body><h1>Jornada DEV - Configuração inicial</h1>
<p>Gerado em $($config.generatedAt)</p>
<p><b>JSON:</b> <code>$jsonPath</code></p>
<pre>$escaped</pre></body></html>
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

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$started=Get-Date
Write-Host '=== Jornada DEV :: Carregar Gold sintética ==='
Write-Host ('Início: '+$started.ToString('o'))
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile=Join-Path $Root '.env'
Write-Host ('Solution: '+$Root)
Write-Host ('Arquivo de ambiente: '+$envFile)
if(-not(Test-Path $envFile)){throw '.env ausente; suba a infraestrutura DEV antes de carregar a Gold sintética.'}

$vars=@{}
Get-Content $envFile | ForEach-Object {
  $l=$_.Trim()
  if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){
    $p=$l.Split('=',2)
    $vars[$p[0].Trim()]=$p[1].Trim()
  }
}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}
Write-Host ('Banco alvo: '+$db)
Write-Host 'Etapa 1/4: verificando container SQL Server...'

$old=$env:SQLCMDPASSWORD
$env:SQLCMDPASSWORD=$password
Push-Location $Root
try {
  $cid=(& docker compose --env-file $envFile ps -q sqlserver | Out-String).Trim()
  if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cid)){throw 'Container SQL Server não está em execução.'}
  Write-Host ('SQL Server container: '+$cid)
  Write-Host 'Etapa 2/4: aplicando fixture Gold sintética idempotente...'
  Write-Host 'SQL: /workspace/database/Jornada_Dev_GoldSynthetic.sql'
  & docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -i /workspace/database/Jornada_Dev_GoldSynthetic.sql
  if($LASTEXITCODE -ne 0){throw "Carga Gold sintética falhou ($LASTEXITCODE)."}
  Write-Host 'Fixture aplicada com sucesso.'
  Write-Host 'Etapa 3/4: consultando exatamente os registros da fixture...'

  $q="SET NOCOUNT ON; SELECT CONVERT(varchar(36),pessoa_uuid) pessoa_uuid,nome_completo,CONVERT(varchar(10),data_nascimento,23) data_nascimento,nome_mae,estado_identidade FROM gold.pessoa WHERE pessoa_uuid IN ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb001','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb002','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb003','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb004','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb005') ORDER BY pessoa_uuid FOR JSON PATH;"
  $json=& docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -Q $q
  if($LASTEXITCODE -ne 0){throw 'Consulta dos registros Gold falhou.'}

  $out=Join-Path $Root '.local/dev-console'
  New-Item -ItemType Directory -Force $out|Out-Null
  $text=($json -join [Environment]::NewLine).Trim()
  if([string]::IsNullOrWhiteSpace($text)){$text='[]'}
  $resultPath=Join-Path $out 'gold-synthetic-records.json'
  [IO.File]::WriteAllText($resultPath,$text,[Text.UTF8Encoding]::new($false))
  $records=@($text|ConvertFrom-Json)
  Write-Host 'Etapa 4/4: resultado persistido.'
  Write-Host ('Arquivo: '+$resultPath)
  Write-Host ('Registros: '+$records.Count)
  foreach($r in $records){
    Write-Host (' - '+$r.pessoa_uuid+' | '+$r.nome_completo+' | '+$r.data_nascimento+' | mãe: '+$r.nome_mae+' | '+$r.estado_identidade)
  }
  $elapsed=(Get-Date)-$started
  Write-Host ('Concluído em '+[math]::Round($elapsed.TotalSeconds,2)+' s.')
  Write-Host '=== SUCESSO: Gold sintética carregada ==='
}
finally {
  Pop-Location
  $env:SQLCMDPASSWORD=$old
}

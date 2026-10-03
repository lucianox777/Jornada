$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile=Join-Path $Root '.env'
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

$old=$env:SQLCMDPASSWORD
$env:SQLCMDPASSWORD=$password
Push-Location $Root
try {
  & docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -i /workspace/database/Jornada_Dev_GoldSynthetic.sql
  if($LASTEXITCODE -ne 0){throw "Carga Gold sintética falhou ($LASTEXITCODE)."}

  $q="SET NOCOUNT ON; SELECT CONVERT(varchar(36),pessoa_uuid) pessoa_uuid,nome_completo,CONVERT(varchar(10),data_nascimento,23) data_nascimento,nome_mae,estado_identidade FROM gold.pessoa WHERE pessoa_uuid IN ('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb001','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb002','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb003','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb004','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbb005') ORDER BY pessoa_uuid FOR JSON PATH;"
  $json=& docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -Q $q
  if($LASTEXITCODE -ne 0){throw 'Consulta dos registros Gold falhou.'}

  $out=Join-Path $Root '.local/dev-console'
  New-Item -ItemType Directory -Force $out|Out-Null
  $text=($json -join [Environment]::NewLine).Trim()
  if([string]::IsNullOrWhiteSpace($text)){$text='[]'}
  [IO.File]::WriteAllText((Join-Path $out 'gold-synthetic-records.json'),$text,[Text.UTF8Encoding]::new($false))
  Write-Host "Gold sintética carregada; registros: $(($text|ConvertFrom-Json).Count)"
}
finally {
  Pop-Location
  $env:SQLCMDPASSWORD=$old
}

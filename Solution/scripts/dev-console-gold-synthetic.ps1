$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}
$started=Get-Date
Write-Host '=== Jornada DEV :: Carregar Gold sintética (30.000, nomes IBGE) ==='
Write-Host ('Início: '+$started.ToString('o'))
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$envFile=$DevConsoleEnvFile
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
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaSyntheticDev'}
if($db -ne 'JornadaSyntheticDev'){throw "Console DEV aceita somente JornadaSyntheticDev; banco atual=$db."}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}
Write-Host ('Banco alvo: '+$db)
Write-Host 'Etapa 1/5: verificando container SQL Server e banco sintético isolado...'

$old=$env:SQLCMDPASSWORD
$env:SQLCMDPASSWORD=$password
Push-Location $Root
try {
  $cid=(& docker compose --env-file $envFile ps -q sqlserver | Out-String).Trim()
  if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($cid)){throw 'Container SQL Server não está em execução.'}
  Write-Host ('SQL Server container: '+$cid)
  Write-Host 'Etapa 2/5: validando a massa sintética canônica de 30.000 pessoas...'
  $expected=30000
  $scaleCount=[int]((& docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';" | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim())
  if($scaleCount -ne $expected){throw "Massa sintética DEV inválida: encontrados=$scaleCount; esperados=$expected. Execute Subir infraestrutura e referências."}

  Write-Host 'Etapa 3/5: aplicando nomes/sobrenomes conforme frequências IBGE ativas...'
  & docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -v SCALE_PEOPLE=30000 SCALE_SEED=355 SCALE_COLLISION_MODULO=37 -i /workspace/database/Jornada_Dev_SyntheticScale_Diversify.sql
  if($LASTEXITCODE -ne 0){throw "Diversificação IBGE falhou ($LASTEXITCODE)."}

  Write-Host 'Etapa 4/5: consultando as 30.000 pessoas Gold sintéticas...'
  $q="SET NOCOUNT ON; SELECT CONVERT(varchar(36),g.pessoa_uuid),REPLACE(REPLACE(g.nome_completo,CHAR(9),' '),CHAR(10),' '),CONVERT(varchar(10),g.data_nascimento,23),REPLACE(REPLACE(g.nome_mae,CHAR(9),' '),CHAR(10),' '),g.estado_identidade FROM gold.pessoa g WHERE EXISTS(SELECT 1 FROM silver.pessoa_observacao o JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.pessoa_uuid=g.pessoa_uuid AND vc.status=N'RESOLVIDO') ORDER BY g.pessoa_uuid;"
  $lines=@(& docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -s "|" -w 65535 -Q $q)
  if($LASTEXITCODE -ne 0){throw 'Consulta dos registros Gold falhou.'}

  $out=Join-Path $Root '.local/dev-console'
  New-Item -ItemType Directory -Force $out|Out-Null
  $records=[Collections.Generic.List[object]]::new()
  foreach($line in $lines){
    if([string]::IsNullOrWhiteSpace($line)){continue}
    $parts=([string]$line).Split('|')
    if($parts.Count -lt 5){continue}
    $records.Add([ordered]@{pessoa_uuid=$parts[0].Trim();nome_completo=$parts[1].Trim();data_nascimento=$parts[2].Trim();nome_mae=$parts[3].Trim();estado_identidade=$parts[4].Trim()})
  }
  if($records.Count -ne $expected){throw "Exportação Gold retornou $($records.Count) registros; esperado=$expected."}
  $resultPath=Join-Path $out 'gold-synthetic-records.json'
  [IO.File]::WriteAllText($resultPath,($records|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
  $profilePath=Join-Path $out 'gold-synthetic-profile.json'
  $profile=[ordered]@{syntheticOnly=$true;database=$db;people=$expected;nameDistribution='IBGE Censo 2022 - frequência publicada';birthDistribution='SCALE DEV determinística; projeção demográfica versionada permanece evidência separada';seed=355;generatedAt=(Get-Date).ToUniversalTime().ToString('o')}
  [IO.File]::WriteAllText($profilePath,($profile|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  Write-Host 'Etapa 5/5: resultados persistidos.'
  Write-Host ('Arquivo JSON: '+$resultPath)
  Write-Host ('Proveniência: '+$profilePath)
  Write-Host ('Registros: '+$records.Count)
  Write-Host ('ARTEFATO: '+$resultPath)
  Write-Host ('ARTEFATO: '+$profilePath)
  $elapsed=(Get-Date)-$started
  Write-Host ('Concluído em '+[math]::Round($elapsed.TotalSeconds,2)+' s.')
  Write-Host '=== SUCESSO: Gold sintética de 30.000 pessoas carregada ==='
}
finally {
  Pop-Location
  $env:SQLCMDPASSWORD=$old
}

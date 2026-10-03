param([Parameter(Mandatory=$true)][ValidateSet('update-build','gold-synthetic')][string]$Action)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$LocalDotnet = if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' } else { $null }
$DotnetExe = if ($LocalDotnet -and (Test-Path $LocalDotnet)) { $LocalDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }

function Invoke-Checked([string]$Exe,[string[]]$Arguments){
  Write-Host ('# '+$Exe+' '+($Arguments -join ' '))
  & $Exe @Arguments
  if($LASTEXITCODE -ne 0){throw "$Exe falhou ($LASTEXITCODE)."}
}

switch($Action){
 'update-build' {
   $Repo=(Resolve-Path (Join-Path $Root '..')).Path
   Push-Location $Repo
   try {
     Invoke-Checked git @('pull','--ff-only')
     Invoke-Checked $DotnetExe @('restore','Solution/Jornada.sln','--locked-mode')
     Invoke-Checked $DotnetExe @('build','Solution/Jornada.sln','--no-restore','--configuration','Release')
     Invoke-Checked git @('rev-parse','HEAD')
   } finally { Pop-Location }
 }
 'gold-synthetic' {
   $envFile=Join-Path $Root '.env';if(-not(Test-Path $envFile)){throw '.env ausente; suba a infraestrutura DEV antes de carregar a Gold sintética.'}
   $vars=@{};Get-Content $envFile|%{$l=$_.Trim();if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){$p=$l.Split('=',2);$vars[$p[0].Trim()]=$p[1].Trim()}}
   $db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'};$password=$vars['JORNADA_SQL_SA_PASSWORD'];if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}
   Push-Location $Root
   try {
     $old=$env:SQLCMDPASSWORD;$env:SQLCMDPASSWORD=$password
     try {
       Invoke-Checked docker @('compose','--env-file',$envFile,'exec','-T','-e','SQLCMDPASSWORD','sqlserver','/opt/mssql-tools18/bin/sqlcmd','-S','localhost','-U','sa','-C','-b','-d',$db,'-i','/workspace/database/Jornada_Dev_LinkageValidation.sql')
       $q="SET NOCOUNT ON; SELECT TOP(200) CONVERT(varchar(36),pessoa_uuid) pessoa_uuid,nome_completo,CONVERT(varchar(10),data_nascimento,23) data_nascimento,nome_mae,estado_identidade FROM gold.pessoa WHERE estado_identidade='REFERENCIA' ORDER BY atualizado_em DESC FOR JSON PATH;"
       $json=& docker compose --env-file $envFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $db -W -h -1 -y 0 -Q $q
       if($LASTEXITCODE -ne 0){throw 'Consulta dos registros Gold falhou.'}
       $out=Join-Path $Root '.local/dev-console';New-Item -ItemType Directory -Force $out|Out-Null
       $text=($json -join [Environment]::NewLine).Trim();if([string]::IsNullOrWhiteSpace($text)){$text='[]'}
       [IO.File]::WriteAllText((Join-Path $out 'gold-synthetic-records.json'),$text,[Text.UTF8Encoding]::new($false))
       Write-Host "Gold sintética carregada; registros: $(($text|ConvertFrom-Json).Count)"
     } finally {$env:SQLCMDPASSWORD=$old}
   } finally {Pop-Location}
 }
}

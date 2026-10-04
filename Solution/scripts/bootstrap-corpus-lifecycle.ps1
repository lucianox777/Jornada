param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Development','Homologation','Production')]
    [string]$EnvironmentProfile,
    [string]$EnvFile
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if([string]::IsNullOrWhiteSpace($EnvFile)){
    if($EnvironmentProfile -ne 'Development'){
        throw 'Homologation/Production exigem -EnvFile explícito; o lifecycle nunca assume credenciais/ambiente não-DEV.'
    }
    . (Join-Path $PSScriptRoot 'dev-console-env.ps1')
    $EnvFile=$DevConsoleEnvFile
}
$EnvFile=[IO.Path]::GetFullPath($EnvFile)
if(-not(Test-Path $EnvFile)){throw "Arquivo de ambiente inexistente: $EnvFile"}

$vars=@{}
Get-Content $EnvFile | ForEach-Object {
    $line=$_.Trim()
    if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){
        $p=$line.Split('=',2); $vars[$p[0].Trim()]=$p[1].Trim()
    }
}
$db=$vars['JORNADA_SQL_DATABASE']
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($db)){throw 'JORNADA_SQL_DATABASE ausente.'}
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

Write-Host "Lifecycle bootstrap: descartando somente o corpus sintético SCALE-* após ativação do modelo."
Write-Host "Perfil esperado: $EnvironmentProfile; banco: $db"
$old=$env:SQLCMDPASSWORD
$env:SQLCMDPASSWORD=$password
Push-Location $Root
try {
    & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -v "EXPECTED_ENVIRONMENT_PROFILE=$EnvironmentProfile" -i /workspace/database/Jornada_BootstrapCorpus_Discard.sql
    if($LASTEXITCODE -ne 0){throw "Descarte do corpus sintético de bootstrap falhou ($LASTEXITCODE)."}
}
finally {
    Pop-Location
    if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
}
Write-Host 'Corpus de bootstrap descartado; modelo/parâmetros ativos preservados.'

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'runtime-mode.ps1')
$RuntimeMode=Get-JornadaRuntimeMode
$script:DevConsoleRuntimeMode=$RuntimeMode

if($RuntimeMode -eq 'PROD'){
  $explicit=[string]$env:JORNADA_CONSOLE_ENV_FILE
  if([string]::IsNullOrWhiteSpace($explicit)){
    throw 'Modo PROD exige JORNADA_CONSOLE_ENV_FILE explícito; a Console nunca fabrica configuração de Produção.'
  }
  $ProdEnv=[IO.Path]::GetFullPath($explicit)
  if(-not(Test-Path $ProdEnv -PathType Leaf)){throw "JORNADA_CONSOLE_ENV_FILE inexistente: $ProdEnv"}
  $map=@{}
  Get-Content $ProdEnv -Encoding UTF8 | ForEach-Object {
    $trim=$_.Trim()
    if($trim -and -not $trim.StartsWith('#') -and $trim.Contains('=')){
      $parts=$trim.Split('=',2);$map[$parts[0].Trim()]=$parts[1].Trim()
    }
  }
  $db=[string]$map['JORNADA_SQL_DATABASE']
  if([string]::IsNullOrWhiteSpace($db)){throw 'JORNADA_SQL_DATABASE ausente na configuração PROD.'}
  $env:JORNADA_LOCAL_ENV_FILE=$ProdEnv
  $script:DevConsoleEnvFile=$ProdEnv
  $script:DevConsoleDatabase=$db
  return
}

$BaseEnv=Join-Path $Root '.env'
$ConsoleEnv=Join-Path $Root '.env.devconsole'
if(-not(Test-Path $BaseEnv)){throw '.env ausente. Execute teste.cmd uma vez para preparar o ambiente local.'}

$lines=Get-Content $BaseEnv -Encoding UTF8
$map=[ordered]@{}
foreach($line in $lines){
  $trim=$line.Trim()
  if($trim -and -not $trim.StartsWith('#') -and $trim.Contains('=')){
    $parts=$trim.Split('=',2)
    $map[$parts[0].Trim()]=$parts[1].Trim()
  }
}
$db=if($map['JORNADA_SQL_DATABASE']){[string]$map['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$map['JORNADA_RUNTIME_MODE']=$RuntimeMode
$map['JORNADA_LOCAL_PROFILE']=('console-'+$RuntimeMode.ToLowerInvariant())
$map['JORNADA_SEMIBLIND_ENABLED']=if($RuntimeMode -eq 'DEV'){'true'}else{'false'}
$map['JORNADA_LOCAL_SYNTHETIC_PEOPLE']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PAIRED']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PENDING']=if($RuntimeMode -eq 'DEV'){'6000'}else{'1000'}
$map['JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR']='true'

$out=@("# Gerado automaticamente pela Jornada Console. Modo=$RuntimeMode; dados locais/sintéticos.")
foreach($entry in $map.GetEnumerator()){$out+=("$($entry.Key)=$($entry.Value)")}
[IO.File]::WriteAllLines($ConsoleEnv,$out,[Text.UTF8Encoding]::new($false))
$env:JORNADA_LOCAL_ENV_FILE=$ConsoleEnv
$script:DevConsoleEnvFile=$ConsoleEnv
$script:DevConsoleDatabase=$db

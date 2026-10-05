$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BaseEnv=Join-Path $Root '.env'
$ConsoleEnv=Join-Path $Root '.env.devconsole'

if(-not(Test-Path $BaseEnv)){throw '.env ausente. Execute teste.cmd uma vez para preparar o ambiente local.'}

$RuntimeMode=if([string]::IsNullOrWhiteSpace($env:JORNADA_RUNTIME_MODE)){'HML'}else{$env:JORNADA_RUNTIME_MODE.Trim().ToUpperInvariant()}
if($RuntimeMode -notin @('HML','DEV','PROD')){throw "JORNADA_RUNTIME_MODE inválido: $RuntimeMode. Use HML, DEV ou PROD."}

$lines=Get-Content $BaseEnv -Encoding UTF8
$map=[ordered]@{}
foreach($line in $lines){
  $trim=$line.Trim()
  if($trim -and -not $trim.StartsWith('#') -and $trim.Contains('=')){
    $parts=$trim.Split('=',2)
    $map[$parts[0].Trim()]=$parts[1].Trim()
  }
}
if([string]::IsNullOrWhiteSpace([string]$map['JORNADA_SQL_DATABASE'])){$map['JORNADA_SQL_DATABASE']='JornadaLocal'}

$map['JORNADA_RUNTIME_MODE']=$RuntimeMode
$map['JORNADA_LOCAL_PROFILE']="dev-console-$($RuntimeMode.ToLowerInvariant())"
$map['JORNADA_SEMIBLIND_ENABLED']=if($RuntimeMode -eq 'DEV'){'true'}else{'false'}
$map['JORNADA_LOCAL_SYNTHETIC_PEOPLE']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PAIRED']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PENDING']=if($RuntimeMode -eq 'DEV'){'6000'}else{'0'}
$map['JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR']='true'

$out=@("# Gerado automaticamente pela Jornada Console. RuntimeMode=$RuntimeMode.")
foreach($entry in $map.GetEnumerator()){$out+=("$($entry.Key)=$($entry.Value)")}
[IO.File]::WriteAllLines($ConsoleEnv,$out,[Text.UTF8Encoding]::new($false))
$env:JORNADA_LOCAL_ENV_FILE=$ConsoleEnv
$script:DevConsoleEnvFile=$ConsoleEnv
$script:DevConsoleDatabase=[string]$map['JORNADA_SQL_DATABASE']
$script:DevConsoleRuntimeMode=$RuntimeMode

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BaseEnv=Join-Path $Root '.env'
$DevEnv=Join-Path $Root '.env.devconsole'

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
$map['JORNADA_SQL_DATABASE']='JornadaSyntheticDev'
$map['JORNADA_LOCAL_PROFILE']='dev-console'
$map['JORNADA_SEMIBLIND_ENABLED']='true'
$map['JORNADA_LOCAL_SYNTHETIC_PEOPLE']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PAIRED']='30000'
$map['JORNADA_LOCAL_SYNTHETIC_PENDING']='6000'
$map['JORNADA_DEV_CONSOLE_MANUAL_PROCESSOR']='true'

$out=@('# Gerado automaticamente pela Jornada DEV Console. Somente dados sintéticos.')
foreach($entry in $map.GetEnumerator()){$out+=("$($entry.Key)=$($entry.Value)")}
[IO.File]::WriteAllLines($DevEnv,$out,[Text.UTF8Encoding]::new($false))
$env:JORNADA_LOCAL_ENV_FILE=$DevEnv
$script:DevConsoleEnvFile=$DevEnv
$script:DevConsoleDatabase='JornadaSyntheticDev'

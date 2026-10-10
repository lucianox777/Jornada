param([Parameter(Mandatory=$true)][string]$EnvFile,[Parameter(Mandatory=$true)][string]$Database)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if($Database -notmatch '^[A-Za-z0-9_]+$'){throw 'Nome SQL invalido.'}
$vars=@{}
foreach($line in Get-Content -LiteralPath $EnvFile){
    if($line.Trim() -and -not $line.Trim().StartsWith('#') -and $line.Contains('=')){
        $parts=$line.Split('=',2);$vars[$parts[0].Trim()]=$parts[1].Trim()
    }
}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'Senha SQL ausente no EnvFile.'}
$tmp=Join-Path ([IO.Path]::GetTempPath()) ('jornada-frozen-ref-'+[Guid]::NewGuid().ToString('N')+'.sql')
$previous=[Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
try{
    & python (Join-Path $PSScriptRoot 'verify-frozen-birth-reference.py') --emit-sql $tmp
    if($LASTEXITCODE -ne 0){throw 'Falha na verificacao e geracao SQL da referencia congelada.'}
    Push-Location $Root
    try{
        $container=(& docker compose --env-file $EnvFile ps -q sqlserver | Out-String).Trim()
        if($LASTEXITCODE -ne 0 -or -not $container){throw 'Container SQL Server indisponivel.'}
        # Copiar arquivo temporario ao container; nenhuma senha na linha de comando.
        & docker cp $tmp ($container+':/tmp/jornada-frozen-reference.sql')
        if($LASTEXITCODE -ne 0){throw 'docker cp falhou.'}
        $env:SQLCMDPASSWORD=$password
        & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $Database -Q "IF OBJECT_ID(N'ref.sp_carregar_distribuicao_nascimento_json',N'P') IS NULL OR OBJECT_ID(N'ref.sp_publicar_distribuicao_nascimento',N'P') IS NULL THROW 52261,'Migrations da referencia congelada ausentes',1;"
        if($LASTEXITCODE -ne 0){throw 'Migrations de referencia ausentes: execute a instalacao de schema antes da carga.'}
        & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -f 65001 -d $Database -i /tmp/jornada-frozen-reference.sql
        if($LASTEXITCODE -ne 0){throw 'Carga SQL da referencia congelada falhou.'}
    }finally{
        if($container){ & docker exec $container rm -f /tmp/jornada-frozen-reference.sql | Out-Null }
        Pop-Location
    }
}finally{
    if($null -eq $previous){Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$previous}
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}

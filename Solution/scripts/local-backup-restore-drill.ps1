$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path; $DrillId='35500000-0000-4000-8000-00000000b001'; $Fixture=Join-Path $Root 'tests/fixtures/bronze/restore-drill.zip'
if(-not(Test-Path $Fixture)){throw "Fixture Bronze não encontrado: $Fixture"}
& (Join-Path $PSScriptRoot 'local-db.ps1') -Action up
$vars=@{}; Get-Content (Join-Path $Root '.env') | % { $l=$_.Trim(); if($l -and -not $l.StartsWith('#') -and $l.Contains('=')){ $p=$l.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$pwd=$vars['JORNADA_SQL_SA_PASSWORD']; $port=if($vars['JORNADA_SQL_PORT']){$vars['JORNADA_SQL_PORT']}else{'14333'}; $db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}; $restoreDb='JornadaRestoreDrill'
New-Item -ItemType Directory -Force (Join-Path $Root '.local/sql-backup'),(Join-Path $Root '.local/backup-drill'),(Join-Path $Root 'data/bronze')|Out-Null
function SqlCmd {
    param([Parameter(Mandatory=$true)][string[]]$SqlCmdArgs)
    Push-Location $Root
    try {
        & docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b @SqlCmdArgs
        if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou'}
    }
    finally { Pop-Location }
}
function Scalar([string]$Database,[string]$Query){ Push-Location $Root; try { $o=& docker compose --env-file .env exec -T -e "SQLCMDPASSWORD=$pwd" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d $Database -h -1 -W -Q "SET NOCOUNT ON; $Query"; if($LASTEXITCODE -ne 0){throw 'sqlcmd falhou'}; return ($o|?{$_.Trim()}|Select-Object -Last 1).Trim() } finally { Pop-Location } }
$sha=(Get-FileHash $Fixture -Algorithm SHA256).Hash.ToLowerInvariant(); $length=(Get-Item $Fixture).Length; $key="sha256/$($sha.Substring(0,2))/$($sha.Substring(2,2))/$sha.zip"; $dest=Join-Path (Join-Path $Root 'data/bronze') ($key -replace '/', [IO.Path]::DirectorySeparatorChar); New-Item -ItemType Directory -Force (Split-Path $dest)|Out-Null; Copy-Item $Fixture $dest -Force
SqlCmd -SqlCmdArgs @('-d',$db,'-v',"DRILL_SHA=$sha","DRILL_LENGTH=$length",'-i','/workspace/database/Jornada_Dev_BackupDrill.sql')
Push-Location $Root; try{ if($env:JORNADA_LOCKED_RESTORE -eq 'true'){dotnet restore Jornada.sln --locked-mode}else{dotnet restore Jornada.sln}; if($LASTEXITCODE-ne 0){throw 'restore falhou'}; dotnet build src/Jornada.Bronze.Verify/Jornada.Bronze.Verify.csproj --configuration Release --no-restore; if($LASTEXITCODE-ne 0){throw 'build Bronze.Verify falhou'} }finally{Pop-Location}
$env:ConnectionStrings__Jornada="Server=localhost,$port;Database=$db;User Id=sa;Password=$pwd;TrustServerCertificate=true;Encrypt=false"; $env:BronzeStorage__RootPath=(Resolve-Path (Join-Path $Root 'data/bronze')).Path
Push-Location $Root; try{ dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id $DrillId --minimum-count 1 | Tee-Object (Join-Path $Root '.local/backup-drill/source-verify.txt'); if($LASTEXITCODE-ne 0){throw 'verificação Bronze de origem falhou'} }finally{Pop-Location}
$backupFile="${db}_v370.bak"; Remove-Item (Join-Path $Root ".local/sql-backup/$backupFile") -Force -ErrorAction SilentlyContinue; SqlCmd -SqlCmdArgs @('-Q',"BACKUP DATABASE [$db] TO DISK=N'/var/opt/mssql/backup/$backupFile' WITH INIT,CHECKSUM,STATS=10; RESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/backup/$backupFile' WITH CHECKSUM;")
$dataLogical=Scalar $db "SELECT TOP(1) name FROM sys.database_files WHERE type_desc='ROWS' ORDER BY file_id;"; $logLogical=Scalar $db "SELECT TOP(1) name FROM sys.database_files WHERE type_desc='LOG' ORDER BY file_id;"
$stamp=(Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'); $evid=Join-Path $Root ".local/backup-drill/$stamp"; New-Item -ItemType Directory -Force $evid|Out-Null; Copy-Item (Join-Path $Root ".local/sql-backup/$backupFile") $evid
$bronzeArchive=Join-Path $evid 'bronze.zip'; Compress-Archive -Path (Join-Path $Root 'data/bronze/*') -DestinationPath $bronzeArchive -Force; $restored=Join-Path $evid 'restored-bronze'; New-Item -ItemType Directory -Force $restored|Out-Null; Expand-Archive $bronzeArchive $restored -Force
SqlCmd -SqlCmdArgs @('-Q',"IF DB_ID(N'$restoreDb') IS NOT NULL BEGIN ALTER DATABASE [$restoreDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restoreDb]; END; RESTORE DATABASE [$restoreDb] FROM DISK=N'/var/opt/mssql/backup/$backupFile' WITH MOVE N'$dataLogical' TO N'/var/opt/mssql/data/$restoreDb.mdf', MOVE N'$logLogical' TO N'/var/opt/mssql/data/${restoreDb}_log.ldf', REPLACE, RECOVERY, CHECKSUM;")
$env:ConnectionStrings__Jornada="Server=localhost,$port;Database=$restoreDb;User Id=sa;Password=$pwd;TrustServerCertificate=true;Encrypt=false"; $env:BronzeStorage__RootPath=(Resolve-Path $restored).Path
Push-Location $Root; try{ dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id $DrillId --minimum-count 1 | Tee-Object (Join-Path $evid 'restored-verify.txt'); if($LASTEXITCODE-ne 0){throw 'verificação Bronze restaurada falhou'} }finally{Pop-Location}
$restoredObject=Join-Path $restored ($key -replace '/', [IO.Path]::DirectorySeparatorChar); if(-not(Test-Path $restoredObject)){throw "objeto restaurado esperado ausente: $restoredObject"}; $original=Join-Path $evid 'original-object.zip'; Copy-Item $restoredObject $original -Force
$missing="$restoredObject.missing"; Move-Item $restoredObject $missing -Force
Push-Location $Root; try{ $output=& dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id $DrillId --minimum-count 1 2>&1; $rc=$LASTEXITCODE; $output|Set-Content -Encoding utf8 (Join-Path $evid 'missing-object.txt') }finally{Pop-Location}; Move-Item $missing $restoredObject -Force
if($rc -eq 0 -or -not(($output -join "`n") -match 'MISSING')){throw 'Bronze.Verify não detectou objeto ausente após restore.'}
Add-Content -Encoding utf8 $restoredObject "`nJORNADA-RESTORE-DRILL-CORRUPTION`n"
Push-Location $Root; try{ $output2=& dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id $DrillId --minimum-count 1 2>&1; $rc2=$LASTEXITCODE; $output2|Set-Content -Encoding utf8 (Join-Path $evid 'corrupt-object.txt') }finally{Pop-Location}; Copy-Item $original $restoredObject -Force
if($rc2 -eq 0 -or -not(($output2 -join "`n") -match 'DIVERGENT')){throw 'Bronze.Verify não detectou objeto corrompido após restore.'}
Push-Location $Root; try{ dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --entrega-id $DrillId --minimum-count 1 | Tee-Object (Join-Path $evid 'final-verify.txt'); if($LASTEXITCODE-ne 0){throw 'verificação final Bronze falhou'} }finally{Pop-Location}
# Inventário físico completo + plano de GC DRY-RUN com órfão controlado antigo.
$orphanTmp=Join-Path $evid 'orphan-fixture.zip'; Copy-Item $Fixture $orphanTmp -Force; [IO.File]::AppendAllText($orphanTmp, "`nJORNADA-ORPHAN-DRY-RUN`n")
$orphanSha=(Get-FileHash $orphanTmp -Algorithm SHA256).Hash.ToLowerInvariant(); $orphanKey="sha256/$($orphanSha.Substring(0,2))/$($orphanSha.Substring(2,2))/$orphanSha.zip"; $orphanDest=Join-Path $restored ($orphanKey -replace '/', [IO.Path]::DirectorySeparatorChar); New-Item -ItemType Directory -Force (Split-Path $orphanDest)|Out-Null; Copy-Item $orphanTmp $orphanDest -Force; (Get-Item $orphanDest).LastWriteTimeUtc=(Get-Date).ToUniversalTime().AddHours(-48)
Push-Location $Root; try{ dotnet run --project src/Jornada.Bronze.Verify --configuration Release --no-build -- --minimum-count 1 --deep --report (Join-Path $evid 'deep-report.json') --gc-plan (Join-Path $evid 'gc-plan.json') --orphan-grace-hours 24 | Tee-Object (Join-Path $evid 'deep-verify.txt'); if($LASTEXITCODE-ne 0){throw 'verificação profunda Bronze falhou'}; python3 scripts/bronze-deep-evidence-gate.py (Join-Path $evid 'deep-report.json') (Join-Path $evid 'gc-plan.json') --minimum-gc-candidates 1 --summary (Join-Path $evid 'deep-summary.json'); if($LASTEXITCODE-ne 0){throw 'gate profundo Bronze/GC dry-run falhou'} }finally{Pop-Location}
SqlCmd -SqlCmdArgs @('-Q',"ALTER DATABASE [$restoreDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restoreDb];")
[ordered]@{status='PASS';sourceDatabase=$db;restoredDatabase=$restoreDb;drillEntregaId=$DrillId;bronzeSha256=$sha;bronzeLength=$length;sqlBackup=$backupFile;sourceVerifyPassed=$true;restoredVerifyPassed=$true;missingObjectDetected=$true;corruptObjectDetected=$true;finalVerifyPassed=$true;deepVerifyPassed=$true;gcDryRunPlanPassed=$true;generatedAtUtc=(Get-Date).ToUniversalTime().ToString('o')}|ConvertTo-Json|Set-Content -Encoding utf8 (Join-Path $evid 'report.json')
Push-Location $Root; try{ python3 scripts/bronze-restore-evidence-gate.py (Join-Path $evid 'report.json'); if($LASTEXITCODE-ne 0){throw 'gate de evidência Bronze falhou'} }finally{Pop-Location}
Write-Host "Backup/restore drill concluído com faults negativos: $evid/report.json"

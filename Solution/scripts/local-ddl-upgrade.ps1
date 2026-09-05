$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile = Join-Path $Root '.env'
$Example = Join-Path $Root '.env.example'
$BaselineRel = if ($env:JORNADA_DDL_BASELINE) { $env:JORNADA_DDL_BASELINE } else { 'database/baselines/Jornada_Fase1_v3.65.sql' }
$BaselineSeedRel = if ($env:JORNADA_DDL_BASELINE_SEED) { $env:JORNADA_DDL_BASELINE_SEED } else { 'database/baselines/Jornada_Seed_Dev_v3.65.sql' }
$Db = if ($env:JORNADA_DDL_UPGRADE_DATABASE) { $env:JORNADA_DDL_UPGRADE_DATABASE } else { 'JornadaDdlUpgradeCheck' }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker não encontrado no PATH.' }
if (-not (Test-Path $EnvFile)) { Copy-Item $Example $EnvFile }
$vars=@{}; Get-Content $EnvFile | ForEach-Object { $line=$_.Trim(); if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){ $p=$line.Split('=',2); $vars[$p[0].Trim()]=$p[1] } }
$password=$vars['JORNADA_SQL_SA_PASSWORD']; if([string]::IsNullOrWhiteSpace($password)){ throw 'JORNADA_SQL_SA_PASSWORD não definido.' }
if($Db -notmatch '^[A-Za-z0-9_]+$'){ throw 'Nome de banco inválido.' }
if(-not (Test-Path (Join-Path $Root $BaselineRel))){ throw "Baseline não encontrado: $BaselineRel" }
if(-not (Test-Path (Join-Path $Root $BaselineSeedRel))){ throw "Seed do baseline não encontrado: $BaselineSeedRel" }
$outDir=Join-Path $Root '.local/ddl-upgrade'; New-Item -ItemType Directory -Force $outDir | Out-Null
function Compose([string[]]$a){ Push-Location $Root; try { & docker compose --env-file $EnvFile @a; if($LASTEXITCODE -ne 0){ throw 'docker compose falhou.' } } finally { Pop-Location } }
function Sql([string[]]$a){ Push-Location $Root; try { & docker compose --env-file $EnvFile exec -T -e "SQLCMDPASSWORD=$password" sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I @a; if($LASTEXITCODE -ne 0){ throw 'sqlcmd falhou.' } } finally { Pop-Location } }
function WaitHealthy { for($i=0;$i -lt 60;$i++){ $s=& docker inspect -f '{{.State.Health.Status}}' jornada-sqlserver-local 2>$null; if($s -eq 'healthy'){ return }; Start-Sleep 2 }; throw 'SQL Server não ficou healthy.' }
function Fingerprint([string]$tag){ $f=Join-Path $outDir "fingerprint-$tag.txt"; $lines=Sql @('-d',$Db,'-i','/workspace/database/Jornada_Dev_DdlFingerprint.sql','-W','-h','-1'); $lines | Where-Object { $_.Trim() } | Set-Content -Encoding utf8 $f; return (Get-FileHash -Algorithm SHA256 $f).Hash.ToLowerInvariant() }
function AssertSentinel { $n=(Sql @('-d',$Db,'-W','-h','-1','-Q',"SET NOCOUNT ON; SELECT COUNT(*) FROM ref.gestor WHERE codigo='ZZ_UPGRADE_SENTINEL' AND nome='Sentinela DDL Upgrade';") | Out-String).Trim(); if($n -ne '1'){ throw 'Dado sentinela não foi preservado.' } }
function AssertPhoneV2 { $q="SET NOCOUNT ON; SELECT CASE WHEN ref.fn_telefone_br_canonico_v2(N'00 55 11 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(N'+55 (11) 99999-0001')='5511999990001' AND ref.fn_telefone_br_canonico_v2(NCHAR(9)+N'+1 (212) 555-0100'+NCHAR(13)+NCHAR(10))='12125550100' AND ref.fn_telefone_br_canonico_v2(NCHAR(160)+N'+55 (11) 99999-0001'+NCHAR(160))='5511999990001' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1')='5511999990001' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH001-TEL-1' AND vigencia_fim IS NULL)='5511999990001' THEN 1 ELSE 0 END;"; $n=(Sql @('-d',$Db,'-W','-h','-1','-Q',$q) | Out-String).Trim(); if($n -ne '1'){ throw 'Migração TELEFONE_BR_CANONICO_V2 não convergiu a chave legada 00.' } }
function AssertEmailV2 { $q="SET NOCOUNT ON; SELECT CASE WHEN ref.fn_email_canonico_v2(N'JOSÉ@EXAMPLE.ORG')=N'josÉ@example.org' AND ref.fn_email_canonico_v2(N'Jose'+NCHAR(769)+N'@Example.org')=N'jose'+NCHAR(769)+N'@example.org' AND (SELECT atributo_instancia_chave FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1')=N'josÉ@example.org' AND (SELECT atributo_instancia_chave FROM gold.pessoa_atributo WHERE source_record_id='SEH002-EMAIL-1' AND vigencia_fim IS NULL)=N'josÉ@example.org' THEN 1 ELSE 0 END;"; $n=(Sql @('-d',$Db,'-W','-h','-1','-Q',$q) | Out-String).Trim(); if($n -ne '1'){ throw 'Migração EMAIL_CANONICO_V2 não convergiu a chave legada.' } }
function AssertSchemaMarker { $q="SET NOCOUNT ON; SELECT CASE WHEN CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa'))=N'3.62' AND CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema'))=N'3.69' THEN 1 ELSE 0 END;"; $n=(Sql @('-d',$Db,'-W','-h','-1','-Q',$q) | Out-String).Trim(); if($n -ne '1'){ throw 'Marcador de schema incompatível.' } }
Compose @('up','-d','sqlserver'); WaitHealthy
Sql @('-Q',"IF DB_ID(N'$Db') IS NOT NULL BEGIN ALTER DATABASE [$Db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Db]; END; CREATE DATABASE [$Db];")
Sql @('-d',$Db,'-i',"/workspace/$($BaselineRel -replace '\\','/')")
Sql @('-d',$Db,'-Q',"INSERT ref.gestor(codigo,nome,ativo) VALUES('ZZ_UPGRADE_SENTINEL','Sentinela DDL Upgrade',1);")
# Fixture real de upgrade a partir do baseline v3.65: chave V1 que preservou '00' apenas como dígitos.
Sql @('-d',$Db,'-i',"/workspace/$($BaselineSeedRel -replace '\\','/')")
Sql @('-d',$Db,'-Q',"DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'00 55 11 99999-0001',atributo_instancia_chave='005511999990001' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;")
Sql @('-d',$Db,'-Q',"DECLARE @id BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH002-EMAIL-1'); UPDATE silver.pessoa_atributo_observacao SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id; UPDATE gold.pessoa_atributo SET valor=N'JOSÉ@EXAMPLE.ORG',atributo_instancia_chave=N'josé@example.org' WHERE pessoa_atributo_observacao_id=@id AND vigencia_fim IS NULL;")
$baseline=Fingerprint 'baseline'
Sql @('-d',$Db,'-i','/workspace/database/Jornada_Fase1.sql'); AssertSentinel; AssertPhoneV2; AssertEmailV2; AssertSchemaMarker; Sql @('-d',$Db,'-i','/workspace/database/Jornada_Runtime_Smoke.sql'); $first=Fingerprint 'current-first'
Sql @('-d',$Db,'-i','/workspace/database/Jornada_Fase1.sql'); AssertSentinel; $second=Fingerprint 'current-second'
if($first -ne $second){ throw 'Fingerprint mudou na segunda aplicação; idempotência violada.' }
@("baseline=$BaselineRel","baseline_seed=$BaselineSeedRel","baseline_fingerprint=$baseline","current_first_fingerprint=$first","current_second_fingerprint=$second","sentinel_preserved=true","phone_v2_legacy_00_migrated=true","email_v2_legacy_migrated=true","schema_marker_exact=true","runtime_smoke=true","idempotent=true") | Set-Content -Encoding utf8 (Join-Path $outDir 'result.txt')
Get-Content (Join-Path $outDir 'result.txt'); Write-Host 'DDL UPGRADE GATE: OK'

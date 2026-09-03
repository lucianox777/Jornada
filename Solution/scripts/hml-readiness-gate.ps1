$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$perf = $env:JORNADA_HML_PERFORMANCE_REPORT
$linkage = $env:JORNADA_HML_LINKAGE_REPORT
$sqlPerf = $env:JORNADA_HML_SQL_PERFORMANCE_REPORT
$apiProjection = $env:JORNADA_HML_API_PROJECTION_REPORT
if ([string]::IsNullOrWhiteSpace($perf)) { throw 'defina JORNADA_HML_PERFORMANCE_REPORT com evidência real do harness de escala em HML' }
if ([string]::IsNullOrWhiteSpace($linkage)) { throw 'defina JORNADA_HML_LINKAGE_REPORT com evidência real do Jornada.Linkage.Evaluation' }
if ([string]::IsNullOrWhiteSpace($sqlPerf)) { throw 'defina JORNADA_HML_SQL_PERFORMANCE_REPORT' }
if ([string]::IsNullOrWhiteSpace($apiProjection)) { throw 'defina JORNADA_HML_API_PROJECTION_REPORT' }
$out = if ([string]::IsNullOrWhiteSpace($env:JORNADA_HML_READINESS_OUT)) { Join-Path $Root '.local/hml-readiness' } else { $env:JORNADA_HML_READINESS_OUT }
New-Item -ItemType Directory -Force $out | Out-Null

& python3 (Join-Path $Root 'scripts/environment-preflight-gate.py') --root $Root --profile hml-runtime --strict --summary (Join-Path $out 'environment-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'preflight do ambiente HML falhou' }
& python3 (Join-Path $Root 'scripts/governance-readiness-gate.py') --root $Root --require-approved --summary (Join-Path $out 'governance-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'governança técnica ainda não aprovada' }
& python3 (Join-Path $Root 'scripts/scheduler-contract-gate.py') --root $Root --require-scheduled --summary (Join-Path $out 'scheduler-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'scheduler corporativo ainda não aprovado/configurado' }
& python3 (Join-Path $Root 'scripts/hml-config-gate.py') --root $Root --require-approved --summary (Join-Path $out 'config-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'contratos HML não aprovados' }
& python3 (Join-Path $Root 'scripts/hml-staleness-gate.py') --strict | Tee-Object -FilePath (Join-Path $out 'staleness.txt')
if ($LASTEXITCODE -ne 0) { throw 'aprovação HML expirada ou incompatível com release/schema/ambiente' }
& python3 (Join-Path $Root 'scripts/performance-evidence-gate.py') $perf `
  --baseline (Join-Path $Root 'config/hml/performance-baseline.json') --require-baseline-approved `
  --summary (Join-Path $out 'performance-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'evidência de desempenho fora do baseline ou baseline não aprovado' }
& python3 (Join-Path $Root 'scripts/linkage-evaluation-evidence-gate.py') $linkage `
  --policy (Join-Path $Root 'config/hml/linkage-evaluation-policy.json') --require-policy-approved `
  --summary (Join-Path $out 'linkage-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'evidência de linkage fora da política ou política não aprovada' }
& python3 (Join-Path $Root 'scripts/sql-performance-evidence-gate.py') $sqlPerf --policy (Join-Path $Root 'config/hml/sql-performance-policy.json') --require-policy-approved --summary (Join-Path $out 'sql-performance-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'evidência SQL fora da política ou política não aprovada' }
& python3 (Join-Path $Root 'scripts/api-projection-evidence-gate.py') $apiProjection --policy (Join-Path $Root 'config/hml/api-projection-load-policy.json') --require-policy-approved --summary (Join-Path $out 'api-projection-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'evidência API 1/10/100/1000 fora da política ou política não aprovada' }
@'
status=OK
environment=READY
governance=APPROVED
scheduler=APPROVED
parameters=APPROVED_AND_CURRENT
performance=APPROVED_AND_WITHIN_BASELINE
linkage=APPROVED_AND_WITHIN_POLICY
sqlPerformance=APPROVED_AND_WITHIN_POLICY
apiProjection=APPROVED_AND_WITHIN_POLICY
calibrationFreshness=PASS
'@ | Set-Content -Encoding utf8 (Join-Path $out 'result.txt')
Write-Host 'HML READINESS GATE: OK'

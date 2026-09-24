param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'local-synthetic-calibration.ps1'
$oldEnv = $env:JORNADA_LOCAL_ENV_FILE
$oldKey = $env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY
$temp = Join-Path ([IO.Path]::GetTempPath()) ('jornada-isolation-' + [Guid]::NewGuid().ToString('N') + '.env')
try {
    @('JORNADA_SQL_DATABASE=JornadaLocal','JORNADA_SQL_SA_PASSWORD=TEST_ONLY','JORNADA_SQL_PORT=14333') |
        Set-Content -LiteralPath $temp -Encoding ASCII
    $env:JORNADA_LOCAL_ENV_FILE = $temp
    $env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY = 'TEST_ONLY_ISOLATION_KEY_32_BYTES'
    $refused = $false
    try { & $script -People 10 -Waves 3 -Seed 42 }
    catch {
        $refused = $_.Exception.Message.Contains('Ensaio sintetico recusado')
        if (-not $refused) { throw }
    }
    if (-not $refused) { throw 'A guarda deixou o ensaio atingir local-db no banco original.' }
    $source = Get-Content -LiteralPath $script -Raw -Encoding UTF8
    $guard = $source.IndexOf('Ensaio sintetico recusado')
    $up = $source.IndexOf("& (Join-Path $Root 'scripts/local-db.ps1') up")
    if ($guard -lt 0 -or $up -lt 0 -or $guard -gt $up) {
        throw 'A guarda deve executar ANTES de local-db up.'
    }
    if (-not $source.Contains('ExclusivePreflight.sql') -or -not $source.Contains('-DatabaseName $db')) {
        throw 'Preflight SQL e banco explicito sao obrigatorios.'
    }
    Write-Host 'SYNTHETIC DATABASE ISOLATION SAFETY: OK'
}
finally {
    $env:JORNADA_LOCAL_ENV_FILE = $oldEnv
    $env:JORNADA_SYNTH_PSEUDONYMIZATION_KEY = $oldKey
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
}

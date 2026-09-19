param(
    [ValidateSet('standard','full')]
    [string]$Suite = 'full',
    [switch]$AllowDestructiveReset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CurrentPowerShell = (Get-Process -Id $PID).Path
$restoreRequired = $false
$primaryFailure = $null

if (-not $AllowDestructiveReset) {
    Write-Host ''
    Write-Warning 'TESTE FROM ZERO NAO EXECUTADO: este fluxo remove volumes, recria o banco e recarrega a referencia IBGE.'
    Write-Host 'Para autorizar explicitamente:'
    Write-Host '# .\scripts\local-test-from-zero.ps1 -Suite full -AllowDestructiveReset'
    throw 'Reset destrutivo nao autorizado.'
}

function Invoke-Script {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [string[]]$Arguments = @()
    )
    $path = Join-Path $PSScriptRoot $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Script nao encontrado: $path" }
    Write-Host ("# .\scripts\{0} {1}" -f $Name,($Arguments -join ' ')).Trim()
    & $CurrentPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name falhou ($LASTEXITCODE)." }
}

Push-Location $Root
try {
    Write-Host ''
    Write-Host 'Jornada - teste explicito FROM ZERO' -ForegroundColor Yellow
    Write-Host 'ATENCAO: volumes SQL/cluster serao removidos e a referencia IBGE sera recarregada.'
    $restoreRequired = $true

    Invoke-Script 'local-cluster.ps1' @('-Action','clean')
    Invoke-Script 'local-db.ps1' @('-Action','reset')
    Invoke-Script 'local-load-ibge-reference.ps1' @('-AllowLoad')

    Invoke-Script 'local-test-all.ps1' @('-Suite',$Suite)

    if ($Suite -eq 'full') {
        Write-Host ''
        Write-Host '=== Scale smoke destrutivo (somente FROM ZERO) ==='
        Invoke-Script 'local-scale.ps1' @('-Profile','smoke')
    }
}
catch {
    $primaryFailure = $_.Exception
}
finally {
    if ($restoreRequired) {
        try {
            Write-Host ''
            Write-Host 'Restaurando ambiente canonico compartilhado apos FROM ZERO...'
            Invoke-Script 'local-db.ps1' @('-Action','reset')
            Invoke-Script 'local-load-ibge-reference.ps1' @('-AllowLoad','-NoBuild')
            Invoke-Script 'local-check-ibge-reference.ps1'
        }
        catch {
            if ($null -eq $primaryFailure) {
                $primaryFailure = $_.Exception
            }
            else {
                $primaryFailure = [InvalidOperationException]::new(
                    "$($primaryFailure.Message) Falha adicional na restauracao canonica: $($_.Exception.Message)")
            }
        }
    }
    Pop-Location
}

if ($null -ne $primaryFailure) { throw $primaryFailure }

Write-Host ''
Write-Host 'LOCAL TEST FROM ZERO: OK' -ForegroundColor Green

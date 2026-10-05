function Get-JornadaRuntimeMode {
    param(
        [switch]$Dev,
        [switch]$Prod
    )

    if ($Dev -and $Prod) {
        throw 'Use apenas um modo explicito: -Dev ou -Prod.'
    }
    if ($Dev) { return 'DEV' }
    if ($Prod) { return 'PROD' }

    $configured = [string]$env:JORNADA_RUNTIME_MODE
    if ([string]::IsNullOrWhiteSpace($configured)) { return 'HML' }

    switch ($configured.Trim().ToUpperInvariant()) {
        'DEV' { return 'DEV' }
        'HML' { return 'HML' }
        'PROD' { return 'PROD' }
        default { throw "JORNADA_RUNTIME_MODE invalido: $configured. Use DEV, HML ou PROD." }
    }
}

function Set-JornadaRuntimeMode {
    param(
        [switch]$Dev,
        [switch]$Prod
    )
    $mode = Get-JornadaRuntimeMode -Dev:$Dev -Prod:$Prod
    $env:JORNADA_RUNTIME_MODE = $mode
    return $mode
}

function Get-JornadaEnvironmentProfile {
    param([Parameter(Mandatory=$true)][ValidateSet('DEV','HML','PROD')][string]$Mode)
    switch ($Mode) {
        'DEV' { return 'Development' }
        'HML' { return 'Homologation' }
        'PROD' { return 'Production' }
    }
}

function Assert-JornadaDestructiveAllowed {
    param(
        [Parameter(Mandatory=$true)][ValidateSet('DEV','HML','PROD')][string]$Mode,
        [Parameter(Mandatory=$true)][string]$Operation,
        [switch]$ConfirmProductionReset
    )
    if ($Mode -eq 'PROD' -and -not $ConfirmProductionReset) {
        throw "Operacao destrutiva '$Operation' bloqueada em PROD. Repita explicitamente com -ConfirmProductionReset fora da interface."
    }
}

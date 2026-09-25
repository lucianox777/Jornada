# Only evaluate actual linkage flow SQL functions, without running the flow.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'local-linkage-validation-flow.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if ($errors.Count -gt 0) { throw 'Linkage validation flow contains PowerShell parse errors.' }
$previousPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('jornada-linkage-flow-mock-' + [guid]::NewGuid().ToString('N'))
$global:MockLinkagePassword = 'Synthetic_Linkage_Flow_SQL_Mock_2026!'
$global:MockLinkageCalls = 0
$global:MockLinkageMode = 'success'
function global:docker {
    $argv = @($args | ForEach-Object { [string]$_ })
    $global:MockLinkageCalls++
    $idx = [array]::IndexOf($argv,'-e')
    if ($argv -notcontains 'exec' -or $idx -lt 0 -or $idx + 1 -ge $argv.Count -or
        $argv[$idx+1] -cne 'SQLCMDPASSWORD') {
        throw 'Get-SqlScalar must pass -e SQLCMDPASSWORD by name.'
    }
    foreach ($argument in $argv) {
        if ($argument.StartsWith('SQLCMDPASSWORD=') -or $argument.Contains($global:MockLinkagePassword)) {
            throw 'Linkage flow exposed SQL secret through Docker argv.'
        }
    }
    if ($env:SQLCMDPASSWORD -cne $global:MockLinkagePassword) {
        throw 'Docker did not inherit temporary SQLCMDPASSWORD.'
    }
    if ($global:MockLinkageMode -eq 'throw') { throw 'Synthetic linkage Docker exception.' }
    $global:LASTEXITCODE = if ($global:MockLinkageMode -eq 'exit') { 29 } else { 0 }
    if ($global:MockLinkageMode -eq 'success') { return ' 17 ' }
}
try {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($fixture,('JORNADA_SQL_SA_PASSWORD=' + $global:MockLinkagePassword + [string][char]10),$utf8)
    $EnvFile = $fixture
    $functions = @($ast.EndBlock.Statements | Where-Object {
        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst]
    })
    foreach ($name in @('Get-SqlPassword','Get-SqlScalar')) {
        $matches = @($functions | Where-Object { $_.Name -ceq $name })
        if ($matches.Count -ne 1) { throw "Missing real linkage function $name." }
        . ([scriptblock]::Create($matches[0].Extent.Text))
    }
    foreach ($scenario in @('success','exit','throw')) {
        $global:MockLinkageMode = $scenario
        $env:SQLCMDPASSWORD = 'PARENT_SCOPE_SENTINEL'
        $before = $global:MockLinkageCalls
        $caught = $false
        try {
            $value = Get-SqlScalar -Query 'SELECT 17'
            if ($scenario -eq 'success' -and $value -ne '17') {
                throw 'Linkage flow scalar result was not preserved.'
            }
        }
        catch {
            if ($scenario -eq 'success') { throw }
            if ($scenario -eq 'exit' -and $_.Exception.Message -notlike '*sqlcmd falhou*') { throw }
            if ($scenario -eq 'throw' -and $_.Exception.Message -notlike '*Synthetic linkage Docker exception*') { throw }
            $caught = $true
        }
        if ($scenario -ne 'success' -and -not $caught) { throw "Linkage flow swallowed $scenario failure." }
        if ($global:MockLinkageCalls -ne $before+1) { throw 'Linkage flow must call Docker once per SQL query.' }
        if ($env:SQLCMDPASSWORD -cne 'PARENT_SCOPE_SENTINEL') {
            throw "Linkage flow did not restore previous SQLCMDPASSWORD after $scenario."
        }
    }
    Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    $global:MockLinkageMode = 'success'
    $value = Get-SqlScalar -Query 'SELECT 17'
    if ($value -ne '17' -or
        $null -ne [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')) {
        throw 'Linkage flow failed to restore an initially absent SQLCMDPASSWORD.'
    }
    if ($global:MockLinkageCalls -ne 4) { throw 'Expected four isolated Docker calls.' }
    $global:LASTEXITCODE = 0
    Write-Host 'LINKAGE FLOW POWERSHELL SQLCMD SECRET TRANSPORT MOCK: OK'
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:\SQLCMDPASSWORD -ErrorAction SilentlyContinue
    } else {
        $env:SQLCMDPASSWORD = $previousPassword
    }
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
    Remove-Item Function:\docker -ErrorAction SilentlyContinue
    Remove-Variable MockLinkagePassword,MockLinkageCalls,MockLinkageMode -Scope Global -ErrorAction SilentlyContinue
}

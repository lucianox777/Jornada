param(
    [Parameter(Mandatory=$true)][string]$ConnectionString,
    [Parameter(Mandatory=$true)][string]$SqlcmdServer,
    [Parameter(Mandatory=$true)][string]$SqlcmdDatabase,
    [Parameter(Mandatory=$true)][string]$SqlcmdUser
)
$ErrorActionPreference='Stop'
if (-not $env:SQLCMDPASSWORD) { throw 'Configure SQLCMDPASSWORD no ambiente, sem gravá-la no repositório.' }
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Scalar([string]$sql) {
    $value=& sqlcmd -S $SqlcmdServer -d $SqlcmdDatabase -U $SqlcmdUser -C -b -W -h -1 -Q "SET NOCOUNT ON; $sql"
    if ($LASTEXITCODE -ne 0) { throw 'sqlcmd falhou.' }
    return (@($value | ForEach-Object { $_.Trim() } | Where-Object { $_ })[-1]).Trim()
}
if ((Scalar "SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status=N'ATIVO';") -lt 1) { throw 'Modelo ATIVO ausente.' }
# A antiga tabela controle.modo_carga_inicial foi removida; a segurança
# depende do modelo ATIVO e da coordenação do Runner (não de tabela legada).
$marker=[Guid]::NewGuid().ToString('N')
$env:ConnectionStrings__Jornada=$ConnectionString
Push-Location $root
try {
    dotnet run --project src/Jornada.Linkage.Runner --configuration Release -- --mode ON_DEMAND --max-records 100 --requested-by DT05_E2E --reason $marker --publish true
    if ($LASTEXITCODE -ne 0) { throw 'Runner real falhou.' }
} finally { Pop-Location }
$run=Scalar "SELECT COUNT(*) FROM identidade.linkage_run WHERE solicitado_por=N'DT05_E2E' AND motivo=N'$marker' AND status=N'PUBLICADO';"
if ($run -ne '1') { throw 'Execução PUBLICADO não encontrada.' }
$raw=Scalar "SELECT COUNT(*) FROM identidade.linkage_resultado r JOIN identidade.linkage_run l ON l.linkage_run_id=r.linkage_run_id WHERE l.solicitado_por=N'DT05_E2E' AND l.motivo=N'$marker';"
if ([int]$raw -lt 1) { throw 'Resultado bruto ausente.' }
Write-Host "DT-05 Runner real publicado: $raw resultados brutos; CPF tardio e replay NAS ainda pendentes."

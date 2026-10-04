param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('up','clean','status')]
    [string]$Action
)

$ErrorActionPreference='Stop'
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Cluster=Join-Path $PSScriptRoot 'local-cluster.ps1'
$EnvFile=Join-Path $Root '.env'

function Invoke-Cluster([string]$ClusterAction,[switch]$NoBuild){
    Write-Host "# pwsh -NoProfile -File scripts/local-cluster.ps1 -Action $ClusterAction$([string]::Concat($(if($NoBuild){' -NoBuild'}else{''})))"
    if($NoBuild){ & $Cluster -Action $ClusterAction -NoBuild }
    else { & $Cluster -Action $ClusterAction }
    if($LASTEXITCODE -ne 0){ throw "local-cluster.ps1 $ClusterAction falhou ($LASTEXITCODE)." }
}

switch($Action){
    'up' {
        Write-Host 'Subindo infraestrutura DEV completa sem recompilar a imagem quando ela já existe...'
        try {
            Invoke-Cluster 'up' -NoBuild
        }
        catch {
            $imageExists=$false
            try {
                & docker image inspect jornada-node:test *> $null
                $imageExists=($LASTEXITCODE -eq 0)
            } catch { $imageExists=$false }

            if($imageExists){ throw }

            Write-Host ''
            Write-Host 'Imagem jornada-node:test ainda não existe. Fazendo build único da imagem para esta máquina...'
            Invoke-Cluster 'up'
        }

        Write-Host ''
        Write-Host 'Infraestrutura básica pronta: SQL Server + schema DEV + NAS + referência IBGE + NODE1/NODE2.'
        Write-Host 'O serviço jornada-reference-bootstrap é um init one-shot: Exited (0) significa CONCLUÍDO com sucesso, não falha.'
        Write-Host '# docker compose --env-file .env ps -a'
        Push-Location $Root
        try {
            & docker compose --env-file $EnvFile ps -a
            if($LASTEXITCODE -ne 0){ throw "docker compose ps -a falhou ($LASTEXITCODE)." }
        } finally { Pop-Location }
    }
    'clean' {
        Invoke-Cluster 'clean'
        Write-Host 'Ambiente DEV destruído: containers, volumes e órfãos locais removidos.'
        Write-Host 'Na próxima subida, a infraestrutura será recriada automaticamente.'
    }
    'status' {
        Invoke-Cluster 'status'
        Write-Host ''
        Write-Host 'Nota: jornada-reference-bootstrap deve aparecer como processo concluído (Exited 0) após materializar/validar a referência.'
    }
}

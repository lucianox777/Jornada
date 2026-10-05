param(
    [ValidateRange(1000,100000)]
    [int]$AdditionalPeople=5000
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Text.UTF8Encoding]::new($false)
if($PSVersionTable.PSVersion.Major -ge 7){$PSStyle.OutputRendering='PlainText'}

$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'dev-console-env.ps1')
$Root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$EnvFile=$DevConsoleEnvFile
if(-not(Test-Path $EnvFile)){throw '.env.devconsole ausente. Suba a infraestrutura DEV primeiro.'}

$vars=@{}
Get-Content $EnvFile | ForEach-Object {
    $line=$_.Trim()
    if($line -and -not $line.StartsWith('#') -and $line.Contains('=')){
        $p=$line.Split('=',2)
        $vars[$p[0].Trim()]=$p[1].Trim()
    }
}
$db=if($vars['JORNADA_SQL_DATABASE']){$vars['JORNADA_SQL_DATABASE']}else{'JornadaLocal'}
$password=$vars['JORNADA_SQL_SA_PASSWORD']
if([string]::IsNullOrWhiteSpace($password)){throw 'JORNADA_SQL_SA_PASSWORD ausente.'}

$dockerProbe=@(& docker info --format '{{.ServerVersion}}' 2>&1)
if($LASTEXITCODE -ne 0){
    $detail=($dockerProbe|Out-String).Trim()
    throw "Docker Desktop/Engine não está em execução ou não está acessível. Inicie o Docker Desktop e tente novamente.$(if($detail){' Detalhe: '+$detail}else{''})"
}

function Invoke-SqlScalar([string]$Query){
    $old=$env:SQLCMDPASSWORD
    $env:SQLCMDPASSWORD=$password
    Push-Location $Root
    try{
        $raw=@(& docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -W -h -1 -Q "SET NOCOUNT ON; $Query")
        if($LASTEXITCODE -ne 0){throw "sqlcmd falhou ($LASTEXITCODE)."}
        return [string](@($raw|ForEach-Object{([string]$_).Trim()}|Where-Object{$_})|Select-Object -Last 1)
    } finally {
        if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
        Pop-Location
    }
}

$before=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';")
if($before -lt 30000){throw "Gold sintética DEV base incompleta: encontrados=$before; esperado pelo menos 30000. Execute Subir infraestrutura, referências e bootstrap."}
$target=$before+$AdditionalPeople
if($target -gt 5000000){throw "Total solicitado excede o limite sintético de 5.000.000: $target."}

Write-Host "=== Jornada DEV :: Adicionar Gold sintética ==="
Write-Host "Antes: $before pessoas"
Write-Host "Adicionar: $AdditionalPeople pessoas"
Write-Host "Alvo: $target pessoas"

$sql=@"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @from bigint=$($before+1), @to bigint=$target, @seed int=355;
DECLARE @gSehab bigint=(SELECT gestor_id FROM ref.gestor WHERE codigo=N'SEHAB');
DECLARE @soSehab bigint=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSehab AND codigo=N'SEHAB');
DECLARE @bpSehab bigint=(SELECT base_pessoa_origem_id FROM ref.sistema_origem_base_pessoa WHERE sistema_origem_id=@soSehab AND padrao=1 AND ativo=1);
DECLARE @lotSehab uniqueidentifier=(
    SELECT TOP(1) l.lote_id
    FROM ingestao.lote l
    JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id
    WHERE e.idempotency_key=N'scale-sehab'
    ORDER BY l.criado_em
);
IF @gSehab IS NULL OR @soSehab IS NULL OR @bpSehab IS NULL OR @lotSehab IS NULL
    THROW 51610, 'Massa sintética SEHAB base não encontrada.', 1;

CREATE TABLE #n(n bigint NOT NULL PRIMARY KEY);
;WITH D10(n) AS (SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9))v(n)),
D100(n) AS (SELECT 1 FROM D10 a CROSS JOIN D10 b),
D10000(n) AS (SELECT 1 FROM D100 a CROSS JOIN D100 b),
D100M(n) AS (SELECT 1 FROM D10000 a CROSS JOIN D10000 b)
INSERT #n(n)
SELECT TOP (@to-@from+1) @from-1+ROW_NUMBER() OVER(ORDER BY(SELECT NULL))
FROM D100M;

CREATE TABLE #gold(n bigint PRIMARY KEY,pessoa_uuid uniqueidentifier,cpf char(11),nome nvarchar(500),nascimento date,mae nvarchar(500));
;WITH generated AS (
 SELECT n,RIGHT(REPLICATE('0',9)+CONVERT(varchar(9),700000000+n),9) cpf_base9 FROM #n
)
INSERT #gold
SELECT g.n,
       CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('JORNADA-V355-',@seed,'-P-',g.n))),
       CONVERT(char(11),CONCAT(g.cpf_base9,d1.digito,d2.digito)),
       CONCAT(N'Pessoa Teste ',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),g.n),10)),
       DATEADD(DAY,CONVERT(int,(((((g.n-1)/2)*7919)+@seed)%36500)),CONVERT(date,'1930-01-01')),
       CONCAT(N'Mae Teste ',RIGHT(REPLICATE('0',8)+CONVERT(varchar(10),(g.n+@seed)%100000000),8))
FROM generated g
CROSS APPLY(SELECT CONVERT(int,SUBSTRING(g.cpf_base9,1,1))*10+CONVERT(int,SUBSTRING(g.cpf_base9,2,1))*9+CONVERT(int,SUBSTRING(g.cpf_base9,3,1))*8+CONVERT(int,SUBSTRING(g.cpf_base9,4,1))*7+CONVERT(int,SUBSTRING(g.cpf_base9,5,1))*6+CONVERT(int,SUBSTRING(g.cpf_base9,6,1))*5+CONVERT(int,SUBSTRING(g.cpf_base9,7,1))*4+CONVERT(int,SUBSTRING(g.cpf_base9,8,1))*3+CONVERT(int,SUBSTRING(g.cpf_base9,9,1))*2 soma)s1
CROSS APPLY(SELECT CASE WHEN s1.soma%11<2 THEN 0 ELSE 11-(s1.soma%11) END digito)d1
CROSS APPLY(SELECT CONVERT(int,SUBSTRING(g.cpf_base9,1,1))*11+CONVERT(int,SUBSTRING(g.cpf_base9,2,1))*10+CONVERT(int,SUBSTRING(g.cpf_base9,3,1))*9+CONVERT(int,SUBSTRING(g.cpf_base9,4,1))*8+CONVERT(int,SUBSTRING(g.cpf_base9,5,1))*7+CONVERT(int,SUBSTRING(g.cpf_base9,6,1))*6+CONVERT(int,SUBSTRING(g.cpf_base9,7,1))*5+CONVERT(int,SUBSTRING(g.cpf_base9,8,1))*4+CONVERT(int,SUBSTRING(g.cpf_base9,9,1))*3+d1.digito*2 soma)s2
CROSS APPLY(SELECT CASE WHEN s2.soma%11<2 THEN 0 ELSE 11-(s2.soma%11) END digito)d2;

IF EXISTS(SELECT 1 FROM #gold WHERE identidade.fn_cpf_ancora_valido(cpf)=0) THROW 51611,'CPF sintético inválido.',1;
IF EXISTS(SELECT 1 FROM #gold g JOIN identidade.cpf_ancora a ON a.cpf=g.cpf COLLATE Latin1_General_100_BIN2 OR a.pessoa_uuid=g.pessoa_uuid) THROW 51612,'Faixa incremental colide com identidade existente.',1;

BEGIN TRAN;
INSERT identidade.pessoa(pessoa_uuid,status,criado_em) SELECT pessoa_uuid,'ATIVO',SYSUTCDATETIME() FROM #gold;
INSERT identidade.cpf_ancora(cpf,pessoa_uuid,criado_em) SELECT cpf,pessoa_uuid,SYSUTCDATETIME() FROM #gold;
INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
SELECT pessoa_uuid,cpf,'PRESENTE',nome,nascimento,mae,1,'CORROBORADO',SYSUTCDATETIME() FROM #gold;
INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id,ultima_recepcao_em,ultima_referencia_recebida)
SELECT @soSehab,CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),n),10)),@bpSehab,SYSUTCDATETIME(),SYSUTCDATETIME() FROM #gold;
INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,@lotSehab,@gSehab,po.codigo_pessoa_origem,1,
LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT('SEHAB:',g.n,':',@seed)),2)),
g.cpf,NULL,g.nome,UPPER(g.nome),g.nascimento,g.mae,UPPER(g.mae),SYSUTCDATETIME()
FROM #gold g JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSehab AND po.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),g.n),10));
INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
SELECT o.pessoa_observacao_id,g.pessoa_uuid,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,SYSUTCDATETIME(),'SCALE_INCREMENTAL'
FROM #gold g JOIN silver.pessoa_observacao o ON o.codigo_pessoa_origem=CONCAT(N'SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(varchar(10),g.n),10));
UPDATE ingestao.lote SET qtd_pessoas=CONVERT(int,@to),atualizado_em=SYSUTCDATETIME() WHERE lote_id=@lotSehab;
COMMIT;
"@

$old=$env:SQLCMDPASSWORD
$env:SQLCMDPASSWORD=$password
Push-Location $Root
try{
    & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -Q $sql
    if($LASTEXITCODE -ne 0){throw "Carga incremental SQL falhou ($LASTEXITCODE)."}

    Write-Host 'Aplicando nomes/sobrenomes IBGE ao universo expandido...'
    & docker compose --env-file $EnvFile exec -T -e SQLCMDPASSWORD sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -I -d $db -v "SCALE_PEOPLE=$target" SCALE_SEED=355 SCALE_COLLISION_MODULO=37 -i /workspace/database/Jornada_Dev_SyntheticScale_Diversify.sql
    if($LASTEXITCODE -ne 0){throw "Diversificação IBGE falhou ($LASTEXITCODE)."}
} finally {
    if($null -eq $old){Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue}else{$env:SQLCMDPASSWORD=$old}
    Pop-Location
}

Write-Host 'Reconstruindo blocking para incluir a nova Gold...'
& (Join-Path $PSScriptRoot 'local-cluster.ps1') -Action blocking -EnvFile $EnvFile -RuntimeMode $DevConsoleRuntimeMode
if($LASTEXITCODE -ne 0){throw "Rebuild de blocking falhou ($LASTEXITCODE)."}

$after=[int64](Invoke-SqlScalar "SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';")
if($after -ne $target){throw "Carga incremental inconsistente: esperado=$target; encontrado=$after."}

$outDir=Join-Path $Root '.local/dev-console'
New-Item -ItemType Directory -Force $outDir|Out-Null
$resultPath=Join-Path $outDir 'gold-synthetic-add.json'
[ordered]@{
    generatedAt=(Get-Date).ToUniversalTime().ToString('o')
    database=$db
    before=$before
    added=$AdditionalPeople
    after=$after
    nameDistribution='IBGE Censo 2022 - frequência publicada'
    blockingRebuilt=$true
}|ConvertTo-Json -Depth 5|Set-Content -Encoding UTF8 $resultPath

Write-Host "SUCESSO: Gold sintética expandida de $before para $after pessoas."
Write-Host "ARTEFATO: $resultPath"

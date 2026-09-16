SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @people BIGINT = $(SCALE_PEOPLE);
DECLARE @seed INT = $(SCALE_SEED);
DECLARE @collisionModulo INT = $(SCALE_COLLISION_MODULO);

IF @people < 1000
    THROW 51560, 'SCALE_PEOPLE inválido para diversificação.', 1;

/*
  O corpus SCALE usa a referência local, versionada e imutável do Censo 2022 —
  Nomes no Brasil. A identidade dos tokens é amostrada com peso proporcional à
  frequência publicada pelo IBGE. A composição do nome completo (nome simples ou
  composto e quantidade de sobrenomes) é somente uma fixture de cobertura do teste:
  o produto publicado pelo IBGE não fornece a distribuição conjunta necessária para
  reconstruir nome completo, coocorrência de sobrenomes ou conectivos.

  O sqlcmd executa dentro do container SQL Server. Os arquivos abaixo são o mesmo
  snapshot canônico consumido pelo loader operacional e são montados read-only em
  /workspace/data/reference/ibge-nomes-2022.
*/

DECLARE @projectionManifest NVARCHAR(MAX);
SELECT @projectionManifest = BulkColumn
FROM OPENROWSET(
    BULK '/workspace/data/reference/ibge-nomes-2022/projection-manifest.json',
    SINGLE_CLOB,
    CODEPAGE = '65001'
) AS manifest_file;

DECLARE @referenceCode NVARCHAR(80) = JSON_VALUE(@projectionManifest, '$.referenceCode');
DECLARE @brasilSha VARCHAR(64);
DECLARE @brasilRows BIGINT;
DECLARE @sexoBrasilSha VARCHAR(64);
DECLARE @sexoBrasilRows BIGINT;

SELECT
    @brasilSha = sha256,
    @brasilRows = rowCount
FROM OPENJSON(@projectionManifest, '$.files')
WITH (
    path NVARCHAR(300) '$.path',
    sha256 VARCHAR(64) '$.sha256',
    rowCount BIGINT '$.rowCount'
)
WHERE path = N'projection/frequencia-brasil.ndjson.gz';

SELECT
    @sexoBrasilSha = sha256,
    @sexoBrasilRows = rowCount
FROM OPENJSON(@projectionManifest, '$.files')
WITH (
    path NVARCHAR(300) '$.path',
    sha256 VARCHAR(64) '$.sha256',
    rowCount BIGINT '$.rowCount'
)
WHERE path = N'projection/frequencia-sexo-brasil.ndjson.gz';

IF @referenceCode IS NULL OR @brasilSha IS NULL OR @brasilRows IS NULL
   OR @sexoBrasilSha IS NULL OR @sexoBrasilRows IS NULL
    THROW 51562, 'Manifesto IBGE local incompleto para o corpus SCALE.', 1;

DECLARE @brasilGzip VARBINARY(MAX);
DECLARE @sexoBrasilGzip VARBINARY(MAX);

SELECT @brasilGzip = BulkColumn
FROM OPENROWSET(
    BULK '/workspace/data/reference/ibge-nomes-2022/projection/frequencia-brasil.ndjson.gz',
    SINGLE_BLOB
) AS brasil_file;

SELECT @sexoBrasilGzip = BulkColumn
FROM OPENROWSET(
    BULK '/workspace/data/reference/ibge-nomes-2022/projection/frequencia-sexo-brasil.ndjson.gz',
    SINGLE_BLOB
) AS sexo_brasil_file;

IF UPPER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', @brasilGzip), 2)) <> UPPER(@brasilSha)
    THROW 51563, 'SHA-256 da referência IBGE BRASIL_TOTAL diverge do manifesto.', 1;
IF UPPER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', @sexoBrasilGzip), 2)) <> UPPER(@sexoBrasilSha)
    THROW 51564, 'SHA-256 da referência IBGE BRASIL_SEXO diverge do manifesto.', 1;

-- A descompressão é feita pelo sistema operacional somente para que BULK INSERT
-- aplique CODEPAGE=65001 linha a linha; a integridade do .gz foi verificada acima.
!! gzip -dc /workspace/data/reference/ibge-nomes-2022/projection/frequencia-brasil.ndjson.gz > /tmp/jornada-scale-frequencia-brasil.ndjson
!! gzip -dc /workspace/data/reference/ibge-nomes-2022/projection/frequencia-sexo-brasil.ndjson.gz > /tmp/jornada-scale-frequencia-sexo-brasil.ndjson

CREATE TABLE #raw_brasil(line NVARCHAR(MAX) NOT NULL);
CREATE TABLE #raw_sexo_brasil(line NVARCHAR(MAX) NOT NULL);

BULK INSERT #raw_brasil
FROM '/tmp/jornada-scale-frequencia-brasil.ndjson'
WITH (CODEPAGE = '65001', DATAFILETYPE = 'char', FIELDTERMINATOR = '0x0b', ROWTERMINATOR = '0x0a', TABLOCK);

BULK INSERT #raw_sexo_brasil
FROM '/tmp/jornada-scale-frequencia-sexo-brasil.ndjson'
WITH (CODEPAGE = '65001', DATAFILETYPE = 'char', FIELDTERMINATOR = '0x0b', ROWTERMINATOR = '0x0a', TABLOCK);

IF (SELECT COUNT_BIG(*) FROM #raw_brasil) <> @brasilRows
    THROW 51565, 'rowCount da referência IBGE BRASIL_TOTAL diverge do manifesto.', 1;
IF (SELECT COUNT_BIG(*) FROM #raw_sexo_brasil) <> @sexoBrasilRows
    THROW 51566, 'rowCount da referência IBGE BRASIL_SEXO diverge do manifesto.', 1;

CREATE TABLE #freq_brasil(
    tipo NVARCHAR(20) NOT NULL,
    valor NVARCHAR(200) NOT NULL,
    frequencia BIGINT NOT NULL
);

INSERT #freq_brasil(tipo, valor, frequencia)
SELECT
    UPPER(COALESCE(JSON_VALUE(line, '$.tipo'), JSON_VALUE(line, '$.Tipo'))),
    LTRIM(RTRIM(COALESCE(JSON_VALUE(line, '$.valor'), JSON_VALUE(line, '$.Valor')))),
    TRY_CONVERT(BIGINT, COALESCE(JSON_VALUE(line, '$.frequencia'), JSON_VALUE(line, '$.Frequencia')))
FROM #raw_brasil;

IF EXISTS(SELECT 1 FROM #freq_brasil WHERE tipo NOT IN(N'NOME',N'SOBRENOME') OR valor=N'' OR frequencia<=0)
    THROW 51567, 'Linha inválida na referência IBGE BRASIL_TOTAL.', 1;

CREATE TABLE #freq_nome_feminino(
    valor NVARCHAR(200) NOT NULL,
    frequencia BIGINT NOT NULL
);

INSERT #freq_nome_feminino(valor, frequencia)
SELECT
    LTRIM(RTRIM(COALESCE(JSON_VALUE(line, '$.valor'), JSON_VALUE(line, '$.Valor')))),
    TRY_CONVERT(BIGINT, COALESCE(JSON_VALUE(line, '$.frequencia'), JSON_VALUE(line, '$.Frequencia')))
FROM #raw_sexo_brasil
WHERE UPPER(COALESCE(JSON_VALUE(line, '$.tipo'), JSON_VALUE(line, '$.Tipo'))) = N'NOME'
  AND UPPER(COALESCE(JSON_VALUE(line, '$.sexo'), JSON_VALUE(line, '$.Sexo'))) = N'FEMININO';

IF NOT EXISTS(SELECT 1 FROM #freq_brasil WHERE tipo=N'NOME')
   OR NOT EXISTS(SELECT 1 FROM #freq_brasil WHERE tipo=N'SOBRENOME')
   OR NOT EXISTS(SELECT 1 FROM #freq_nome_feminino)
    THROW 51568, 'Referência IBGE não possui os estratos necessários para o corpus SCALE.', 1;
IF EXISTS(SELECT 1 FROM #freq_nome_feminino WHERE valor=N'' OR frequencia<=0)
    THROW 51569, 'Linha inválida na referência IBGE de nomes femininos.', 1;

CREATE TABLE #nome_range(
    upper_bound BIGINT NOT NULL PRIMARY KEY,
    valor NVARCHAR(200) NOT NULL
);
CREATE TABLE #nome_feminino_range(
    upper_bound BIGINT NOT NULL PRIMARY KEY,
    valor NVARCHAR(200) NOT NULL
);
CREATE TABLE #sobrenome_range(
    upper_bound BIGINT NOT NULL PRIMARY KEY,
    valor NVARCHAR(200) NOT NULL
);

;WITH f AS (
    SELECT valor, SUM(frequencia) AS frequencia
    FROM #freq_brasil
    WHERE tipo=N'NOME'
    GROUP BY valor
), r AS (
    SELECT valor,
           SUM(frequencia) OVER(ORDER BY valor COLLATE Latin1_General_100_BIN2 ROWS UNBOUNDED PRECEDING) AS upper_bound
    FROM f
)
INSERT #nome_range(upper_bound,valor)
SELECT upper_bound,valor FROM r;

;WITH f AS (
    SELECT valor, SUM(frequencia) AS frequencia
    FROM #freq_nome_feminino
    GROUP BY valor
), r AS (
    SELECT valor,
           SUM(frequencia) OVER(ORDER BY valor COLLATE Latin1_General_100_BIN2 ROWS UNBOUNDED PRECEDING) AS upper_bound
    FROM f
)
INSERT #nome_feminino_range(upper_bound,valor)
SELECT upper_bound,valor FROM r;

;WITH f AS (
    SELECT valor, SUM(frequencia) AS frequencia
    FROM #freq_brasil
    WHERE tipo=N'SOBRENOME'
    GROUP BY valor
), r AS (
    SELECT valor,
           SUM(frequencia) OVER(ORDER BY valor COLLATE Latin1_General_100_BIN2 ROWS UNBOUNDED PRECEDING) AS upper_bound
    FROM f
)
INSERT #sobrenome_range(upper_bound,valor)
SELECT upper_bound,valor FROM r;

DECLARE @nomeTotal BIGINT=(SELECT MAX(upper_bound) FROM #nome_range);
DECLARE @nomeFemininoTotal BIGINT=(SELECT MAX(upper_bound) FROM #nome_feminino_range);
DECLARE @sobrenomeTotal BIGINT=(SELECT MAX(upper_bound) FROM #sobrenome_range);
IF @nomeTotal IS NULL OR @nomeTotal<=0 OR @nomeFemininoTotal IS NULL OR @nomeFemininoTotal<=0 OR @sobrenomeTotal IS NULL OR @sobrenomeTotal<=0
    THROW 51570, 'Pesos IBGE inválidos para amostragem SCALE.', 1;

CREATE TABLE #scale_names(
    n BIGINT NOT NULL PRIMARY KEY,
    nome NVARCHAR(500) NOT NULL,
    mae NVARCHAR(500) NOT NULL,
    nome_componentes TINYINT NOT NULL,
    sobrenome_componentes TINYINT NOT NULL,
    mae_nome_componentes TINYINT NOT NULL,
    mae_sobrenome_componentes TINYINT NOT NULL
);

;WITH source_n AS (
    SELECT DISTINCT TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10)) AS n
    FROM silver.pessoa_origem po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%'
)
INSERT #scale_names(n,nome,mae,nome_componentes,sobrenome_componentes,mae_nome_componentes,mae_sobrenome_componentes)
SELECT n.n,
       CONCAT(
           pg1.valor,
           CASE WHEN shape.nome_componentes=2 AND pg2.valor<>pg1.valor THEN CONCAT(N' ',pg2.valor) ELSE N'' END,
           N' ',ps1.valor,
           CASE WHEN shape.sobrenome_componentes>=2 AND ps2.valor<>ps1.valor
                THEN CONCAT(CASE WHEN shape.conector_pessoa=1 THEN N' dos ' ELSE N' ' END,ps2.valor) ELSE N'' END,
           CASE WHEN shape.sobrenome_componentes>=3 AND ps3.valor<>ps1.valor AND ps3.valor<>ps2.valor
                THEN CONCAT(N' ',ps3.valor) ELSE N'' END),
       CONCAT(
           mg1.valor,
           CASE WHEN shape.mae_nome_componentes=2 AND mg2.valor<>mg1.valor THEN CONCAT(N' ',mg2.valor) ELSE N'' END,
           N' ',ms1.valor,
           CASE WHEN shape.mae_sobrenome_componentes>=2 AND ms2.valor<>ms1.valor
                THEN CONCAT(CASE WHEN shape.conector_mae=1 THEN N' de ' ELSE N' ' END,ms2.valor) ELSE N'' END,
           CASE WHEN shape.mae_sobrenome_componentes>=3 AND ms3.valor<>ms1.valor AND ms3.valor<>ms2.valor
                THEN CONCAT(N' ',ms3.valor) ELSE N'' END),
       shape.nome_componentes,
       shape.sobrenome_componentes,
       shape.mae_nome_componentes,
       shape.mae_sobrenome_componentes
FROM source_n n
CROSS APPLY (SELECT
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':P:G1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_pg1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':P:G2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_pg2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':P:S1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':P:S2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':P:S3:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps3,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':M:G1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_mg1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':M:G2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_mg2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':M:S1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':M:S2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@seed,N':M:S3:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms3
) h
CROSS APPLY (SELECT
    (h.h_pg1%@nomeTotal)+1 AS t_pg1,
    (h.h_pg2%@nomeTotal)+1 AS t_pg2,
    (h.h_ps1%@sobrenomeTotal)+1 AS t_ps1,
    (h.h_ps2%@sobrenomeTotal)+1 AS t_ps2,
    (h.h_ps3%@sobrenomeTotal)+1 AS t_ps3,
    (h.h_mg1%@nomeFemininoTotal)+1 AS t_mg1,
    (h.h_mg2%@nomeFemininoTotal)+1 AS t_mg2,
    (h.h_ms1%@sobrenomeTotal)+1 AS t_ms1,
    (h.h_ms2%@sobrenomeTotal)+1 AS t_ms2,
    (h.h_ms3%@sobrenomeTotal)+1 AS t_ms3
) target
CROSS APPLY (SELECT
    CONVERT(TINYINT,CASE WHEN ((n.n+CONVERT(BIGINT,@seed))%4+4)%4=0 THEN 2 ELSE 1 END) AS nome_componentes,
    CONVERT(TINYINT,1+(((n.n+CONVERT(BIGINT,@seed))%3+3)%3)) AS sobrenome_componentes,
    CONVERT(TINYINT,CASE WHEN ((n.n+CONVERT(BIGINT,@seed)+1)%5+5)%5=0 THEN 2 ELSE 1 END) AS mae_nome_componentes,
    CONVERT(TINYINT,1+(((n.n+CONVERT(BIGINT,@seed)+1)%3+3)%3)) AS mae_sobrenome_componentes,
    CONVERT(BIT,CASE WHEN ((n.n+CONVERT(BIGINT,@seed))%10+10)%10=0 THEN 1 ELSE 0 END) AS conector_pessoa,
    CONVERT(BIT,CASE WHEN ((n.n+CONVERT(BIGINT,@seed)+3)%10+10)%10=0 THEN 1 ELSE 0 END) AS conector_mae
) shape
OUTER APPLY (SELECT TOP(1) valor FROM #nome_range WHERE upper_bound>=target.t_pg1 ORDER BY upper_bound) pg1
OUTER APPLY (SELECT TOP(1) valor FROM #nome_range WHERE upper_bound>=target.t_pg2 ORDER BY upper_bound) pg2
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ps1 ORDER BY upper_bound) ps1
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ps2 ORDER BY upper_bound) ps2
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ps3 ORDER BY upper_bound) ps3
OUTER APPLY (SELECT TOP(1) valor FROM #nome_feminino_range WHERE upper_bound>=target.t_mg1 ORDER BY upper_bound) mg1
OUTER APPLY (SELECT TOP(1) valor FROM #nome_feminino_range WHERE upper_bound>=target.t_mg2 ORDER BY upper_bound) mg2
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ms1 ORDER BY upper_bound) ms1
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ms2 ORDER BY upper_bound) ms2
OUTER APPLY (SELECT TOP(1) valor FROM #sobrenome_range WHERE upper_bound>=target.t_ms3 ORDER BY upper_bound) ms3
WHERE n.n IS NOT NULL AND n.n BETWEEN 1 AND @people;

IF (SELECT COUNT_BIG(*) FROM #scale_names)<>@people
    THROW 51561, 'Diversificação não encontrou toda a população SCALE-SEHAB.', 1;
IF EXISTS(SELECT 1 FROM #scale_names WHERE nome=N'' OR mae=N'')
    THROW 51571, 'Amostragem IBGE produziu nome sintético vazio.', 1;

-- A fonte determinística e a Gold recebem exatamente a mesma identidade textual.
UPDATE po
SET nome_completo=sn.nome,
    nome_cmp=UPPER(sn.nome),
    nome_mae=sn.mae,
    nome_mae_cmp=UPPER(sn.mae)
FROM silver.pessoa_observacao po
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';

UPDATE g
SET nome_completo=sn.nome,
    nome_mae=sn.mae
FROM gold.pessoa g
JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_uuid=g.pessoa_uuid AND vc.status='RESOLVIDO'
JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vc.pessoa_observacao_id
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';

-- A segunda fonte conserva as mutações do corpus sobre identidades amostradas do IBGE.
UPDATE po
SET nome_completo=CASE
        WHEN sn.n%11=0 THEN CONCAT(sn.nome,N' Filho')
        WHEN sn.n%7=0 THEN CONCAT(LEFT(sn.nome,1),N'. ',SUBSTRING(sn.nome,CHARINDEX(N' ',sn.nome)+1,500))
        ELSE sn.nome END,
    nome_cmp=UPPER(CASE
        WHEN sn.n%11=0 THEN CONCAT(sn.nome,N' Filho')
        WHEN sn.n%7=0 THEN CONCAT(LEFT(sn.nome,1),N'. ',SUBSTRING(sn.nome,CHARINDEX(N' ',sn.nome)+1,500))
        ELSE sn.nome END),
    nome_mae=CASE
        WHEN sn.n%13=0 THEN CONCAT(LEFT(sn.mae,1),N'. ',SUBSTRING(sn.mae,CHARINDEX(N' ',sn.mae)+1,500))
        ELSE sn.mae END,
    nome_mae_cmp=UPPER(CASE
        WHEN sn.n%13=0 THEN CONCAT(LEFT(sn.mae,1),N'. ',SUBSTRING(sn.mae,CHARINDEX(N' ',sn.mae)+1,500))
        ELSE sn.mae END)
FROM silver.pessoa_observacao po
JOIN #scale_names sn ON sn.n=TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SMADS-%';

-- Colisões controladas usam a identidade IBGE-amostrada da pessoa vizinha, mantendo
-- a mãe verdadeira. Assim existe concorrência evidencial sem chave textual universal.
;WITH pending AS (
    SELECT po.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10)) AS pending_n,
           ((TRY_CONVERT(BIGINT,RIGHT(po.codigo_pessoa_origem,10))-1)%@people)+1 AS truth_n
    FROM silver.pessoa_observacao po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
), resolved AS (
    SELECT p.*,
           CASE WHEN p.truth_n%2=0 THEN p.truth_n-1
                WHEN p.truth_n<@people THEN p.truth_n+1
                ELSE p.truth_n-1 END AS neighbor_n
    FROM pending p
)
UPDATE po
SET nome_completo=CASE
        WHEN @collisionModulo>0 AND r.pending_n%NULLIF(@collisionModulo,0)=0 THEN neighbor.nome
        WHEN r.pending_n%9=0 THEN CONCAT(truth.nome,N' Junior')
        WHEN r.pending_n%6=0 THEN CONCAT(LEFT(truth.nome,1),N'. ',SUBSTRING(truth.nome,CHARINDEX(N' ',truth.nome)+1,500))
        ELSE truth.nome END,
    nome_cmp=UPPER(CASE
        WHEN @collisionModulo>0 AND r.pending_n%NULLIF(@collisionModulo,0)=0 THEN neighbor.nome
        WHEN r.pending_n%9=0 THEN CONCAT(truth.nome,N' Junior')
        WHEN r.pending_n%6=0 THEN CONCAT(LEFT(truth.nome,1),N'. ',SUBSTRING(truth.nome,CHARINDEX(N' ',truth.nome)+1,500))
        ELSE truth.nome END),
    nome_mae=CASE
        WHEN r.pending_n%8=0 THEN CONCAT(LEFT(truth.mae,1),N'. ',SUBSTRING(truth.mae,CHARINDEX(N' ',truth.mae)+1,500))
        ELSE truth.mae END,
    nome_mae_cmp=UPPER(CASE
        WHEN r.pending_n%8=0 THEN CONCAT(LEFT(truth.mae,1),N'. ',SUBSTRING(truth.mae,CHARINDEX(N' ',truth.mae)+1,500))
        ELSE truth.mae END)
FROM silver.pessoa_observacao po
JOIN resolved r ON r.pessoa_observacao_id=po.pessoa_observacao_id
JOIN #scale_names truth ON truth.n=r.truth_n
JOIN #scale_names neighbor ON neighbor.n=r.neighbor_n;

-- O hash inclui a referência e o conteúdo sintético efetivamente persistido.
UPDATE po
SET conteudo_hash=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT(
        'SCALE-IBGE-WEIGHTED:',@referenceCode,':',@seed,':',po.codigo_pessoa_origem,':',ISNULL(po.cpf,N''),':',
        ISNULL(po.nome_completo,N''),':',ISNULL(CONVERT(VARCHAR(10),po.data_nascimento,23),''),':',
        ISNULL(po.nome_mae,N''))),2))
FROM silver.pessoa_observacao po
WHERE po.codigo_pessoa_origem LIKE N'SCALE-%';

SELECT @referenceCode AS frequencia_nome_referencia,
       @brasilSha AS frequencia_brasil_sha256,
       COUNT_BIG(*) AS diversified_people,
       COUNT(DISTINCT LEFT(nome,CHARINDEX(N' ',nome+N' ')-1)) AS distinct_first_names,
       COUNT(DISTINCT LEFT(mae,CHARINDEX(N' ',mae+N' ')-1)) AS distinct_mother_first_names,
       SUM(CASE WHEN nome_componentes=2 THEN 1 ELSE 0 END) AS compound_given_name_fixtures,
       SUM(CASE WHEN sobrenome_componentes>=2 THEN 1 ELSE 0 END) AS multiple_surname_fixtures
FROM #scale_names;

!! rm -f /tmp/jornada-scale-frequencia-brasil.ndjson /tmp/jornada-scale-frequencia-sexo-brasil.ndjson

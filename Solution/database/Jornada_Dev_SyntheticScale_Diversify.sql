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
  Nomes no Brasil, previamente carregada pelo NameFrequencySnapshotLoader canônico.
  A identidade dos tokens é amostrada com peso proporcional à frequência publicada.

  A composição do nome completo (nome simples/composto, quantidade de sobrenomes e
  conectivos) é somente uma fixture de cobertura. O produto publicado pelo IBGE não
  fornece a distribuição conjunta necessária para reconstruir nomes completos,
  coocorrência de sobrenomes ou conectivos.
*/

DECLARE @referenceId BIGINT;
DECLARE @referenceCode NVARCHAR(80);
DECLARE @referenceSha VARCHAR(64);

SELECT TOP(1)
    @referenceId = frequencia_nome_versao_id,
    @referenceCode = codigo,
    @referenceSha = CONVERT(VARCHAR(64),conteudo_sha256,2)
FROM ref.frequencia_nome_versao
WHERE status=N'ATIVA';

IF @referenceId IS NULL OR @referenceCode IS NULL OR @referenceSha IS NULL
    THROW 51562, 'Corpus SCALE exige referência IBGE de frequências ATIVA.', 1;

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

-- NOME nacional total: distribuição marginal publicada, sem sexo/período.
;WITH f AS (
    SELECT valor, SUM(CONVERT(BIGINT,frequencia)) AS frequencia
    FROM ref.frequencia_nome
    WHERE frequencia_nome_versao_id=@referenceId
      AND tipo=N'NOME'
      AND sexo=N'TODOS'
      AND periodo_nascimento=N'TODOS'
      AND escopo_geografico=N'BRASIL'
      AND uf_codigo='00'
      AND municipio_codigo='0000000'
    GROUP BY valor
), r AS (
    SELECT valor,
           SUM(frequencia) OVER(ORDER BY valor COLLATE Latin1_General_100_BIN2 ROWS UNBOUNDED PRECEDING) AS upper_bound
    FROM f
)
INSERT #nome_range(upper_bound,valor)
SELECT upper_bound,valor FROM r;

-- Para nome da mãe, usa o estrato feminino nacional publicado.
;WITH f AS (
    SELECT valor, SUM(CONVERT(BIGINT,frequencia)) AS frequencia
    FROM ref.frequencia_nome
    WHERE frequencia_nome_versao_id=@referenceId
      AND tipo=N'NOME'
      AND sexo=N'FEMININO'
      AND periodo_nascimento=N'TODOS'
      AND escopo_geografico=N'BRASIL'
      AND uf_codigo='00'
      AND municipio_codigo='0000000'
    GROUP BY valor
), r AS (
    SELECT valor,
           SUM(frequencia) OVER(ORDER BY valor COLLATE Latin1_General_100_BIN2 ROWS UNBOUNDED PRECEDING) AS upper_bound
    FROM f
)
INSERT #nome_feminino_range(upper_bound,valor)
SELECT upper_bound,valor FROM r;

-- SOBRENOME é componente publicado sem posição canônica.
;WITH f AS (
    SELECT valor, SUM(CONVERT(BIGINT,frequencia)) AS frequencia
    FROM ref.frequencia_nome
    WHERE frequencia_nome_versao_id=@referenceId
      AND tipo=N'SOBRENOME'
      AND sexo=N'TODOS'
      AND periodo_nascimento=N'TODOS'
      AND escopo_geografico=N'BRASIL'
      AND uf_codigo='00'
      AND municipio_codigo='0000000'
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

IF @nomeTotal IS NULL OR @nomeTotal<=0
   OR @nomeFemininoTotal IS NULL OR @nomeFemininoTotal<=0
   OR @sobrenomeTotal IS NULL OR @sobrenomeTotal<=0
    THROW 51563, 'Referência IBGE ATIVA não possui os estratos nacionais necessários ao SCALE.', 1;

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
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':P:G1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_pg1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':P:G2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_pg2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':P:S1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':P:S2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':P:S3:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ps3,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':M:G1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_mg1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':M:G2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_mg2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':M:S1:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms1,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':M:S2:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms2,
    CONVERT(BIGINT,SUBSTRING(HASHBYTES('SHA2_256',CONCAT(@referenceSha,N':',@seed,N':M:S3:',n.n)),1,8)) & CAST(9223372036854775807 AS BIGINT) AS h_ms3
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
    THROW 51564, 'Amostragem IBGE produziu nome sintético vazio.', 1;

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

-- A colisão deliberada troca somente o primeiro nome pelo primeiro nome da pessoa
-- vizinha do mesmo par de nascimento. Os sobrenomes e a mãe permanecem verdadeiros:
-- isso ainda força competição no blocking sem fabricar uma identidade completa falsa
-- que possa ultrapassar o threshold de resolução apenas por construção do fixture.
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
), prepared AS (
    SELECT r.*,
           CONCAT(
               LEFT(neighbor.nome,CHARINDEX(N' ',neighbor.nome+N' ')-1),
               N' ',
               SUBSTRING(truth.nome,CHARINDEX(N' ',truth.nome)+1,500)
           ) AS collision_nome,
           truth.nome AS truth_nome,
           truth.mae AS truth_mae
    FROM resolved r
    JOIN #scale_names truth ON truth.n=r.truth_n
    JOIN #scale_names neighbor ON neighbor.n=r.neighbor_n
)
UPDATE po
SET nome_completo=CASE
        WHEN @collisionModulo>0 AND p.pending_n%NULLIF(@collisionModulo,0)=0 THEN p.collision_nome
        WHEN p.pending_n%9=0 THEN CONCAT(p.truth_nome,N' Junior')
        WHEN p.pending_n%6=0 THEN CONCAT(LEFT(p.truth_nome,1),N'. ',SUBSTRING(p.truth_nome,CHARINDEX(N' ',p.truth_nome)+1,500))
        ELSE p.truth_nome END,
    nome_cmp=UPPER(CASE
        WHEN @collisionModulo>0 AND p.pending_n%NULLIF(@collisionModulo,0)=0 THEN p.collision_nome
        WHEN p.pending_n%9=0 THEN CONCAT(p.truth_nome,N' Junior')
        WHEN p.pending_n%6=0 THEN CONCAT(LEFT(p.truth_nome,1),N'. ',SUBSTRING(p.truth_nome,CHARINDEX(N' ',p.truth_nome)+1,500))
        ELSE p.truth_nome END),
    nome_mae=CASE
        WHEN p.pending_n%8=0 THEN CONCAT(LEFT(p.truth_mae,1),N'. ',SUBSTRING(p.truth_mae,CHARINDEX(N' ',p.truth_mae)+1,500))
        ELSE p.truth_mae END,
    nome_mae_cmp=UPPER(CASE
        WHEN p.pending_n%8=0 THEN CONCAT(LEFT(p.truth_mae,1),N'. ',SUBSTRING(p.truth_mae,CHARINDEX(N' ',p.truth_mae)+1,500))
        ELSE p.truth_mae END)
FROM silver.pessoa_observacao po
JOIN prepared p ON p.pessoa_observacao_id=po.pessoa_observacao_id;

-- O hash inclui a referência e o conteúdo sintético efetivamente persistido.
UPDATE po
SET conteudo_hash=LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT(
        'SCALE-IBGE-WEIGHTED:',@referenceCode,':',@referenceSha,':',@seed,':',po.codigo_pessoa_origem,':',ISNULL(po.cpf,N''),':',
        ISNULL(po.nome_completo,N''),':',ISNULL(CONVERT(VARCHAR(10),po.data_nascimento,23),''),':',
        ISNULL(po.nome_mae,N''))),2))
FROM silver.pessoa_observacao po
WHERE po.codigo_pessoa_origem LIKE N'SCALE-%';

SELECT @referenceCode AS frequencia_nome_referencia,
       @referenceSha AS frequencia_nome_sha256,
       COUNT_BIG(*) AS diversified_people,
       COUNT(DISTINCT LEFT(nome,CHARINDEX(N' ',nome+N' ')-1)) AS distinct_first_names,
       COUNT(DISTINCT LEFT(mae,CHARINDEX(N' ',mae+N' ')-1)) AS distinct_mother_first_names,
       SUM(CASE WHEN nome_componentes=2 THEN 1 ELSE 0 END) AS compound_given_name_fixtures,
       SUM(CASE WHEN sobrenome_componentes>=2 THEN 1 ELSE 0 END) AS multiple_surname_fixtures
FROM #scale_names;
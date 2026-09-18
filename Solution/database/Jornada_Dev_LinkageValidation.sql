SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

/*
  Corpus sintético DEV para avaliação independente do modelo já calibrado.

  Regra de isolamento: este script recusa execução sem um modelo calibrado ATIVO e só então
  injeta as observações de validação. Assim, o corpus abaixo não participa da estimação m/u
  que produziu o modelo sob teste. O prefixo inclui um fragmento do modelo_id para impedir
  reutilização silenciosa do mesmo corpus contra outro modelo na mesma base.

  Cenários:
    - POS: 40 observações com verdade conhecida e ruídos controlados;
    - NEG: 40 impostores sem par verdadeiro, em quatro níveis de adversarialidade;
    - CONFLICT: 10 observações diante de dois candidatos sintéticos indistinguíveis,
      criados depois da calibração, para exercitar a margem de conflito.

  Este fixture não define threshold de qualidade nem constitui homologação. Ele existe para
  separar blocking, decisão, falsos vínculos e cobertura da regra de conflito em DEV/CI.
*/

DECLARE @activeModelId UNIQUEIDENTIFIER;
DECLARE @activeModelVersion INT;
SELECT TOP(1)
    @activeModelId=modelo_id,
    @activeModelVersion=versao
FROM identidade.modelo_linkage
WHERE status=N'ATIVO'
  AND ISNULL(amostra_metodo,N'')<>N'SEED_DEV_FIXO_NAO_TREINADO'
ORDER BY versao DESC;

IF @activeModelId IS NULL
    THROW 51610, 'Validação independente exige modelo calibrado ATIVO antes da injeção do corpus.', 1;

DECLARE @modelShort NVARCHAR(8)=LEFT(CONVERT(NVARCHAR(36),@activeModelId),8);
DECLARE @prefix NVARCHAR(80)=CONCAT(N'SCALE-VAL-',@modelShort,N'-%');

IF EXISTS(SELECT 1 FROM silver.pessoa_observacao WHERE codigo_pessoa_origem LIKE N'SCALE-VAL-%')
BEGIN
    IF EXISTS(
        SELECT 1
        FROM silver.pessoa_observacao
        WHERE codigo_pessoa_origem LIKE N'SCALE-VAL-%'
          AND codigo_pessoa_origem NOT LIKE @prefix)
        THROW 51611, 'Já existe corpus SCALE-VAL ligado a outro modelo. Execute clean/reset antes de validar um novo modelo.', 1;

    SELECT
        CONVERT(VARCHAR(36),@activeModelId) AS modelo_id,
        @activeModelVersion AS modelo_versao,
        @modelShort AS modelo_fragmento,
        SUM(CASE WHEN codigo_pessoa_origem LIKE CONCAT(N'SCALE-VAL-',@modelShort,N'-POS-%') THEN 1 ELSE 0 END) AS positivos,
        SUM(CASE WHEN codigo_pessoa_origem LIKE CONCAT(N'SCALE-VAL-',@modelShort,N'-NEG-%') THEN 1 ELSE 0 END) AS negativos,
        SUM(CASE WHEN codigo_pessoa_origem LIKE CONCAT(N'SCALE-VAL-',@modelShort,N'-CONFLICT-%') THEN 1 ELSE 0 END) AS conflitos,
        SUM(CASE WHEN codigo_pessoa_origem LIKE CONCAT(N'SCALE-VAL-',@modelShort,N'-CAND-%') THEN 1 ELSE 0 END) AS candidatos_conflito,
        N'EXISTENTE_MESMO_MODELO' AS estado
    FROM silver.pessoa_observacao
    WHERE codigo_pessoa_origem LIKE @prefix;
    RETURN;
END;

DECLARE @gSehab BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=N'SEHAB');
DECLARE @gSmdet BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo=N'SMDET');
DECLARE @soSehab BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSehab AND codigo=N'SEHAB');
DECLARE @soSmdet BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmdet AND codigo=N'TRABALHO');
DECLARE @lotSehab UNIQUEIDENTIFIER=(
    SELECT TOP(1) po.lote_id
    FROM silver.pessoa_observacao po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%'
    ORDER BY po.pessoa_observacao_id);
DECLARE @lotSmdet UNIQUEIDENTIFIER=(
    SELECT TOP(1) po.lote_id
    FROM silver.pessoa_observacao po
    WHERE po.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
    ORDER BY po.pessoa_observacao_id);

IF @gSehab IS NULL OR @gSmdet IS NULL OR @soSehab IS NULL OR @soSmdet IS NULL OR @lotSehab IS NULL OR @lotSmdet IS NULL
    THROW 51612, 'Corpus SCALE base deve existir antes da validação independente.', 1;

CREATE TABLE #n(n INT NOT NULL PRIMARY KEY);
;WITH d(n) AS (
    SELECT n FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)
)
INSERT #n(n)
SELECT TOP(40) ROW_NUMBER() OVER(ORDER BY (SELECT NULL))
FROM d a CROSS JOIN d b;

CREATE TABLE #truth(
    n INT NOT NULL PRIMARY KEY,
    pessoa_uuid UNIQUEIDENTIFIER NOT NULL,
    nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NULL);

INSERT #truth(n,pessoa_uuid,nome,nascimento,mae)
SELECT TRY_CONVERT(INT,RIGHT(po.codigo_pessoa_origem,10)),
       vc.pessoa_uuid,
       g.nome_completo,
       g.data_nascimento,
       g.nome_mae
FROM silver.pessoa_observacao po
JOIN identidade.v_vinculo_corrente vc
  ON vc.pessoa_observacao_id=po.pessoa_observacao_id
 AND vc.status=N'RESOLVIDO'
 AND vc.pessoa_uuid IS NOT NULL
JOIN gold.pessoa g ON g.pessoa_uuid=vc.pessoa_uuid
WHERE po.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%'
  AND TRY_CONVERT(INT,RIGHT(po.codigo_pessoa_origem,10)) BETWEEN 1 AND 210;

IF (SELECT COUNT(*) FROM #truth WHERE n BETWEEN 1 AND 40)<>40
   OR (SELECT COUNT(*) FROM #truth WHERE n BETWEEN 101 AND 140)<>40
   OR (SELECT COUNT(*) FROM #truth WHERE n BETWEEN 201 AND 210)<>10
    THROW 51613, 'Corpus SCALE base não contém as 90 identidades de referência exigidas pela validação.', 1;

CREATE TABLE #positive(
    n INT NOT NULL PRIMARY KEY,
    codigo NVARCHAR(200) NOT NULL UNIQUE,
    scenario NVARCHAR(40) NOT NULL,
    truth_uuid UNIQUEIDENTIFIER NOT NULL,
    nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NULL);

INSERT #positive(n,codigo,scenario,truth_uuid,nome,nascimento,mae)
SELECT n.n,
       CONCAT(N'SCALE-VAL-',@modelShort,N'-POS-',scenario.codigo,N'-',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       scenario.codigo,
       t.pessoa_uuid,
       CASE scenario.codigo
           WHEN N'NAME_ABBREV' THEN CONCAT(LEFT(t.nome,1),N'. ',SUBSTRING(t.nome,CHARINDEX(N' ',t.nome)+1,500))
           WHEN N'COMBINED' THEN CONCAT(t.nome,N' Junior')
           ELSE t.nome END,
       CASE scenario.codigo
           WHEN N'BIRTH_SHIFT' THEN DATEADD(DAY,1,t.nascimento)
           WHEN N'COMBINED' THEN DATEADD(DAY,1,t.nascimento)
           ELSE t.nascimento END,
       CASE scenario.codigo
           WHEN N'MOTHER_ABBREV' THEN CASE WHEN t.mae IS NULL THEN NULL ELSE CONCAT(LEFT(t.mae,1),N'. ',SUBSTRING(t.mae,CHARINDEX(N' ',t.mae)+1,500)) END
           WHEN N'COMBINED' THEN CASE WHEN t.mae IS NULL THEN NULL ELSE CONCAT(LEFT(t.mae,1),N'. ',SUBSTRING(t.mae,CHARINDEX(N' ',t.mae)+1,500)) END
           ELSE t.mae END
FROM #n n
JOIN #truth t ON t.n=n.n
CROSS APPLY (SELECT CASE (n.n-1)%5
    WHEN 0 THEN N'EXACT'
    WHEN 1 THEN N'NAME_ABBREV'
    WHEN 2 THEN N'MOTHER_ABBREV'
    WHEN 3 THEN N'BIRTH_SHIFT'
    ELSE N'COMBINED' END AS codigo) scenario;

CREATE TABLE #negative(
    n INT NOT NULL PRIMARY KEY,
    codigo NVARCHAR(200) NOT NULL UNIQUE,
    scenario NVARCHAR(40) NOT NULL,
    nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NULL);

INSERT #negative(n,codigo,scenario,nome,nascimento,mae)
SELECT n.n,
       CONCAT(N'SCALE-VAL-',@modelShort,N'-NEG-',scenario.codigo,N'-',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       scenario.codigo,
       CASE scenario.codigo
           WHEN N'TWIN_LIKE' THEN
               CASE
                   WHEN CHARINDEX(N' ',t.nome)>0
                       THEN CONCAT(
                           LEFT(t.nome,CHARINDEX(N' ',t.nome)-1),
                           N'A',
                           SUBSTRING(t.nome,CHARINDEX(N' ',t.nome),500))
                   ELSE CONCAT(t.nome,N'A')
               END
           WHEN N'NAME_COLLISION' THEN t.nome
           WHEN N'HARD_HOMONYM' THEN t.nome
           ELSE CONCAT(N'Pessoa Distinta Validacao ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)) END,
       t.nascimento,
       CASE scenario.codigo
           WHEN N'TWIN_LIKE' THEN t.mae
           WHEN N'MOTHER_COLLISION' THEN t.mae
           WHEN N'HARD_HOMONYM' THEN t.mae
           ELSE CONCAT(N'Mae Distinta Validacao ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)) END
FROM #n n
JOIN #truth t ON t.n=100+n.n
CROSS APPLY (SELECT CASE (n.n-1)%4
    WHEN 0 THEN N'TWIN_LIKE'
    WHEN 1 THEN N'NAME_COLLISION'
    WHEN 2 THEN N'MOTHER_COLLISION'
    ELSE N'HARD_HOMONYM' END AS codigo) scenario;

CREATE TABLE #conflict(
    n INT NOT NULL PRIMARY KEY,
    candidate_a_uuid UNIQUEIDENTIFIER NOT NULL,
    candidate_b_uuid UNIQUEIDENTIFIER NOT NULL,
    candidate_a_cpf CHAR(11) NOT NULL,
    candidate_b_cpf CHAR(11) NOT NULL,
    candidate_a_codigo NVARCHAR(200) NOT NULL UNIQUE,
    candidate_b_codigo NVARCHAR(200) NOT NULL UNIQUE,
    pending_codigo NVARCHAR(200) NOT NULL UNIQUE,
    nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NULL);

INSERT #conflict(
    n,candidate_a_uuid,candidate_b_uuid,candidate_a_cpf,candidate_b_cpf,
    candidate_a_codigo,candidate_b_codigo,pending_codigo,nome,nascimento,mae)
SELECT n.n,
       CONVERT(UNIQUEIDENTIFIER,HASHBYTES('MD5',CONCAT('JORNADA-VAL-A:',CONVERT(VARCHAR(36),@activeModelId),':',n.n))),
       CONVERT(UNIQUEIDENTIFIER,HASHBYTES('MD5',CONCAT('JORNADA-VAL-B:',CONVERT(VARCHAR(36),@activeModelId),':',n.n))),
       RIGHT(REPLICATE('0',11)+CONVERT(VARCHAR(20),88000000000 + n.n*2),11),
       RIGHT(REPLICATE('0',11)+CONVERT(VARCHAR(20),88000000001 + n.n*2),11),
       CONCAT(N'SCALE-VAL-',@modelShort,N'-CAND-A-',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       CONCAT(N'SCALE-VAL-',@modelShort,N'-CAND-B-',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       CONCAT(N'SCALE-VAL-',@modelShort,N'-CONFLICT-',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       CONCAT(t.nome,N' Validacao Conflito ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6)),
       t.nascimento,
       CONCAT(COALESCE(t.mae,N'Mae'),N' Validacao Conflito ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(6),n.n),6))
FROM #n n
JOIN #truth t ON t.n=200+n.n
WHERE n.n<=10;

CREATE TABLE #single_probe(
    probe NVARCHAR(40) NOT NULL PRIMARY KEY,
    candidate_uuid UNIQUEIDENTIFIER NOT NULL,
    candidate_cpf CHAR(11) NOT NULL UNIQUE,
    candidate_codigo NVARCHAR(200) NOT NULL UNIQUE,
    pending_codigo NVARCHAR(200) NOT NULL UNIQUE,
    candidate_nome NVARCHAR(500) NOT NULL,
    pending_nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NOT NULL);

INSERT #single_probe(
    probe,candidate_uuid,candidate_cpf,candidate_codigo,pending_codigo,
    candidate_nome,pending_nome,nascimento,mae)
VALUES
(
    N'TWIN_SINGLE',
    CONVERT(UNIQUEIDENTIFIER,HASHBYTES('MD5',CONCAT('JORNADA-VAL-TWIN-SINGLE:',CONVERT(VARCHAR(36),@activeModelId)))),
    '88999999001',
    CONCAT(N'SCALE-VAL-',@modelShort,N'-PROBE-CAND-TWIN_SINGLE'),
    CONCAT(N'SCALE-VAL-',@modelShort,N'-PROBE-TWIN_SINGLE'),
    N'GABRIEL OLIVEIRA LIMA VALIDACAO UNICA',
    N'GABRIELA OLIVEIRA LIMA VALIDACAO UNICA',
    CONVERT(DATE,'2099-12-30'),
    N'MARIA APARECIDA LIMA VALIDACAO UNICA'
),
(
    N'SURNAME_SINGLE',
    CONVERT(UNIQUEIDENTIFIER,HASHBYTES('MD5',CONCAT('JORNADA-VAL-SURNAME-SINGLE:',CONVERT(VARCHAR(36),@activeModelId)))),
    '88999999002',
    CONCAT(N'SCALE-VAL-',@modelShort,N'-PROBE-CAND-SURNAME_SINGLE'),
    CONCAT(N'SCALE-VAL-',@modelShort,N'-PROBE-SURNAME_SINGLE'),
    N'MARIA APARECIDA DA SILVA VALIDACAO UNICA',
    N'MARIA APARECIDA DA SOUZA VALIDACAO UNICA',
    CONVERT(DATE,'2099-12-31'),
    N'ANA CRISTINA SILVA VALIDACAO UNICA'
);

BEGIN TRY
    BEGIN TRAN;

    INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,ultima_recepcao_em,ultima_referencia_recebida)
    SELECT @soSmdet,codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #positive
    UNION ALL
    SELECT @soSmdet,codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #negative
    UNION ALL
    SELECT @soSmdet,pending_codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT @soSehab,candidate_a_codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT @soSehab,candidate_b_codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT @soSehab,candidate_codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #single_probe
    UNION ALL
    SELECT @soSmdet,pending_codigo,'2026-09-17T12:00:00+00:00','2026-09-17T12:00:00+00:00' FROM #single_probe;

    INSERT identidade.pessoa(pessoa_uuid,status,criado_em)
    SELECT candidate_a_uuid,N'ATIVO','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT candidate_b_uuid,N'ATIVO','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT candidate_uuid,N'ATIVO','2026-09-17T12:00:00+00:00' FROM #single_probe;

    INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
    SELECT candidate_a_uuid,candidate_a_cpf,N'PRESENTE',nome,nascimento,mae,1,N'BASELINE_FONTE_UNICA','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT candidate_b_uuid,candidate_b_cpf,N'PRESENTE',nome,nascimento,mae,1,N'BASELINE_FONTE_UNICA','2026-09-17T12:00:00+00:00' FROM #conflict
    UNION ALL
    SELECT candidate_uuid,candidate_cpf,N'PRESENTE',candidate_nome,nascimento,mae,1,N'BASELINE_FONTE_UNICA','2026-09-17T12:00:00+00:00' FROM #single_probe;

    INSERT silver.pessoa_observacao(
        pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
        cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
    SELECT po.pessoa_origem_id,@lotSehab,@gSehab,c.candidate_a_codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-CAND-A:',@modelShort,':',c.n)),2)),
           c.candidate_a_cpf,NULL,c.nome,UPPER(c.nome),c.nascimento,c.mae,UPPER(c.mae),'2026-09-17T12:00:00+00:00'
    FROM #conflict c
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSehab AND po.codigo_pessoa_origem=c.candidate_a_codigo
    UNION ALL
    SELECT po.pessoa_origem_id,@lotSehab,@gSehab,c.candidate_b_codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-CAND-B:',@modelShort,':',c.n)),2)),
           c.candidate_b_cpf,NULL,c.nome,UPPER(c.nome),c.nascimento,c.mae,UPPER(c.mae),'2026-09-17T12:00:00+00:00'
    FROM #conflict c
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSehab AND po.codigo_pessoa_origem=c.candidate_b_codigo
    UNION ALL
    SELECT po.pessoa_origem_id,@lotSehab,@gSehab,p.candidate_codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-PROBE-CAND:',@modelShort,':',p.probe)),2)),
           p.candidate_cpf,NULL,p.candidate_nome,UPPER(p.candidate_nome),p.nascimento,p.mae,UPPER(p.mae),'2026-09-17T12:00:00+00:00'
    FROM #single_probe p
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSehab AND po.codigo_pessoa_origem=p.candidate_codigo;

    INSERT identidade.vinculo_fonte(
        pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
    SELECT obs.pessoa_observacao_id,c.candidate_a_uuid,N'CPF_DETERMINISTICO',NULL,N'RESOLVIDO',NULL,1,'2026-09-17T12:01:00+00:00',N'VALIDACAO_INDEPENDENTE_CANDIDATO'
    FROM #conflict c
    JOIN silver.pessoa_observacao obs ON obs.codigo_pessoa_origem=c.candidate_a_codigo
    UNION ALL
    SELECT obs.pessoa_observacao_id,c.candidate_b_uuid,N'CPF_DETERMINISTICO',NULL,N'RESOLVIDO',NULL,1,'2026-09-17T12:01:00+00:00',N'VALIDACAO_INDEPENDENTE_CANDIDATO'
    FROM #conflict c
    JOIN silver.pessoa_observacao obs ON obs.codigo_pessoa_origem=c.candidate_b_codigo
    UNION ALL
    SELECT obs.pessoa_observacao_id,p.candidate_uuid,N'CPF_DETERMINISTICO',NULL,N'RESOLVIDO',NULL,1,'2026-09-17T12:01:00+00:00',N'VALIDACAO_INDEPENDENTE_PROBE_CANDIDATO'
    FROM #single_probe p
    JOIN silver.pessoa_observacao obs ON obs.codigo_pessoa_origem=p.candidate_codigo;

    INSERT silver.pessoa_observacao(
        pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
        cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
    SELECT po.pessoa_origem_id,@lotSmdet,@gSmdet,p.codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-POS:',@modelShort,':',p.n,':',p.scenario)),2)),
           NULL,N'SEM_CPF',p.nome,UPPER(p.nome),p.nascimento,p.mae,CASE WHEN p.mae IS NULL THEN NULL ELSE UPPER(p.mae) END,
           DATEADD(SECOND,p.n,CONVERT(datetimeoffset(0),'2026-09-17T12:10:00+00:00'))
    FROM #positive p
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmdet AND po.codigo_pessoa_origem=p.codigo
    UNION ALL
    SELECT po.pessoa_origem_id,@lotSmdet,@gSmdet,n.codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-NEG:',@modelShort,':',n.n,':',n.scenario)),2)),
           NULL,N'SEM_CPF',n.nome,UPPER(n.nome),n.nascimento,n.mae,CASE WHEN n.mae IS NULL THEN NULL ELSE UPPER(n.mae) END,
           DATEADD(SECOND,100+n.n,CONVERT(datetimeoffset(0),'2026-09-17T12:10:00+00:00'))
    FROM #negative n
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmdet AND po.codigo_pessoa_origem=n.codigo
    UNION ALL
    SELECT po.pessoa_origem_id,@lotSmdet,@gSmdet,c.pending_codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-CONFLICT:',@modelShort,':',c.n)),2)),
           NULL,N'SEM_CPF',c.nome,UPPER(c.nome),c.nascimento,c.mae,UPPER(c.mae),
           DATEADD(SECOND,200+c.n,CONVERT(datetimeoffset(0),'2026-09-17T12:10:00+00:00'))
    FROM #conflict c
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmdet AND po.codigo_pessoa_origem=c.pending_codigo
    UNION ALL
    SELECT po.pessoa_origem_id,@lotSmdet,@gSmdet,p.pending_codigo,1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('VAL-PROBE-PENDING:',@modelShort,':',p.probe)),2)),
           NULL,N'SEM_CPF',p.pending_nome,UPPER(p.pending_nome),p.nascimento,p.mae,UPPER(p.mae),
           DATEADD(SECOND,300+ROW_NUMBER() OVER(ORDER BY p.probe),CONVERT(datetimeoffset(0),'2026-09-17T12:10:00+00:00'))
    FROM #single_probe p
    JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmdet AND po.codigo_pessoa_origem=p.pending_codigo;

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;

SELECT
    CONVERT(VARCHAR(36),@activeModelId) AS modelo_id,
    @activeModelVersion AS modelo_versao,
    @modelShort AS modelo_fragmento,
    (SELECT COUNT(*) FROM #positive) AS positivos,
    (SELECT COUNT(*) FROM #negative) AS negativos,
    (SELECT COUNT(*) FROM #conflict) AS conflitos,
    (SELECT COUNT(*)*2 FROM #conflict) AS candidatos_conflito,
    (SELECT COUNT(*) FROM #single_probe) AS probes_candidato_unico,
    N'INJETADO_APOS_CALIBRACAO' AS estado;

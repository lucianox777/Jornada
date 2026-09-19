SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @people BIGINT = $(SCALE_PEOPLE);
DECLARE @paired BIGINT = $(SCALE_PAIRED);
DECLARE @pending BIGINT = $(SCALE_PENDING);
DECLARE @seed INT = $(SCALE_SEED);
DECLARE @collisionModulo INT = $(SCALE_COLLISION_MODULO);
DECLARE @birthShiftModulo INT = $(SCALE_BIRTH_SHIFT_MODULO);

IF @people < 1000 OR @people > 5000000
    THROW 51550, 'SCALE_PEOPLE deve estar entre 1.000 e 5.000.000.', 1;
IF @paired < 2 OR @paired > @people
    THROW 51551, 'SCALE_PAIRED deve estar entre 2 e SCALE_PEOPLE.', 1;
IF @pending < 1 OR @pending > 2000000
    THROW 51552, 'SCALE_PENDING deve estar entre 1 e 2.000.000.', 1;
IF @collisionModulo < 0 OR @birthShiftModulo < 0
    THROW 51555, 'Modulos de colisao/deslocamento devem ser >= 0 (0 desabilita).', 1;
IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-%')
    THROW 51553, 'Massa SCALE já existe. Execute local-db reset antes de gerar uma nova massa.', 1;

DECLARE @gSehab BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB'),
        @gSmads BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS'),
        @gSmdet BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMDET');
DECLARE @soSehab BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSehab AND codigo='SEHAB'),
        @soSmads BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmads AND codigo='ASSISTENCIA'),
        @soSmdet BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmdet AND codigo='TRABALHO');
DECLARE @bpSehab BIGINT=(SELECT base_pessoa_origem_id FROM ref.sistema_origem_base_pessoa WHERE sistema_origem_id=@soSehab AND padrao=1 AND ativo=1),
        @bpSmads BIGINT=(SELECT base_pessoa_origem_id FROM ref.sistema_origem_base_pessoa WHERE sistema_origem_id=@soSmads AND padrao=1 AND ativo=1),
        @bpSmdet BIGINT=(SELECT base_pessoa_origem_id FROM ref.sistema_origem_base_pessoa WHERE sistema_origem_id=@soSmdet AND padrao=1 AND ativo=1);
DECLARE @gpvSehab BIGINT=(SELECT TOP(1) gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND status='ATIVA' ORDER BY versao DESC),
        @gpvSmads BIGINT=(SELECT TOP(1) gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND status='ATIVA' ORDER BY versao DESC),
        @gpvSmdet BIGINT=(SELECT TOP(1) gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmdet AND status='ATIVA' ORDER BY versao DESC);

IF @gSehab IS NULL OR @gSmads IS NULL OR @gSmdet IS NULL OR @soSehab IS NULL OR @soSmads IS NULL OR @soSmdet IS NULL
   OR @bpSehab IS NULL OR @bpSmads IS NULL OR @bpSmdet IS NULL
    THROW 51554, 'Jornada_Seed_Dev.sql com Bases de Pessoa v4 deve ser aplicado antes da massa SCALE.', 1;

DECLARE @maxN BIGINT = (SELECT MAX(v) FROM (VALUES(@people),(@paired),(@pending)) x(v));
CREATE TABLE #n(n BIGINT NOT NULL PRIMARY KEY);
;WITH D10(n) AS (
    SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)
),
D100(n) AS (SELECT 1 FROM D10 a CROSS JOIN D10 b),
D10000(n) AS (SELECT 1 FROM D100 a CROSS JOIN D100 b),
D100M(n) AS (SELECT 1 FROM D10000 a CROSS JOIN D10000 b)
INSERT #n(n)
SELECT TOP (@maxN) ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM D100M;

CREATE TABLE #gold(
    n BIGINT NOT NULL PRIMARY KEY,
    pessoa_uuid UNIQUEIDENTIFIER NOT NULL,
    cpf CHAR(11) NOT NULL,
    nome NVARCHAR(500) NOT NULL,
    nascimento DATE NOT NULL,
    mae NVARCHAR(500) NOT NULL);

-- Cada par consecutivo mantém a mesma data, mas o multiplicador primo permuta os pares
-- por aproximadamente 100 anos. Isso preserva colisões reais para u sem concentrar 5.000
-- Pessoas em poucos anos e criar blocos sinteticamente patológicos por ano de nascimento.
;WITH generated AS (
    SELECT n,
           RIGHT(REPLICATE('0',9)+CONVERT(VARCHAR(9),700000000+n),9) AS cpf_base9
    FROM #n
    WHERE n<=@people
)
INSERT #gold(n,pessoa_uuid,cpf,nome,nascimento,mae)
SELECT g.n,
       CONVERT(UNIQUEIDENTIFIER,HASHBYTES('MD5',CONCAT('JORNADA-V355-',@seed,'-P-',g.n))),
       CONVERT(CHAR(11),CONCAT(g.cpf_base9,d1.digito,d2.digito)),
       CONCAT(N'Pessoa Teste ',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),g.n),10)),
       DATEADD(DAY,CONVERT(INT,(((((g.n-1)/2)*7919)+@seed)%36500)),CONVERT(DATE,'1930-01-01')),
       CONCAT(N'Mae Teste ',RIGHT(REPLICATE('0',8)+CONVERT(VARCHAR(10),(g.n+@seed)%100000000),8))
FROM generated g
CROSS APPLY (
    SELECT
        CONVERT(INT,SUBSTRING(g.cpf_base9,1,1))*10+
        CONVERT(INT,SUBSTRING(g.cpf_base9,2,1))*9+
        CONVERT(INT,SUBSTRING(g.cpf_base9,3,1))*8+
        CONVERT(INT,SUBSTRING(g.cpf_base9,4,1))*7+
        CONVERT(INT,SUBSTRING(g.cpf_base9,5,1))*6+
        CONVERT(INT,SUBSTRING(g.cpf_base9,6,1))*5+
        CONVERT(INT,SUBSTRING(g.cpf_base9,7,1))*4+
        CONVERT(INT,SUBSTRING(g.cpf_base9,8,1))*3+
        CONVERT(INT,SUBSTRING(g.cpf_base9,9,1))*2 AS soma
) s1
CROSS APPLY (
    SELECT CASE WHEN s1.soma%11<2 THEN 0 ELSE 11-(s1.soma%11) END AS digito
) d1
CROSS APPLY (
    SELECT
        CONVERT(INT,SUBSTRING(g.cpf_base9,1,1))*11+
        CONVERT(INT,SUBSTRING(g.cpf_base9,2,1))*10+
        CONVERT(INT,SUBSTRING(g.cpf_base9,3,1))*9+
        CONVERT(INT,SUBSTRING(g.cpf_base9,4,1))*8+
        CONVERT(INT,SUBSTRING(g.cpf_base9,5,1))*7+
        CONVERT(INT,SUBSTRING(g.cpf_base9,6,1))*6+
        CONVERT(INT,SUBSTRING(g.cpf_base9,7,1))*5+
        CONVERT(INT,SUBSTRING(g.cpf_base9,8,1))*4+
        CONVERT(INT,SUBSTRING(g.cpf_base9,9,1))*3+
        d1.digito*2 AS soma
) s2
CROSS APPLY (
    SELECT CASE WHEN s2.soma%11<2 THEN 0 ELSE 11-(s2.soma%11) END AS digito
) d2;

IF EXISTS(SELECT 1 FROM #gold WHERE identidade.fn_cpf_ancora_valido(cpf)=0)
    THROW 51556, 'Gerador SCALE produziu CPF sintético estruturalmente inválido.', 1;
IF EXISTS(
    SELECT 1
    FROM #gold g
    JOIN identidade.cpf_ancora a
      ON a.cpf=g.cpf COLLATE Latin1_General_100_BIN2
      OR a.pessoa_uuid=g.pessoa_uuid)
    THROW 51557, 'Faixa de CPF/UUID sintético SCALE colide com âncora existente.', 1;

INSERT identidade.pessoa(pessoa_uuid,status,criado_em)
SELECT pessoa_uuid,'ATIVO','2026-08-31T10:00:00+00:00' FROM #gold;

-- O corpus DEV deve obedecer à mesma fonte de verdade da produção:
-- CPF presente na Gold implica uma âncora permanente CPF -> UUID.
INSERT identidade.cpf_ancora(cpf,pessoa_uuid,criado_em)
SELECT cpf,pessoa_uuid,'2026-08-31T10:00:00+00:00' FROM #gold;

INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
SELECT pessoa_uuid,cpf,'PRESENTE',nome,nascimento,mae,2,'CORROBORADO','2026-08-31T10:00:00+00:00' FROM #gold;

DECLARE @entSehab UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000001',
        @lotSehab UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000002',
        @entSmads UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000003',
        @lotSmads UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000004',
        @entSmdet UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000005',
        @lotSmdet UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000006';

INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
VALUES
(@entSehab,@gSehab,@soSehab,@gpvSehab,NULL,NULL,NULL,'scale-sehab',REPLICATE('a',64),1,'PROCESSADA','2026-08-31T00:00:00+00:00','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00'),
(@entSmads,@gSmads,@soSmads,@gpvSmads,NULL,NULL,NULL,'scale-smads',REPLICATE('b',64),1,'PROCESSADA','2026-08-31T00:00:00+00:00','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00'),
(@entSmdet,@gSmdet,@soSmdet,@gpvSmdet,NULL,NULL,NULL,'scale-smdet',REPLICATE('c',64),1,'PROCESSADA','2026-08-31T00:00:00+00:00','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00');

INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em)
VALUES
(@lotSehab,@entSehab,1,1,CONVERT(INT,@people),0,'PROCESSADO','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00'),
(@lotSmads,@entSmads,1,1,CONVERT(INT,@paired),0,'PROCESSADO','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00'),
(@lotSmdet,@entSmdet,1,1,CONVERT(INT,@pending),0,'PROCESSADO','2026-08-31T10:00:00+00:00','2026-08-31T10:00:00+00:00');

INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id,ultima_recepcao_em,ultima_referencia_recebida)
SELECT @soSehab,CONCAT('SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n),10)),@bpSehab,'2026-08-31T10:00:00+00:00','2026-08-31T00:00:00+00:00' FROM #n WHERE n<=@people
UNION ALL
SELECT @soSmads,CONCAT('SCALE-SMADS-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n),10)),@bpSmads,'2026-08-31T10:00:00+00:00','2026-08-31T00:00:00+00:00' FROM #n WHERE n<=@paired
UNION ALL
SELECT @soSmdet,CONCAT('SCALE-PEND-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n),10)),@bpSmdet,'2026-08-31T10:00:00+00:00','2026-08-31T00:00:00+00:00' FROM #n WHERE n<=@pending;

-- SEHAB ancora deterministicamente toda a Gold sintética para que a projeção local de blocking
-- cubra o mesmo universo usado pela amostra u; os primeiros @paired recebem também uma fonte
-- SMADS independente e formam os pares inter-Gestor usados para estimar m.
INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,@lotSehab,@gSehab,po.codigo_pessoa_origem,1,
       LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('SEHAB:',g.n,':',@seed)),2)),
       g.cpf,NULL,g.nome,UPPER(g.nome),g.nascimento,g.mae,UPPER(g.mae),'2026-08-31T00:00:00+00:00'
FROM #gold g JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSehab AND po.codigo_pessoa_origem=CONCAT('SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),g.n),10))
WHERE g.n<=@people;

-- A segunda fonte determinística inclui erros cadastrais plausíveis de nascimento. Eles continuam
-- ligados pelo CPF ao mesmo UUID, portanto alimentam m sem circularidade e sem atribuir pesos à mão.
INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,@lotSmads,@gSmads,po.codigo_pessoa_origem,1,
       LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('SMADS:',g.n,':',@seed)),2)),
       g.cpf,NULL,
       CASE WHEN g.n%11=0 THEN CONCAT(g.nome,N' Filho') WHEN g.n%7=0 THEN REPLACE(g.nome,N' Teste ',N' T. ') ELSE g.nome END,
       UPPER(CASE WHEN g.n%11=0 THEN CONCAT(g.nome,N' Filho') WHEN g.n%7=0 THEN REPLACE(g.nome,N' Teste ',N' T. ') ELSE g.nome END),
       CASE
         WHEN @birthShiftModulo>0
          AND g.n%NULLIF(@birthShiftModulo,0)=0
          AND DAY(g.nascimento)<=12
          AND DAY(g.nascimento)<>MONTH(g.nascimento)
           THEN DATEFROMPARTS(YEAR(g.nascimento),DAY(g.nascimento),MONTH(g.nascimento))
         WHEN @birthShiftModulo>0
          AND g.n%(@birthShiftModulo+1)=0
           THEN DATEADD(YEAR,CASE WHEN YEAR(g.nascimento)<=1999 THEN 100 ELSE -100 END,g.nascimento)
         WHEN @birthShiftModulo>0
          AND g.n%(@birthShiftModulo+2)=0
          AND DAY(g.nascimento) BETWEEN 2 AND 27
           THEN DATEADD(DAY,CASE WHEN DAY(g.nascimento)%10 IN(0,9) THEN -1 ELSE 1 END,g.nascimento)
         ELSE g.nascimento
       END,
       CASE WHEN g.n%13=0 THEN REPLACE(g.mae,N'Mae ',N'Maria ') ELSE g.mae END,
       UPPER(CASE WHEN g.n%13=0 THEN REPLACE(g.mae,N'Mae ',N'Maria ') ELSE g.mae END),
       '2026-08-31T00:01:00+00:00'
FROM #gold g JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmads AND po.codigo_pessoa_origem=CONCAT('SCALE-SMADS-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),g.n),10))
WHERE g.n<=@paired;

INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,ativo,resolvido_em,motivo)
SELECT obs.pessoa_observacao_id,g.pessoa_uuid,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,'2026-08-31T10:01:00+00:00','SCALE_CORROBORACAO'
FROM #gold g
JOIN silver.pessoa_observacao obs ON obs.codigo_pessoa_origem=CONCAT('SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),g.n),10))
WHERE g.n<=@people
UNION ALL
SELECT obs.pessoa_observacao_id,g.pessoa_uuid,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,1,'2026-08-31T10:01:00+00:00','SCALE_CORROBORACAO'
FROM #gold g
JOIN silver.pessoa_observacao obs ON obs.codigo_pessoa_origem=CONCAT('SCALE-SMADS-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),g.n),10))
WHERE g.n<=@paired;

-- Observações sem CPF para exercitar o Runner. 10% mantêm data deliberadamente fora do universo;
-- as demais incluem nascimento exato e as mesmas classes semânticas de erro usadas no treino m.
INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
SELECT po.pessoa_origem_id,@lotSmdet,@gSmdet,po.codigo_pessoa_origem,1,
       LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('PEND:',n.n,':',@seed)),2)),
       NULL,'SEM_CPF',
       CASE WHEN @collisionModulo>0 AND n.n%@collisionModulo=0 THEN CONCAT(N'Pessoa Colisao ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(10),n.n%@collisionModulo),6))
            WHEN n.n%9=0 THEN CONCAT(g.nome,N' Junior') WHEN n.n%6=0 THEN REPLACE(g.nome,N' Teste ',N' ') ELSE g.nome END,
       UPPER(CASE WHEN @collisionModulo>0 AND n.n%@collisionModulo=0 THEN CONCAT(N'Pessoa Colisao ',RIGHT(REPLICATE('0',6)+CONVERT(VARCHAR(10),n.n%@collisionModulo),6))
            WHEN n.n%9=0 THEN CONCAT(g.nome,N' Junior') WHEN n.n%6=0 THEN REPLACE(g.nome,N' Teste ',N' ') ELSE g.nome END),
       CASE
         WHEN n.n%10=0 THEN DATEADD(DAY,CONVERT(INT,n.n%365),CONVERT(DATE,'1900-01-01'))
         WHEN @birthShiftModulo>0
          AND n.n%NULLIF(@birthShiftModulo,0)=0
          AND DAY(g.nascimento)<=12
          AND DAY(g.nascimento)<>MONTH(g.nascimento)
           THEN DATEFROMPARTS(YEAR(g.nascimento),DAY(g.nascimento),MONTH(g.nascimento))
         WHEN @birthShiftModulo>0
          AND n.n%(@birthShiftModulo+1)=0
           THEN DATEADD(YEAR,CASE WHEN YEAR(g.nascimento)<=1999 THEN 100 ELSE -100 END,g.nascimento)
         WHEN @birthShiftModulo>0
          AND n.n%(@birthShiftModulo+2)=0
          AND DAY(g.nascimento) BETWEEN 2 AND 27
           THEN DATEADD(DAY,CASE WHEN DAY(g.nascimento)%10 IN(0,9) THEN -1 ELSE 1 END,g.nascimento)
         ELSE g.nascimento
       END,
       CASE WHEN n.n%8=0 THEN REPLACE(g.mae,N'Mae ',N'M. ') ELSE g.mae END,
       UPPER(CASE WHEN n.n%8=0 THEN REPLACE(g.mae,N'Mae ',N'M. ') ELSE g.mae END),
       DATEADD(SECOND,CONVERT(INT,n.n%3600),CONVERT(datetimeoffset(0),'2026-08-31T01:00:00+00:00'))
FROM #n n
JOIN #gold g ON g.n=((n.n-1)%@people)+1
JOIN silver.pessoa_origem po ON po.sistema_origem_id=@soSmdet AND po.codigo_pessoa_origem=CONCAT('SCALE-PEND-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n.n),10))
WHERE n.n<=@pending;

SELECT
    @people AS gold_people,
    @paired AS paired_people,
    @pending AS pending_without_cpf,
    @seed AS scale_seed,
    @collisionModulo AS collision_modulo,
    @birthShiftModulo AS birth_shift_modulo,
    (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE codigo_pessoa_origem LIKE 'SCALE-%') AS synthetic_observations,
    (SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte vf JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vf.pessoa_observacao_id WHERE po.codigo_pessoa_origem LIKE 'SCALE-%' AND vf.metodo_resolucao='CPF_DETERMINISTICO' AND vf.ativo=1) AS deterministic_links;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
  Expansão não destrutiva do corpus adicional exclusivo do modo DEV.

  O corpus base SCALE-SEHAB/SMADS já deve existir. Este script acrescenta somente
  origens/observações SCALE-PEND sem CPF, usando a verdade Gold existente como
  referência de identidade. Não cria vínculo: a publicação deve acontecer pelo
  Jornada.Linkage.Runner real depois da calibração.
*/
DECLARE @targetPending BIGINT=$(SCALE_PENDING);
DECLARE @people BIGINT=(SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-SEHAB-%');
DECLARE @currentPending BIGINT=(SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-PEND-%');

IF @targetPending < 0 OR @targetPending > 2000000
    THROW 51570,'SCALE_PENDING DEV deve estar entre 0 e 2.000.000.',1;
IF @people < 1000
    THROW 51571,'Corpus SCALE-SEHAB base ausente; gere o bootstrap antes do adicional DEV.',1;
IF @currentPending > @targetPending
    THROW 51572,'Corpus SCALE-PEND atual é maior que o alvo; reset explícito é necessário para reduzir a massa.',1;
IF @currentPending = @targetPending
BEGIN
    SELECT @currentPending AS pending_before,@targetPending AS pending_after,N'UNCHANGED' AS status;
    RETURN;
END;

DECLARE @gSmdet BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMDET');
DECLARE @soSmdet BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmdet AND codigo='TRABALHO');
DECLARE @bpSmdet BIGINT=(SELECT base_pessoa_origem_id FROM ref.sistema_origem_base_pessoa WHERE sistema_origem_id=@soSmdet AND padrao=1 AND ativo=1);
DECLARE @lotSmdet UNIQUEIDENTIFIER='35500000-0000-4000-8000-000000000006';

IF @gSmdet IS NULL OR @soSmdet IS NULL OR @bpSmdet IS NULL
    THROW 51573,'Referências SMDET necessárias ao corpus DEV não estão disponíveis.',1;
IF NOT EXISTS(SELECT 1 FROM ingestao.lote WHERE lote_id=@lotSmdet)
    THROW 51574,'Lote sintético SMDET base ausente; reset/bootstrap explícito é necessário.',1;

CREATE TABLE #n(n BIGINT NOT NULL PRIMARY KEY);
;WITH D10(n) AS (
    SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) v(n)
),
D100(n) AS (SELECT 1 FROM D10 a CROSS JOIN D10 b),
D10000(n) AS (SELECT 1 FROM D100 a CROSS JOIN D100 b),
D100M(n) AS (SELECT 1 FROM D10000 a CROSS JOIN D10000 b)
INSERT #n(n)
SELECT TOP (@targetPending-@currentPending) @currentPending+ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
FROM D100M;

BEGIN TRANSACTION;
BEGIN TRY
    INSERT silver.pessoa_origem(
        sistema_origem_id,codigo_pessoa_origem,base_pessoa_origem_id,
        ultima_recepcao_em,ultima_referencia_recebida)
    SELECT @soSmdet,
           CONCAT('SCALE-PEND-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n),10)),
           @bpSmdet,'2026-08-31T10:00:00+00:00','2026-08-31T00:00:00+00:00'
    FROM #n;

    ;WITH truth AS (
        SELECT n.n,
               poPending.pessoa_origem_id,
               g.nome_completo,
               g.data_nascimento,
               g.nome_mae
        FROM #n n
        CROSS APPLY (SELECT ((n.n-1)%@people)+1 AS truth_n) x
        JOIN silver.pessoa_origem poSehab
          ON poSehab.codigo_pessoa_origem=
             CONCAT('SCALE-SEHAB-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),x.truth_n),10))
        JOIN silver.pessoa_observacao obsSehab
          ON obsSehab.pessoa_origem_id=poSehab.pessoa_origem_id
        JOIN identidade.v_vinculo_corrente vc
          ON vc.pessoa_observacao_id=obsSehab.pessoa_observacao_id
         AND vc.status=N'RESOLVIDO'
        JOIN gold.pessoa g ON g.pessoa_uuid=vc.pessoa_uuid
        JOIN silver.pessoa_origem poPending
          ON poPending.sistema_origem_id=@soSmdet
         AND poPending.codigo_pessoa_origem=
             CONCAT('SCALE-PEND-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n.n),10))
    )
    INSERT silver.pessoa_observacao(
        pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,
        conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,
        data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
    SELECT pessoa_origem_id,@lotSmdet,@gSmdet,
           CONCAT('SCALE-PEND-',RIGHT(REPLICATE('0',10)+CONVERT(VARCHAR(10),n),10)),
           1,
           LOWER(CONVERT(VARCHAR(64),HASHBYTES('SHA2_256',CONCAT('DEV-PEND:',n)),2)),
           NULL,N'SEM_CPF',
           nome_completo,UPPER(nome_completo),data_nascimento,nome_mae,UPPER(nome_mae),
           DATEADD(SECOND,CONVERT(INT,n%3600),CONVERT(datetimeoffset(0),'2026-08-31T01:00:00+00:00'))
    FROM truth;

    UPDATE ingestao.lote
       SET qtd_pessoas=CONVERT(INT,@targetPending),
           atualizado_em=SYSUTCDATETIME()
     WHERE lote_id=@lotSmdet;

    IF (SELECT COUNT_BIG(*) FROM silver.pessoa_origem WHERE codigo_pessoa_origem LIKE N'SCALE-PEND-%')<>@targetPending
        THROW 51575,'Expansão SCALE-PEND DEV não atingiu a cardinalidade esperada.',1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT @currentPending AS pending_before,@targetPending AS pending_after,N'EXPANDED' AS status;

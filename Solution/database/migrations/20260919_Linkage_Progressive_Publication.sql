SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Linkage -> identidade progressiva
 ---------------------------------
 Mantém o resultado estatístico bruto em identidade.linkage_resultado e acrescenta
 uma decisão operacional publicada separada. O initial_uuid nunca participa do score:
 ele só pode ser promovido como NOVA_IDENTIDADE depois de run completo, íntegro e sem
 candidato, para Pessoa que possua origem persistente.
*/

IF COL_LENGTH('identidade.linkage_resultado','resultado_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD resultado_publicacao NVARCHAR(30) NULL;
IF COL_LENGTH('identidade.linkage_resultado','pessoa_uuid_publicado') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD pessoa_uuid_publicado UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('identidade.linkage_resultado','status_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD status_publicacao NVARCHAR(30) NULL;
IF COL_LENGTH('identidade.linkage_resultado','motivo_publicacao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD motivo_publicacao NVARCHAR(160) NULL;
IF COL_LENGTH('identidade.linkage_resultado','pessoa_origem_id_publicado') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD pessoa_origem_id_publicado BIGINT NULL;
IF COL_LENGTH('identidade.linkage_resultado','progressiva_versao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD progressiva_versao BIGINT NULL;
IF COL_LENGTH('identidade.linkage_resultado','politica_publicacao_versao') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD politica_publicacao_versao NVARCHAR(120) NULL;
IF COL_LENGTH('identidade.linkage_resultado','universo_referencia') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD universo_referencia NVARCHAR(255) NULL;
IF COL_LENGTH('identidade.linkage_resultado','publicado_em') IS NULL
    ALTER TABLE identidade.linkage_resultado ADD publicado_em DATETIMEOFFSET(7) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_resultado')
      AND name='fk_linkage_resultado_uuid_publicado')
    ALTER TABLE identidade.linkage_resultado
      ADD CONSTRAINT fk_linkage_resultado_uuid_publicado
      FOREIGN KEY(pessoa_uuid_publicado) REFERENCES identidade.pessoa(pessoa_uuid);
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_resultado')
      AND name='fk_linkage_resultado_origem_publicada')
    ALTER TABLE identidade.linkage_resultado
      ADD CONSTRAINT fk_linkage_resultado_origem_publicada
      FOREIGN KEY(pessoa_origem_id_publicado) REFERENCES silver.pessoa_origem(pessoa_origem_id);
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_resultado')
      AND name='ck_linkage_resultado_publicacao')
    ALTER TABLE identidade.linkage_resultado DROP CONSTRAINT ck_linkage_resultado_publicacao;
GO
ALTER TABLE identidade.linkage_resultado WITH CHECK
ADD CONSTRAINT ck_linkage_resultado_publicacao CHECK(
    (resultado_publicacao IS NULL
      AND pessoa_uuid_publicado IS NULL
      AND status_publicacao IS NULL
      AND motivo_publicacao IS NULL
      AND pessoa_origem_id_publicado IS NULL
      AND progressiva_versao IS NULL
      AND politica_publicacao_versao IS NULL
      AND universo_referencia IS NULL
      AND publicado_em IS NULL)
    OR
    (resultado_publicacao IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE','INDEFINIDA')
      AND status_publicacao IN('RESOLVIDO','NAO_RESOLVIDO','CONFLITO')
      AND motivo_publicacao IS NOT NULL
      AND politica_publicacao_versao IS NOT NULL
      AND universo_referencia IS NOT NULL
      AND publicado_em IS NOT NULL
      AND (
        (resultado_publicacao IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE')
          AND status_publicacao='RESOLVIDO' AND pessoa_uuid_publicado IS NOT NULL)
        OR
        (resultado_publicacao='INDEFINIDA'
          AND status_publicacao IN('NAO_RESOLVIDO','CONFLITO') AND pessoa_uuid_publicado IS NULL)
      ))
);
GO

CREATE OR ALTER PROCEDURE identidade.sp_publicar_resolucao_progressiva_linkage
 @linkage_run_id UNIQUEIDENTIFIER,
 @pessoa_observacao_id BIGINT,
 @versao_resultado BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF @@TRANCOUNT=0
    THROW 51830,'Publicação progressiva do Linkage exige transação explícita.',1;
 IF @linkage_run_id IS NULL OR @pessoa_observacao_id IS NULL OR @pessoa_observacao_id<=0
    THROW 51831,'Run/observação inválidos para publicação progressiva.',1;

 DECLARE
   @run_status NVARCHAR(30),
   @run_avaliados BIGINT,
   @run_elegiveis BIGINT,
   @pessoa_origem_id BIGINT,
   @resultado NVARCHAR(30),
   @target UNIQUEIDENTIFIER,
   @status_publicacao NVARCHAR(30),
   @motivo_publicacao NVARCHAR(160),
   @politica NVARCHAR(120),
   @universo NVARCHAR(255),
   @modelo_versao INT,
   @raw_status NVARCHAR(30),
   @raw_motivo NVARCHAR(120);

 SELECT @run_status=lr.status,@run_avaliados=lr.avaliados,@run_elegiveis=lr.registros_elegiveis
 FROM identidade.linkage_run lr WITH(UPDLOCK,HOLDLOCK)
 WHERE lr.linkage_run_id=@linkage_run_id;

 IF @run_status IS NULL OR @run_status<>'EXECUTANDO'
    THROW 51832,'Somente run EXECUTANDO pode publicar resolução progressiva.',1;
 IF @run_avaliados<>@run_elegiveis
    THROW 51833,'Run incompleto não pode publicar resolução progressiva.',1;

 SELECT
   @pessoa_origem_id=po.pessoa_origem_id,
   @resultado=r.resultado_publicacao,
   @target=r.pessoa_uuid_publicado,
   @status_publicacao=r.status_publicacao,
   @motivo_publicacao=r.motivo_publicacao,
   @politica=r.politica_publicacao_versao,
   @universo=r.universo_referencia,
   @modelo_versao=r.modelo_versao,
   @raw_status=r.status,
   @raw_motivo=r.motivo
 FROM identidade.linkage_resultado r WITH(UPDLOCK,HOLDLOCK)
 JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
   ON po.pessoa_observacao_id=r.pessoa_observacao_id
 WHERE r.linkage_run_id=@linkage_run_id
   AND r.pessoa_observacao_id=@pessoa_observacao_id;

 IF @resultado IS NULL OR @status_publicacao IS NULL OR @motivo_publicacao IS NULL
    OR @politica IS NULL OR @universo IS NULL
    THROW 51834,'Decisão operacional do Linkage não foi materializada.',1;
 IF @pessoa_origem_id IS NULL
    THROW 51835,'Observação sem origem persistente não possui ledger progressivo.',1;

 DECLARE @ensure TABLE(
   initial_uuid UNIQUEIDENTIFIER NOT NULL,
   legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,
   estado VARCHAR(20) NOT NULL,
   versao BIGINT NOT NULL);
 INSERT @ensure(initial_uuid,legacy_pessoa_uuid,estado,versao)
 EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;

 DECLARE
   @initial UNIQUEIDENTIFIER,
   @current UNIQUEIDENTIFIER,
   @estado VARCHAR(20),
   @versao BIGINT,
   @nova BIGINT,
   @canonical UNIQUEIDENTIFIER,
   @event_target UNIQUEIDENTIFIER,
   @novo_estado VARCHAR(20),
   @externo UNIQUEIDENTIFIER,
   @now DATETIMEOFFSET(7),
   @modelo_ref NVARCHAR(120),
   @evidencia NVARCHAR(255);

 SELECT
   @initial=initial_uuid,
   @current=canonical_uuid,
   @estado=estado,
   @versao=versao
 FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK)
 WHERE pessoa_origem_id=@pessoa_origem_id;

 IF @resultado='NOVA_IDENTIDADE'
 BEGIN
   IF @raw_status<>'NAO_RESOLVIDO' OR @raw_motivo NOT LIKE N'SEM_CANDIDATO_%'
      THROW 51836,'NOVA_IDENTIDADE exige resultado bruto sem candidato.',1;
   IF @target IS NULL OR @target<>@initial
      THROW 51837,'NOVA_IDENTIDADE deve promover exatamente o initial_uuid da origem.',1;
 END;

 IF @resultado='ASSOCIACAO_EXISTENTE'
 BEGIN
   IF @target IS NULL
      THROW 51838,'ASSOCIACAO_EXISTENTE exige UUID destino.',1;
   IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WITH(HOLDLOCK) WHERE pessoa_uuid=@target)
      THROW 51839,'Destino publicado não existe em identidade.pessoa.',1;
   IF NOT EXISTS(
      SELECT 1 FROM identidade.cpf_ancora a WITH(HOLDLOCK) WHERE a.pessoa_uuid=@target
      UNION ALL
      SELECT 1 FROM identidade.pessoa_origem_progressiva p WITH(HOLDLOCK)
       WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@target
      UNION ALL
      SELECT 1 FROM identidade.vinculo_fonte v WITH(HOLDLOCK)
       WHERE v.ativo=1 AND v.status='RESOLVIDO' AND v.pessoa_uuid=@target
         AND v.metodo_resolucao IN('CPF_DETERMINISTICO','UUID_JORNADA_RETROALIMENTACAO','CORRECAO_GOVERNADA'))
   )
      THROW 51840,'ASSOCIACAO_EXISTENTE exige referência canônica previamente estabelecida.',1;
 END;

 IF @resultado='INDEFINIDA' AND @target IS NOT NULL
    THROW 51841,'INDEFINIDA não pode publicar UUID.',1;

 IF @estado='REFERENCIA'
 BEGIN
   IF @current IS NULL OR @resultado<>'ASSOCIACAO_EXISTENTE' OR @target<>@current
      THROW 51842,'Referência progressiva existente só pode ser confirmada; alteração exige correção governada.',1;
   SET @versao_resultado=@versao;
   RETURN;
 END;

 SET @nova=@versao+1;
 SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
 SET @modelo_ref=LEFT(CONCAT(N'LINKAGE_MODEL_V',CONVERT(NVARCHAR(20),@modelo_versao)),120);
 SET @evidencia=LEFT(CONCAT(N'LINKAGE_RUN:',CONVERT(NVARCHAR(36),@linkage_run_id),N';OBS:',CONVERT(NVARCHAR(30),@pessoa_observacao_id)),255);

 IF @resultado='INDEFINIDA'
 BEGIN
   SET @canonical=NULL;
   SET @event_target=NULL;
   SET @novo_estado='INDEFINIDA';
   SET @externo=NULL;
 END
 ELSE IF @resultado='NOVA_IDENTIDADE'
 BEGIN
   SET @canonical=@initial;
   SET @event_target=NULL;
   SET @novo_estado='REFERENCIA';
   SET @externo=NULL;
 END
 ELSE
 BEGIN
   SET @canonical=@target;
   SET @event_target=@target;
   SET @novo_estado='REFERENCIA';
   SET @externo=CASE WHEN @target=@initial THEN NULL ELSE @target END;
 END;

 INSERT identidade.pessoa_origem_progressiva_evento(
   evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
   evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em)
 VALUES(
   NEWID(),@pessoa_origem_id,@nova,'RESOLUCAO',@novo_estado,@canonical,@versao,@resultado,@event_target,
   @evidencia,@politica,@modelo_ref,
   CASE WHEN @resultado='NOVA_IDENTIDADE' THEN @universo ELSE NULL END,
   1,@now);

 UPDATE identidade.pessoa_origem_progressiva
 SET canonical_uuid=@canonical,
     estado=@novo_estado,
     versao=@nova,
     ultima_resolucao_em=@now,
     ultimo_destino_externo_uuid=@externo,
     atualizado_em=@now
 WHERE pessoa_origem_id=@pessoa_origem_id
   AND versao=@versao;

 IF @@ROWCOUNT<>1
    THROW 51843,'Estado progressivo mudou durante a publicação.',1;

 SET @versao_resultado=@nova;
END;
GO

CREATE OR ALTER VIEW identidade.v_vinculo_corrente AS
WITH probabilistico_publicado AS (
    SELECT
      r.pessoa_observacao_id,
      COALESCE(r.pessoa_uuid_publicado,r.pessoa_uuid_resolvido) pessoa_uuid_resolvido,
      r.score_melhor,
      COALESCE(r.status_publicacao,r.status) status,
      COALESCE(r.motivo_publicacao,r.motivo) motivo,
      r.modelo_id,
      r.linkage_run_id,
      COALESCE(r.publicado_em,r.calculado_em) calculado_em,
      ROW_NUMBER() OVER(
        PARTITION BY r.pessoa_observacao_id
        ORDER BY lr.publicado_em DESC,lr.iniciado_em DESC,lr.linkage_run_id DESC) rn
    FROM identidade.linkage_resultado r
    JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
    WHERE lr.status='PUBLICADO'
), prob_corrente AS (
    SELECT * FROM probabilistico_publicado WHERE rn=1
), base_ativa AS (
    SELECT vf.* FROM identidade.vinculo_fonte vf WHERE vf.ativo=1
)
SELECT
 b.vinculo_id,b.pessoa_observacao_id,b.pessoa_uuid,b.metodo_resolucao,b.score,b.status,b.motivo,
 b.modelo_id,b.linkage_run_id,b.resolvido_em
FROM base_ativa b
WHERE b.metodo_resolucao IN(
        'CPF_DETERMINISTICO','UUID_JORNADA_RETROALIMENTACAO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO')
   OR NOT EXISTS(
        SELECT 1 FROM prob_corrente p WHERE p.pessoa_observacao_id=b.pessoa_observacao_id)
UNION ALL
SELECT
 CAST(NULL AS BIGINT),p.pessoa_observacao_id,p.pessoa_uuid_resolvido,'LINKAGE_PROBABILISTICO',
 p.score_melhor,p.status,p.motivo,p.modelo_id,p.linkage_run_id,p.calculado_em
FROM prob_corrente p
WHERE NOT EXISTS(
    SELECT 1 FROM base_ativa b
    WHERE b.pessoa_observacao_id=p.pessoa_observacao_id
      AND b.metodo_resolucao IN(
        'CPF_DETERMINISTICO','UUID_JORNADA_RETROALIMENTACAO','CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'));
GO

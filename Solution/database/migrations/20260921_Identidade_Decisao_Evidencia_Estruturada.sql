SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Evidência estruturada de decisões governadas de identidade
  ----------------------------------------------------------
  Complementa o ledger canônico sem reinterpretar atos históricos.

  Novas gravações distinguem:
  - DOCUMENTO_VERIFICADO: houve documento verificado e documento_tipo_codigo é obrigatório;
  - CONFIRMACAO_SEM_DOCUMENTO: confirmação institucional sem documento apresentado;
  - DECISAO_PREVIA_APLICADA: aplicação de caso já decidido; não constitui nova evidência.

  LEGADO_NAO_CLASSIFICADO existe apenas para backfill dos eventos anteriores a esta
  migração e é rejeitado pela procedure para novas gravações.
*/

IF OBJECT_ID(N'auditoria.decisao_identidade_evento',N'U') IS NULL
    THROW 51950,'Evidência estruturada exige auditoria.decisao_identidade_evento instalada.',1;
GO

IF COL_LENGTH(N'auditoria.decisao_identidade_evento',N'evidencia_tipo') IS NULL
    ALTER TABLE auditoria.decisao_identidade_evento
      ADD evidencia_tipo NVARCHAR(40) NULL;
GO

IF COL_LENGTH(N'auditoria.decisao_identidade_evento',N'documento_tipo_codigo') IS NULL
    ALTER TABLE auditoria.decisao_identidade_evento
      ADD documento_tipo_codigo NVARCHAR(80) NULL;
GO

UPDATE auditoria.decisao_identidade_evento
SET evidencia_tipo=N'LEGADO_NAO_CLASSIFICADO'
WHERE evidencia_tipo IS NULL;
GO

ALTER TABLE auditoria.decisao_identidade_evento
  ALTER COLUMN evidencia_tipo NVARCHAR(40) NOT NULL;
GO

IF EXISTS(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento')
      AND name=N'ck_decisao_identidade_evidencia_tipo')
    ALTER TABLE auditoria.decisao_identidade_evento
      DROP CONSTRAINT ck_decisao_identidade_evidencia_tipo;
GO

ALTER TABLE auditoria.decisao_identidade_evento WITH CHECK
  ADD CONSTRAINT ck_decisao_identidade_evidencia_tipo CHECK(
    evidencia_tipo IN(
      N'DOCUMENTO_VERIFICADO',
      N'CONFIRMACAO_SEM_DOCUMENTO',
      N'DECISAO_PREVIA_APLICADA',
      N'LEGADO_NAO_CLASSIFICADO'));
GO

IF EXISTS(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento')
      AND name=N'ck_decisao_identidade_documento')
    ALTER TABLE auditoria.decisao_identidade_evento
      DROP CONSTRAINT ck_decisao_identidade_documento;
GO

ALTER TABLE auditoria.decisao_identidade_evento WITH CHECK
  ADD CONSTRAINT ck_decisao_identidade_documento CHECK(
    (evidencia_tipo=N'DOCUMENTO_VERIFICADO'
       AND documento_tipo_codigo IS NOT NULL
       AND LEN(documento_tipo_codigo) BETWEEN 1 AND 80
       AND documento_tipo_codigo NOT LIKE N'%[^A-Z0-9_]%' COLLATE Latin1_General_100_BIN2)
    OR
    (evidencia_tipo<>N'DOCUMENTO_VERIFICADO' AND documento_tipo_codigo IS NULL));
GO

IF EXISTS(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento')
      AND name=N'ck_decisao_identidade_evidencia_evento')
    ALTER TABLE auditoria.decisao_identidade_evento
      DROP CONSTRAINT ck_decisao_identidade_evidencia_evento;
GO

ALTER TABLE auditoria.decisao_identidade_evento WITH CHECK
  ADD CONSTRAINT ck_decisao_identidade_evidencia_evento CHECK(
    (evento_tipo=N'CASO_CONFLITO_APLICADO'
       AND evidencia_tipo IN(N'DECISAO_PREVIA_APLICADA',N'LEGADO_NAO_CLASSIFICADO'))
    OR
    (evento_tipo<>N'CASO_CONFLITO_APLICADO'
       AND evidencia_tipo<>N'DECISAO_PREVIA_APLICADA'));
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_decisao_identidade
 @credencial_id UNIQUEIDENTIFIER,
 @gestor_codigo NVARCHAR(30),
 @evento_tipo NVARCHAR(40),
 @evidencia_tipo NVARCHAR(40),
 @documento_tipo_codigo NVARCHAR(80)=NULL,
 @correcao_id UNIQUEIDENTIFIER=NULL,
 @caso_id UNIQUEIDENTIFIER=NULL,
 @divergencia_id BIGINT=NULL,
 @correlation_id UNIQUEIDENTIFIER=NULL,
 @operacao_id UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF @@TRANCOUNT=0 OR XACT_STATE()<>1
   THROW 51942,'Decisão de identidade deve ser registrada na mesma transação da mutação governada.',1;

 SET @evidencia_tipo=UPPER(LTRIM(RTRIM(COALESCE(@evidencia_tipo,N''))));
 SET @documento_tipo_codigo=NULLIF(UPPER(LTRIM(RTRIM(@documento_tipo_codigo))),N'');

 IF @evidencia_tipo=N'LEGADO_NAO_CLASSIFICADO'
   THROW 51951,'LEGADO_NAO_CLASSIFICADO é reservado ao backfill histórico e não pode ser gravado em novos atos.',1;

 IF @evento_tipo=N'CASO_CONFLITO_APLICADO'
 BEGIN
   IF @evidencia_tipo<>N'DECISAO_PREVIA_APLICADA' OR @documento_tipo_codigo IS NOT NULL
     THROW 51952,'Aplicação de caso governado deve usar DECISAO_PREVIA_APLICADA sem documento novo.',1;
 END
 ELSE
 BEGIN
   IF @evidencia_tipo NOT IN(N'DOCUMENTO_VERIFICADO',N'CONFIRMACAO_SEM_DOCUMENTO')
     THROW 51953,'Ato humano deve declarar DOCUMENTO_VERIFICADO ou CONFIRMACAO_SEM_DOCUMENTO.',1;

   IF @evidencia_tipo=N'DOCUMENTO_VERIFICADO'
      AND (@documento_tipo_codigo IS NULL
           OR LEN(@documento_tipo_codigo)>80
           OR @documento_tipo_codigo LIKE N'%[^A-Z0-9_]%' COLLATE Latin1_General_100_BIN2)
     THROW 51954,'DOCUMENTO_VERIFICADO exige documento_tipo_codigo estrutural válido.',1;

   IF @evidencia_tipo=N'CONFIRMACAO_SEM_DOCUMENTO'
      AND @documento_tipo_codigo IS NOT NULL
     THROW 51955,'CONFIRMACAO_SEM_DOCUMENTO não admite documento_tipo_codigo.',1;
 END

 DECLARE @gestor_id BIGINT,
         @credencial_tipo NVARCHAR(20),
         @codigo_publico NVARCHAR(200);

 SELECT @gestor_id=c.gestor_id,
        @credencial_tipo=c.tipo_credencial,
        @codigo_publico=c.codigo_publico
 FROM controle.credencial_api c WITH(HOLDLOCK)
 JOIN ref.gestor g WITH(HOLDLOCK)
   ON g.gestor_id=c.gestor_id
 WHERE c.credencial_id=@credencial_id
   AND c.ativo=1
   AND g.ativo=1
   AND g.codigo=@gestor_codigo;

 IF @gestor_id IS NULL
   THROW 51943,'Credencial autenticada não pertence ao Gestor informado ou está inativa.',1;
 IF @credencial_tipo<>N'GESTOR'
   THROW 51944,'Atos governados de identidade exigem credencial institucional do Gestor.',1;

 DECLARE @ato_referencia NVARCHAR(300)=NULL,
         @justificativa NVARCHAR(2000)=NULL,
         @resultado_codigo NVARCHAR(30)=NULL;

 IF @evento_tipo=N'CORRECAO_CPF_APLICADA'
 BEGIN
   SELECT @ato_referencia=c.ato_referencia,
          @justificativa=c.justificativa,
          @resultado_codigo=c.status
   FROM identidade.correcao_identidade c WITH(HOLDLOCK)
   WHERE c.correcao_id=@correcao_id
     AND c.gestor_responsavel_id=@gestor_id
     AND c.status=N'APLICADA';
 END
 ELSE IF @evento_tipo=N'CASO_CONFLITO_ABERTO'
 BEGIN
   SELECT @ato_referencia=c.ato_referencia,
          @justificativa=c.justificativa,
          @resultado_codigo=c.status
   FROM identidade.caso_conflito_identidade c WITH(HOLDLOCK)
   WHERE c.caso_id=@caso_id
     AND c.gestor_responsavel_id=@gestor_id
     AND c.status=N'ABERTO';
 END
 ELSE IF @evento_tipo=N'CASO_CONFLITO_APLICADO'
 BEGIN
   SELECT @ato_referencia=c.ato_referencia,
          @justificativa=c.justificativa,
          @resultado_codigo=c.status
   FROM identidade.caso_conflito_identidade c WITH(HOLDLOCK)
   WHERE c.caso_id=@caso_id
     AND c.gestor_responsavel_id=@gestor_id
     AND c.status=N'APLICADO';
 END
 ELSE IF @evento_tipo=N'DIVERGENCIA_DESFECHO'
 BEGIN
   SELECT @justificativa=
            LEFT(CONCAT(
              N'Desfecho=',COALESCE(NULLIF(d.desfecho,N''),N'NAO_INFORMADO'),
              CASE WHEN NULLIF(d.observacao_desfecho,N'') IS NULL
                   THEN N''
                   ELSE CONCAT(N'; Observacao=',d.observacao_desfecho)
              END),2000),
          @resultado_codigo=d.status
   FROM qualidade.divergencia_gestor d WITH(HOLDLOCK)
   WHERE d.divergencia_id=@divergencia_id
     AND d.gestor_id=@gestor_id
     AND d.status IN(N'RESOLVIDA',N'DESCARTADA');
 END
 ELSE
   THROW 51945,'Tipo de evento de decisão de identidade não suportado.',1;

 IF @resultado_codigo IS NULL OR NULLIF(LTRIM(RTRIM(@justificativa)),N'') IS NULL
   THROW 51946,'Mutação governada não está em estado persistido compatível com o evento solicitado.',1;

 SET @operacao_id=NEWID();

 INSERT auditoria.decisao_identidade_evento(
   operacao_id,evento_tipo,resultado_codigo,
   credencial_id,gestor_id,credencial_tipo,codigo_publico,
   correcao_id,caso_id,divergencia_id,
   evidencia_tipo,documento_tipo_codigo,
   ato_referencia,justificativa,correlation_id)
 VALUES(
   @operacao_id,@evento_tipo,@resultado_codigo,
   @credencial_id,@gestor_id,@credencial_tipo,@codigo_publico,
   @correcao_id,@caso_id,@divergencia_id,
   @evidencia_tipo,@documento_tipo_codigo,
   @ato_referencia,@justificativa,@correlation_id);
END;
GO

CREATE OR ALTER VIEW auditoria.v_decisao_identidade_evento AS
SELECT e.decisao_identidade_evento_id,e.operacao_id,e.evento_tipo,e.resultado_codigo,
       e.credencial_id,e.gestor_id,e.credencial_tipo,e.codigo_publico,
       e.correcao_id,e.caso_id,e.divergencia_id,
       e.evidencia_tipo,e.documento_tipo_codigo,
       CAST(CASE WHEN e.evidencia_tipo=N'CONFIRMACAO_SEM_DOCUMENTO' THEN 1 ELSE 0 END AS BIT)
         AS elegivel_referencia_estrato_dificil,
       e.ato_referencia,e.justificativa,e.correlation_id,e.ocorrido_em
FROM auditoria.decisao_identidade_evento e;
GO

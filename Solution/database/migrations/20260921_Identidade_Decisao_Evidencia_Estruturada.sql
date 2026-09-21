SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Evidência estruturada para decisões governadas de identidade
  ------------------------------------------------------------
  Mantém ato_referencia/justificativa como contexto, mas passa a exigir uma
  classificação estruturada da evidência usada na decisão. Eventos históricos
  anteriores a esta migração são marcados explicitamente como LEGADO_NAO_CLASSIFICADO.
*/

IF OBJECT_ID(N'auditoria.decisao_identidade_evento',N'U') IS NULL
    THROW 51950,'Ledger de decisão de identidade não instalado.',1;
GO

IF COL_LENGTH(N'auditoria.decisao_identidade_evento',N'evidencia_tipo') IS NULL
    ALTER TABLE auditoria.decisao_identidade_evento
        ADD evidencia_tipo NVARCHAR(60) NULL;
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
    ALTER COLUMN evidencia_tipo NVARCHAR(60) NOT NULL;
GO

IF OBJECT_ID(N'auditoria.CK_decisao_identidade_evidencia_tipo',N'C') IS NOT NULL
    ALTER TABLE auditoria.decisao_identidade_evento DROP CONSTRAINT CK_decisao_identidade_evidencia_tipo;
GO
ALTER TABLE auditoria.decisao_identidade_evento WITH CHECK
ADD CONSTRAINT CK_decisao_identidade_evidencia_tipo CHECK(
    evidencia_tipo IN(
      N'DOCUMENTO_VERIFICADO',
      N'CONFIRMACAO_INSTITUCIONAL_SEM_DOCUMENTO',
      N'ATO_GOVERNADO_SEM_NOVA_EVIDENCIA',
      N'LEGADO_NAO_CLASSIFICADO'));
GO

IF OBJECT_ID(N'auditoria.CK_decisao_identidade_documento',N'C') IS NOT NULL
    ALTER TABLE auditoria.decisao_identidade_evento DROP CONSTRAINT CK_decisao_identidade_documento;
GO
ALTER TABLE auditoria.decisao_identidade_evento WITH CHECK
ADD CONSTRAINT CK_decisao_identidade_documento CHECK(
    (evidencia_tipo=N'DOCUMENTO_VERIFICADO'
      AND NULLIF(LTRIM(RTRIM(documento_tipo_codigo)),N'') IS NOT NULL)
    OR
    (evidencia_tipo<>N'DOCUMENTO_VERIFICADO' AND documento_tipo_codigo IS NULL));
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_decisao_identidade
 @credencial_id UNIQUEIDENTIFIER,
 @gestor_codigo NVARCHAR(30),
 @evento_tipo NVARCHAR(40),
 @evidencia_tipo NVARCHAR(60),
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

 SET @evidencia_tipo=UPPER(LTRIM(RTRIM(@evidencia_tipo)));
 SET @documento_tipo_codigo=NULLIF(UPPER(LTRIM(RTRIM(@documento_tipo_codigo))),N'');

 IF @evidencia_tipo NOT IN(
      N'DOCUMENTO_VERIFICADO',
      N'CONFIRMACAO_INSTITUCIONAL_SEM_DOCUMENTO',
      N'ATO_GOVERNADO_SEM_NOVA_EVIDENCIA')
   THROW 51951,'Tipo estruturado de evidência de decisão não suportado.',1;

 IF (@evidencia_tipo=N'DOCUMENTO_VERIFICADO' AND @documento_tipo_codigo IS NULL)
    OR (@evidencia_tipo<>N'DOCUMENTO_VERIFICADO' AND @documento_tipo_codigo IS NOT NULL)
   THROW 51952,'DOCUMENTO_VERIFICADO exige documento_tipo_codigo; demais tipos proíbem documento_tipo_codigo.',1;

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
       e.ato_referencia,e.justificativa,e.correlation_id,e.ocorrido_em
FROM auditoria.decisao_identidade_evento e;
GO

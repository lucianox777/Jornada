SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Ledger canônico de decisões de identidade
  -----------------------------------------
  controle.api_evento continua sendo telemetria/auditoria HTTP. Este ledger é a
  fonte de verdade append-only para atos governados que alteram ou encerram uma
  decisão de identidade.

  A autoria canônica é a credencial institucional autenticada. correlation_id é
  apenas contexto de requisição e nunca identifica o autor. O CPF declarativo de
  agente, quando houver no pipeline HTTP, não participa deste ledger.
*/

IF OBJECT_ID(N'controle.credencial_api',N'U') IS NULL
   OR OBJECT_ID(N'identidade.correcao_identidade',N'U') IS NULL
   OR OBJECT_ID(N'identidade.caso_conflito_identidade',N'U') IS NULL
   OR OBJECT_ID(N'qualidade.divergencia_gestor',N'U') IS NULL
    THROW 51940,'Ledger de decisão de identidade exige credenciais e estruturas governadas instaladas.',1;
GO

IF OBJECT_ID(N'auditoria.decisao_identidade_evento',N'U') IS NULL
CREATE TABLE auditoria.decisao_identidade_evento(
 decisao_identidade_evento_id BIGINT IDENTITY PRIMARY KEY,
 operacao_id UNIQUEIDENTIFIER NOT NULL,
 evento_tipo NVARCHAR(40) NOT NULL,
 resultado_codigo NVARCHAR(30) NOT NULL,
 credencial_id UNIQUEIDENTIFIER NOT NULL REFERENCES controle.credencial_api(credencial_id),
 gestor_id BIGINT NOT NULL REFERENCES ref.gestor(gestor_id),
 credencial_tipo NVARCHAR(20) NOT NULL,
 codigo_publico NVARCHAR(200) NOT NULL,
 correcao_id UNIQUEIDENTIFIER NULL REFERENCES identidade.correcao_identidade(correcao_id),
 caso_id UNIQUEIDENTIFIER NULL REFERENCES identidade.caso_conflito_identidade(caso_id),
 divergencia_id BIGINT NULL REFERENCES qualidade.divergencia_gestor(divergencia_id),
 ato_referencia NVARCHAR(300) NULL,
 justificativa NVARCHAR(2000) NOT NULL,
 correlation_id UNIQUEIDENTIFIER NULL,
 ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
 CONSTRAINT uq_decisao_identidade_operacao UNIQUE(operacao_id),
 CONSTRAINT ck_decisao_identidade_credencial_tipo CHECK(credencial_tipo=N'GESTOR'),
 CONSTRAINT ck_decisao_identidade_evento_tipo CHECK(evento_tipo IN(
   N'CORRECAO_CPF_APLICADA',
   N'CASO_CONFLITO_ABERTO',
   N'CASO_CONFLITO_APLICADO',
   N'DIVERGENCIA_DESFECHO')),
 CONSTRAINT ck_decisao_identidade_referencia CHECK(
   (evento_tipo=N'CORRECAO_CPF_APLICADA' AND correcao_id IS NOT NULL AND caso_id IS NULL AND divergencia_id IS NULL)
   OR
   (evento_tipo IN(N'CASO_CONFLITO_ABERTO',N'CASO_CONFLITO_APLICADO') AND correcao_id IS NULL AND caso_id IS NOT NULL AND divergencia_id IS NULL)
   OR
   (evento_tipo=N'DIVERGENCIA_DESFECHO' AND correcao_id IS NULL AND caso_id IS NULL AND divergencia_id IS NOT NULL)));
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento') AND name=N'UX_decisao_identidade_correcao')
 CREATE UNIQUE INDEX UX_decisao_identidade_correcao
 ON auditoria.decisao_identidade_evento(correcao_id)
 WHERE correcao_id IS NOT NULL;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento') AND name=N'UX_decisao_identidade_caso_evento')
 CREATE UNIQUE INDEX UX_decisao_identidade_caso_evento
 ON auditoria.decisao_identidade_evento(caso_id,evento_tipo)
 WHERE caso_id IS NOT NULL;
GO

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'auditoria.decisao_identidade_evento') AND name=N'UX_decisao_identidade_divergencia')
 CREATE UNIQUE INDEX UX_decisao_identidade_divergencia
 ON auditoria.decisao_identidade_evento(divergencia_id)
 WHERE divergencia_id IS NOT NULL;
GO

CREATE OR ALTER TRIGGER auditoria.tr_decisao_identidade_evento_append_only
ON auditoria.decisao_identidade_evento
INSTEAD OF UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 THROW 51941,'Ledger canônico de decisões de identidade é append-only.',1;
END;
GO

CREATE OR ALTER PROCEDURE auditoria.sp_registrar_decisao_identidade
 @credencial_id UNIQUEIDENTIFIER,
 @gestor_codigo NVARCHAR(30),
 @evento_tipo NVARCHAR(40),
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
   ato_referencia,justificativa,correlation_id)
 VALUES(
   @operacao_id,@evento_tipo,@resultado_codigo,
   @credencial_id,@gestor_id,@credencial_tipo,@codigo_publico,
   @correcao_id,@caso_id,@divergencia_id,
   @ato_referencia,@justificativa,@correlation_id);
END;
GO

CREATE OR ALTER VIEW auditoria.v_decisao_identidade_evento AS
SELECT e.decisao_identidade_evento_id,e.operacao_id,e.evento_tipo,e.resultado_codigo,
       e.credencial_id,e.gestor_id,e.credencial_tipo,e.codigo_publico,
       e.correcao_id,e.caso_id,e.divergencia_id,
       e.ato_referencia,e.justificativa,e.correlation_id,e.ocorrido_em
FROM auditoria.decisao_identidade_evento e;
GO

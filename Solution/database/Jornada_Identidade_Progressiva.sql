-- Identidade progressiva V1: armazenamento aditivo, sem ativação probabilística.
-- Aplicar após Jornada_Fase1.sql, em banco compatível. Requer sqlcmd/GO.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva','U') IS NULL
BEGIN
 CREATE TABLE identidade.pessoa_origem_progressiva(
  pessoa_origem_id BIGINT NOT NULL PRIMARY KEY REFERENCES silver.pessoa_origem(pessoa_origem_id),
  initial_uuid UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES identidade.pessoa(pessoa_uuid),
  canonical_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
  legacy_pessoa_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
  estado VARCHAR(20) NOT NULL CONSTRAINT DF_progressiva_estado DEFAULT('PROVISORIA'),
  versao BIGINT NOT NULL CONSTRAINT DF_progressiva_versao DEFAULT(0),
  ultima_resolucao_em DATETIMEOFFSET(7) NULL,
  ultimo_destino_externo_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
  criado_em DATETIMEOFFSET(7) NOT NULL,
  atualizado_em DATETIMEOFFSET(7) NOT NULL,
  CONSTRAINT ck_progressiva_estado CHECK(
   (versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND ultima_resolucao_em IS NULL AND ultimo_destino_externo_uuid IS NULL)
   OR (versao>0 AND ((estado='REFERENCIA' AND canonical_uuid IS NOT NULL) OR (estado='INDEFINIDA' AND canonical_uuid IS NULL)) AND ultima_resolucao_em IS NOT NULL)),
  CONSTRAINT ck_progressiva_externo CHECK(ultimo_destino_externo_uuid IS NULL OR ultimo_destino_externo_uuid<>initial_uuid AND (canonical_uuid IS NULL OR canonical_uuid=ultimo_destino_externo_uuid)),
  CONSTRAINT ck_progressiva_datas CHECK(atualizado_em>=criado_em AND (ultima_resolucao_em IS NULL OR ultima_resolucao_em>=criado_em))
 );
END;
GO
IF OBJECT_ID('identidade.pessoa_origem_progressiva_evento','U') IS NULL
BEGIN
 CREATE TABLE identidade.pessoa_origem_progressiva_evento(
  evento_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  pessoa_origem_id BIGINT NOT NULL REFERENCES identidade.pessoa_origem_progressiva(pessoa_origem_id),
  versao BIGINT NOT NULL,
  tipo VARCHAR(20) NOT NULL,
  estado VARCHAR(20) NOT NULL,
  canonical_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
  expected_version BIGINT NULL,
  resultado VARCHAR(30) NULL,
  target_uuid UNIQUEIDENTIFIER NULL REFERENCES identidade.pessoa(pessoa_uuid),
  evidencia_referencia NVARCHAR(255) NULL,
  politica_versao NVARCHAR(120) NULL,
  modelo_versao NVARCHAR(120) NULL,
  universo_referencia NVARCHAR(255) NULL,
  completo BIT NULL,
  ocorrido_em DATETIMEOFFSET(7) NOT NULL,
  CONSTRAINT uq_progressiva_evento_versao UNIQUE(pessoa_origem_id,versao),
  CONSTRAINT ck_progressiva_evento_tipo CHECK(tipo IN('CRIACAO','RESOLUCAO')),
  CONSTRAINT ck_progressiva_evento_estado CHECK(estado IN('PROVISORIA','REFERENCIA','INDEFINIDA')),
  CONSTRAINT ck_progressiva_evento_criacao CHECK(
   (tipo='CRIACAO' AND versao=0 AND estado='PROVISORIA' AND canonical_uuid IS NULL AND expected_version IS NULL AND resultado IS NULL AND target_uuid IS NULL AND evidencia_referencia IS NULL AND politica_versao IS NULL AND modelo_versao IS NULL AND universo_referencia IS NULL AND completo IS NULL)
   OR (tipo='RESOLUCAO' AND versao>0 AND expected_version=versao-1 AND completo=1 AND evidencia_referencia IS NOT NULL AND politica_versao IS NOT NULL AND resultado IN('NOVA_IDENTIDADE','ASSOCIACAO_EXISTENTE','INDEFINIDA') AND
    ((resultado='INDEFINIDA' AND estado='INDEFINIDA' AND canonical_uuid IS NULL AND target_uuid IS NULL) OR
     (resultado='NOVA_IDENTIDADE' AND estado='REFERENCIA' AND canonical_uuid IS NOT NULL AND target_uuid IS NULL AND universo_referencia IS NOT NULL) OR
     (resultado='ASSOCIACAO_EXISTENTE' AND estado='REFERENCIA' AND canonical_uuid=target_uuid AND canonical_uuid IS NOT NULL AND target_uuid IS NOT NULL))))
 );
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_progressiva_origem_guard ON identidade.pessoa_origem_progressiva AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.pessoa_origem_id=d.pessoa_origem_id WHERE i.pessoa_origem_id IS NULL)
   THROW 51110,'Referência inicial de origem não pode ser excluída ou transferida.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.initial_uuid<>d.initial_uuid OR i.criado_em<>d.criado_em OR
        ISNULL(i.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.legacy_pessoa_uuid,'00000000-0000-0000-0000-000000000000'))
   THROW 51111,'UUID inicial, origem e referência legada são imutáveis.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.versao<d.versao OR i.versao>d.versao+1 OR
    (i.versao=d.versao AND (i.estado<>d.estado OR
      ISNULL(i.canonical_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.canonical_uuid,'00000000-0000-0000-0000-000000000000') OR
      ISNULL(i.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000')<>ISNULL(d.ultimo_destino_externo_uuid,'00000000-0000-0000-0000-000000000000') OR
      ISNULL(i.ultima_resolucao_em,'0001-01-01')<>ISNULL(d.ultima_resolucao_em,'0001-01-01'))))
   THROW 51112,'Alteração de referência exige avanço de uma versão.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.pessoa_origem_id=i.pessoa_origem_id
  WHERE i.versao<>d.versao AND NOT EXISTS(
   SELECT 1 FROM identidade.pessoa_origem_progressiva_evento e
   WHERE e.pessoa_origem_id=i.pessoa_origem_id AND e.versao=i.versao AND e.tipo='RESOLUCAO' AND e.estado=i.estado
     AND (e.canonical_uuid=i.canonical_uuid OR (e.canonical_uuid IS NULL AND i.canonical_uuid IS NULL))
     AND e.ocorrido_em=i.ultima_resolucao_em))
   THROW 51117,'Avanço exige recibo de resolução correspondente.',1;
END;
GO
CREATE OR ALTER TRIGGER identidade.tr_progressiva_evento_append_only ON identidade.pessoa_origem_progressiva_evento INSTEAD OF UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51113,'Histórico progressivo é append-only.',1;
END;
GO
-- A procedure exige a transação do chamador. A chave de origem é o lock de serialização.
-- Não altera vínculos atuais, Gold, identity_map ou estado de CPF.
CREATE OR ALTER PROCEDURE identidade.sp_assegurar_origem_progressiva
 @pessoa_origem_id BIGINT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 51114,'Criação progressiva exige transação explícita.',1;
 IF @pessoa_origem_id IS NULL OR @pessoa_origem_id<=0 THROW 51115,'Origem inválida.',1;
 DECLARE @uuid UNIQUEIDENTIFIER,@legacy UNIQUEIDENTIFIER,@now DATETIMEOFFSET(7);
 IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_origem_id=@pessoa_origem_id)
   THROW 51116,'Origem inexistente.',1;
 SELECT @uuid=initial_uuid FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_origem_id=@pessoa_origem_id;
 IF @uuid IS NULL
 BEGIN
   SET @uuid=NEWID();
   SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
   -- Captura somente a atribuição corrente da última versão, quando o vínculo está RESOLVIDO.
   -- RESOLVIDO aqui pertence a vinculo_fonte; é proveniência legada, não estado progressivo.
   SELECT TOP(1) @legacy=v.pessoa_uuid
   FROM silver.pessoa_observacao o
   LEFT JOIN identidade.vinculo_fonte v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.ativo=1 AND v.status='RESOLVIDO'
   WHERE o.pessoa_origem_id=@pessoa_origem_id
   ORDER BY o.versao_interna DESC,o.pessoa_observacao_id DESC;
   INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO');
   INSERT identidade.pessoa_origem_progressiva(pessoa_origem_id,initial_uuid,legacy_pessoa_uuid,criado_em,atualizado_em)
   VALUES(@pessoa_origem_id,@uuid,@legacy,@now,@now);
   INSERT identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,ocorrido_em)
   VALUES(NEWID(),@pessoa_origem_id,0,'CRIACAO','PROVISORIA',@now);
 END;
 SELECT initial_uuid,legacy_pessoa_uuid,estado,versao
 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@pessoa_origem_id;
END;
GO
-- Publicação determinística de REFERENCIA, com recibo append-only. Não usa score nem modelo.
CREATE OR ALTER PROCEDURE identidade.sp_publicar_referencia_progressiva_deterministica
 @pessoa_origem_id BIGINT,
 @canonical_uuid UNIQUEIDENTIFIER,
 @evidencia_referencia NVARCHAR(255),
 @politica_versao NVARCHAR(120),
 @versao_resultado BIGINT OUTPUT
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 IF @@TRANCOUNT=0 THROW 51118,'Publicação de referência exige transação explícita.',1;
 IF @pessoa_origem_id IS NULL OR @pessoa_origem_id<=0 THROW 51115,'Origem inválida.',1;
 IF @canonical_uuid IS NULL OR @canonical_uuid='00000000-0000-0000-0000-000000000000' THROW 51119,'UUID canônico inválido.',1;
 IF @evidencia_referencia IS NULL OR LTRIM(RTRIM(@evidencia_referencia))='' OR @politica_versao IS NULL OR LTRIM(RTRIM(@politica_versao))=''
   THROW 51120,'Publicação de referência exige evidência e política.',1;
 DECLARE @ensure TABLE(initial_uuid UNIQUEIDENTIFIER NOT NULL,legacy_pessoa_uuid UNIQUEIDENTIFIER NULL,estado VARCHAR(20) NOT NULL,versao BIGINT NOT NULL);
 INSERT INTO @ensure(initial_uuid,legacy_pessoa_uuid,estado,versao)
 EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@pessoa_origem_id;
 DECLARE @initial UNIQUEIDENTIFIER,@current UNIQUEIDENTIFIER,@estado VARCHAR(20),@versao BIGINT,@nova BIGINT,@now DATETIMEOFFSET(7),@externo UNIQUEIDENTIFIER;
 SELECT @initial=initial_uuid,@current=canonical_uuid,@estado=estado,@versao=versao
   FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK)
  WHERE pessoa_origem_id=@pessoa_origem_id;
 IF @estado='REFERENCIA' AND @current=@canonical_uuid
 BEGIN SET @versao_resultado=@versao; RETURN; END;
 IF @estado='REFERENCIA' AND (@current<>@canonical_uuid OR @current IS NULL)
   THROW 51121,'Referência progressiva já aponta outro UUID; correção governada necessária.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.pessoa WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_uuid=@canonical_uuid)
   THROW 51122,'UUID canônico inexistente.',1;
 SET @nova=@versao+1;
 SET @now=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
 SET @externo=CASE WHEN @canonical_uuid=@initial THEN NULL ELSE @canonical_uuid END;
 INSERT identidade.pessoa_origem_progressiva_evento(
  evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,
  evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em)
 VALUES(
  NEWID(),@pessoa_origem_id,@nova,'RESOLUCAO','REFERENCIA',@canonical_uuid,@versao,'ASSOCIACAO_EXISTENTE',@canonical_uuid,
  @evidencia_referencia,@politica_versao,NULL,NULL,1,@now);
 UPDATE identidade.pessoa_origem_progressiva
    SET canonical_uuid=@canonical_uuid,estado='REFERENCIA',versao=@nova,
        ultima_resolucao_em=@now,ultimo_destino_externo_uuid=@externo,atualizado_em=@now
  WHERE pessoa_origem_id=@pessoa_origem_id;
 SET @versao_resultado=@nova;
END;
GO
-- Não executar backfill automaticamente na instalação. O executor aplica lotes limitados,
-- com uma transação por origem, preservando o checkpoint e a capacidade de retomada.

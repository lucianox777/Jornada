SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Trilha canônica de transições do modelo de Linkage.
  Não substitui a validação estatística da issue #31 e não atribui autoria humana.
  A identidade registrada é técnica (aplicação/login/host); autoria corporativa
  individual depende da integração externa PRODAM (#378).
*/

IF OBJECT_ID(N'identidade.modelo_linkage',N'U') IS NULL
    THROW 51960,'Ledger de promoção exige identidade.modelo_linkage instalado.',1;
GO

IF OBJECT_ID(N'auditoria.modelo_linkage_estado_evento',N'U') IS NULL
CREATE TABLE auditoria.modelo_linkage_estado_evento(
    modelo_linkage_estado_evento_id BIGINT IDENTITY PRIMARY KEY,
    modelo_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.modelo_linkage(modelo_id),
    modelo_versao INT NOT NULL,
    status_anterior NVARCHAR(20) NULL,
    status_novo NVARCHAR(20) NOT NULL,
    operacao_codigo NVARCHAR(50) NOT NULL,
    motivo NVARCHAR(500) NULL,
    executor_aplicacao NVARCHAR(128) NOT NULL,
    executor_login NVARCHAR(256) NOT NULL,
    executor_host NVARCHAR(128) NULL,
    source_revision NVARCHAR(80) NULL,
    ocorrido_em DATETIMEOFFSET(7) NOT NULL DEFAULT(SYSDATETIMEOFFSET()),
    CONSTRAINT ck_modelo_linkage_estado_evento_status CHECK(
      status_novo IN(N'GERANDO',N'RASCUNHO',N'VALIDADO',N'ATIVO',N'INATIVO',N'FALHOU')
      AND (status_anterior IS NULL OR status_anterior IN(N'GERANDO',N'RASCUNHO',N'VALIDADO',N'ATIVO',N'INATIVO',N'FALHOU')))
);
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'auditoria.modelo_linkage_estado_evento')
      AND name=N'IX_modelo_linkage_estado_evento_modelo')
CREATE INDEX IX_modelo_linkage_estado_evento_modelo
ON auditoria.modelo_linkage_estado_evento(modelo_id,modelo_linkage_estado_evento_id DESC)
INCLUDE(modelo_versao,status_anterior,status_novo,operacao_codigo,ocorrido_em);
GO

-- Primeira instalação sobre banco existente ganha um snapshot explícito do estado observado.
IF NOT EXISTS(SELECT 1 FROM auditoria.modelo_linkage_estado_evento)
BEGIN
    INSERT auditoria.modelo_linkage_estado_evento(
        modelo_id,modelo_versao,status_anterior,status_novo,operacao_codigo,motivo,
        executor_aplicacao,executor_login,executor_host,source_revision,ocorrido_em)
    SELECT m.modelo_id,m.versao,NULL,m.status,N'MIGRATION_SNAPSHOT',
           N'Estado observado na instalação inicial do ledger de promoção.',
           LEFT(COALESCE(APP_NAME(),N'SQL'),128),
           LEFT(COALESCE(ORIGINAL_LOGIN(),SUSER_SNAME(),N'UNKNOWN'),256),
           LEFT(HOST_NAME(),128),
           NULL,
           SYSDATETIMEOFFSET()
    FROM identidade.modelo_linkage m;
END;
GO

CREATE OR ALTER TRIGGER auditoria.tr_modelo_linkage_estado_evento_append_only
ON auditoria.modelo_linkage_estado_evento
INSTEAD OF UPDATE,DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51961,'Ledger de transições do modelo de Linkage é append-only.',1;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_estado_evento
ON identidade.modelo_linkage
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT auditoria.modelo_linkage_estado_evento(
        modelo_id,modelo_versao,status_anterior,status_novo,operacao_codigo,motivo,
        executor_aplicacao,executor_login,executor_host,source_revision)
    SELECT
        i.modelo_id,
        i.versao,
        d.status,
        i.status,
        CASE
          WHEN d.modelo_id IS NULL AND i.status=N'GERANDO' THEN N'GENERATE_DRAFT_START'
          WHEN d.modelo_id IS NULL THEN N'INSERT_EXISTING_STATE'
          WHEN d.status=N'GERANDO' AND i.status=N'RASCUNHO' THEN N'GENERATE_DRAFT_COMPLETE'
          WHEN d.status=N'GERANDO' AND i.status=N'FALHOU' THEN N'GENERATE_DRAFT_FAILED'
          WHEN d.status=N'RASCUNHO' AND i.status=N'VALIDADO' THEN N'VALIDATE'
          WHEN d.status=N'VALIDADO' AND i.status=N'ATIVO' THEN N'ACTIVATE'
          WHEN d.status=N'ATIVO' AND i.status=N'INATIVO' THEN N'ACTIVATE_SUPERSEDED'
          ELSE N'STATUS_TRANSITION'
        END,
        CASE
          WHEN i.status=N'FALHOU' THEN i.falha_resumo
          ELSE CONVERT(NVARCHAR(500),SESSION_CONTEXT(N'Jornada.ModelTransitionReason'))
        END,
        LEFT(COALESCE(APP_NAME(),N'SQL'),128),
        LEFT(COALESCE(ORIGINAL_LOGIN(),SUSER_SNAME(),N'UNKNOWN'),256),
        LEFT(HOST_NAME(),128),
        LEFT(CONVERT(NVARCHAR(80),SESSION_CONTEXT(N'Jornada.SourceRevision')),80)
    FROM inserted i
    LEFT JOIN deleted d ON d.modelo_id=i.modelo_id
    WHERE d.modelo_id IS NULL OR d.status<>i.status;
END;
GO

CREATE OR ALTER VIEW auditoria.v_modelo_linkage_estado_evento AS
SELECT
    e.modelo_linkage_estado_evento_id,
    e.modelo_id,
    e.modelo_versao,
    e.status_anterior,
    e.status_novo,
    e.operacao_codigo,
    e.motivo,
    e.executor_aplicacao,
    e.executor_login,
    e.executor_host,
    e.source_revision,
    e.ocorrido_em
FROM auditoria.modelo_linkage_estado_evento e;
GO

/* DT-05 — ledger de transições semânticas da publicação probabilística.
   A migração é aditiva; linkage_resultado continua sendo a prova por run.
   A procedure é invocada pelo Runner na transação SERIALIZABLE de publicação,
   sob Jornada.Linkage.Runner.Publish (sp_getapplock).
*/
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'identidade.linkage_transicao_semantica', N'U') IS NULL
BEGIN
    CREATE TABLE identidade.linkage_transicao_semantica (
        transicao_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        pessoa_observacao_id BIGINT NOT NULL REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
        linkage_resultado_id BIGINT NOT NULL REFERENCES identidade.linkage_resultado(linkage_resultado_id),
        linkage_run_id UNIQUEIDENTIFIER NOT NULL REFERENCES identidade.linkage_run(linkage_run_id),
        assinatura_versao SMALLINT NOT NULL,
        assinatura_sha256 VARBINARY(32) NOT NULL,
        assinatura_anterior_sha256 VARBINARY(32) NULL,
        transicao_tipo NVARCHAR(30) NOT NULL,
        registrada_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_linkage_transicao_registrada DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT UQ_linkage_transicao_resultado UNIQUE(linkage_resultado_id),
        CONSTRAINT CK_linkage_transicao_tipo CHECK(transicao_tipo IN(N'INICIAL',N'ALTERACAO_SEMANTICA'))
    );
END;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'identidade.linkage_transicao_semantica') AND name=N'IX_linkage_transicao_observacao')
    CREATE INDEX IX_linkage_transicao_observacao ON identidade.linkage_transicao_semantica(pessoa_observacao_id,transicao_id DESC)
    INCLUDE(assinatura_sha256,linkage_run_id);
GO
CREATE OR ALTER PROCEDURE identidade.sp_registrar_transicoes_linkage_run
    @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @@TRANCOUNT=0 THROW 51940,N'DT-05 exige transação de publicação ativa.',1;
    IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run WHERE linkage_run_id=@linkage_run_id AND status=N'EXECUTANDO')
        THROW 51941,N'DT-05 aceita somente run em publicação.',1;

    -- Assinatura V1: decisão operacional publicada, evidência discreta relevante,
    -- ranking/conflito, modelo exato e política. Exclui timestamps, run_id e ruído
    -- de arredondamento do score (mantido no resultado bruto por run).
    ;WITH assinaturas AS (
        SELECT r.linkage_resultado_id,r.linkage_run_id,r.pessoa_observacao_id,
               HASHBYTES('SHA2_256',CONVERT(VARBINARY(MAX),CONCAT(
                  N'DT05_V1|',
                  CONVERT(NVARCHAR(36),r.modelo_id),N'|',r.modelo_versao,N'|',
                  COALESCE(r.status,N'<NULL>'),N'|',COALESCE(r.motivo,N'<NULL>'),N'|',
                  COALESCE(r.resultado_publicacao,N'<NULL>'),N'|',
                  COALESCE(CONVERT(NVARCHAR(36),r.pessoa_uuid_publicado),N'<NULL>'),N'|',
                  COALESCE(r.status_publicacao,N'<NULL>'),N'|',
                  COALESCE(r.motivo_publicacao,N'<NULL>'),N'|',
                  COALESCE(CONVERT(NVARCHAR(36),r.melhor_candidato_uuid),N'<NULL>'),N'|',
                  COALESCE(CONVERT(NVARCHAR(36),r.segundo_candidato_uuid),N'<NULL>'),N'|',
                  COALESCE(r.politica_publicacao_versao,N'<NULL>'),N'|',
                  COALESCE(CONVERT(NVARCHAR(36),r.pessoa_origem_id_publicado),N'<NULL>')
               ))) AS assinatura
        FROM identidade.linkage_resultado r
        WHERE r.linkage_run_id=@linkage_run_id
    )
    INSERT identidade.linkage_transicao_semantica(
        pessoa_observacao_id,linkage_resultado_id,linkage_run_id,assinatura_versao,
        assinatura_sha256,assinatura_anterior_sha256,transicao_tipo)
    SELECT s.pessoa_observacao_id,s.linkage_resultado_id,s.linkage_run_id,1,
           s.assinatura,anterior.assinatura_sha256,
           CASE WHEN anterior.transicao_id IS NULL THEN N'INICIAL' ELSE N'ALTERACAO_SEMANTICA' END
    FROM assinaturas s
    OUTER APPLY (
        SELECT TOP(1) t.transicao_id,t.assinatura_sha256
        FROM identidade.linkage_transicao_semantica t WITH(UPDLOCK,HOLDLOCK)
        WHERE t.pessoa_observacao_id=s.pessoa_observacao_id
        ORDER BY t.transicao_id DESC
    ) anterior
    WHERE (anterior.transicao_id IS NULL OR anterior.assinatura_sha256<>s.assinatura)
      AND NOT EXISTS(
          SELECT 1 FROM identidade.linkage_transicao_semantica t
          WHERE t.linkage_resultado_id=s.linkage_resultado_id
      );
END;
GO

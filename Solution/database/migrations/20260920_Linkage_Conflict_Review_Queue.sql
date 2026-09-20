SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Encaminha somente conflitos probabilísticos efetivamente PUBLICADOS para a fila
  institucional já existente. Runs de validação/conclusão sem publicação não entram.
  A divergência recebe linkage_run_id próprio para ligar o caso ao resultado bruto,
  modelo, scores e candidatos já persistidos em identidade.linkage_resultado. O campo
  correlation_id preserva a correlação original do run.
*/

IF OBJECT_ID(N'qualidade.divergencia_gestor',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
    THROW 51830,'Fila de divergências de Linkage exige schema de qualidade e Linkage instalados.',1;
GO

IF COL_LENGTH(N'qualidade.divergencia_gestor',N'linkage_run_id') IS NULL
    ALTER TABLE qualidade.divergencia_gestor ADD linkage_run_id UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'qualidade.divergencia_gestor')
      AND name=N'fk_divergencia_gestor_linkage_run')
    ALTER TABLE qualidade.divergencia_gestor
      ADD CONSTRAINT fk_divergencia_gestor_linkage_run
      FOREIGN KEY(linkage_run_id) REFERENCES identidade.linkage_run(linkage_run_id);
GO
IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'qualidade.divergencia_gestor')
      AND name=N'IX_divergencia_gestor_linkage_run')
    CREATE INDEX IX_divergencia_gestor_linkage_run
      ON qualidade.divergencia_gestor(linkage_run_id,pessoa_observacao_id)
      WHERE linkage_run_id IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE qualidade.sp_sincronizar_divergencias_linkage
 @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS(
        SELECT 1
        FROM identidade.linkage_run WITH(HOLDLOCK)
        WHERE linkage_run_id=@linkage_run_id
          AND status=N'PUBLICADO')
        THROW 51831,'Somente linkage_run PUBLICADO pode alimentar a fila de divergências.',1;

    INSERT qualidade.divergencia_gestor(
        gestor_id,tipo,motivo,pessoa_observacao_id,codigo_pessoa_origem,
        status,correlation_id,linkage_run_id)
    SELECT po.gestor_id,
           N'DIVERGENCIA_IDENTIDADE',
           LEFT(CONCAT(N'LINKAGE_PROBABILISTICO:',COALESCE(r.motivo_publicacao,r.motivo,N'AMBIGUO')),120),
           r.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           N'ABERTA',
           lr.correlation_id,
           @linkage_run_id
    FROM identidade.linkage_resultado r WITH(HOLDLOCK)
    JOIN identidade.linkage_run lr WITH(HOLDLOCK)
      ON lr.linkage_run_id=r.linkage_run_id
    JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
      ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id=@linkage_run_id
      AND r.status_publicacao=N'CONFLITO'
      AND NOT EXISTS(
          SELECT 1
          FROM qualidade.divergencia_gestor d WITH(UPDLOCK,HOLDLOCK)
          WHERE d.pessoa_observacao_id=r.pessoa_observacao_id
            AND d.tipo=N'DIVERGENCIA_IDENTIDADE'
            AND (d.status=N'ABERTA' OR d.linkage_run_id=@linkage_run_id));
END;
GO

CREATE OR ALTER VIEW qualidade.v_divergencia_linkage_contexto
AS
SELECT d.divergencia_id,
       d.gestor_id,
       d.status AS divergencia_status,
       d.aberta_em,
       d.encerrada_em,
       r.linkage_run_id,
       r.modelo_id,
       r.modelo_versao,
       r.linkage_resultado_id,
       r.pessoa_observacao_id,
       r.melhor_candidato_uuid,
       r.score_melhor,
       r.segundo_candidato_uuid,
       r.score_segundo,
       r.margem,
       r.status AS linkage_status_bruto,
       r.motivo AS linkage_motivo_bruto,
       r.status_publicacao,
       r.motivo_publicacao
FROM qualidade.divergencia_gestor d
JOIN identidade.linkage_resultado r
  ON r.linkage_run_id=d.linkage_run_id
 AND r.pessoa_observacao_id=d.pessoa_observacao_id
WHERE d.tipo=N'DIVERGENCIA_IDENTIDADE'
  AND r.status_publicacao=N'CONFLITO';
GO

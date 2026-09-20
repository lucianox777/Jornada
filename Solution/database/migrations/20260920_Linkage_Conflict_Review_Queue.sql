SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Encaminha somente conflitos probabilísticos efetivamente PUBLICADOS para a fila
  institucional já existente. Runs de validação/conclusão sem publicação não entram.
  O linkage_run_id é preservado em correlation_id para ligar a divergência ao resultado
  bruto, modelo, scores e candidatos já persistidos em identidade.linkage_resultado.
*/

IF OBJECT_ID(N'qualidade.divergencia_gestor',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
    THROW 51830,'Fila de divergências de Linkage exige schema de qualidade e Linkage instalados.',1;
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
        status,correlation_id)
    SELECT po.gestor_id,
           N'DIVERGENCIA_IDENTIDADE',
           LEFT(CONCAT(N'LINKAGE_PROBABILISTICO:',COALESCE(r.motivo_publicacao,r.motivo,N'AMBIGUO')),120),
           r.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           N'ABERTA',
           @linkage_run_id
    FROM identidade.linkage_resultado r WITH(HOLDLOCK)
    JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
      ON po.pessoa_observacao_id=r.pessoa_observacao_id
    WHERE r.linkage_run_id=@linkage_run_id
      AND r.status_publicacao=N'CONFLITO'
      AND NOT EXISTS(
          SELECT 1
          FROM qualidade.divergencia_gestor d WITH(UPDLOCK,HOLDLOCK)
          WHERE d.pessoa_observacao_id=r.pessoa_observacao_id
            AND d.tipo=N'DIVERGENCIA_IDENTIDADE'
            AND (d.status=N'ABERTA' OR d.correlation_id=@linkage_run_id));
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
  ON r.linkage_run_id=d.correlation_id
 AND r.pessoa_observacao_id=d.pessoa_observacao_id
WHERE d.tipo=N'DIVERGENCIA_IDENTIDADE'
  AND r.status_publicacao=N'CONFLITO';
GO

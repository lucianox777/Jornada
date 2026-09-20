SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Revisão governada de conflitos probabilísticos
  ----------------------------------------------
  Mantém uma única fila institucional em qualidade.divergencia_gestor e liga cada
  divergência probabilística à evidência imutável de identidade.linkage_resultado.
  Scores, margem, modelo e candidatos não são duplicados nem expostos pela fila pública.
*/

IF OBJECT_ID(N'qualidade.divergencia_gestor',N'U') IS NULL
   OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
    THROW 51830,'Fila governada de Linkage exige divergencia_gestor e linkage_resultado instalados.',1;
GO

IF COL_LENGTH(N'qualidade.divergencia_gestor',N'linkage_resultado_id') IS NULL
    ALTER TABLE qualidade.divergencia_gestor ADD linkage_resultado_id BIGINT NULL;
GO

IF NOT EXISTS(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'qualidade.divergencia_gestor')
      AND name=N'fk_divergencia_gestor_linkage_resultado')
    ALTER TABLE qualidade.divergencia_gestor WITH CHECK
      ADD CONSTRAINT fk_divergencia_gestor_linkage_resultado
      FOREIGN KEY(linkage_resultado_id)
      REFERENCES identidade.linkage_resultado(linkage_resultado_id);
GO

IF NOT EXISTS(
    SELECT 1
    FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'qualidade.divergencia_gestor')
      AND name=N'UX_divergencia_gestor_linkage_aberta')
    CREATE UNIQUE INDEX UX_divergencia_gestor_linkage_aberta
      ON qualidade.divergencia_gestor(pessoa_observacao_id)
      WHERE status=N'ABERTA'
        AND linkage_resultado_id IS NOT NULL
        AND pessoa_observacao_id IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE qualidade.sp_registrar_conflitos_linkage_publicados
 @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;

 IF NOT EXISTS(
   SELECT 1 FROM identidade.linkage_run WITH(HOLDLOCK)
   WHERE linkage_run_id=@linkage_run_id AND status=N'EXECUTANDO')
   THROW 51831,'Fila de revisão só pode ser atualizada durante a publicação de run EXECUTANDO.',1;

 DECLARE @conflitos_linkage TABLE(
   pessoa_observacao_id BIGINT NOT NULL PRIMARY KEY,
   linkage_resultado_id BIGINT NOT NULL,
   gestor_id BIGINT NOT NULL,
   codigo_pessoa_origem NVARCHAR(255) NULL,
   motivo NVARCHAR(120) NOT NULL,
   correlation_id UNIQUEIDENTIFIER NULL
 );

 INSERT @conflitos_linkage(
   pessoa_observacao_id,linkage_resultado_id,gestor_id,
   codigo_pessoa_origem,motivo,correlation_id)
 SELECT r.pessoa_observacao_id,r.linkage_resultado_id,po.gestor_id,
        po.codigo_pessoa_origem,
        LEFT(COALESCE(NULLIF(r.motivo_publicacao,N''),NULLIF(r.motivo,N''),N'LINKAGE_AMBIGUO'),120),
        lr.correlation_id
 FROM identidade.linkage_resultado r WITH(HOLDLOCK)
 JOIN silver.pessoa_observacao po WITH(HOLDLOCK)
   ON po.pessoa_observacao_id=r.pessoa_observacao_id
 JOIN identidade.linkage_run lr WITH(HOLDLOCK)
   ON lr.linkage_run_id=r.linkage_run_id
 WHERE r.linkage_run_id=@linkage_run_id
   AND r.status=N'CONFLITO'
   AND r.status_publicacao=N'CONFLITO'
   AND COALESCE(r.motivo_publicacao,N'') NOT LIKE N'PRECEDENCIA[_]%';

 UPDATE d
    SET linkage_resultado_id=c.linkage_resultado_id,
        motivo=c.motivo,
        codigo_pessoa_origem=COALESCE(d.codigo_pessoa_origem,c.codigo_pessoa_origem),
        correlation_id=COALESCE(c.correlation_id,d.correlation_id)
 FROM qualidade.divergencia_gestor d WITH(UPDLOCK,HOLDLOCK)
 JOIN @conflitos_linkage c
   ON c.pessoa_observacao_id=d.pessoa_observacao_id
 WHERE d.status=N'ABERTA'
   AND d.tipo=N'DIVERGENCIA_IDENTIDADE'
   AND d.linkage_resultado_id IS NOT NULL;

 INSERT qualidade.divergencia_gestor(
   gestor_id,tipo,motivo,pessoa_observacao_id,codigo_pessoa_origem,
   status,correlation_id,linkage_resultado_id)
 SELECT c.gestor_id,N'DIVERGENCIA_IDENTIDADE',c.motivo,c.pessoa_observacao_id,
        c.codigo_pessoa_origem,N'ABERTA',c.correlation_id,c.linkage_resultado_id
 FROM @conflitos_linkage c
 WHERE NOT EXISTS(
   SELECT 1
   FROM qualidade.divergencia_gestor d WITH(UPDLOCK,HOLDLOCK)
   WHERE d.pessoa_observacao_id=c.pessoa_observacao_id
     AND d.status=N'ABERTA'
     AND d.tipo=N'DIVERGENCIA_IDENTIDADE'
     AND d.linkage_resultado_id IS NOT NULL);
END;
GO

CREATE OR ALTER VIEW qualidade.v_divergencia_gestor_aberta AS
SELECT d.divergencia_id,d.gestor_id,g.codigo gestor,d.tipo,d.motivo,
       d.pessoa_observacao_id,d.registro_observacao_id,d.codigo_pessoa_origem,
       d.linkage_resultado_id,d.correlation_id,d.aberta_em
FROM qualidade.divergencia_gestor d
JOIN ref.gestor g ON g.gestor_id=d.gestor_id
WHERE d.status=N'ABERTA';
GO

/*
  Superfície interna/restrita de auditoria. Não é a resposta da API de fila.
  O FK preserva a proveniência sem copiar score/candidatos para divergencia_gestor.
*/
CREATE OR ALTER VIEW qualidade.v_divergencia_linkage_contexto AS
SELECT d.divergencia_id,d.gestor_id,g.codigo gestor,d.pessoa_observacao_id,
       d.codigo_pessoa_origem,d.motivo,d.status,d.aberta_em,d.encerrada_em,
       r.linkage_resultado_id,r.linkage_run_id,r.modelo_id,r.modelo_versao,
       r.melhor_candidato_uuid,r.score_melhor,
       r.segundo_candidato_uuid,r.score_segundo,r.margem,
       r.status raw_status,r.motivo raw_motivo,
       r.resultado_publicacao,r.status_publicacao,r.motivo_publicacao,
       lr.tipo_run,lr.correlation_id linkage_correlation_id,
       lr.iniciado_em linkage_iniciado_em,lr.publicado_em linkage_publicado_em
FROM qualidade.divergencia_gestor d
JOIN ref.gestor g ON g.gestor_id=d.gestor_id
JOIN identidade.linkage_resultado r ON r.linkage_resultado_id=d.linkage_resultado_id
JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
WHERE d.linkage_resultado_id IS NOT NULL;
GO

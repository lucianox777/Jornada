SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Retenção histórica da avaliação sintética (#416)
  -------------------------------------------------
  A avaliação é validada contra um RASCUNHO existente e seu fingerprint canônico
  dentro de sp_registrar_avaliacao_sintetica_linkage. Depois de persistida, porém,
  precisa sobreviver à limpeza DEV do estado operacional/modelos.

  modelo_id permanece como identificador histórico, mas deixa de ser uma FK viva.
  modelo_versao + modelo_snapshot_sha256 + ruleset/fingerprints preservam a prova
  autocontida do snapshot efetivamente avaliado.
*/

IF OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica',N'U') IS NULL
   OR OBJECT_ID(N'identidade.modelo_linkage',N'U') IS NULL
    THROW 51920,'Retenção sintética exige ledger e modelo_linkage instalados.',1;
GO

DECLARE @drop NVARCHAR(MAX)=N'';

SELECT @drop=@drop+
       N'ALTER TABLE auditoria.linkage_avaliacao_sintetica DROP CONSTRAINT '
       +QUOTENAME(fk.name)+N';'
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc
  ON fkc.constraint_object_id=fk.object_id
JOIN sys.columns parent_column
  ON parent_column.object_id=fkc.parent_object_id
 AND parent_column.column_id=fkc.parent_column_id
WHERE fk.parent_object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica')
  AND fk.referenced_object_id=OBJECT_ID(N'identidade.modelo_linkage')
  AND parent_column.name=N'modelo_id';

IF @drop<>N''
    EXEC sys.sp_executesql @drop;
GO

IF EXISTS(
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc
      ON fkc.constraint_object_id=fk.object_id
    JOIN sys.columns parent_column
      ON parent_column.object_id=fkc.parent_object_id
     AND parent_column.column_id=fkc.parent_column_id
    WHERE fk.parent_object_id=OBJECT_ID(N'auditoria.linkage_avaliacao_sintetica')
      AND fk.referenced_object_id=OBJECT_ID(N'identidade.modelo_linkage')
      AND parent_column.name=N'modelo_id')
    THROW 51921,'FK viva entre avaliação sintética e modelo_linkage não foi removida.',1;
GO

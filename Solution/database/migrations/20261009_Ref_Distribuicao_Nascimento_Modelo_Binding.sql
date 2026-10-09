SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- A referência demográfica operacional é uma versão publicada em ref.
-- Vínculo por FK: o SHA do arquivo é proveniência, não substitui os dados em ref.
IF COL_LENGTH(N'identidade.modelo_linkage_referencia_demografica',N'distribuicao_versao_id') IS NULL
 ALTER TABLE identidade.modelo_linkage_referencia_demografica
 ADD distribuicao_versao_id BIGINT NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys
 WHERE parent_object_id=OBJECT_ID(N'identidade.modelo_linkage_referencia_demografica')
 AND name=N'fk_modelo_linkage_ref_demografica_distribuicao')
 ALTER TABLE identidade.modelo_linkage_referencia_demografica WITH CHECK
 ADD CONSTRAINT fk_modelo_linkage_ref_demografica_distribuicao
 FOREIGN KEY(distribuicao_versao_id)
 REFERENCES ref.distribuicao_nascimento_versao(distribuicao_versao_id);
GO

-- Pin novo somente sobre versão publicada, com identidade consistente.
-- NULL temporário mantém o rollout DEV incremental; gate obrigatório de
-- publicação deve ser ligado após backfill e testes end-to-end.
CREATE OR ALTER TRIGGER identidade.tr_modelo_linkage_ref_demografica_guard
ON identidade.modelo_linkage_referencia_demografica
AFTER INSERT
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM inserted i
   JOIN identidade.modelo_linkage m ON m.modelo_id=i.modelo_id
   WHERE m.status NOT IN(N'GERANDO',N'RASCUNHO'))
  THROW 52210,'Referência demográfica só pode ser fixada durante geração/rascunho.',1;

 IF EXISTS(
   SELECT 1 FROM inserted i
   LEFT JOIN ref.distribuicao_nascimento_versao v
     ON v.distribuicao_versao_id=i.distribuicao_versao_id
   WHERE i.distribuicao_versao_id IS NOT NULL
     AND (v.status<>N'PUBLICADA'
       OR v.codigo<>i.codigo_referencia
       OR v.geografia<>i.geografia
       OR v.data_referencia<>i.data_referencia
       OR v.metodo<>i.metodo
       OR v.fonte_arquivo_sha256<>i.arquivo_sha256))
  THROW 52235,'Pin demográfico diverge da distribuição publicada em ref.',1;
END;
GO

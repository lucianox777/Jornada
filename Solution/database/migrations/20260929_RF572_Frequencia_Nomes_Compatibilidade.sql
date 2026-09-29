SET XACT_ABORT ON;
GO
-- RF-572: metadados da validação de compatibilidade; NULL em versões legadas.
IF COL_LENGTH('ref.frequencia_nome_versao','normalizacao_versao') IS NULL
 ALTER TABLE ref.frequencia_nome_versao ADD normalizacao_versao NVARCHAR(80) NULL;
GO
IF COL_LENGTH('ref.frequencia_nome_versao','manifest_schema_version') IS NULL
 ALTER TABLE ref.frequencia_nome_versao ADD manifest_schema_version INT NULL;
GO
CREATE OR ALTER TRIGGER ref.tr_frequencia_nome_versao_metadado_immutavel ON ref.frequencia_nome_versao AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.frequencia_nome_versao_id=d.frequencia_nome_versao_id WHERE i.frequencia_nome_versao_id IS NULL AND d.status<>'CARREGANDO')
  THROW 51631,'Versão publicada de frequências não pode ser excluída.',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.frequencia_nome_versao_id=i.frequencia_nome_versao_id WHERE d.status<>'CARREGANDO' AND (i.codigo<>d.codigo OR i.fonte<>d.fonte OR i.edicao<>d.edicao OR i.data_referencia<>d.data_referencia OR ISNULL(i.publicado_em,'19000101')<>ISNULL(d.publicado_em,'19000101') OR ISNULL(i.conteudo_sha256,0x00)<>ISNULL(d.conteudo_sha256,0x00) OR ISNULL(i.normalizacao_versao,N'')<>ISNULL(d.normalizacao_versao,N'') OR ISNULL(i.manifest_schema_version,-1)<>ISNULL(d.manifest_schema_version,-1)))
  THROW 51632,'Metadados de uma versão publicada de frequências são imutáveis.',1;
END;
GO

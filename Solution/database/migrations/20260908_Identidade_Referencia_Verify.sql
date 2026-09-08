SET XACT_ABORT ON;
GO
IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE versao=1 AND estado='REFERENCIA')
 THROW 51152,'Referência histórica não normalizada.',1;
IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE estado='RESOLVIDA')
 THROW 51153,'Estado legado permanece na projeção corrente.',1;
IF NOT EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva_evento WHERE versao=1 AND estado='RESOLVIDA')
 THROW 51154,'Recibo histórico foi alterado.',1;
IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva p JOIN identidade.pessoa_origem_progressiva_evento e ON e.pessoa_origem_id=p.pessoa_origem_id AND e.versao=p.versao WHERE p.versao>0 AND (e.canonical_uuid<>p.canonical_uuid OR e.ocorrido_em<>p.ultima_resolucao_em))
 THROW 51155,'Migração alterou referência, versão ou data.',1;
IF NOT EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID('identidade.tr_progressiva_origem_guard') AND is_disabled=0)
 THROW 51156,'Guarda progressiva ausente ou desabilitada.',1;
IF NOT EXISTS(SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID('identidade.tr_progressiva_evento_append_only') AND is_disabled=0)
 THROW 51157,'Histórico append-only desprotegido.',1;
PRINT 'PROGRESSIVE REFERENCE SQLSERVER: OK';
GO

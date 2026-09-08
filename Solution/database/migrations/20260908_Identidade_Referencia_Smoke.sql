SET XACT_ABORT ON;
GO
-- Executar antes da migração V2, em banco descartável com V1 instalado.
BEGIN TRANSACTION;
DECLARE @source BIGINT,@uuid UNIQUEIDENTIFIER,@now DATETIMEOFFSET(7)=TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00');
SELECT TOP(1) @source=pessoa_origem_id FROM silver.pessoa_origem ORDER BY pessoa_origem_id;
IF @source IS NULL THROW 51150,'Fixture Silver ausente.',1;
DECLARE @r TABLE(initial_uuid UNIQUEIDENTIFIER,legacy_pessoa_uuid UNIQUEIDENTIFIER,estado VARCHAR(20),versao BIGINT);
INSERT INTO @r EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
SELECT @uuid=initial_uuid FROM @r;
IF EXISTS(SELECT 1 FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source AND versao<>0)
 THROW 51151,'Fixture exige origem progressiva inicial.',1;
INSERT identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,evidencia_referencia,politica_versao,universo_referencia,completo,ocorrido_em)
VALUES(NEWID(),@source,1,'RESOLUCAO','RESOLVIDA',@uuid,0,'NOVA_IDENTIDADE','synthetic:reference-upgrade','TEST_V1','synthetic:complete',1,@now);
UPDATE identidade.pessoa_origem_progressiva SET estado='RESOLVIDA',canonical_uuid=@uuid,versao=1,ultima_resolucao_em=@now,atualizado_em=@now WHERE pessoa_origem_id=@source;
COMMIT;
GO

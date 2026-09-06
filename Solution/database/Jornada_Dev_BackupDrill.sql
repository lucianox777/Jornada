
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @sha CHAR(64)='$(DRILL_SHA)';
DECLARE @length BIGINT=$(DRILL_LENGTH);
DECLARE @entrega UNIQUEIDENTIFIER='35500000-0000-4000-8000-00000000B001';
DECLARE @g BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB');
DECLARE @so BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@g AND codigo='SEHAB');
DECLARE @gpv BIGINT=(SELECT TOP(1) gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@g AND status='ATIVA' ORDER BY versao DESC);
IF @sha LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 OR LEN(@sha)<>64 THROW 51560,'DRILL_SHA inválido.',1;
IF @length<=0 THROW 51561,'DRILL_LENGTH inválido.',1;
IF @g IS NULL OR @so IS NULL OR @gpv IS NULL THROW 51562,'Seed de desenvolvimento não aplicado.',1;
IF NOT EXISTS(SELECT 1 FROM ingestao.entrega WHERE entrega_id=@entrega)
 INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
 VALUES(@entrega,@g,@so,@gpv,NULL,NULL,NULL,'backup-restore-drill-v355',@sha,@length,'PROCESSADA','2026-08-31T00:00:00+00:00','2026-08-31T11:00:00+00:00','2026-08-31T11:00:00+00:00');
IF NOT EXISTS(SELECT 1 FROM bronze.entrega_arquivo WHERE entrega_id=@entrega)
 INSERT bronze.entrega_arquivo(entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em)
 VALUES(@entrega,CONCAT('DRILL_',@sha,'.zip'),'application/zip',CONCAT('sha256/',SUBSTRING(@sha,1,2),'/',SUBSTRING(@sha,3,2),'/',@sha,'.zip'),@sha,@length,'2026-08-31T11:00:00+00:00');
SELECT CONVERT(varchar(36),@entrega) AS entrega_id,@sha AS sha256,@length AS tamanho_bytes;

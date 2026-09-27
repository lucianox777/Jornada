/* DT-05: capture the complete visible Silver source set under the Runner corpus lease.
   Bronze is append-only; source ZIPs are pinned once per run, no payload copies.
   This SQL evidence is NOT by itself a complete historical decision replay. */
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'identidade.linkage_bronze_captura',N'U') IS NULL
BEGIN
 CREATE TABLE identidade.linkage_bronze_captura(
   linkage_run_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY REFERENCES identidade.linkage_run(linkage_run_id),
   source_high_watermark BIGINT NOT NULL,
   silver_observacoes BIGINT NOT NULL,
   objetos_bronze BIGINT NOT NULL,
   capturado_em DATETIMEOFFSET(7) NOT NULL DEFAULT SYSUTCDATETIME(),
   CONSTRAINT CK_linkage_bronze_captura_contagens CHECK(silver_observacoes>=0 AND objetos_bronze>=0)
 );
END;
GO
CREATE OR ALTER PROCEDURE identidade.sp_capturar_fontes_bronze_linkage
 @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON;
 SET XACT_ABORT ON;
 DECLARE @high_watermark BIGINT,@status NVARCHAR(30);
 SELECT @high_watermark=pessoa_observacao_id_high_watermark,@status=status
 FROM identidade.linkage_run WHERE linkage_run_id=@linkage_run_id;
 IF @status<>N'EXECUTANDO' OR @high_watermark IS NULL
   THROW 51960,N'DT-05 exige execução ativa e universo congelado.',1;
 IF EXISTS(SELECT 1 FROM identidade.linkage_bronze_captura WHERE linkage_run_id=@linkage_run_id)
   THROW 51961,N'DT-05 captura já concluída; não recapturar universo.',1;
 DECLARE @refs TABLE(objeto_chave NVARCHAR(1024) NOT NULL,payload_sha256 CHAR(64) NOT NULL,
                     PRIMARY KEY(payload_sha256));
 IF EXISTS(
    SELECT 1 FROM silver.pessoa_observacao po
    JOIN ingestao.lote l ON l.lote_id=po.lote_id
    LEFT JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
    WHERE po.pessoa_observacao_id<=@high_watermark
      AND (b.entrega_id IS NULL OR b.estado_armazenamento<>N'DISPONIVEL'))
   THROW 51962,N'DT-05: fonte Bronze ausente ou indisponível para o corpus.',1;
 INSERT @refs(objeto_chave,payload_sha256)
 SELECT MIN(b.objeto_chave),b.payload_sha256
 FROM silver.pessoa_observacao po
 JOIN ingestao.lote l ON l.lote_id=po.lote_id
 JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
 WHERE po.pessoa_observacao_id<=@high_watermark
 GROUP BY b.payload_sha256;
 DECLARE @key NVARCHAR(1024),@sha CHAR(64);
 DECLARE pins CURSOR LOCAL FAST_FORWARD FOR SELECT objeto_chave,payload_sha256 FROM @refs ORDER BY payload_sha256;
 OPEN pins;
 FETCH NEXT FROM pins INTO @key,@sha;
 WHILE @@FETCH_STATUS=0
 BEGIN
   EXEC identidade.sp_fixar_bronze_para_linkage @linkage_run_id,@key,@sha;
   FETCH NEXT FROM pins INTO @key,@sha;
 END;
 CLOSE pins;
 DEALLOCATE pins;
 -- The corpus lease prevents new Silver writes while this runs; Bronze ingestion
 -- may append, but cannot affect the already visible Silver source set.
 INSERT identidade.linkage_bronze_captura(linkage_run_id,source_high_watermark,silver_observacoes,objetos_bronze)
 SELECT @linkage_run_id,@high_watermark,
   (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE pessoa_observacao_id<=@high_watermark),
   (SELECT COUNT_BIG(*) FROM @refs);
END;
GO

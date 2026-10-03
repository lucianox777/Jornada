/* DT-05: Bronze provenance follows the exact immutable run universe.
   Candidate reference state is frozen separately by candidate-state. */
SET XACT_ABORT ON;
GO
CREATE OR ALTER PROCEDURE identidade.sp_capturar_fontes_bronze_linkage
 @linkage_run_id UNIQUEIDENTIFIER
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @high_watermark BIGINT,@status NVARCHAR(30);
 SELECT @high_watermark=pessoa_observacao_id_high_watermark,@status=status
 FROM identidade.linkage_run WHERE linkage_run_id=@linkage_run_id;
 IF @status<>N'EXECUTANDO' OR @high_watermark IS NULL
   THROW 51960,N'DT-05 exige execução ativa e universo congelado.',1;
 IF EXISTS(SELECT 1 FROM identidade.linkage_bronze_captura WHERE linkage_run_id=@linkage_run_id)
   THROW 51961,N'DT-05 captura já concluída; não recapturar universo.',1;
 IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run_item WHERE linkage_run_id=@linkage_run_id)
   THROW 51963,N'DT-05: universo do run vazio; captura Bronze recusada.',1;

 DECLARE @refs TABLE(objeto_chave NVARCHAR(1024) NOT NULL,payload_sha256 CHAR(64) NOT NULL,
                     PRIMARY KEY(payload_sha256));
 IF EXISTS(
    SELECT 1 FROM identidade.linkage_run_item li
    JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=li.pessoa_observacao_id
    JOIN ingestao.lote l ON l.lote_id=po.lote_id
    LEFT JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
    WHERE li.linkage_run_id=@linkage_run_id
      AND (b.entrega_id IS NULL OR b.estado_armazenamento<>N'DISPONIVEL'))
   THROW 51962,N'DT-05: fonte Bronze ausente ou indisponível para o universo do run.',1;

 INSERT @refs(objeto_chave,payload_sha256)
 SELECT MIN(b.objeto_chave),b.payload_sha256
 FROM identidade.linkage_run_item li
 JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=li.pessoa_observacao_id
 JOIN ingestao.lote l ON l.lote_id=po.lote_id
 JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
 WHERE li.linkage_run_id=@linkage_run_id
 GROUP BY b.payload_sha256;

 DECLARE @key NVARCHAR(1024),@sha CHAR(64);
 DECLARE pins CURSOR LOCAL FAST_FORWARD FOR SELECT objeto_chave,payload_sha256 FROM @refs ORDER BY payload_sha256;
 OPEN pins; FETCH NEXT FROM pins INTO @key,@sha;
 WHILE @@FETCH_STATUS=0
 BEGIN
   EXEC identidade.sp_fixar_bronze_para_linkage @linkage_run_id,@key,@sha;
   FETCH NEXT FROM pins INTO @key,@sha;
 END;
 CLOSE pins; DEALLOCATE pins;

 INSERT identidade.linkage_bronze_captura(linkage_run_id,source_high_watermark,silver_observacoes,objetos_bronze)
 SELECT @linkage_run_id,@high_watermark,
   (SELECT COUNT_BIG(*) FROM identidade.linkage_run_item WHERE linkage_run_id=@linkage_run_id),
   (SELECT COUNT_BIG(*) FROM @refs);
END;
GO

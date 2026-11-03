SET NOCOUNT ON;
SET XACT_ABORT OFF;
-- Run ONLY on a disposable SQL instance with both ref migrations installed.
IF OBJECT_ID(N'ref.sp_registrar_corpus_sintetico_arquivo_v9', N'P') IS NULL
    THROW 52420, 'V9 ref archive migration has not been installed.', 1;
IF NOT EXISTS (SELECT 1 FROM ref.corpus_sintetico_versao_v9
               WHERE codigo = N'V9_SEED42_30000_CSHARP_V1')
    THROW 52421, 'V9 synthetic ref provenance registry absent.', 1;
GO
BEGIN TRY
    EXEC ref.sp_registrar_corpus_sintetico_arquivo_v9
         @codigo=N'V9_SEED42_30000_CSHARP_V1',
         @arquivo=N'../observacoes.csv', @conteudo=0x01;
    THROW 52422, 'Unsafe filename was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 52402 THROW;
END CATCH;
GO
BEGIN TRY
    EXEC ref.sp_registrar_corpus_sintetico_arquivo_v9
         @codigo=N'V9_SEED42_30000_CSHARP_V1',
         @arquivo=N'pessoas_verdade.csv', @conteudo=0x01;
    THROW 52423, 'Incorrect SHA256 was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 52405 THROW;
END CATCH;
GO
BEGIN TRY
    EXEC ref.sp_registrar_corpus_sintetico_arquivo_v9
         @codigo=N'UNKNOWN', @arquivo=N'observacoes.csv', @conteudo=0x01;
    THROW 52424, 'Unknown version was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 52404 THROW;
END CATCH;
GO
IF EXISTS (SELECT 1 FROM ref.corpus_sintetico_arquivo_v9
           WHERE codigo = N'V9_SEED42_30000_CSHARP_V1')
    THROW 52425, 'Negative tests unexpectedly inserted corpus bytes.', 1;
GO
BEGIN TRY
    EXEC ref.sp_exigir_corpus_sintetico_v9_completo
         @codigo=N'V9_SEED42_30000_CSHARP_V1';
    THROW 52426, 'Incomplete ref corpus was accepted.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 52407 THROW;
END CATCH;
PRINT 'PASS: V9 ref rejects path traversal, forged bytes, unknown version and incomplete corpus.';
GO

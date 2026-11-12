SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- DT-15: explicit provenance binding only. Never infer model identity from
-- filenames, timestamps, finalist-validation receipts or SQL result hashes.
IF OBJECT_ID(N'auditoria.modelo_linkage_artefato_calibracao_original', N'U') IS NULL
BEGIN
    CREATE TABLE auditoria.modelo_linkage_artefato_calibracao_original
    (
        modelo_id UNIQUEIDENTIFIER NOT NULL
            REFERENCES identidade.modelo_linkage(modelo_id),
        nome_arquivo_original NVARCHAR(260) NOT NULL,
        sha256_bytes_originais BINARY(32) NOT NULL,
        referencia_armazenamento NVARCHAR(1000) NOT NULL,
        tamanho_bytes BIGINT NOT NULL,
        registrado_por NVARCHAR(256) NOT NULL,
        registrado_em DATETIMEOFFSET(7) NOT NULL
            CONSTRAINT df_dt15_artefato_registrado_em DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT pk_dt15_artefato_modelo PRIMARY KEY(modelo_id),
        CONSTRAINT ck_dt15_artefato_tamanho CHECK(tamanho_bytes > 0),
        CONSTRAINT ck_dt15_artefato_nome CHECK(
            LEN(LTRIM(RTRIM(nome_arquivo_original))) > 0
            AND nome_arquivo_original NOT LIKE N'%/%'
            AND nome_arquivo_original NOT LIKE N'%\%'),
        CONSTRAINT ck_dt15_artefato_referencia CHECK(LEN(LTRIM(RTRIM(referencia_armazenamento))) > 0),
        CONSTRAINT ck_dt15_artefato_autor CHECK(LEN(LTRIM(RTRIM(registrado_por))) > 0)
    );
END;
GO

CREATE OR ALTER TRIGGER auditoria.tr_dt15_artefato_calibracao_append_only
ON auditoria.modelo_linkage_artefato_calibracao_original
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51930, 'Vínculo DT-15 entre modelo e JSON original é imutável.', 1;
END;
GO

-- Caller must have verified the SHA-256 of the original bytes and storage
-- immutability before registering. The database cannot read external files.
CREATE OR ALTER PROCEDURE auditoria.sp_registrar_artefato_calibracao_original_dt15
    @modelo_id UNIQUEIDENTIFIER,
    @nome_arquivo_original NVARCHAR(260),
    @sha256_bytes_originais BINARY(32),
    @referencia_armazenamento NVARCHAR(1000),
    @tamanho_bytes BIGINT,
    @registrado_por NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF @modelo_id IS NULL OR @sha256_bytes_originais IS NULL
        OR DATALENGTH(@sha256_bytes_originais) <> 32
        THROW 51931, 'Modelo e SHA-256 original são obrigatórios.', 1;
    IF @tamanho_bytes IS NULL OR @tamanho_bytes <= 0
        THROW 51932, 'Tamanho do artefato inválido.', 1;
    IF NULLIF(LTRIM(RTRIM(@nome_arquivo_original)), N'') IS NULL
        OR @nome_arquivo_original LIKE N'%/%'
        OR @nome_arquivo_original LIKE N'%\%'
        THROW 51933, 'Nome original de arquivo inválido.', 1;
    IF NULLIF(LTRIM(RTRIM(@referencia_armazenamento)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@registrado_por)), N'') IS NULL
        THROW 51934, 'Armazenamento e responsável são obrigatórios.', 1;

    BEGIN TRANSACTION;
    DECLARE @status NVARCHAR(20);
    SELECT @status = status
    FROM identidade.modelo_linkage WITH (UPDLOCK, HOLDLOCK)
    WHERE modelo_id = @modelo_id;
    IF @status IS NULL
    BEGIN
        ROLLBACK;
        THROW 51935, 'Modelo não encontrado.', 1;
    END;
    IF @status NOT IN (N'GERANDO', N'RASCUNHO', N'VALIDADO', N'ATIVO')
    BEGIN
        ROLLBACK;
        THROW 51936, 'Status do modelo incompatível com registro de evidência.', 1;
    END;
    IF EXISTS (SELECT 1 FROM auditoria.modelo_linkage_artefato_calibracao_original WITH (UPDLOCK, HOLDLOCK)
               WHERE modelo_id = @modelo_id)
    BEGIN
        ROLLBACK;
        THROW 51937, 'Vínculo original já registrado: substituição proibida.', 1;
    END;
    INSERT INTO auditoria.modelo_linkage_artefato_calibracao_original
        (modelo_id, nome_arquivo_original, sha256_bytes_originais,
         referencia_armazenamento, tamanho_bytes, registrado_por)
    VALUES (@modelo_id, @nome_arquivo_original, @sha256_bytes_originais,
            @referencia_armazenamento, @tamanho_bytes, @registrado_por);
    COMMIT;
END;
GO

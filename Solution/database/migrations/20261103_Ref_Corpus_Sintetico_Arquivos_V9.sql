SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
-- Durable V9 synthetic source bytes. No operational linkage or IBGE frequencies.
-- Apply ONLY to an explicitly selected persistent SQL ref database; the GitHub
-- Actions JornadaHarness service is ephemeral and is not durable storage.
IF OBJECT_ID(N'ref.corpus_sintetico_versao_v9', N'U') IS NULL
    THROW 52400, 'Install the approved V9 synthetic provenance registry first.', 1;
GO
IF OBJECT_ID(N'ref.corpus_sintetico_arquivo_v9', N'U') IS NULL
BEGIN
    CREATE TABLE ref.corpus_sintetico_arquivo_v9
    (
        codigo NVARCHAR(120) NOT NULL,
        arquivo NVARCHAR(64) NOT NULL,
        tamanho_bytes BIGINT NOT NULL,
        sha256 CHAR(64) NOT NULL,
        conteudo VARBINARY(MAX) NOT NULL,
        registrado_em DATETIMEOFFSET(7) NOT NULL
            CONSTRAINT df_ref_corpus_sintetico_arquivo_v9_registro DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT pk_ref_corpus_sintetico_arquivo_v9 PRIMARY KEY (codigo, arquivo),
        CONSTRAINT fk_ref_corpus_sintetico_arquivo_v9_versao FOREIGN KEY (codigo)
            REFERENCES ref.corpus_sintetico_versao_v9(codigo),
        CONSTRAINT ck_ref_corpus_sintetico_arquivo_v9_nome CHECK
            (arquivo IN (N'pessoas_verdade.csv', N'observacoes.csv',
                         N'gabarito.json', N'generation-manifest.json')),
        CONSTRAINT ck_ref_corpus_sintetico_arquivo_v9_conteudo CHECK
            (tamanho_bytes > 0 AND tamanho_bytes <= 536870912
             AND tamanho_bytes = DATALENGTH(conteudo)
             AND sha256 = CONVERT(CHAR(64), HASHBYTES('SHA2_256', conteudo), 2))
    );
END;
GO
-- Append-only, even when the caller has table UPDATE/DELETE privileges.
CREATE OR ALTER TRIGGER ref.tr_corpus_sintetico_arquivo_v9_immutable
ON ref.corpus_sintetico_arquivo_v9
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 52401, 'V9 synthetic corpus bytes are immutable.', 1;
END;
GO
-- A caller should open ONE SQL transaction and register all four bytes there.
-- An incomplete archive is NEVER eligible for reuse; readers call the gate below.
CREATE OR ALTER PROCEDURE ref.sp_registrar_corpus_sintetico_arquivo_v9
    @codigo NVARCHAR(120),
    @arquivo NVARCHAR(64),
    @conteudo VARBINARY(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @codigo IS NULL OR @arquivo IS NULL OR @arquivo NOT IN
       (N'pessoas_verdade.csv', N'observacoes.csv', N'gabarito.json',
        N'generation-manifest.json')
        THROW 52402, 'Unrecognized V9 synthetic artifact.', 1;
    IF @conteudo IS NULL OR DATALENGTH(@conteudo) = 0
        OR DATALENGTH(@conteudo) > 536870912
        THROW 52403, 'Invalid V9 synthetic artifact size.', 1;

    DECLARE @esperado CHAR(64), @obtido CHAR(64), @tamanho BIGINT;
    SELECT @esperado = CASE @arquivo
             WHEN N'pessoas_verdade.csv' THEN corpus_sha256
             WHEN N'observacoes.csv' THEN observacoes_sha256
             WHEN N'gabarito.json' THEN gabarito_sha256
             WHEN N'generation-manifest.json' THEN manifesto_sha256
           END
    FROM ref.corpus_sintetico_versao_v9 WITH (UPDLOCK, HOLDLOCK)
    WHERE codigo = @codigo AND origem = N'SINTETICO_IBGE'
          AND status = N'EVIDENCIA_NAO_OPERACIONAL';
    IF @esperado IS NULL
        THROW 52404, 'Unapproved V9 synthetic reference version.', 1;
    SELECT @obtido = CONVERT(CHAR(64), HASHBYTES('SHA2_256', @conteudo), 2),
           @tamanho = DATALENGTH(@conteudo);
    IF @obtido <> @esperado
        THROW 52405, 'V9 synthetic artifact differs from pinned hash.', 1;

    IF EXISTS (SELECT 1 FROM ref.corpus_sintetico_arquivo_v9 WITH (UPDLOCK, HOLDLOCK)
               WHERE codigo = @codigo AND arquivo = @arquivo)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM ref.corpus_sintetico_arquivo_v9
                       WHERE codigo = @codigo AND arquivo = @arquivo
                         AND sha256 = @esperado AND tamanho_bytes = @tamanho)
            THROW 52406, 'V9 archive collision: existing bytes differ.', 1;
        RETURN; -- Verified idempotent replay, NEVER replace.
    END;
    INSERT ref.corpus_sintetico_arquivo_v9(codigo, arquivo, tamanho_bytes, sha256, conteudo)
    VALUES (@codigo, @arquivo, @tamanho, @obtido, @conteudo);
END;
GO
CREATE OR ALTER PROCEDURE ref.sp_exigir_corpus_sintetico_v9_completo
    @codigo NVARCHAR(120)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @qtde INT;
    SELECT @qtde = COUNT(*)
    FROM ref.corpus_sintetico_versao_v9 AS v
    INNER JOIN ref.corpus_sintetico_arquivo_v9 AS a ON a.codigo = v.codigo
    WHERE v.codigo = @codigo
      AND v.origem = N'SINTETICO_IBGE' AND v.status = N'EVIDENCIA_NAO_OPERACIONAL'
      AND a.sha256 = CASE a.arquivo
           WHEN N'pessoas_verdade.csv' THEN v.corpus_sha256
           WHEN N'observacoes.csv' THEN v.observacoes_sha256
           WHEN N'gabarito.json' THEN v.gabarito_sha256
           WHEN N'generation-manifest.json' THEN v.manifesto_sha256
          END;
    IF @qtde <> 4
        THROW 52407, 'V9 synthetic archive incomplete or invalid; regenerate or restore from verified source.', 1;
    SELECT @codigo AS codigo, @qtde AS arquivos, N'EVIDENCIA_NAO_OPERACIONAL' AS status;
END;
GO

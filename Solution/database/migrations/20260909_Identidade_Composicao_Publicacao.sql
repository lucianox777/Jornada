SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'identidade.composicao_publicacao', N'U') IS NULL
BEGIN
    CREATE TABLE identidade.composicao_publicacao(
        decision_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_identidade_composicao_publicacao PRIMARY KEY,
        recomposition_plan_hash CHAR(64) NOT NULL,
        factual_revalidation_version NVARCHAR(80) NOT NULL,
        publication_version NVARCHAR(80) NOT NULL,
        command_hash CHAR(64) NOT NULL,
        published_by NVARCHAR(200) NOT NULL,
        published_at DATETIMEOFFSET(7) NOT NULL,
        mutation_count INT NOT NULL,
        state NVARCHAR(20) NOT NULL,
        CONSTRAINT FK_identidade_composicao_publicacao_recomposicao
            FOREIGN KEY(decision_id) REFERENCES identidade.composicao_recomposicao_plano(decision_id),
        CONSTRAINT CK_identidade_composicao_publicacao_plan_hash
            CHECK(recomposition_plan_hash NOT LIKE '%[^0-9a-f]%' AND LEN(recomposition_plan_hash)=64),
        CONSTRAINT CK_identidade_composicao_publicacao_command_hash
            CHECK(command_hash NOT LIKE '%[^0-9a-f]%' AND LEN(command_hash)=64),
        CONSTRAINT CK_identidade_composicao_publicacao_count CHECK(mutation_count>=0),
        CONSTRAINT CK_identidade_composicao_publicacao_state CHECK(state=N'PUBLICADA')
    );
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_composicao_publicacao_append_only
ON identidade.composicao_publicacao
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51064, 'identidade.composicao_publicacao e append-only.', 1;
END;
GO

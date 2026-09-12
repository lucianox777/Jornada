SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('identidade.linkage_run','frequencia_nome_versao_id') IS NULL
    ALTER TABLE identidade.linkage_run ADD frequencia_nome_versao_id BIGINT NULL;
GO
IF COL_LENGTH('identidade.linkage_run','frequencia_nome_versao_codigo') IS NULL
    ALTER TABLE identidade.linkage_run ADD frequencia_nome_versao_codigo NVARCHAR(80) NULL;
GO
IF COL_LENGTH('identidade.linkage_run','frequencia_nome_conteudo_sha256') IS NULL
    ALTER TABLE identidade.linkage_run ADD frequencia_nome_conteudo_sha256 BINARY(32) NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_run')
      AND name='fk_linkage_run_frequencia_nome_versao')
BEGIN
    ALTER TABLE identidade.linkage_run WITH CHECK
        ADD CONSTRAINT fk_linkage_run_frequencia_nome_versao
        FOREIGN KEY(frequencia_nome_versao_id)
        REFERENCES ref.frequencia_nome_versao(frequencia_nome_versao_id);
END;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.linkage_run')
      AND name='ck_linkage_run_frequencia_nome_snapshot')
BEGIN
    ALTER TABLE identidade.linkage_run WITH CHECK
        ADD CONSTRAINT ck_linkage_run_frequencia_nome_snapshot CHECK(
            (frequencia_nome_versao_id IS NULL
             AND frequencia_nome_versao_codigo IS NULL
             AND frequencia_nome_conteudo_sha256 IS NULL)
            OR
            (frequencia_nome_versao_id IS NOT NULL
             AND frequencia_nome_versao_codigo IS NOT NULL
             AND frequencia_nome_conteudo_sha256 IS NOT NULL)
        );
END;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('identidade.linkage_run')
      AND name='ix_linkage_run_frequencia_nome_versao')
BEGIN
    CREATE INDEX ix_linkage_run_frequencia_nome_versao
        ON identidade.linkage_run(frequencia_nome_versao_id, iniciado_em)
        WHERE frequencia_nome_versao_id IS NOT NULL;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_run_congela_frequencia_nome
ON identidade.linkage_run
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- A proveniência é sempre derivada do modelo selecionado. Callers não podem injetar
    -- uma versão/hash diferente da referência que o modelo realmente congelou.
    IF EXISTS(
        SELECT 1 FROM inserted
        WHERE frequencia_nome_versao_id IS NOT NULL
           OR frequencia_nome_versao_codigo IS NOT NULL
           OR frequencia_nome_conteudo_sha256 IS NOT NULL)
        THROW 51650,'A referência de nomes do linkage_run é derivada do modelo e não pode ser informada pelo caller.',1;

    UPDATE lr
       SET frequencia_nome_versao_id=m.frequencia_nome_versao_id,
           frequencia_nome_versao_codigo=v.codigo,
           frequencia_nome_conteudo_sha256=v.conteudo_sha256
    FROM identidade.linkage_run lr
    JOIN inserted i ON i.linkage_run_id=lr.linkage_run_id
    JOIN identidade.modelo_linkage m ON m.modelo_id=lr.modelo_id
    JOIN ref.frequencia_nome_versao v
      ON v.frequencia_nome_versao_id=m.frequencia_nome_versao_id
    WHERE m.frequencia_nome_versao_id IS NOT NULL;
END;
GO

CREATE OR ALTER TRIGGER identidade.tr_linkage_run_frequencia_nome_immutavel
ON identidade.linkage_run
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS(
        SELECT 1
        FROM inserted i
        JOIN deleted d ON d.linkage_run_id=i.linkage_run_id
        WHERE
            ISNULL(i.frequencia_nome_versao_id,-1)<>ISNULL(d.frequencia_nome_versao_id,-1)
            OR ISNULL(i.frequencia_nome_versao_codigo,N'')<>ISNULL(d.frequencia_nome_versao_codigo,N'')
            OR ISNULL(i.frequencia_nome_conteudo_sha256,0x00)<>ISNULL(d.frequencia_nome_conteudo_sha256,0x00)
    )
    BEGIN
        -- Única transição permitida: o AFTER INSERT acima preenche uma vez o snapshot
        -- de um run PREPARANDO que acabou de nascer sem proveniência explícita.
        IF EXISTS(
            SELECT 1
            FROM inserted i
            JOIN deleted d ON d.linkage_run_id=i.linkage_run_id
            JOIN identidade.modelo_linkage m ON m.modelo_id=i.modelo_id
            JOIN ref.frequencia_nome_versao v
              ON v.frequencia_nome_versao_id=m.frequencia_nome_versao_id
            WHERE
                (ISNULL(i.frequencia_nome_versao_id,-1)<>ISNULL(d.frequencia_nome_versao_id,-1)
                 OR ISNULL(i.frequencia_nome_versao_codigo,N'')<>ISNULL(d.frequencia_nome_versao_codigo,N'')
                 OR ISNULL(i.frequencia_nome_conteudo_sha256,0x00)<>ISNULL(d.frequencia_nome_conteudo_sha256,0x00))
                AND NOT (
                    d.status='PREPARANDO'
                    AND i.status='PREPARANDO'
                    AND d.frequencia_nome_versao_id IS NULL
                    AND d.frequencia_nome_versao_codigo IS NULL
                    AND d.frequencia_nome_conteudo_sha256 IS NULL
                    AND i.frequencia_nome_versao_id=m.frequencia_nome_versao_id
                    AND i.frequencia_nome_versao_codigo=v.codigo
                    AND i.frequencia_nome_conteudo_sha256=v.conteudo_sha256
                )
        )
            THROW 51651,'A referência de nomes congelada no linkage_run é imutável.',1;

        -- Mudanças para NULL, para uma referência sem vínculo no modelo, ou em runs fora de
        -- PREPARANDO também são sempre proibidas.
        IF EXISTS(
            SELECT 1
            FROM inserted i
            JOIN deleted d ON d.linkage_run_id=i.linkage_run_id
            LEFT JOIN identidade.modelo_linkage m ON m.modelo_id=i.modelo_id
            LEFT JOIN ref.frequencia_nome_versao v
              ON v.frequencia_nome_versao_id=m.frequencia_nome_versao_id
            WHERE
                (ISNULL(i.frequencia_nome_versao_id,-1)<>ISNULL(d.frequencia_nome_versao_id,-1)
                 OR ISNULL(i.frequencia_nome_versao_codigo,N'')<>ISNULL(d.frequencia_nome_versao_codigo,N'')
                 OR ISNULL(i.frequencia_nome_conteudo_sha256,0x00)<>ISNULL(d.frequencia_nome_conteudo_sha256,0x00))
                AND (
                    m.frequencia_nome_versao_id IS NULL
                    OR v.frequencia_nome_versao_id IS NULL
                    OR d.status<>'PREPARANDO'
                    OR i.status<>'PREPARANDO'
                    OR d.frequencia_nome_versao_id IS NOT NULL
                    OR d.frequencia_nome_versao_codigo IS NOT NULL
                    OR d.frequencia_nome_conteudo_sha256 IS NOT NULL
                    OR i.frequencia_nome_versao_id<>m.frequencia_nome_versao_id
                    OR i.frequencia_nome_versao_codigo<>v.codigo
                    OR i.frequencia_nome_conteudo_sha256<>v.conteudo_sha256
                )
        )
            THROW 51651,'A referência de nomes congelada no linkage_run é imutável.',1;
    END;
END;
GO

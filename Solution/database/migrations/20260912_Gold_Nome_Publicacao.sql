SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- A frequência populacional não é materializada em gold.pessoa: ela depende da versão
-- de ref.frequencia_nome fixada no modelo/run. Gold persiste somente a chave semântica
-- estável usada para consultar essa referência versionada.
IF COL_LENGTH('gold.pessoa','nome_publicacao_normalizado') IS NULL
    ALTER TABLE gold.pessoa ADD nome_publicacao_normalizado NVARCHAR(200) NULL;
GO
IF COL_LENGTH('gold.pessoa','nome_publicacao_metodo_versao') IS NULL
    ALTER TABLE gold.pessoa ADD nome_publicacao_metodo_versao NVARCHAR(80) NULL;
GO
IF COL_LENGTH('gold.pessoa','nome_publicacao_normalizacao_versao') IS NULL
    ALTER TABLE gold.pessoa ADD nome_publicacao_normalizacao_versao NVARCHAR(80) NULL;
GO

-- Backfill somente a partir de nome_cmp, que já foi produzido pelo normalizador C#
-- canônico. Não tentamos reproduzir remoção de diacríticos/whitespace em T-SQL.
;WITH chave AS(
    SELECT g.pessoa_uuid,
           LEFT(src.nome_cmp,CHARINDEX(N' ',src.nome_cmp+N' ')-1) AS nome_publicacao_normalizado
    FROM gold.pessoa g
    OUTER APPLY(
        SELECT TOP(1) po.nome_cmp
        FROM silver.pessoa_observacao po
        JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
        WHERE vc.pessoa_uuid=g.pessoa_uuid
          AND vc.status='RESOLVIDO'
          AND po.nome_completo=g.nome_completo
          AND NULLIF(LTRIM(RTRIM(po.nome_cmp)),N'') IS NOT NULL
        ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
    ) src
)
UPDATE g
SET nome_publicacao_normalizado=c.nome_publicacao_normalizado,
    nome_publicacao_metodo_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END,
    nome_publicacao_normalizacao_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IDENTITY_NORMALIZATION_V1' END
FROM gold.pessoa g
JOIN chave c ON c.pessoa_uuid=g.pessoa_uuid;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('gold.pessoa') AND name='ck_gold_pessoa_nome_publicacao_completo')
BEGIN
    ALTER TABLE gold.pessoa WITH CHECK ADD CONSTRAINT ck_gold_pessoa_nome_publicacao_completo CHECK(
        (nome_publicacao_normalizado IS NULL AND nome_publicacao_metodo_versao IS NULL AND nome_publicacao_normalizacao_versao IS NULL)
        OR
        (NULLIF(LTRIM(RTRIM(nome_publicacao_normalizado)),N'') IS NOT NULL
         AND nome_publicacao_metodo_versao=N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1'
         AND nome_publicacao_normalizacao_versao=N'IDENTITY_NORMALIZATION_V1'));
END;
GO

-- Centraliza a manutenção para cobrir tanto o Processor quanto recomposições governadas
-- executadas por identidade.sp_recompor_gold_pessoa. O trigger apenas tokeniza nome_cmp já
-- normalizado: não implementa um segundo normalizador de nomes em SQL.
CREATE OR ALTER TRIGGER gold.tr_pessoa_nome_publicacao
ON gold.pessoa
AFTER INSERT,UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL()>1 RETURN;

    ;WITH chave AS(
        SELECT i.pessoa_uuid,
               LEFT(src.nome_cmp,CHARINDEX(N' ',src.nome_cmp+N' ')-1) AS nome_publicacao_normalizado
        FROM inserted i
        OUTER APPLY(
            SELECT TOP(1) po.nome_cmp
            FROM silver.pessoa_observacao po
            JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE vc.pessoa_uuid=i.pessoa_uuid
              AND vc.status='RESOLVIDO'
              AND po.nome_completo=i.nome_completo
              AND NULLIF(LTRIM(RTRIM(po.nome_cmp)),N'') IS NOT NULL
            ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC
        ) src
    )
    UPDATE g
    SET nome_publicacao_normalizado=c.nome_publicacao_normalizado,
        nome_publicacao_metodo_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END,
        nome_publicacao_normalizacao_versao=CASE WHEN c.nome_publicacao_normalizado IS NULL THEN NULL ELSE N'IDENTITY_NORMALIZATION_V1' END
    FROM gold.pessoa g
    JOIN chave c ON c.pessoa_uuid=g.pessoa_uuid
    WHERE ISNULL(g.nome_publicacao_normalizado,N'')<>ISNULL(c.nome_publicacao_normalizado,N'')
       OR ISNULL(g.nome_publicacao_metodo_versao,N'')<>CASE WHEN c.nome_publicacao_normalizado IS NULL THEN N'' ELSE N'IBGE_CENSO_2022_NOMES_PUBLICACAO_V1' END
       OR ISNULL(g.nome_publicacao_normalizacao_versao,N'')<>CASE WHEN c.nome_publicacao_normalizado IS NULL THEN N'' ELSE N'IDENTITY_NORMALIZATION_V1' END;
END;
GO

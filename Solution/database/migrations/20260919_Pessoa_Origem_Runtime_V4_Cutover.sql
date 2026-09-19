SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Runtime v4 - cutover da Pessoa de origem
 ----------------------------------------
 Pré-requisitos: 20260913_Base_Pessoa_Origem.sql.
 Depois deste ponto todo pessoa_origem possui Base explícita. A unicidade deixa de
 ser (sistema,codigo) e passa a ser (base,codigo), permitindo que um mesmo sistema
 autorizado consulte bases distintas sem colidir namespaces.
*/

IF OBJECT_ID('ref.base_pessoa_origem','U') IS NULL
   OR OBJECT_ID('ref.sistema_origem_base_pessoa','U') IS NULL
   OR COL_LENGTH('silver.pessoa_origem','base_pessoa_origem_id') IS NULL
    THROW 51290,'Cutover v4 exige a migração Base_Pessoa_Origem aplicada.',1;
GO

UPDATE po
   SET base_pessoa_origem_id=sb.base_pessoa_origem_id
FROM silver.pessoa_origem po
JOIN ref.sistema_origem_base_pessoa sb
  ON sb.sistema_origem_id=po.sistema_origem_id
 AND sb.padrao=1
 AND sb.ativo=1
WHERE po.base_pessoa_origem_id IS NULL;
GO

IF EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE base_pessoa_origem_id IS NULL)
    THROW 51291,'Cutover v4 encontrou Pessoa de origem sem Base autorizada.',1;
GO

IF EXISTS(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='uq_pessoa_origem')
    ALTER TABLE silver.pessoa_origem DROP CONSTRAINT uq_pessoa_origem;
GO

ALTER TABLE silver.pessoa_origem
    ALTER COLUMN base_pessoa_origem_id BIGINT NOT NULL;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='uq_pessoa_origem_base_codigo')
    CREATE UNIQUE INDEX uq_pessoa_origem_base_codigo
        ON silver.pessoa_origem(base_pessoa_origem_id,codigo_pessoa_origem);
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Runtime v4 - cutover da Pessoa de origem
 ----------------------------------------
 Pré-requisitos: 20260913_Base_Pessoa_Origem.sql.
 Depois deste ponto toda LINHA EXISTENTE em silver.pessoa_origem possui Base explícita.
 Isto não obriga toda observação de Pessoa a possuir código/origem: quando a fonte não
 possui código local, silver.pessoa_observacao.pessoa_origem_id permanece NULL e nenhuma
 linha artificial é criada em silver.pessoa_origem. Para as origens que existem, a
 unicidade deixa de ser (sistema,codigo) e passa a ser (base,codigo).
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

/* O índice filtrado preparatório depende da coluna ainda anulável. Ele precisa sair
   antes do ALTER COLUMN e é recriado abaixo já como unicidade definitiva v4. */
IF EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='uq_pessoa_origem_base_codigo')
    DROP INDEX uq_pessoa_origem_base_codigo ON silver.pessoa_origem;
GO

ALTER TABLE silver.pessoa_origem
    ALTER COLUMN base_pessoa_origem_id BIGINT NOT NULL;
GO

CREATE UNIQUE INDEX uq_pessoa_origem_base_codigo
    ON silver.pessoa_origem(base_pessoa_origem_id,codigo_pessoa_origem);
GO


/* UUID_JORNADA é retroalimentação interna: vincula a observação a uma Pessoa já
   existente, sem criar identity_map externo e sem score/modelo probabilístico. */
IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte')
      AND name='ck_vinculo_metodo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_metodo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_metodo
 CHECK(metodo_resolucao IN(
    'CPF_DETERMINISTICO','UUID_JORNADA_RETROALIMENTACAO',
    'PENDENTE_PROBABILISTICO','LINKAGE_PROBABILISTICO',
    'CORRECAO_GOVERNADA','CONFLITO_GOVERNADO'));
GO

IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('identidade.vinculo_fonte')
      AND name='ck_vinculo_modelo')
    ALTER TABLE identidade.vinculo_fonte DROP CONSTRAINT ck_vinculo_modelo;
GO
ALTER TABLE identidade.vinculo_fonte WITH CHECK ADD CONSTRAINT ck_vinculo_modelo CHECK(
    (metodo_resolucao='CPF_DETERMINISTICO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='UUID_JORNADA_RETROALIMENTACAO' AND score IS NULL AND modelo_id IS NULL) OR
    (metodo_resolucao='PENDENTE_PROBABILISTICO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL) OR
    (metodo_resolucao='LINKAGE_PROBABILISTICO' AND score IS NOT NULL AND modelo_id IS NOT NULL) OR
    (metodo_resolucao='CORRECAO_GOVERNADA' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NOT NULL) OR
    (metodo_resolucao='CONFLITO_GOVERNADO' AND score IS NULL AND modelo_id IS NULL AND pessoa_uuid IS NULL));
GO

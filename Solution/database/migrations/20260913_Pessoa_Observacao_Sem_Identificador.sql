SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Observação de Pessoa sem identificador
 -------------------------------------
 Pessoa v4 permite zero identificadores. Portanto a observação Silver não pode
 depender da existência de silver.pessoa_origem nem de codigo_pessoa_origem.

 Isto NÃO cria chave sintética a partir de nome, nascimento, CPF ou hash.
 Quando a fonte possui código local estável, pessoa_origem_id e
 codigo_pessoa_origem continuam preenchidos e preservam a semântica histórica.
 Quando não possui, ambos permanecem NULL e a continuidade futura depende da
 resolução de identidade e/ou de um identificador posteriormente retroalimentado.
*/

IF COL_LENGTH('silver.pessoa_observacao','pessoa_origem_id') IS NOT NULL
BEGIN
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN pessoa_origem_id BIGINT NULL;
END;
GO

IF COL_LENGTH('silver.pessoa_observacao','codigo_pessoa_origem') IS NOT NULL
BEGIN
    ALTER TABLE silver.pessoa_observacao ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL;
END;
GO

IF OBJECT_ID('silver.pessoa_observacao','U') IS NOT NULL
   AND NOT EXISTS(
       SELECT 1
       FROM sys.check_constraints
       WHERE parent_object_id=OBJECT_ID('silver.pessoa_observacao')
         AND name='ck_pessoa_observacao_origem_coerente')
BEGIN
    ALTER TABLE silver.pessoa_observacao WITH CHECK
      ADD CONSTRAINT ck_pessoa_observacao_origem_coerente CHECK(
          (pessoa_origem_id IS NULL AND codigo_pessoa_origem IS NULL)
          OR
          (pessoa_origem_id IS NOT NULL AND codigo_pessoa_origem IS NOT NULL)
      );
END;
GO

/*
 Fatos continuam exigindo uma referência local estável ao registro de Pessoa no
 pacote/contrato factual vigente. A permissão de observação sem identificador não
 autoriza inventar codigoPessoaOrigem nem associar fatos por atributos mutáveis.
*/

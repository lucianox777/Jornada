SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Vínculo fato -> Pessoa sem exigir código local da Pessoa
 --------------------------------------------------------
 A relação obrigatória é silver.registro_observacao.pessoa_observacao_id.
 Os campos pessoa_origem_id/codigo_pessoa_origem em Gold/Serving são snapshots de
 proveniência e podem ser NULL quando a fonte não possui código estável de Pessoa.
 sistema_origem_id permanece obrigatório porque o fato sempre pertence a um sistema.
*/

ALTER TABLE gold.beneficio_concedido ALTER COLUMN pessoa_origem_id BIGINT NULL;
ALTER TABLE gold.beneficio_concedido ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN pessoa_origem_id BIGINT NULL;
ALTER TABLE gold.servico_prestado ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN pessoa_origem_id BIGINT NULL;
ALTER TABLE serving.registro_integrado ALTER COLUMN codigo_pessoa_origem NVARCHAR(255) NULL;
GO

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

/*
 item_processado audita a observação recebida. Para PESSOA, pessoa_origem_id é
 opcional pelo mesmo motivo: uma observação válida pode não possuir código local.
 codigo_origem continua obrigatório e, nesses casos, o Processor usa
 OBSERVACAO:<pessoa_observacao_id> apenas como chave de auditoria do lote.
*/
IF EXISTS(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID('ingestao.item_processado')
      AND name='ck_item_processado_origem')
    ALTER TABLE ingestao.item_processado DROP CONSTRAINT ck_item_processado_origem;
GO
ALTER TABLE ingestao.item_processado WITH CHECK
 ADD CONSTRAINT ck_item_processado_origem CHECK(
   (classe_item='PESSOA' AND registro_origem_id IS NULL) OR
   (classe_item='REGISTRO' AND pessoa_origem_id IS NULL AND registro_origem_id IS NOT NULL));
GO

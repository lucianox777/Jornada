SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
 Base de Pessoa de Origem
 -------------------------
 Separa o sistema que transmite/produz registros do namespace que atribui
 codigo_pessoa_origem. Uma mesma base pode ser autorizada para vários sistemas,
 inclusive de Gestores diferentes.

 Compatibilidade: cada sistema já existente recebe uma base PRIVADA própria,
 preservando o namespace anterior. Esta migração é preparatória: o runtime v1-v3
 ainda pode inserir pessoa_origem sem base explícita até o cutover do Processor v4.
 Nenhum compartilhamento intersistema é criado automaticamente.
*/

IF OBJECT_ID('ref.base_pessoa_origem','U') IS NULL
BEGIN
    CREATE TABLE ref.base_pessoa_origem(
        base_pessoa_origem_id BIGINT IDENTITY PRIMARY KEY,
        codigo NVARCHAR(120) NOT NULL,
        nome NVARCHAR(200) NOT NULL,
        gestor_custodiante_id BIGINT NULL REFERENCES ref.gestor(gestor_id),
        escopo NVARCHAR(20) NOT NULL CONSTRAINT DF_base_pessoa_origem_escopo DEFAULT('PRIVADA'),
        confianca_identidade NVARCHAR(40) NOT NULL CONSTRAINT DF_base_pessoa_origem_confianca DEFAULT('HOMOLOGADA_DETERMINISTICA'),
        ativo BIT NOT NULL CONSTRAINT DF_base_pessoa_origem_ativo DEFAULT(1),
        criado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_base_pessoa_origem_criado DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT uq_base_pessoa_origem_codigo UNIQUE(codigo),
        CONSTRAINT ck_base_pessoa_origem_codigo CHECK(LEN(codigo) BETWEEN 1 AND 120 AND codigo NOT LIKE '%[^A-Z0-9_-]%' COLLATE Latin1_General_100_BIN2),
        CONSTRAINT ck_base_pessoa_origem_escopo CHECK(escopo IN('PRIVADA','COMPARTILHADA')),
        CONSTRAINT ck_base_pessoa_origem_confianca CHECK(confianca_identidade IN('HOMOLOGADA_DETERMINISTICA','REFERENCIAL','NAO_HOMOLOGADA'))
    );
END;
GO

IF OBJECT_ID('ref.sistema_origem_base_pessoa','U') IS NULL
BEGIN
    CREATE TABLE ref.sistema_origem_base_pessoa(
        sistema_origem_id BIGINT NOT NULL REFERENCES ref.sistema_origem(sistema_origem_id),
        base_pessoa_origem_id BIGINT NOT NULL REFERENCES ref.base_pessoa_origem(base_pessoa_origem_id),
        padrao BIT NOT NULL CONSTRAINT DF_sistema_base_pessoa_padrao DEFAULT(0),
        ativo BIT NOT NULL CONSTRAINT DF_sistema_base_pessoa_ativo DEFAULT(1),
        autorizado_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_sistema_base_pessoa_autorizado DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT pk_sistema_origem_base_pessoa PRIMARY KEY(sistema_origem_id,base_pessoa_origem_id)
    );
    CREATE UNIQUE INDEX uq_sistema_origem_base_pessoa_padrao
        ON ref.sistema_origem_base_pessoa(sistema_origem_id)
        WHERE padrao=1 AND ativo=1;
END;
GO

/* Namespace institucional emitido pela própria Jornada.
   É criado sem autorização automática: um sistema só pode usá-lo após vínculo explícito.
   gestor_custodiante_id=NULL significa custódia da própria plataforma, não de uma Secretaria. */
IF NOT EXISTS(SELECT 1 FROM ref.base_pessoa_origem WHERE codigo='JORNADA')
BEGIN
    INSERT ref.base_pessoa_origem(codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
    VALUES('JORNADA','Identificador de Pessoa emitido pela Jornada',NULL,'COMPARTILHADA','HOMOLOGADA_DETERMINISTICA');
END;
GO

/* Uma base privada por sistema existente. O código deriva somente da PK técnica do
   sistema: é determinístico, globalmente único, curto e não depende do tamanho/grafia
   dos códigos de Gestor/Sistema. Os nomes humanos ficam apenas na descrição. */
INSERT ref.base_pessoa_origem(codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
SELECT CONCAT('SYS_',CONVERT(VARCHAR(20),s.sistema_origem_id)),
       CONCAT('Base privativa legada - ',g.codigo,' / ',s.codigo),
       s.gestor_id,'PRIVADA','HOMOLOGADA_DETERMINISTICA'
FROM ref.sistema_origem s
JOIN ref.gestor g ON g.gestor_id=s.gestor_id
WHERE NOT EXISTS(
    SELECT 1 FROM ref.base_pessoa_origem b
    WHERE b.codigo=CONCAT('SYS_',CONVERT(VARCHAR(20),s.sistema_origem_id)));
GO

INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao,ativo)
SELECT s.sistema_origem_id,b.base_pessoa_origem_id,1,1
FROM ref.sistema_origem s
JOIN ref.base_pessoa_origem b ON b.codigo=CONCAT('SYS_',CONVERT(VARCHAR(20),s.sistema_origem_id))
WHERE NOT EXISTS(
    SELECT 1 FROM ref.sistema_origem_base_pessoa sb
    WHERE sb.sistema_origem_id=s.sistema_origem_id
      AND sb.base_pessoa_origem_id=b.base_pessoa_origem_id);
GO

IF COL_LENGTH('silver.pessoa_origem','base_pessoa_origem_id') IS NULL
    ALTER TABLE silver.pessoa_origem ADD base_pessoa_origem_id BIGINT NULL;
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
    THROW 51270,'Não foi possível associar as Pessoas de origem existentes a uma Base de Pessoa de Origem.',1;
GO

IF NOT EXISTS(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID('silver.pessoa_origem')
      AND name='fk_pessoa_origem_base_pessoa')
    ALTER TABLE silver.pessoa_origem WITH CHECK
        ADD CONSTRAINT fk_pessoa_origem_base_pessoa
        FOREIGN KEY(base_pessoa_origem_id) REFERENCES ref.base_pessoa_origem(base_pessoa_origem_id);
GO

/* Pré-cutover: preserva uq_pessoa_origem(sistema_origem_id,codigo_pessoa_origem) e
   mantém base_pessoa_origem_id anulável para o runtime v1-v3. A unicidade do novo
   namespace vale para linhas já migradas e para o runtime v4 que informar a base.
   O cutover posterior pode tornar a coluna obrigatória somente depois de o Processor
   gravar a base explicitamente em todos os caminhos. */
IF NOT EXISTS(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID('silver.pessoa_origem') AND name='uq_pessoa_origem_base_codigo')
    CREATE UNIQUE INDEX uq_pessoa_origem_base_codigo
        ON silver.pessoa_origem(base_pessoa_origem_id,codigo_pessoa_origem)
        WHERE base_pessoa_origem_id IS NOT NULL;
GO

/* Uso efetivo conhecido no instante da migração: registra quais sistemas já observaram
   cada identidade de origem. Novas observações passam a ser registradas pelo runtime v4. */
IF OBJECT_ID('silver.pessoa_origem_sistema','U') IS NULL
BEGIN
    CREATE TABLE silver.pessoa_origem_sistema(
        pessoa_origem_id BIGINT NOT NULL REFERENCES silver.pessoa_origem(pessoa_origem_id),
        sistema_origem_id BIGINT NOT NULL REFERENCES ref.sistema_origem(sistema_origem_id),
        primeira_observacao_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_pessoa_origem_sistema_primeira DEFAULT(SYSDATETIMEOFFSET()),
        ultima_observacao_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_pessoa_origem_sistema_ultima DEFAULT(SYSDATETIMEOFFSET()),
        CONSTRAINT pk_pessoa_origem_sistema PRIMARY KEY(pessoa_origem_id,sistema_origem_id)
    );
END;
GO

MERGE silver.pessoa_origem_sistema AS t
USING (
    SELECT pessoa_origem_id,sistema_origem_id,criado_em
    FROM silver.pessoa_origem
    WHERE base_pessoa_origem_id IS NOT NULL
) AS s
ON t.pessoa_origem_id=s.pessoa_origem_id AND t.sistema_origem_id=s.sistema_origem_id
WHEN NOT MATCHED THEN
    INSERT(pessoa_origem_id,sistema_origem_id,primeira_observacao_em,ultima_observacao_em)
    VALUES(s.pessoa_origem_id,s.sistema_origem_id,s.criado_em,s.criado_em);
GO

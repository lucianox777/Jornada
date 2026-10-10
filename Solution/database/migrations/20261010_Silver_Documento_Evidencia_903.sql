SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- #903 fase 2. Evidencias documentais historicas por observacao Silver.
-- Depende da migracao 20261010_Ref_Catalogo_Documental_903.sql.
-- Uma evidencia e uma ocorrencia, nao um tipo nem um identificador unico.
IF OBJECT_ID(N'silver.documento_evidencia_observacao',N'U') IS NULL
BEGIN
 CREATE TABLE silver.documento_evidencia_observacao (
   documento_evidencia_id BIGINT IDENTITY(1,1) NOT NULL
     CONSTRAINT pk_documento_evidencia_observacao PRIMARY KEY,
   pessoa_observacao_id BIGINT NOT NULL,
   modelo_documento_id BIGINT NOT NULL,
   base_origem_codigo NVARCHAR(120) NOT NULL,
   ocorrencia_origem_codigo NVARCHAR(255) NOT NULL,
   classe_evidencia NVARCHAR(20) NOT NULL,
   data_evidencia DATE NULL,
   data_atendimento DATETIMEOFFSET(7) NULL,
   emissor_codigo NVARCHAR(120) NULL,
   uf_emissor CHAR(2) NULL,
   status_validacao NVARCHAR(20) NOT NULL
     CONSTRAINT df_documento_evidencia_status DEFAULT(N'NAO_VALIDADO'),
   capturado_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT df_documento_evidencia_capturado DEFAULT(SYSDATETIMEOFFSET()),
   CONSTRAINT fk_documento_evidencia_pessoa FOREIGN KEY(pessoa_observacao_id)
     REFERENCES silver.pessoa_observacao(pessoa_observacao_id),
   CONSTRAINT fk_documento_evidencia_modelo FOREIGN KEY(modelo_documento_id)
     REFERENCES ref.modelo_documento(modelo_documento_id),
   CONSTRAINT uq_documento_evidencia_origem UNIQUE(base_origem_codigo,ocorrencia_origem_codigo),
   CONSTRAINT uq_documento_evidencia_modelo UNIQUE(documento_evidencia_id,modelo_documento_id),
   CONSTRAINT ck_documento_evidencia_classe CHECK(classe_evidencia IN(N'DOCUMENTO',N'AUTODECLARACAO')),
   CONSTRAINT ck_documento_evidencia_status CHECK(status_validacao IN(N'VALIDO',N'INVALIDO',N'NAO_VALIDADO',N'CONFLITANTE')),
   CONSTRAINT ck_documento_evidencia_origem CHECK(LEN(LTRIM(RTRIM(base_origem_codigo)))>0
     AND LEN(LTRIM(RTRIM(ocorrencia_origem_codigo)))>0),
   CONSTRAINT ck_documento_evidencia_uf CHECK(uf_emissor IS NULL OR uf_emissor LIKE '[A-Z][A-Z]')
 );
 CREATE INDEX ix_documento_evidencia_pessoa
   ON silver.documento_evidencia_observacao(pessoa_observacao_id,classe_evidencia,data_evidencia);
END;
GO

-- Somente valores presentes e explicitamente admitidos no modelo.
-- A FK composta garante que o atributo pertence ao MESMO modelo da instancia.
IF OBJECT_ID(N'silver.documento_evidencia_valor',N'U') IS NULL
BEGIN
 CREATE TABLE silver.documento_evidencia_valor (
   documento_evidencia_id BIGINT NOT NULL,
   modelo_documento_id BIGINT NOT NULL,
   atributo_codigo NVARCHAR(80) NOT NULL,
   valor_original NVARCHAR(2000) NOT NULL,
   CONSTRAINT pk_documento_evidencia_valor
     PRIMARY KEY(documento_evidencia_id,atributo_codigo),
   CONSTRAINT fk_documento_evidencia_valor_instancia
     FOREIGN KEY(documento_evidencia_id,modelo_documento_id)
     REFERENCES silver.documento_evidencia_observacao(documento_evidencia_id,modelo_documento_id),
   CONSTRAINT fk_documento_evidencia_valor_cobertura
     FOREIGN KEY(modelo_documento_id,atributo_codigo)
     REFERENCES ref.modelo_documento_atributo(modelo_documento_id,atributo_codigo),
   CONSTRAINT ck_documento_evidencia_valor_nao_vazio CHECK(LEN(LTRIM(RTRIM(valor_original)))>0)
 );
END;
GO

-- Nenhuma data de atendimento e usada como data da evidencia.
-- Nenhuma coluna de data e repetida por atributo. Nao executa selecao Gold.

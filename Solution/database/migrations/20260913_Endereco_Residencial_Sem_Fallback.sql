SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('silver.endereco_residencial_geografia_observacao','U') IS NULL
BEGIN
 CREATE TABLE silver.endereco_residencial_geografia_observacao(
  endereco_residencial_geografia_observacao_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_endereco_residencial_geografia_observacao PRIMARY KEY,
  pessoa_atributo_observacao_id BIGINT NOT NULL CONSTRAINT UQ_endereco_residencial_geografia_atributo UNIQUE,
  subprefeitura_id BIGINT NULL, distrito_id BIGINT NULL,
  situacao_geografia NVARCHAR(30) NOT NULL, origem_geografia NVARCHAR(30) NOT NULL, referencia_malha NVARCHAR(120) NULL,
  resolvido_em DATETIMEOFFSET NULL, source_as_of DATETIMEOFFSET NOT NULL, criado_em DATETIMEOFFSET NOT NULL CONSTRAINT DF_endereco_residencial_geografia_criado DEFAULT SYSDATETIMEOFFSET(),
  CONSTRAINT FK_endereco_residencial_geografia_atributo FOREIGN KEY(pessoa_atributo_observacao_id) REFERENCES silver.pessoa_atributo_observacao(pessoa_atributo_observacao_id),
  CONSTRAINT FK_endereco_residencial_geografia_subpref FOREIGN KEY(subprefeitura_id) REFERENCES ref.subprefeitura(subprefeitura_id),
  CONSTRAINT FK_endereco_residencial_geografia_distrito FOREIGN KEY(distrito_id) REFERENCES ref.distrito(distrito_id),
  CONSTRAINT CK_endereco_residencial_geografia_situacao CHECK(situacao_geografia IN('RESOLVIDA','FORA_MUNICIPIO','SEM_ENDERECO_APTO','NAO_RESOLVIDA_ORIGEM')),
  CONSTRAINT CK_endereco_residencial_geografia_origem CHECK(origem_geografia='ORIGEM'),
  CONSTRAINT CK_endereco_residencial_geografia_resolvida CHECK((situacao_geografia='RESOLVIDA' AND subprefeitura_id IS NOT NULL AND distrito_id IS NOT NULL AND referencia_malha IS NOT NULL) OR (situacao_geografia<>'RESOLVIDA' AND subprefeitura_id IS NULL AND distrito_id IS NULL AND referencia_malha IS NULL))
 );
END;

INSERT silver.endereco_residencial_geografia_observacao(pessoa_atributo_observacao_id,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em,source_as_of)
SELECT rt.pessoa_atributo_observacao_id,rt.subprefeitura_id,rt.distrito_id,rt.situacao_geografia,'ORIGEM',rt.referencia_malha,rt.resolvido_em,pa.ingested_at
FROM silver.referencia_territorial_observacao rt
JOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id
WHERE rt.fonte_semantica='ENDERECO_RESIDENCIAL'
AND NOT EXISTS(SELECT 1 FROM silver.endereco_residencial_geografia_observacao eg WHERE eg.pessoa_atributo_observacao_id=rt.pessoa_atributo_observacao_id);
GO
CREATE OR ALTER VIEW silver.v_pessoa_geografia_residencial AS
SELECT po.pessoa_observacao_id,pa.pessoa_atributo_observacao_id,eg.endereco_residencial_geografia_observacao_id,eg.subprefeitura_id,eg.distrito_id,eg.situacao_geografia,eg.origem_geografia,eg.referencia_malha,eg.resolvido_em
FROM silver.pessoa_observacao po
OUTER APPLY(
 SELECT TOP(1) pa0.pessoa_atributo_observacao_id
 FROM silver.pessoa_atributo_observacao pa0
 WHERE pa0.pessoa_observacao_id=po.pessoa_observacao_id AND pa0.atributo_codigo='ENDERECO_RESIDENCIAL'
 ORDER BY COALESCE(pa0.referencia_evidencia,pa0.verificado_em,pa0.atualizado_em_origem,pa0.ingested_at) DESC,pa0.pessoa_atributo_observacao_id DESC
) x
LEFT JOIN silver.pessoa_atributo_observacao pa ON pa.pessoa_atributo_observacao_id=x.pessoa_atributo_observacao_id
LEFT JOIN silver.endereco_residencial_geografia_observacao eg ON eg.pessoa_atributo_observacao_id=pa.pessoa_atributo_observacao_id;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema') EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71'; ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';
COMMIT;

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
IF COL_LENGTH('gold.beneficio_concedido','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('gold.beneficio_concedido','subprefeitura_residencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.beneficio_concedido','distrito_residencia_id') IS NULL ALTER TABLE gold.beneficio_concedido ADD distrito_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE gold.servico_prestado ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','subprefeitura_residencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('gold.servico_prestado','distrito_residencia_id') IS NULL ALTER TABLE gold.servico_prestado ADD distrito_residencia_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','endereco_residencial_geografia_observacao_id') IS NULL ALTER TABLE serving.registro_integrado ADD endereco_residencial_geografia_observacao_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','subprefeitura_residencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD subprefeitura_residencia_id BIGINT NULL;
IF COL_LENGTH('serving.registro_integrado','distrito_residencia_id') IS NULL ALTER TABLE serving.registro_integrado ADD distrito_residencia_id BIGINT NULL;
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
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM gold.beneficio_concedido f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM gold.servico_prestado f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
UPDATE f SET endereco_residencial_geografia_observacao_id=g.endereco_residencial_geografia_observacao_id,subprefeitura_residencia_id=g.subprefeitura_id,distrito_residencia_id=g.distrito_id
FROM serving.registro_integrado f JOIN silver.registro_observacao ro ON ro.registro_observacao_id=f.registro_observacao_id JOIN silver.v_pessoa_geografia_residencial g ON g.pessoa_observacao_id=ro.pessoa_observacao_id
WHERE f.endereco_residencial_geografia_observacao_id IS NULL;
GO
CREATE OR ALTER VIEW serving.v_bi_linkage AS
SELECT po.pessoa_observacao_id,g.codigo gestor,so.codigo sistema_origem,po.source_as_of,vc.status,vc.metodo_resolucao,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' THEN vc.score END score,
       CASE WHEN vc.pessoa_uuid IS NULL THEN 0 ELSE 1 END possui_uuid,
       CASE WHEN vc.metodo_resolucao='CPF_DETERMINISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_cpf,
       CASE WHEN vc.metodo_resolucao='LINKAGE_PROBABILISTICO' AND vc.status='RESOLVIDO' THEN 1 ELSE 0 END resolvido_probabilistico,
       CASE WHEN vc.status='NAO_RESOLVIDO' THEN 1 ELSE 0 END nao_resolvido,
       CASE WHEN vc.status='CONFLITO' THEN 1 ELSE 0 END conflito,
       CASE WHEN lrres.motivo='SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO' THEN 1 ELSE 0 END sem_candidato_no_bloco,
       lrres.motivo motivo_linkage,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,po.cpf_ausente_motivo,
       CASE WHEN DATEPART(DAY,po.data_nascimento)=1 AND DATEPART(MONTH,po.data_nascimento)=1 THEN 1 ELSE 0 END nascimento_0101,
       CASE WHEN rg.subprefeitura_id IS NULL OR rg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_residencia_preenchida,
       ml.versao modelo_linkage_versao,vc.linkage_run_id,lr.tipo_run,lr.iniciado_em linkage_run_iniciado_em,
       lr.finalizado_em linkage_run_finalizado_em,ptl.valor t_linkage,lr.status linkage_run_status,
       COALESCE(sp.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') subprefeitura_residencia,
       COALESCE(d.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') distrito_residencia
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id
JOIN ref.sistema_origem so ON so.sistema_origem_id=pori.sistema_origem_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN identidade.modelo_linkage ml ON ml.modelo_id=vc.modelo_id
LEFT JOIN identidade.linkage_run lr ON lr.linkage_run_id=vc.linkage_run_id
LEFT JOIN identidade.parametro_linkage ptl ON ptl.modelo_id=vc.modelo_id AND ptl.nome='T_LINKAGE'
LEFT JOIN identidade.linkage_resultado lrres ON lrres.linkage_run_id=vc.linkage_run_id AND lrres.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=rg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=rg.distrito_id;
GO
CREATE OR ALTER VIEW serving.v_bi_qualidade_pessoa AS
SELECT po.pessoa_observacao_id,g.codigo gestor,po.source_as_of,
       COALESCE(sp.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') subprefeitura_residencia,
       COALESCE(d.nome,'SEM_ENDERECO_RESIDENCIAL_RESOLVIDO') distrito_residencia,
       rg.situacao_geografia,rg.referencia_malha,
       CASE WHEN po.cpf IS NULL THEN 0 ELSE 1 END cpf_preenchido,
       CASE WHEN LEN(LTRIM(RTRIM(po.nome_completo)))>0 THEN 1 ELSE 0 END nome_preenchido,
       CASE WHEN po.data_nascimento IS NULL THEN 0 ELSE 1 END nascimento_preenchido,
       CASE WHEN LEN(LTRIM(RTRIM(po.nome_mae)))>0 THEN 1 ELSE 0 END nome_mae_preenchido,
       CASE WHEN rg.subprefeitura_id IS NULL OR rg.distrito_id IS NULL THEN 0 ELSE 1 END geografia_residencia_preenchida,
       po.cpf_ausente_motivo,gp.status_cpf
FROM silver.pessoa_observacao po
JOIN ref.gestor g ON g.gestor_id=po.gestor_id
LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN gold.pessoa gp ON gp.pessoa_uuid=vc.pessoa_uuid
LEFT JOIN silver.v_pessoa_geografia_residencial rg ON rg.pessoa_observacao_id=po.pessoa_observacao_id
LEFT JOIN ref.subprefeitura sp ON sp.subprefeitura_id=rg.subprefeitura_id
LEFT JOIN ref.distrito d ON d.distrito_id=rg.distrito_id;
GO
CREATE OR ALTER VIEW serving.v_bi_qualidade_identidade_origem AS
SELECT l.* FROM serving.v_bi_linkage l;
GO
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
 EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';
ELSE EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema',@value=N'3.71';
COMMIT;

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Massa sintética determinística da arquitetura v3.37, com Referência Territorial como fonte geográfica única da visualização.
-- O contrato externo usa envelope único: manifest.json + pessoas.jsonl + registros.jsonl; registros.jsonl pode estar vazio.
-- A fonte envia chaves estáveis, não números de versão; a Jornada versiona internamente por hash de conteúdo.
-- ingestao.lote existe somente como detalhe técnico interno do Processor.

MERGE ref.gestor AS t USING (VALUES
 ('SEHAB','Secretaria Municipal de Habitação'),
 ('SMADS','Secretaria Municipal de Assistência e Desenvolvimento Social'),
 ('SMDET','Secretaria Municipal de Desenvolvimento Econômico e Trabalho'),
 ('SMS','Secretaria Municipal da Saúde')) AS s(codigo,nome)
ON t.codigo=s.codigo WHEN NOT MATCHED THEN INSERT(codigo,nome) VALUES(s.codigo,s.nome);

IF NOT EXISTS(SELECT 1 FROM ref.subprefeitura WHERE codigo='SE' AND nome=N'Sé')
 INSERT ref.subprefeitura(codigo,nome,observado_em) VALUES('SE',N'Sé','2026-01-01T00:00:00-03:00');
IF NOT EXISTS(SELECT 1 FROM ref.subprefeitura WHERE codigo='ITAQ' AND nome=N'Itaquera')
 INSERT ref.subprefeitura(codigo,nome,observado_em) VALUES('ITAQ',N'Itaquera','2026-01-01T00:00:00-03:00');
DECLARE @spSe BIGINT=(SELECT TOP(1) subprefeitura_id FROM ref.subprefeitura WHERE codigo='SE' AND nome=N'Sé' ORDER BY subprefeitura_id DESC),
        @spI BIGINT=(SELECT TOP(1) subprefeitura_id FROM ref.subprefeitura WHERE codigo='ITAQ' AND nome=N'Itaquera' ORDER BY subprefeitura_id DESC);
IF NOT EXISTS(SELECT 1 FROM ref.distrito WHERE codigo='SE' AND nome=N'Sé' AND subprefeitura_id=@spSe)
 INSERT ref.distrito(subprefeitura_id,codigo,nome,observado_em) VALUES(@spSe,'SE',N'Sé','2026-01-01T00:00:00-03:00');
IF NOT EXISTS(SELECT 1 FROM ref.distrito WHERE codigo='ITAQUERA' AND nome=N'Itaquera' AND subprefeitura_id=@spI)
 INSERT ref.distrito(subprefeitura_id,codigo,nome,observado_em) VALUES(@spI,'ITAQUERA',N'Itaquera','2026-01-01T00:00:00-03:00');
DECLARE @dSe BIGINT=(SELECT TOP(1) distrito_id FROM ref.distrito WHERE codigo='SE' AND nome=N'Sé' AND subprefeitura_id=@spSe ORDER BY distrito_id DESC),
        @dI BIGINT=(SELECT TOP(1) distrito_id FROM ref.distrito WHERE codigo='ITAQUERA' AND nome=N'Itaquera' AND subprefeitura_id=@spI ORDER BY distrito_id DESC);
-- As dimensões acima representam versões observadas da hierarquia Distrito -> Subprefeitura.
-- O snapshot da classificação fica associado à observação do ENDERECO_RESIDENCIAL; não existe entidade Território de Referência.
DECLARE @gSehab BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SEHAB'),
        @gSmads BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMADS'),
        @gSmdet BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMDET'),
        @gSms BIGINT=(SELECT gestor_id FROM ref.gestor WHERE codigo='SMS');

MERGE ref.sistema_origem AS t USING (VALUES
 (@gSehab,'SEHAB',N'Sistema finalístico de Habitação'),
 (@gSmads,'ASSISTENCIA',N'Sistema finalístico de Assistência'),
 (@gSmdet,'TRABALHO',N'Sistema finalístico de Trabalho'),
 (@gSms,'SAUDE',N'Sistema finalístico de Saúde')) AS s(gestor_id,codigo,nome)
ON t.gestor_id=s.gestor_id AND t.codigo=s.codigo
WHEN NOT MATCHED THEN INSERT(gestor_id,codigo,nome) VALUES(s.gestor_id,s.codigo,s.nome);
DECLARE @soSehab BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSehab AND codigo='SEHAB'),
        @soSmads BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmads AND codigo='ASSISTENCIA'),
        @soSmdet BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSmdet AND codigo='TRABALHO'),
        @soSms BIGINT=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@gSms AND codigo='SAUDE');

-- Código do Tipo: exatamente 4 caracteres alfanuméricos A-Z/0-9.
MERGE ref.tipo_registro AS t USING (VALUES
 (@gSehab,'BENEFICIO','AA01','Auxílio Aluguel'),
 (@gSmads,'BENEFICIO','AR01','Auxílio Reencontro'),
 (@gSmdet,'BENEFICIO','POT1','Programa Operação Trabalho'),
 (@gSmads,'SERVICO','CRA1','Serviço CRAS'),
 (@gSmads,'SERVICO','CPO1','Serviço Centro POP'),
 (@gSmads,'SERVICO','CAS1','Serviço Casa-Abrigo Sigilosa')) AS s(gestor_id,natureza,codigo,nome)
ON t.codigo=s.codigo
WHEN NOT MATCHED THEN INSERT(gestor_id,natureza,codigo,nome) VALUES(s.gestor_id,s.natureza,s.codigo,s.nome);

DECLARE @aa BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='AA01'),
        @ar BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='AR01'),
        @pot BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='POT1'),
        @cras BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='CRA1'),
        @cpop BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='CPO1'),
        @cas1 BIGINT=(SELECT tipo_registro_id FROM ref.tipo_registro WHERE codigo='CAS1');

DECLARE @tipos TABLE(tipo_registro_id BIGINT,regras NVARCHAR(200),schema_pessoa NVARCHAR(255),schema_pessoa_hash BINARY(32),schema_registro NVARCHAR(255),schema_registro_hash BINARY(32),tipo_medida NVARCHAR(30),qc NVARCHAR(20),regime_vigencia NVARCHAR(30),data_inicio_permitida_concessao DATE,data_fim_permitida_concessao DATE,monitorar_atraso BIT,prazo_recebimento_dias INT,marco_atraso_codigo NVARCHAR(30),origina_endereco_casa_abrigo_sigilosa BIT);
INSERT @tipos VALUES
(@aa,'Regras sintéticas AA01.','config/contracts/registros/AA01/v1/pessoa.schema.json',0x30f12102f5a6043f092f3af601ded82bc4e7268c3fdb34d326a91d2e547f5e94,'config/contracts/registros/AA01/v1/registro.schema.json',0xcfb1de16dd73839dba6982f92ee0abe778bd67f4de2c79f9be5cb4b08f291da4,'MONETARIO','IMPLEMENTADO','PRAZO_INDETERMINADO',NULL,NULL,1,7,'DATA_EVENTO_CONCESSAO',0),
(@ar,'Regras sintéticas AR01.','config/contracts/registros/AR01/v1/pessoa.schema.json',0xcfc653b792de1d56d9748ff939ef78b0849ad901f607baddc8559b4b2dcdfa41,'config/contracts/registros/AR01/v1/registro.schema.json',0xfb25b6bd16596e817a667d39ecae933de90362fa5a0d7cd014f41f034f8c446d,'MONETARIO','NAO_IMPLEMENTADO','PRAZO_INDETERMINADO',NULL,NULL,1,7,'DATA_EVENTO_CONCESSAO',0),
(@pot,'Regras sintéticas POT1.','config/contracts/registros/POT1/v1/pessoa.schema.json',0xefe204199e2f27e461f1436c079c2055167e03432cad244a6fd6c3b2f84f6614,'config/contracts/registros/POT1/v1/registro.schema.json',0xe9dea336183debb4576952e39f6110e7e72f10df27971428d1e3d223359b510c,'MONETARIO','IMPLEMENTADO','PRAZO_INDETERMINADO',NULL,NULL,1,7,'DATA_EVENTO_CONCESSAO',0),
(@cras,'Regras sintéticas CRA1.','config/contracts/registros/CRA1/v1/pessoa.schema.json',0x4d715b2cc259f5dd5869a6d50c25f84cbbc390bace6721c663fd3afb2d0d6edc,'config/contracts/registros/CRA1/v1/registro.schema.json',0xfe10e8f81ff29b90379239540f7a94759febba5b2c6be1863e3953cd4dc2da95,'SEM_MEDIDA','NAO_IMPLEMENTADO','NAO_APLICAVEL',NULL,NULL,1,7,'DATA_HORA_SERVICO',0),
(@cpop,'Regras sintéticas CPO1.','config/contracts/registros/CPO1/v1/pessoa.schema.json',0x06ae0e7812be6666b3b1630547b057b48c0bfa0777d7cfc525f970c6b7e036c5,'config/contracts/registros/CPO1/v1/registro.schema.json',0xadc382a7602c0b556ea752422c151735e4b54b7b66cdf749d000cf88e76b3312,'SEM_MEDIDA','NAO_IMPLEMENTADO','NAO_APLICAVEL',NULL,NULL,1,7,'DATA_HORA_SERVICO',0),
(@cas1,'Regras sintéticas CAS1 - casa-abrigo-sigilosa.','config/contracts/registros/CAS1/v1/pessoa.schema.json',0x5de1477d65cfdfd19e7b24771c8bc9a791d18fdc22813ee60d4ea73a8fdec0c8,'config/contracts/registros/CAS1/v1/registro.schema.json',0x392fb177f9ecb075375632916b55f84033759113bb4d2256f3e7d718bbb0ba18,'SEM_MEDIDA','NAO_IMPLEMENTADO','NAO_APLICAVEL',NULL,NULL,0,NULL,NULL,1);
INSERT ref.tipo_registro_versao(tipo_registro_id,versao,vigencia_inicio,regras_texto,schema_pessoa_ref,schema_pessoa_sha256,schema_registro_ref,schema_registro_sha256,tipo_medida,regime_vigencia,data_inicio_permitida_concessao,data_fim_permitida_concessao,monitorar_atraso,prazo_recebimento_dias,marco_atraso_codigo,qc_status,origina_endereco_casa_abrigo_sigilosa,status)
SELECT x.tipo_registro_id,1,'2026-01-01',x.regras,x.schema_pessoa,x.schema_pessoa_hash,x.schema_registro,x.schema_registro_hash,x.tipo_medida,x.regime_vigencia,x.data_inicio_permitida_concessao,x.data_fim_permitida_concessao,x.monitorar_atraso,x.prazo_recebimento_dias,x.marco_atraso_codigo,x.qc,x.origina_endereco_casa_abrigo_sigilosa,'ATIVA'
FROM @tipos x WHERE NOT EXISTS(SELECT 1 FROM ref.tipo_registro_versao v WHERE v.tipo_registro_id=x.tipo_registro_id AND v.versao=1);
UPDATE v SET schema_pessoa_sha256=x.schema_pessoa_hash,schema_registro_sha256=x.schema_registro_hash,
 regime_vigencia=x.regime_vigencia,data_inicio_permitida_concessao=x.data_inicio_permitida_concessao,data_fim_permitida_concessao=x.data_fim_permitida_concessao,
 monitorar_atraso=x.monitorar_atraso,prazo_recebimento_dias=x.prazo_recebimento_dias,marco_atraso_codigo=x.marco_atraso_codigo,origina_endereco_casa_abrigo_sigilosa=x.origina_endereco_casa_abrigo_sigilosa
FROM ref.tipo_registro_versao v JOIN @tipos x ON x.tipo_registro_id=v.tipo_registro_id WHERE v.versao=1;
DECLARE @aav BIGINT=(SELECT tipo_registro_versao_id FROM ref.tipo_registro_versao WHERE tipo_registro_id=@aa AND versao=1),
        @arv BIGINT=(SELECT tipo_registro_versao_id FROM ref.tipo_registro_versao WHERE tipo_registro_id=@ar AND versao=1),
        @potv BIGINT=(SELECT tipo_registro_versao_id FROM ref.tipo_registro_versao WHERE tipo_registro_id=@pot AND versao=1),
        @crv BIGINT=(SELECT tipo_registro_versao_id FROM ref.tipo_registro_versao WHERE tipo_registro_id=@cras AND versao=1);

MERGE ref.atributo_transversal AS t USING (VALUES
 ('ENDERECO_RESIDENCIAL','Endereço residencial','ENDERECO_CANONICO_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','SINGLE','UNICA_V1'),
 ('ENDERECO_CASA_ABRIGO_SIGILOSA','Endereço de casa-abrigo-sigilosa','ENDERECO_CANONICO_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','SINGLE','UNICA_V1'),
 ('REFERENCIA_TERRITORIAL','Referência territorial da Pessoa','REFERENCIA_TERRITORIAL_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','SINGLE','UNICA_V1'),
 ('TELEFONE_CONTATO','Telefone de contato','TELEFONE_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','MULTI','TELEFONE_BR_CANONICO_V2'),
 ('EMAIL_CONTATO','E-mail de contato','EMAIL_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','MULTI','EMAIL_CANONICO_V2'),
 ('NOME_SOCIAL','Nome social','TEXTO_UTF8_V1','REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1','SINGLE','UNICA_V1')) AS s(atributo_codigo,nome,formato_codigo,regra_temporal_codigo,cardinalidade,chave_instancia_codigo)
ON t.atributo_codigo=s.atributo_codigo
WHEN MATCHED THEN UPDATE SET nome=s.nome,formato_codigo=s.formato_codigo,regra_temporal_codigo=s.regra_temporal_codigo,cardinalidade=s.cardinalidade,chave_instancia_codigo=s.chave_instancia_codigo
WHEN NOT MATCHED THEN INSERT(atributo_codigo,nome,formato_codigo,regra_temporal_codigo,cardinalidade,chave_instancia_codigo) VALUES(s.atributo_codigo,s.nome,s.formato_codigo,s.regra_temporal_codigo,s.cardinalidade,s.chave_instancia_codigo);

-- Contratos cadastrais versionados por Gestor.
INSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)
SELECT g.gestor_id,1,'2026-01-01',CONCAT('config/contracts/gestores/',g.codigo,'/pessoa/v1/pessoa.schema.json'),
       CASE g.codigo WHEN 'SEHAB' THEN 0x5de6ddecfe95db8575ed321b83fb0eb1801cf3f6767406701f3146d0f474fd44 WHEN 'SMADS' THEN 0xa382a796a9b08b68bad09854b59fad6d64e6f0280812bfc604266bf6845f41f1 WHEN 'SMDET' THEN 0xdae81766352934486e1d1ef319261911d549fe987817e10f38fd5e0aa6bf4743 WHEN 'SMS' THEN 0xa3eee657504e51e0d54960b1e87dd24450b1a3d70f4b6ae9a5647c104b39e9a0 END,
       'ATIVA','2026-08-27'
FROM ref.gestor g WHERE g.codigo IN('SMS','SEHAB','SMADS','SMDET')
AND NOT EXISTS(SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=1);
UPDATE v SET pessoa_schema_sha256=CASE g.codigo WHEN 'SEHAB' THEN 0x5de6ddecfe95db8575ed321b83fb0eb1801cf3f6767406701f3146d0f474fd44 WHEN 'SMADS' THEN 0xa382a796a9b08b68bad09854b59fad6d64e6f0280812bfc604266bf6845f41f1 WHEN 'SMDET' THEN 0xdae81766352934486e1d1ef319261911d549fe987817e10f38fd5e0aa6bf4743 WHEN 'SMS' THEN 0xa3eee657504e51e0d54960b1e87dd24450b1a3d70f4b6ae9a5647c104b39e9a0 END FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id WHERE v.versao=1 AND g.codigo IN('SMS','SEHAB','SMADS','SMDET');
DECLARE @gpvSehab BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSehab AND versao=1),
        @gpvSmads BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSmads AND versao=1),
        @gpvSms BIGINT=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@gSms AND versao=1);

-- Credenciais no banco guardam somente secret_ref. Os IDs abaixo são os mesmos da fixture
-- config/security/test-access-keys.json para que toda chamada DEV possa ser auditada por FK.
DECLARE @credSehab UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111111',
        @credSmads UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111112',
        @credAa UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111113',
        @credCras UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111114',
        @credSms UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111115',
        @credSmdet UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111116',
        @credAr UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111117',
        @credPot UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111118',
        @credCpop UNIQUEIDENTIFIER='11111111-1111-4111-8111-111111111119';
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credSehab)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,codigo_publico,secret_ref,scopes) VALUES(@credSehab,'GESTOR',@gSehab,'SEHAB','secretRef:DEV_SEHAB_KEY','jornada.identidade.resolve jornada.identidade.conflitos.read jornada.identidade.corrigir jornada.ingestao.write jornada.ingestao.status jornada.pessoas.read jornada.registros.read jornada.possibilidades.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credSmads)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,codigo_publico,secret_ref,scopes) VALUES(@credSmads,'GESTOR',@gSmads,'SMADS','secretRef:DEV_SMADS_KEY','jornada.identidade.resolve jornada.identidade.conflitos.read jornada.identidade.corrigir jornada.ingestao.write jornada.ingestao.status jornada.pessoas.read jornada.registros.read jornada.possibilidades.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credSmdet)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,codigo_publico,secret_ref,scopes) VALUES(@credSmdet,'GESTOR',@gSmdet,'SMDET','secretRef:DEV_SMDET_KEY','jornada.identidade.resolve jornada.identidade.conflitos.read jornada.identidade.corrigir jornada.ingestao.write jornada.ingestao.status jornada.pessoas.read jornada.registros.read jornada.possibilidades.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credSms)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,codigo_publico,secret_ref,scopes) VALUES(@credSms,'GESTOR',@gSms,'SMS','secretRef:DEV_SMS_KEY','jornada.identidade.resolve jornada.identidade.conflitos.read jornada.identidade.corrigir jornada.ingestao.write jornada.ingestao.status jornada.pessoas.read jornada.registros.read jornada.possibilidades.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credAa)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,tipo_registro_id,codigo_publico,secret_ref,scopes) VALUES(@credAa,'BENEFICIO',@gSehab,@aa,'AA01','secretRef:DEV_AA01_KEY','jornada.pessoas.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credAr)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,tipo_registro_id,codigo_publico,secret_ref,scopes) VALUES(@credAr,'BENEFICIO',@gSmads,@ar,'AR01','secretRef:DEV_AR01_KEY','jornada.pessoas.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credPot)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,tipo_registro_id,codigo_publico,secret_ref,scopes) VALUES(@credPot,'BENEFICIO',@gSmdet,@pot,'POT1','secretRef:DEV_POT1_KEY','jornada.pessoas.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credCras)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,tipo_registro_id,codigo_publico,secret_ref,scopes) VALUES(@credCras,'SERVICO',@gSmads,@cras,'CRA1','secretRef:DEV_CRA1_KEY','jornada.pessoas.read');
IF NOT EXISTS(SELECT 1 FROM controle.credencial_api WHERE credencial_id=@credCpop)
 INSERT controle.credencial_api(credencial_id,tipo_credencial,gestor_id,tipo_registro_id,codigo_publico,secret_ref,scopes) VALUES(@credCpop,'SERVICO',@gSmads,@cpop,'CPO1','secretRef:DEV_CPO1_KEY','jornada.pessoas.read');
-- Evolução de seed: credenciais institucionais existentes recebem os scopes administrativos de identidade.
UPDATE controle.credencial_api SET scopes='jornada.identidade.resolve jornada.identidade.conflitos.read jornada.identidade.corrigir jornada.ingestao.write jornada.ingestao.status jornada.pessoas.read jornada.registros.read jornada.possibilidades.read' WHERE credencial_id IN(@credSehab,@credSmads,@credSmdet,@credSms);

-- Pessoas Golden sintéticas iniciais: o seed prepara corpus para demonstrar CPF determinístico e fallback probabilístico.
DECLARE @u1 UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
        @u2 UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
        @u3 UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3',
        @u4 UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa4',
        @u5 UNIQUEIDENTIFIER='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa5';
INSERT identidade.pessoa(pessoa_uuid,status)
SELECT v.id,'ATIVO' FROM (VALUES(@u1),(@u2),(@u3),(@u4),(@u5)) v(id)
WHERE NOT EXISTS(SELECT 1 FROM identidade.pessoa p WHERE p.pessoa_uuid=v.id);
INSERT identidade.identity_map(pessoa_uuid,tipo,identificador,vigencia_inicio,gestor_origem_id,source_record_id,metodo_resolucao)
SELECT v.id,'CPF',v.cpf,'2026-01-01',@gSms,CONCAT('BOOT-',v.cpf),'CPF_DETERMINISTICO'
FROM (VALUES(@u1,'11144477735'),(@u2,'52998224725'),(@u3,'12345678909'),(@u4,'39053344705')) v(id,cpf)
WHERE NOT EXISTS(SELECT 1 FROM identidade.identity_map m WHERE m.tipo='CPF' AND m.identificador=v.cpf AND m.vigencia_fim IS NULL);
INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
SELECT v.id,v.cpf,'PRESENTE',v.nome,v.nasc,v.mae,1,'BASELINE_FONTE_UNICA','2026-08-27T08:05:00-03:00'
FROM (VALUES
(@u1,'11144477735',N'Maria da Silva',CAST('1982-04-10' AS date),N'Ana de Souza'),
(@u2,'52998224725',N'João de Souza',CAST('1977-09-22' AS date),N'Maria de Souza'),
(@u3,'12345678909',N'Mariana Albuquerque',CAST('1988-12-05' AS date),N'Teresa Albuquerque'),
(@u4,'39053344705',N'Paulo Ferreira',CAST('1970-07-07' AS date),N'Rita Ferreira')) v(id,cpf,nome,nasc,mae)
WHERE NOT EXISTS(SELECT 1 FROM gold.pessoa p WHERE p.pessoa_uuid=v.id);

DECLARE @model UNIQUEIDENTIFIER='44444444-4444-4444-8444-444444444444';
IF NOT EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE modelo_id=@model)
 INSERT identidade.modelo_linkage(modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,base_referencia,snapshot_referencia,registros_lidos,pessoas_unicas,gerado_em,ativado_em,snapshot_capturado_em,amostra_metodo,amostra_pool_tamanho,amostra_m_tamanho,amostra_u_tamanho)
 VALUES(@model,1,'ATIVO','FELLEGI_SUNTER_ANCHORED_V1','IDENTITY_NORMALIZATION_V1','GOLD_PESSOA_UUID_PK','gold.pessoa','gold.pessoa seed snapshot',4,4,'2026-08-27T08:10:00-03:00','2026-08-27T08:10:00-03:00','2026-08-27T11:10:00','SEED_DEV_FIXO_NAO_TREINADO',4,4,4);
IF NOT EXISTS(SELECT 1 FROM identidade.estatistica_linkage WHERE modelo_id=@model)
 INSERT identidade.estatistica_linkage(modelo_id,nome,valor,metodo) VALUES
 (@model,'POPULATION_SIZE',4,'COUNT_BIG'),(@model,'POPULATION_WITH_CPF',4,'SUM_CASE'),(@model,'DISTINCT_FULL_NAME',4,'APPROX_COUNT_DISTINCT'),(@model,'DISTINCT_MOTHER_NAME',4,'APPROX_COUNT_DISTINCT'),(@model,'DISTINCT_BIRTH_DATE',4,'APPROX_COUNT_DISTINCT');
IF NOT EXISTS(SELECT 1 FROM identidade.parametro_linkage WHERE modelo_id=@model)
 INSERT identidade.parametro_linkage(modelo_id,nome,valor) VALUES
 (@model,'POPULATION_SIZE',4),(@model,'POPULATION_WITH_CPF',4),(@model,'DISTINCT_BIRTH_DATE',4),(@model,'TRAINING_SAMPLE_POOL_SIZE',4),(@model,'MIN_M_INDEPENDENT_PAIRS',4),(@model,'M_SAMPLE_SIZE',4),(@model,'U_SAMPLE_SIZE',4),(@model,'SMOOTHING_ALPHA',0.5),(@model,'PRIOR_MATCH_PROBABILITY',0.10),(@model,'PRIOR_BLOCK_MIN',0.000001),(@model,'PRIOR_BLOCK_MAX',0.25),(@model,'T_LINKAGE',0.85),(@model,'CONFLICT_MARGIN',0.03),(@model,'BLOCKING_EXACT_BIRTH_DATE',1),(@model,'M_NOME_EXACT',0.70),(@model,'M_NOME_HIGH',0.20),(@model,'M_NOME_MEDIUM',0.08),(@model,'M_NOME_LOW',0.02),(@model,'U_NOME_EXACT',0.01),(@model,'U_NOME_HIGH',0.03),(@model,'U_NOME_MEDIUM',0.16),(@model,'U_NOME_LOW',0.80),(@model,'M_NOME_MAE_EXACT',0.75),(@model,'M_NOME_MAE_HIGH',0.15),(@model,'M_NOME_MAE_MEDIUM',0.08),(@model,'M_NOME_MAE_LOW',0.02),(@model,'U_NOME_MAE_EXACT',0.01),(@model,'U_NOME_MAE_HIGH',0.03),(@model,'U_NOME_MAE_MEDIUM',0.16),(@model,'U_NOME_MAE_LOW',0.80),(@model,'M_DATA_NASCIMENTO_EXACT',0.90),(@model,'U_DATA_NASCIMENTO_EXACT',0.05);

-- Entrega cadastral: SMS pode contribuir sem contexto factual; registros.jsonl estaria vazio.
DECLARE @entPessoa UNIQUEIDENTIFIER='10000000-0000-4000-8000-000000000001', @lotPessoa UNIQUEIDENTIFIER='10000000-0000-4000-8000-000000000002';
IF NOT EXISTS(SELECT 1 FROM ingestao.entrega WHERE entrega_id=@entPessoa)
 INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
 VALUES(@entPessoa,@gSms,@soSms,@gpvSms,NULL,NULL,NULL,'dev-entrega-pessoa-sms-1','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'PROCESSADA','2026-08-27T00:00:00-03:00','2026-08-27T11:00:00+00:00','2026-08-27T08:03:00-03:00');
IF NOT EXISTS(SELECT 1 FROM bronze.entrega_arquivo WHERE entrega_id=@entPessoa)
 INSERT bronze.entrega_arquivo(entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em) VALUES(@entPessoa,'ENTREGA_SMS_SAUDE_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','application/zip','sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'2026-08-27T11:00:00+00:00');
IF NOT EXISTS(SELECT 1 FROM ingestao.lote WHERE lote_id=@lotPessoa)
 INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em) VALUES(@lotPessoa,@entPessoa,1,1,2,0,'PROCESSADO','2026-08-27T08:00:01-03:00','2026-08-27T08:03:00-03:00');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSms AND codigo_pessoa_origem='SMS001') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSms,'SMS001');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSms AND codigo_pessoa_origem='SMS002') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSms,'SMS002');
DECLARE @smsO1 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSms AND codigo_pessoa_origem='SMS001'),
        @smsO2 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSms AND codigo_pessoa_origem='SMS002');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_observacao WHERE lote_id=@lotPessoa)
 INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of) VALUES
 (@smsO1,@lotPessoa,@gSms,'SMS001',1,REPLICATE('1',64),'11144477735',NULL,N'Maria da Silva',N'MARIA DA SILVA','1982-04-10',N'Ana de Souza',N'ANA DE SOUZA','2026-08-27T00:00:00-03:00'),
 (@smsO2,@lotPessoa,@gSms,'SMS002',1,REPLICATE('2',64),'52998224725',NULL,N'João de Souza',N'JOAO DE SOUZA','1977-09-22',N'Maria de Souza',N'MARIA DE SOUZA','2026-08-27T00:00:00-03:00');
DECLARE @smsP1 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotPessoa AND codigo_pessoa_origem='SMS001'),
        @smsP2 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotPessoa AND codigo_pessoa_origem='SMS002');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_campo_verificacao_observacao WHERE pessoa_observacao_id=@smsP1)
 INSERT silver.pessoa_campo_verificacao_observacao(pessoa_observacao_id,gestor_id,campo_codigo,evidencia_tipo,referencia_evidencia,verificado_em,source_transaction_id) VALUES
 (@smsP1,@gSms,'CPF','DOCUMENTO_OFICIAL','ATENDIMENTO-SMS-001','2026-08-27T07:55:00-03:00','SMS-TX-20260827-001'),
 (@smsP1,@gSms,'NOME_COMPLETO','DOCUMENTO_OFICIAL','ATENDIMENTO-SMS-001','2026-08-27T07:55:00-03:00','SMS-TX-20260827-001'),
 (@smsP1,@gSms,'DATA_NASCIMENTO','DOCUMENTO_OFICIAL','ATENDIMENTO-SMS-001','2026-08-27T07:55:00-03:00','SMS-TX-20260827-001'),
 (@smsP1,@gSms,'NOME_MAE','DOCUMENTO_OFICIAL','ATENDIMENTO-SMS-001','2026-08-27T07:55:00-03:00','SMS-TX-20260827-001');
IF NOT EXISTS(SELECT 1 FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@smsP1)
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,resolvido_em) VALUES
 (@smsP1,@u1,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T08:01:00-03:00'),
 (@smsP2,@u2,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T08:01:00-03:00');

-- Entrega factual: Pessoas relacionadas + Benefícios Concedidos (AA01).
DECLARE @entBen UNIQUEIDENTIFIER='20000000-0000-4000-8000-000000000001', @lotBen UNIQUEIDENTIFIER='20000000-0000-4000-8000-000000000002';
IF NOT EXISTS(SELECT 1 FROM ingestao.entrega WHERE entrega_id=@entBen)
 INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
 VALUES(@entBen,@gSehab,@soSehab,@gpvSehab,'BENEFICIO',@aa,@aav,'dev-entrega-aa01-1','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'PROCESSADA','2026-08-27T00:00:00-03:00','2026-08-27T12:00:00+00:00','2026-08-27T09:03:00-03:00');
IF NOT EXISTS(SELECT 1 FROM bronze.entrega_arquivo WHERE entrega_id=@entBen)
 INSERT bronze.entrega_arquivo(entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em) VALUES(@entBen,'ENTREGA_SEHAB_SEHAB_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','application/zip','sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'2026-08-27T12:00:00+00:00');
IF NOT EXISTS(SELECT 1 FROM ingestao.lote WHERE lote_id=@lotBen)
 INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em) VALUES(@lotBen,@entBen,1,1,6,6,'PROCESSADO','2026-08-27T09:00:01-03:00','2026-08-27T09:03:00-03:00');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH001') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH001');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH002') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH002');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH003') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH003');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH004') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH004');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH005') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH005');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH006') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSehab,'SEH006');
DECLARE @po1 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH001'),
        @po2 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH002'),
        @po3 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH003'),
        @po4 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH004'),
        @po5 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH005'),
        @po6 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSehab AND codigo_pessoa_origem='SEH006');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_observacao WHERE lote_id=@lotBen)
 INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of) VALUES
 (@po1,@lotBen,@gSehab,'SEH001',1,REPLICATE('3',64),'11144477735',NULL,N'Maria da Silva',N'MARIA DA SILVA','1982-04-10',N'Ana de Souza',N'ANA DE SOUZA','2026-08-27T00:00:00-03:00'),
 (@po2,@lotBen,@gSehab,'SEH002',1,REPLICATE('4',64),'52998224725',NULL,N'João Souza',N'JOAO SOUZA','1977-09-22',N'Maria de Souza',N'MARIA DE SOUZA','2026-08-27T00:00:00-03:00'),
 (@po3,@lotBen,@gSehab,'SEH003',1,REPLICATE('5',64),'12345678909',NULL,N'Mariana Albuquerque',N'MARIANA ALBUQUERQUE','1988-12-05',N'Teresa Albuquerque',N'TERESA ALBUQUERQUE','2026-08-27T00:00:00-03:00'),
 (@po4,@lotBen,@gSehab,'SEH004',1,REPLICATE('6',64),'39053344705',NULL,N'Paulo Ferreira',N'PAULO FERREIRA','1970-07-07',N'Rita Ferreira',N'RITA FERREIRA','2026-08-27T00:00:00-03:00'),
 (@po5,@lotBen,@gSehab,'SEH005',1,REPLICATE('7',64),NULL,'SEM_CPF',N'Carlos Santos',N'CARLOS SANTOS','1990-01-15',N'Lucia Santos',N'LUCIA SANTOS','2026-08-27T00:00:00-03:00'),
 (@po6,@lotBen,@gSehab,'SEH006',1,REPLICATE('8',64),NULL,'EM_REGULARIZACAO',N'Luciana Lima',N'LUCIANA LIMA','1995-03-18',N'Sandra Lima',N'SANDRA LIMA','2026-08-27T00:00:00-03:00');
DECLARE @p1 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH001'),
        @p2 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH002'),
        @p3 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH003'),
        @p4 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH004'),
        @p5 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH005'),
        @p6 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotBen AND codigo_pessoa_origem='SEH006');
IF NOT EXISTS(SELECT 1 FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@p1)
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,resolvido_em) VALUES
 (@p1,@u1,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T09:04:00-03:00'),(@p2,@u2,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T09:04:00-03:00'),(@p3,@u3,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T09:04:00-03:00'),(@p4,@u4,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T09:04:00-03:00'),(@p5,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,NULL),(@p6,NULL,'PENDENTE_PROBABILISTICO',NULL,'NAO_RESOLVIDO',NULL,NULL);

DECLARE @linkRun UNIQUEIDENTIFIER='55555555-5555-4555-8555-555555555555';
IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run WHERE linkage_run_id=@linkRun)
 INSERT identidade.linkage_run(linkage_run_id,modelo_id,modelo_versao,tipo_run,status,gestor_codigo_filtro,limite_solicitado,escopo_json,batch_size,max_parallelism,pessoa_observacao_id_high_watermark,registros_elegiveis,avaliados,resolvidos,nao_resolvidos,conflitos,sem_candidato_no_bloco,solicitado_por,motivo,correlation_id,iniciado_em,finalizado_em,publicado_em)
 VALUES(@linkRun,@model,1,'ON_DEMAND','PUBLICADO','SEHAB',2,N'{"mode":"ON_DEMAND","gestorCodigo":"SEHAB","maxRecords":2,"publish":true}',10000,1,1000000,2,2,1,0,1,0,'seed-dev','run publicado atomicamente',@linkRun,'2026-08-27T09:05:00-03:00','2026-08-27T09:06:00-03:00','2026-08-27T09:06:00-03:00');
IF NOT EXISTS(SELECT 1 FROM identidade.linkage_run_item WHERE linkage_run_id=@linkRun)
 INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id) VALUES(@linkRun,@p5),(@linkRun,@p6);
IF NOT EXISTS(SELECT 1 FROM identidade.linkage_resultado WHERE linkage_run_id=@linkRun)
 INSERT identidade.linkage_resultado(linkage_run_id,modelo_id,modelo_versao,pessoa_observacao_id,pessoa_uuid_resolvido,melhor_candidato_uuid,score_melhor,segundo_candidato_uuid,score_segundo,margem,status,motivo,calculado_em) VALUES
 (@linkRun,@model,1,@p5,@u5,@u5,0.91,@u4,0.20,0.71,'RESOLVIDO',NULL,'2026-08-27T09:06:00-03:00'),(@linkRun,@model,1,@p6,NULL,@u3,0.70,@u2,0.69,0.01,'CONFLITO','MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE','2026-08-27T09:06:00-03:00');
IF NOT EXISTS(SELECT 1 FROM gold.pessoa WHERE pessoa_uuid=@u5)
 INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em) VALUES(@u5,NULL,'EM_REGULARIZACAO',N'Carlos Santos','1990-01-15',N'Lucia Santos',1,'BASELINE_FALLBACK','2026-08-27T09:06:00-03:00');

-- Atributos transversais ficam em estrutura genérica, não em novas colunas de gold.pessoa.
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@p1)
 INSERT silver.pessoa_atributo_observacao(source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,valor,status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at) VALUES
 ('SEH001-END-1',@p1,@gSehab,'ENDERECO_RESIDENCIAL','#',N'CEP=01001000|COD_LOG=000001|NUMERO=100|COMPLEMENTO=', 'COMPROVADO','FONTE_INSTITUCIONAL','2026-08-20T00:00:00-03:00','2026-08-21T09:00:00-03:00','2026-08-20T00:00:00-03:00','2026-08-27T12:00:00+00:00'),
 ('SEH001-TEL-1',@p1,@gSehab,'TELEFONE_CONTATO','5511999990001',N'+55 11 99999-0001','COMPROVADO','ATENDIMENTO_CONFIRMADO','2026-08-26T00:00:00-03:00','2026-08-26T10:00:00-03:00','2026-08-26T00:00:00-03:00','2026-08-27T12:00:00+00:00'),
 ('SEH002-EMAIL-1',@p2,@gSehab,'EMAIL_CONTATO','joao@example.test',N'joao@example.test','COMPROVADO','DOCUMENTO','2026-08-15T00:00:00-03:00','2026-08-16T09:00:00-03:00','2026-08-15T00:00:00-03:00','2026-08-27T12:00:00+00:00');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao WHERE source_record_id='SEH001-TEL-2')
 INSERT silver.pessoa_atributo_observacao(source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,atributo_instancia_chave,valor,status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at)
 VALUES('SEH001-TEL-2',@p1,@gSehab,'TELEFONE_CONTATO','5511988880002',N'+55 (11) 98888-0002','COMPROVADO','ATENDIMENTO_CONFIRMADO','2026-08-26T01:00:00-03:00','2026-08-26T11:00:00-03:00','2026-08-26T01:00:00-03:00','2026-08-27T12:00:00+00:00');
DECLARE @endP1 BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@p1 AND atributo_codigo='ENDERECO_RESIDENCIAL');
IF @endP1 IS NOT NULL AND NOT EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao WHERE pessoa_atributo_observacao_id=@endP1)
 INSERT silver.referencia_territorial_observacao(pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em)
 VALUES(@endP1,'DOMICILIAR','ENDERECO_RESIDENCIAL',@spSe,@dSe,'RESOLVIDA','ORIGEM','SEED_MALHA_2026','2026-08-20T00:00:00-03:00');
IF NOT EXISTS(SELECT 1 FROM gold.pessoa_atributo WHERE pessoa_uuid=@u1 AND atributo_codigo='ENDERECO_RESIDENCIAL')
 INSERT gold.pessoa_atributo(pessoa_uuid,atributo_codigo,atributo_instancia_chave,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em)
 SELECT @u1,pa.atributo_codigo,pa.atributo_instancia_chave,pa.valor,pa.fonte_gestor_id,pa.pessoa_atributo_observacao_id,pa.source_record_id,pa.evidencia_tipo,pa.referencia_evidencia,pa.verificado_em,COALESCE(pa.referencia_evidencia,pa.verificado_em),COALESCE(pa.referencia_evidencia,pa.verificado_em),'2026-08-27T09:03:00-03:00'
 FROM silver.pessoa_atributo_observacao pa WHERE pa.pessoa_observacao_id=@p1 AND pa.atributo_codigo='ENDERECO_RESIDENCIAL' AND pa.status_evidencia='COMPROVADO';
INSERT gold.pessoa_atributo(pessoa_uuid,atributo_codigo,atributo_instancia_chave,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em)
SELECT @u1,pa.atributo_codigo,pa.atributo_instancia_chave,pa.valor,pa.fonte_gestor_id,pa.pessoa_atributo_observacao_id,pa.source_record_id,pa.evidencia_tipo,pa.referencia_evidencia,pa.verificado_em,COALESCE(pa.referencia_evidencia,pa.verificado_em),COALESCE(pa.referencia_evidencia,pa.verificado_em),'2026-08-27T09:03:00-03:00'
FROM silver.pessoa_atributo_observacao pa
WHERE pa.pessoa_observacao_id=@p1 AND pa.atributo_codigo='TELEFONE_CONTATO' AND pa.status_evidencia='COMPROVADO'
  AND NOT EXISTS(SELECT 1 FROM gold.pessoa_atributo ga WHERE ga.pessoa_uuid=@u1 AND ga.atributo_codigo=pa.atributo_codigo AND ga.atributo_instancia_chave=pa.atributo_instancia_chave AND ga.vigencia_fim IS NULL);
IF NOT EXISTS(SELECT 1 FROM gold.pessoa_atributo WHERE pessoa_uuid=@u2 AND atributo_codigo='EMAIL_CONTATO')
 INSERT gold.pessoa_atributo(pessoa_uuid,atributo_codigo,atributo_instancia_chave,valor,fonte_gestor_id,pessoa_atributo_observacao_id,source_record_id,evidencia_tipo,referencia_evidencia,verificado_em,precedencia_em,vigencia_inicio,atualizado_em)
 SELECT @u2,pa.atributo_codigo,pa.atributo_instancia_chave,pa.valor,pa.fonte_gestor_id,pa.pessoa_atributo_observacao_id,pa.source_record_id,pa.evidencia_tipo,pa.referencia_evidencia,pa.verificado_em,COALESCE(pa.referencia_evidencia,pa.verificado_em),COALESCE(pa.referencia_evidencia,pa.verificado_em),'2026-08-27T09:03:00-03:00'
 FROM silver.pessoa_atributo_observacao pa WHERE pa.pessoa_observacao_id=@p2 AND pa.atributo_codigo='EMAIL_CONTATO' AND pa.status_evidencia='COMPROVADO';

-- Identidades estáveis dos Benefícios Concedidos no sistema finalístico.
DECLARE @rBen TABLE(codigo NVARCHAR(255), pessoa_observacao_id BIGINT, data_inicio_concessao DATE, data_fim_concessao DATE, data_evento_concessao DATE, situacao_vigencia NVARCHAR(20), situacao_vigencia_desde DATE, motivo_encerramento NVARCHAR(30), valor_concedido DECIMAL(18,2), hash CHAR(64));
INSERT @rBen VALUES
 ('AA-2026-004711',@p1,'2026-01-01',NULL,'2026-08-25','VIGENTE',NULL,NULL,600,REPLICATE('a',64)),
 ('AA-2026-004712',@p2,'2026-02-01',NULL,'2026-08-19','VIGENTE',NULL,NULL,700,REPLICATE('b',64)),
 ('AA-2026-004713',@p3,'2026-03-01',NULL,'2026-08-10','VIGENTE',NULL,NULL,650,REPLICATE('c',64)),
 ('AA-2026-004714',@p4,'2026-04-01','2026-07-31','2026-08-01','ENCERRADA','2026-08-01','TERMINO_REGULAR',600,REPLICATE('d',64)),
 ('AA-2026-004715',@p5,'2026-05-01',NULL,'2026-08-27','VIGENTE',NULL,NULL,600,REPLICATE('e',64)),
 ('AA-2026-004716',@p6,'2026-06-01',NULL,'2026-07-20','VIGENTE',NULL,NULL,700,REPLICATE('f',64));
INSERT silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id)
SELECT @soSehab,r.codigo,'BENEFICIO',@aa FROM @rBen r
WHERE NOT EXISTS(SELECT 1 FROM silver.registro_origem o WHERE o.sistema_origem_id=@soSehab AND o.codigo_registro_origem=r.codigo);
IF NOT EXISTS(SELECT 1 FROM silver.registro_observacao WHERE lote_id=@lotBen)
 INSERT silver.registro_observacao(registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,valor_concedido,source_as_of)
 SELECT o.registro_origem_id,r.codigo,1,'INCLUSAO',r.hash,@lotBen,@gSehab,'BENEFICIO',@aa,@aav,r.pessoa_observacao_id,r.data_inicio_concessao,r.data_fim_concessao,r.data_evento_concessao,r.situacao_vigencia,r.situacao_vigencia_desde,r.motivo_encerramento,r.valor_concedido,'2026-08-27T00:00:00-03:00'
 FROM @rBen r JOIN silver.registro_origem o ON o.sistema_origem_id=@soSehab AND o.codigo_registro_origem=r.codigo;
IF NOT EXISTS(SELECT 1 FROM qualidade.qc_registro_implementacao WHERE tipo_registro_versao_id=@aav)
 INSERT qualidade.qc_registro_implementacao VALUES(@aav,'AA01.QC.v1','IMPLEMENTADO');
IF NOT EXISTS(SELECT 1 FROM qualidade.qc_registro_implementacao WHERE tipo_registro_versao_id=@potv)
 INSERT qualidade.qc_registro_implementacao VALUES(@potv,'POT1.QC.v1','IMPLEMENTADO');
IF NOT EXISTS(SELECT 1 FROM qualidade.qc_registro_resultado)
 INSERT qualidade.qc_registro_resultado(registro_observacao_id,resultado,regra_codigo,motivo,executado_em)
 SELECT registro_observacao_id,CASE WHEN registro_observacao_id%4=0 THEN 'DIVERGENTE' ELSE 'VALIDO' END,CASE WHEN registro_observacao_id%4=0 THEN 'R03' END,CASE WHEN registro_observacao_id%4=0 THEN 'Valor fora da faixa sintética.' END,'2026-08-27T09:02:00-03:00' FROM silver.registro_observacao WHERE lote_id=@lotBen;
IF NOT EXISTS(SELECT 1 FROM gold.beneficio_concedido)
 INSERT gold.beneficio_concedido(registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
 SELECT ro.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,ro.versao_interna,ro.operacao,'VIGENTE',po.pessoa_origem_id,pori.sistema_origem_id,po.codigo_pessoa_origem,po.cpf,po.cpf_ausente_motivo,vf.pessoa_uuid,'ATRIBUIDA',ro.gestor_id,ro.tipo_registro_id,ro.tipo_registro_versao_id,@entBen,ro.data_inicio_concessao,ro.data_fim_concessao,ro.data_evento_concessao,ro.situacao_vigencia,ro.situacao_vigencia_desde,ro.motivo_encerramento,pg.referencia_territorial_observacao_id,pg.natureza_referencia,pg.subprefeitura_id,pg.distrito_id,ro.valor_concedido,ro.quantidade,ro.unidade,ro.source_as_of,COALESCE(qr.resultado,'VALIDO'),1,ro.registrado_em,NULL
 FROM silver.registro_observacao ro JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id AND vf.pessoa_uuid IS NOT NULL LEFT JOIN qualidade.qc_registro_resultado qr ON qr.registro_observacao_id=ro.registro_observacao_id WHERE ro.lote_id=@lotBen AND ro.natureza='BENEFICIO';
INSERT serving.registro_integrado(registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,data_inicio_concessao,data_fim_concessao,data_evento_concessao,situacao_vigencia,situacao_vigencia_desde,motivo_encerramento,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,valor_concedido,quantidade,unidade,source_as_of,qc_resultado,qc_especifico_implementado,vigencia_versao_inicio,vigencia_versao_fim)
SELECT b.registro_observacao_id,b.registro_origem_id,b.codigo_registro_origem,b.versao_interna,b.operacao,b.status_analitico,b.pessoa_origem_id,b.sistema_origem_id,b.codigo_pessoa_origem,b.cpf_declarado,b.cpf_ausente_motivo,b.pessoa_uuid,b.estado_atribuicao_identidade,b.gestor_id,'BENEFICIO',b.tipo_registro_id,b.tipo_registro_versao_id,b.entrega_id,1,b.data_inicio_concessao,b.data_fim_concessao,b.data_evento_concessao,b.situacao_vigencia,b.situacao_vigencia_desde,b.motivo_encerramento,b.referencia_territorial_observacao_id,b.natureza_referencia_territorial,b.subprefeitura_referencia_id,b.distrito_referencia_id,b.valor_concedido,b.quantidade,b.unidade,b.source_as_of,b.qc_resultado,b.qc_especifico_implementado,b.vigencia_versao_inicio,b.vigencia_versao_fim FROM gold.beneficio_concedido b
WHERE NOT EXISTS(SELECT 1 FROM serving.registro_integrado ri WHERE ri.registro_observacao_id=b.registro_observacao_id);

-- Entrega factual: Pessoas relacionadas + Serviços Prestados (CRA1).
DECLARE @entS UNIQUEIDENTIFIER='30000000-0000-4000-8000-000000000001', @lotS UNIQUEIDENTIFIER='30000000-0000-4000-8000-000000000002';
IF NOT EXISTS(SELECT 1 FROM ingestao.entrega WHERE entrega_id=@entS)
 INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
 VALUES(@entS,@gSmads,@soSmads,@gpvSmads,'SERVICO',@cras,@crv,'dev-entrega-cra1-1','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'PROCESSADA','2026-08-27T00:00:00-03:00','2026-08-27T13:00:00+00:00','2026-08-27T10:01:00-03:00');
IF NOT EXISTS(SELECT 1 FROM bronze.entrega_arquivo WHERE entrega_id=@entS)
 INSERT bronze.entrega_arquivo(entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em) VALUES(@entS,'ENTREGA_SMADS_ASSISTENCIA_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','application/zip','sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip','8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',4,'2026-08-27T13:00:00+00:00');
IF NOT EXISTS(SELECT 1 FROM ingestao.lote WHERE lote_id=@lotS)
 INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,criado_em,atualizado_em) VALUES(@lotS,@entS,1,1,2,2,'PROCESSADO','2026-08-27T10:00:01-03:00','2026-08-27T10:01:00-03:00');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSmads AND codigo_pessoa_origem='CRAS001') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSmads,'CRAS001');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_origem WHERE sistema_origem_id=@soSmads AND codigo_pessoa_origem='CRAS002') INSERT silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@soSmads,'CRAS002');
DECLARE @spo1 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSmads AND codigo_pessoa_origem='CRAS001'),
        @spo2 BIGINT=(SELECT pessoa_origem_id FROM silver.pessoa_origem WHERE sistema_origem_id=@soSmads AND codigo_pessoa_origem='CRAS002');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_observacao WHERE lote_id=@lotS)
 INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of) VALUES
 (@spo1,@lotS,@gSmads,'CRAS001',1,REPLICATE('9',64),'11144477735',NULL,N'Maria da Silva',N'MARIA DA SILVA','1982-04-10',N'Ana de Souza',N'ANA DE SOUZA','2026-08-27T00:00:00-03:00'),(@spo2,@lotS,@gSmads,'CRAS002',1,REPLICATE('0',64),'52998224725',NULL,N'João de Souza',N'JOAO DE SOUZA','1977-09-22',N'Maria de Souza',N'MARIA DE SOUZA','2026-08-27T00:00:00-03:00');
DECLARE @sp1 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotS AND codigo_pessoa_origem='CRAS001'), @sp2 BIGINT=(SELECT pessoa_observacao_id FROM silver.pessoa_observacao WHERE lote_id=@lotS AND codigo_pessoa_origem='CRAS002');
IF NOT EXISTS(SELECT 1 FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@sp1 AND atributo_codigo='ENDERECO_RESIDENCIAL')
 INSERT silver.pessoa_atributo_observacao(source_record_id,pessoa_observacao_id,fonte_gestor_id,atributo_codigo,valor,status_evidencia,evidencia_tipo,referencia_evidencia,verificado_em,atualizado_em_origem,ingested_at) VALUES
 ('CRAS001-END-1',@sp1,@gSmads,'ENDERECO_RESIDENCIAL',N'CEP=01001000|NUMERO=100','COMPROVADO','FONTE_INSTITUCIONAL','2026-08-26T00:00:00-03:00','2026-08-26T09:00:00-03:00','2026-08-26T00:00:00-03:00','2026-08-27T13:00:00+00:00'),
 ('CRAS001-REF-TERR-1',@sp1,@gSmads,'REFERENCIA_TERRITORIAL',N'DISTRITO=SE|SUBPREFEITURA=SE','DECLARADO','FONTE_INSTITUCIONAL',NULL,NULL,'2026-08-27T00:00:00-03:00','2026-08-27T13:00:00+00:00'),
 ('CRAS002-END-1',@sp2,@gSmads,'ENDERECO_RESIDENCIAL',N'CEP=08295005|NUMERO=200','COMPROVADO','FONTE_INSTITUCIONAL','2026-08-16T00:00:00-03:00','2026-08-16T09:00:00-03:00','2026-08-16T00:00:00-03:00','2026-08-27T13:00:00+00:00');
DECLARE @endSp1 BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@sp1 AND atributo_codigo='ENDERECO_RESIDENCIAL'),
        @endSp2 BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@sp2 AND atributo_codigo='ENDERECO_RESIDENCIAL');
DECLARE @refSp1 BIGINT=(SELECT pessoa_atributo_observacao_id FROM silver.pessoa_atributo_observacao WHERE pessoa_observacao_id=@sp1 AND atributo_codigo='REFERENCIA_TERRITORIAL');
IF @endSp1 IS NOT NULL AND NOT EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao WHERE pessoa_atributo_observacao_id=@endSp1)
 INSERT silver.referencia_territorial_observacao(pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em) VALUES(@endSp1,'DOMICILIAR','ENDERECO_RESIDENCIAL',@spSe,@dSe,'RESOLVIDA','ORIGEM','SEED_MALHA_2026','2026-08-26T09:01:00-03:00');
IF @endSp2 IS NOT NULL AND NOT EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao WHERE pessoa_atributo_observacao_id=@endSp2)
 INSERT silver.referencia_territorial_observacao(pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em) VALUES(@endSp2,'DOMICILIAR','ENDERECO_RESIDENCIAL',@spI,@dI,'RESOLVIDA','ORIGEM','SEED_MALHA_2026','2026-08-16T09:01:00-03:00');
IF @refSp1 IS NOT NULL AND NOT EXISTS(SELECT 1 FROM silver.referencia_territorial_observacao WHERE pessoa_atributo_observacao_id=@refSp1)
 INSERT silver.referencia_territorial_observacao(pessoa_atributo_observacao_id,natureza_referencia,fonte_semantica,subprefeitura_id,distrito_id,situacao_geografia,origem_geografia,referencia_malha,resolvido_em) VALUES(@refSp1,'REFERENCIA_TERRITORIAL_DECLARADA','REFERENCIA_TERRITORIAL',@spSe,@dSe,'RESOLVIDA','ORIGEM','ORIGEM-SMADS-2026-08','2026-08-27T00:00:00-03:00');
IF NOT EXISTS(SELECT 1 FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@sp1)
 INSERT identidade.vinculo_fonte(pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,modelo_id,resolvido_em) VALUES(@sp1,@u1,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T10:00:30-03:00'),(@sp2,@u2,'CPF_DETERMINISTICO',NULL,'RESOLVIDO',NULL,'2026-08-27T10:00:30-03:00');
IF NOT EXISTS(SELECT 1 FROM silver.registro_origem WHERE sistema_origem_id=@soSmads AND codigo_registro_origem='CRA-2026-000001') INSERT silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id) VALUES(@soSmads,'CRA-2026-000001','SERVICO',@cras);
IF NOT EXISTS(SELECT 1 FROM silver.registro_origem WHERE sistema_origem_id=@soSmads AND codigo_registro_origem='CRA-2026-000002') INSERT silver.registro_origem(sistema_origem_id,codigo_registro_origem,natureza,tipo_registro_id) VALUES(@soSmads,'CRA-2026-000002','SERVICO',@cras);
DECLARE @sro1 BIGINT=(SELECT registro_origem_id FROM silver.registro_origem WHERE sistema_origem_id=@soSmads AND codigo_registro_origem='CRA-2026-000001'),
        @sro2 BIGINT=(SELECT registro_origem_id FROM silver.registro_origem WHERE sistema_origem_id=@soSmads AND codigo_registro_origem='CRA-2026-000002');
IF NOT EXISTS(SELECT 1 FROM silver.registro_observacao WHERE lote_id=@lotS)
 INSERT silver.registro_observacao(registro_origem_id,codigo_registro_origem,versao_interna,operacao,conteudo_hash,lote_id,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,pessoa_observacao_id,data_hora_servico,unidade_servico,situacao,source_as_of) VALUES
 (@sro1,'CRA-2026-000001',1,'INCLUSAO',REPLICATE('1',64),@lotS,@gSmads,'SERVICO',@cras,@crv,@sp1,'2026-08-26T10:30:00-03:00',N'CRAS Sé','REALIZADO','2026-08-27T00:00:00-03:00'),
 (@sro2,'CRA-2026-000002',1,'INCLUSAO',REPLICATE('2',64),@lotS,@gSmads,'SERVICO',@cras,@crv,@sp2,'2026-08-17T11:00:00-03:00',N'CRAS Itaquera','REALIZADO','2026-08-27T00:00:00-03:00');
IF NOT EXISTS(SELECT 1 FROM gold.servico_prestado)
 INSERT gold.servico_prestado(registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,tipo_registro_id,tipo_registro_versao_id,entrega_id,data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,source_as_of,vigencia_versao_inicio,vigencia_versao_fim)
 SELECT ro.registro_observacao_id,ro.registro_origem_id,ro.codigo_registro_origem,ro.versao_interna,ro.operacao,'VIGENTE',po.pessoa_origem_id,pori.sistema_origem_id,po.codigo_pessoa_origem,po.cpf,po.cpf_ausente_motivo,vf.pessoa_uuid,'ATRIBUIDA',ro.gestor_id,ro.tipo_registro_id,ro.tipo_registro_versao_id,@entS,ro.data_hora_servico,ro.unidade_servico,ro.situacao,pg.referencia_territorial_observacao_id,pg.natureza_referencia,pg.subprefeitura_id,pg.distrito_id,ro.source_as_of,ro.registrado_em,NULL FROM silver.registro_observacao ro JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id JOIN silver.pessoa_origem pori ON pori.pessoa_origem_id=po.pessoa_origem_id LEFT JOIN silver.v_pessoa_referencia_territorial pg ON pg.pessoa_observacao_id=po.pessoa_observacao_id JOIN identidade.v_vinculo_corrente vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id AND vf.pessoa_uuid IS NOT NULL WHERE ro.lote_id=@lotS AND ro.natureza='SERVICO';
INSERT serving.registro_integrado(registro_observacao_id,registro_origem_id,codigo_registro_origem,versao_interna,operacao,status_analitico,pessoa_origem_id,sistema_origem_id,codigo_pessoa_origem,cpf_declarado,cpf_ausente_motivo,pessoa_uuid,estado_atribuicao_identidade,gestor_id,natureza,tipo_registro_id,tipo_registro_versao_id,entrega_id,entrega_completa,data_hora_servico,unidade_servico,situacao,referencia_territorial_observacao_id,natureza_referencia_territorial,subprefeitura_referencia_id,distrito_referencia_id,source_as_of,vigencia_versao_inicio,vigencia_versao_fim)
SELECT s.registro_observacao_id,s.registro_origem_id,s.codigo_registro_origem,s.versao_interna,s.operacao,s.status_analitico,s.pessoa_origem_id,s.sistema_origem_id,s.codigo_pessoa_origem,s.cpf_declarado,s.cpf_ausente_motivo,s.pessoa_uuid,s.estado_atribuicao_identidade,s.gestor_id,'SERVICO',s.tipo_registro_id,s.tipo_registro_versao_id,s.entrega_id,1,s.data_hora_servico,s.unidade_servico,s.situacao,s.referencia_territorial_observacao_id,s.natureza_referencia_territorial,s.subprefeitura_referencia_id,s.distrito_referencia_id,s.source_as_of,s.vigencia_versao_inicio,s.vigencia_versao_fim FROM gold.servico_prestado s
WHERE NOT EXISTS(SELECT 1 FROM serving.registro_integrado ri WHERE ri.registro_observacao_id=s.registro_observacao_id);

-- Possibilidades: avaliações derivadas, nunca fatos nem declaração de direito.
IF NOT EXISTS(SELECT 1 FROM qualidade.possibilidade_implementacao)
 INSERT qualidade.possibilidade_implementacao(natureza,tipo_registro_versao_id,implementacao_versao,status) VALUES('BENEFICIO',@arv,'AR01.POSS.v1','IMPLEMENTADO'),('BENEFICIO',@potv,'POT1.POSS.v1','IMPLEMENTADO'),('SERVICO',@crv,'CRA1.POSS.v1','IMPLEMENTADO');
-- v3.94: avaliações de possibilidade são derivadas e podem ser invalidadas por correções/fusões
-- de identidade. O seed DEV deve ser convergente por linha canônica, não apenas quando a tabela
-- inteira está vazia, para que uma reaplicação restaure o fixture sem duplicar avaliações.
IF NOT EXISTS(SELECT 1 FROM qualidade.avaliacao_possibilidade WHERE pessoa_uuid=@u1 AND tipo_registro_versao_id=@arv AND implementacao_versao='AR01.POSS.v1' AND avaliado_em='2026-08-27')
 INSERT qualidade.avaliacao_possibilidade(pessoa_uuid,natureza,tipo_registro_versao_id,resultado,motivo,implementacao_versao,avaliado_em,validade_ate)
 VALUES(@u1,'BENEFICIO',@arv,'COMPATIVEL','Perfil sintético compatível com critérios preliminares.','AR01.POSS.v1','2026-08-27','2026-09-27');
IF NOT EXISTS(SELECT 1 FROM qualidade.avaliacao_possibilidade WHERE pessoa_uuid=@u2 AND tipo_registro_versao_id=@potv AND implementacao_versao='POT1.POSS.v1' AND avaliado_em='2026-08-27')
 INSERT qualidade.avaliacao_possibilidade(pessoa_uuid,natureza,tipo_registro_versao_id,resultado,motivo,implementacao_versao,avaliado_em,validade_ate)
 VALUES(@u2,'BENEFICIO',@potv,'COMPATIVEL','Perfil sintético compatível com critérios preliminares.','POT1.POSS.v1','2026-08-27','2026-09-27');
IF NOT EXISTS(SELECT 1 FROM qualidade.avaliacao_possibilidade WHERE pessoa_uuid=@u5 AND tipo_registro_versao_id=@arv AND implementacao_versao='AR01.POSS.v1' AND avaliado_em='2026-08-27')
 INSERT qualidade.avaliacao_possibilidade(pessoa_uuid,natureza,tipo_registro_versao_id,resultado,motivo,implementacao_versao,avaliado_em,validade_ate)
 VALUES(@u5,'BENEFICIO',@arv,'NAO_AVALIAVEL','Dados insuficientes para avaliação automática.','AR01.POSS.v1','2026-08-27',NULL);
IF NOT EXISTS(SELECT 1 FROM qualidade.avaliacao_possibilidade WHERE pessoa_uuid=@u3 AND tipo_registro_versao_id=@crv AND implementacao_versao='CRA1.POSS.v1' AND avaliado_em='2026-08-27')
 INSERT qualidade.avaliacao_possibilidade(pessoa_uuid,natureza,tipo_registro_versao_id,resultado,motivo,implementacao_versao,avaliado_em,validade_ate)
 VALUES(@u3,'SERVICO',@crv,'COMPATIVEL','Geografia residencial enriquecida e perfil sintéticos compatíveis com serviço de referência.','CRA1.POSS.v1','2026-08-27','2026-09-27');

-- Eventos sintéticos: não armazenam chave de acesso.
DECLARE @agentSehabHash BINARY(32)=0xe3a9c69f0991a00b93ff5a1108b4b1f50bb827ceaa72cc7ed842035e80acb135; -- HMAC DEV de CPF sintético 52998224725.
IF NOT EXISTS(SELECT 1 FROM controle.api_evento)
 INSERT controle.api_evento(credencial_id,gestor_id,correlation_id,rota,metodo,status_http,duracao_ms,bytes_recebidos,pessoa_uuid,recurso_codigo,agente_cpf_hash,agente_hash_versao,ocorrido_em) VALUES
 (@credSms,@gSms,NEWID(),'/api/v1/ingestao/entregas','POST',202,150,42000,NULL,NULL,NULL,NULL,'2026-08-25T08:00:00-03:00'),
 (@credSehab,@gSehab,NEWID(),'/api/v1/ingestao/entregas','POST',202,210,185000,NULL,'AA01',NULL,NULL,'2026-08-25T10:00:00-03:00'),
 (@credSehab,@gSehab,NEWID(),'/api/v1/identidade/resolver','POST',200,45,500,@u1,NULL,@agentSehabHash,1,'2026-08-25T10:05:00-03:00'),
 (@credSmads,@gSmads,NEWID(),'/api/v1/ingestao/entregas','POST',202,180,12000,NULL,'CRA1',NULL,NULL,'2026-08-26T09:10:00-03:00'),
 (@credSehab,@gSehab,NEWID(),'/api/v1/pessoas/{uuid}/registros','GET',200,62,NULL,@u1,NULL,@agentSehabHash,1,'2026-08-27T12:00:00-03:00'),
 (@credAa,@gSehab,NEWID(),'/api/v1/pessoas/{uuid}','GET',200,55,NULL,@u1,'AA01',@agentSehabHash,1,'2026-08-27T12:05:00-03:00'),
 (@credCras,@gSmads,NEWID(),'/api/v1/pessoas/{uuid}','GET',200,58,NULL,@u3,'CRA1',NULL,NULL,'2026-08-27T12:06:00-03:00');

INSERT controle.api_evento_pessoa(api_evento_id,pessoa_uuid)
SELECT ae.api_evento_id,ae.pessoa_uuid FROM controle.api_evento ae
WHERE ae.pessoa_uuid IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM controle.api_evento_pessoa ep WHERE ep.api_evento_id=ae.api_evento_id AND ep.pessoa_uuid=ae.pessoa_uuid);

-- v3.91: a fixture DEV é convergente para as três Entregas canônicas.
-- Testes de retenção/processor exercitam transições destrutivas sobre essas linhas; uma nova
-- execução do seed deve restaurar exatamente o estado canônico, sem depender da ordem dos testes.
UPDATE ingestao.entrega
   SET payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       bytes_recebidos=4,status='PROCESSADA',data_referencia='2026-08-27T00:00:00-03:00',
       recebido_em='2026-08-27T11:00:00+00:00',ultima_atualizacao='2026-08-27T08:03:00-03:00'
 WHERE entrega_id=@entPessoa;
UPDATE bronze.entrega_arquivo
   SET nome_arquivo='ENTREGA_SMS_SAUDE_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       content_type='application/zip',
       objeto_chave='sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       tamanho_bytes=4,recebido_em='2026-08-27T11:00:00+00:00',
       estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL
 WHERE entrega_id=@entPessoa;
UPDATE ingestao.lote
   SET status='PROCESSADO',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
       ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,lease_id=NULL,lease_owner=NULL,
       lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,poison_em=NULL,
       criado_em='2026-08-27T08:00:01-03:00',atualizado_em='2026-08-27T08:03:00-03:00'
 WHERE lote_id=@lotPessoa;

UPDATE ingestao.entrega
   SET payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       bytes_recebidos=4,status='PROCESSADA',data_referencia='2026-08-27T00:00:00-03:00',
       recebido_em='2026-08-27T12:00:00+00:00',ultima_atualizacao='2026-08-27T09:03:00-03:00'
 WHERE entrega_id=@entBen;
UPDATE bronze.entrega_arquivo
   SET nome_arquivo='ENTREGA_SEHAB_SEHAB_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       content_type='application/zip',
       objeto_chave='sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       tamanho_bytes=4,recebido_em='2026-08-27T12:00:00+00:00',
       estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL
 WHERE entrega_id=@entBen;
UPDATE ingestao.lote
   SET status='PROCESSADO',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
       ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,lease_id=NULL,lease_owner=NULL,
       lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,poison_em=NULL,
       criado_em='2026-08-27T09:00:01-03:00',atualizado_em='2026-08-27T09:03:00-03:00'
 WHERE lote_id=@lotBen;

UPDATE ingestao.entrega
   SET payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       bytes_recebidos=4,status='PROCESSADA',data_referencia='2026-08-27T00:00:00-03:00',
       recebido_em='2026-08-27T13:00:00+00:00',ultima_atualizacao='2026-08-27T10:01:00-03:00'
 WHERE entrega_id=@entS;
UPDATE bronze.entrega_arquivo
   SET nome_arquivo='ENTREGA_SMADS_ASSISTENCIA_v2_8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       content_type='application/zip',
       objeto_chave='sha256/8d/cc/8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2.zip',
       payload_sha256='8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2',
       tamanho_bytes=4,recebido_em='2026-08-27T13:00:00+00:00',
       estado_armazenamento='DISPONIVEL',expurgo_iniciado_em=NULL,expurgado_em=NULL,retencao_motivo=NULL
 WHERE entrega_id=@entS;
UPDATE ingestao.lote
   SET status='PROCESSADO',erro_codigo=NULL,tentativa_count=0,recuperacao_count=0,
       ultima_tentativa_em=NULL,proxima_tentativa_em=NULL,lease_id=NULL,lease_owner=NULL,
       lease_adquirido_em=NULL,heartbeat_em=NULL,lease_expira_em=NULL,poison_em=NULL,
       criado_em='2026-08-27T10:00:01-03:00',atualizado_em='2026-08-27T10:01:00-03:00'
 WHERE lote_id=@lotS;

-- Recalcula o indicador derivado de completude a partir dos lotes internos.
DECLARE @recalc UNIQUEIDENTIFIER;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT entrega_id FROM ingestao.entrega;
OPEN c; FETCH NEXT FROM c INTO @recalc;
WHILE @@FETCH_STATUS=0 BEGIN EXEC ingestao.sp_recalcular_entrega @entrega_id=@recalc; FETCH NEXT FROM c INTO @recalc; END;
CLOSE c; DEALLOCATE c;


-- Observabilidade de processamento v3.26: cada item recebido tem resultado consultável sem criar versão extra na Silver.
INSERT ingestao.item_processado(lote_id,classe_item,pessoa_origem_id,registro_origem_id,codigo_origem,resultado,versao_interna,conteudo_hash,data_referencia,processado_em)
SELECT po.lote_id,'PESSOA',po.pessoa_origem_id,NULL,po.codigo_pessoa_origem,'INCLUIDO',po.versao_interna,po.conteudo_hash,po.source_as_of,COALESCE(l.atualizado_em,SYSDATETIMEOFFSET())
FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id
WHERE NOT EXISTS(SELECT 1 FROM ingestao.item_processado ip WHERE ip.lote_id=po.lote_id AND ip.classe_item='PESSOA' AND ip.codigo_origem=po.codigo_pessoa_origem);

INSERT ingestao.item_processado(lote_id,classe_item,pessoa_origem_id,registro_origem_id,codigo_origem,resultado,versao_interna,conteudo_hash,data_referencia,processado_em)
SELECT ro.lote_id,'REGISTRO',NULL,ro.registro_origem_id,ro.codigo_registro_origem,
       CASE WHEN ro.operacao='EXCLUSAO' THEN 'EXCLUIDO' ELSE 'INCLUIDO' END,
       ro.versao_interna,ro.conteudo_hash,ro.source_as_of,COALESCE(l.atualizado_em,SYSDATETIMEOFFSET())
FROM silver.registro_observacao ro JOIN ingestao.lote l ON l.lote_id=ro.lote_id
WHERE NOT EXISTS(SELECT 1 FROM ingestao.item_processado ip WHERE ip.lote_id=ro.lote_id AND ip.classe_item='REGISTRO' AND ip.codigo_origem=ro.codigo_registro_origem);

UPDATE po SET ultima_recepcao_em=x.ultima_recepcao_em,ultima_referencia_recebida=x.ultima_referencia
FROM silver.pessoa_origem po
CROSS APPLY(SELECT MAX(l.atualizado_em) ultima_recepcao_em,MAX(obs.source_as_of) ultima_referencia
            FROM silver.pessoa_observacao obs JOIN ingestao.lote l ON l.lote_id=obs.lote_id WHERE obs.pessoa_origem_id=po.pessoa_origem_id) x
WHERE x.ultima_recepcao_em IS NOT NULL;

UPDATE ro SET ultima_recepcao_em=x.ultima_recepcao_em,ultima_referencia_recebida=x.ultima_referencia
FROM silver.registro_origem ro
CROSS APPLY(SELECT MAX(l.atualizado_em) ultima_recepcao_em,MAX(obs.source_as_of) ultima_referencia
            FROM silver.registro_observacao obs JOIN ingestao.lote l ON l.lote_id=obs.lote_id WHERE obs.registro_origem_id=ro.registro_origem_id) x
WHERE x.ultima_recepcao_em IS NOT NULL;

COMMIT TRANSACTION;
GO

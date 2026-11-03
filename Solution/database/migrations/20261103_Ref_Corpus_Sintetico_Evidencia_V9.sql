SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
-- V9 synthetic-source provenance, distinct from public/immutable IBGE name
-- frequency and from operational model activation. No PII or raw rows.
IF OBJECT_ID(N'ref.corpus_sintetico_versao_v9',N'U') IS NULL
BEGIN
 CREATE TABLE ref.corpus_sintetico_versao_v9 (
  codigo NVARCHAR(120) NOT NULL CONSTRAINT pk_ref_corpus_sintetico_versao_v9 PRIMARY KEY,
  origem NVARCHAR(40) NOT NULL,
  status NVARCHAR(50) NOT NULL,
  gerador_versao NVARCHAR(120) NOT NULL,
  gerador_semente BIGINT NOT NULL,
  pessoas INT NOT NULL,
  corpus_sha256 CHAR(64) NOT NULL,
  observacoes_sha256 CHAR(64) NOT NULL,
  gabarito_sha256 CHAR(64) NOT NULL,
  manifesto_sha256 CHAR(64) NOT NULL,
  referencia_ibge_codigo NVARCHAR(120) NOT NULL,
  referencia_ibge_sha256 CHAR(64) NOT NULL,
  referencia_ibge_tf_materializado_sha256 CHAR(64) NOT NULL,
  registro_em DATETIMEOFFSET(7) NOT NULL CONSTRAINT df_ref_corpus_sintetico_registro DEFAULT(SYSDATETIMEOFFSET()),
  CONSTRAINT ck_ref_corpus_sintetico_origem CHECK(origem=N'SINTETICO_IBGE'),
  CONSTRAINT ck_ref_corpus_sintetico_status CHECK(status=N'EVIDENCIA_NAO_OPERACIONAL'),
  CONSTRAINT ck_ref_corpus_sintetico_base CHECK(pessoas=30000 AND gerador_semente=42),
  CONSTRAINT ck_ref_corpus_sintetico_sha CHECK(
    corpus_sha256 NOT LIKE '%[^0-9A-F]%' AND
    observacoes_sha256 NOT LIKE '%[^0-9A-F]%' AND
    gabarito_sha256 NOT LIKE '%[^0-9A-F]%' AND
    manifesto_sha256 NOT LIKE '%[^0-9A-F]%' AND
    referencia_ibge_sha256 NOT LIKE '%[^0-9A-F]%' AND
    referencia_ibge_tf_materializado_sha256 NOT LIKE '%[^0-9A-F]%'
  )
 );
END;
GO
CREATE OR ALTER TRIGGER ref.tr_corpus_sintetico_v9_immutable
ON ref.corpus_sintetico_versao_v9
AFTER UPDATE, DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted)
  THROW 52390, 'Evidencia de corpus sintetico V9 e imutavel.', 1;
END;
GO
-- Idempotent, exact-content registration. Never changes the IBGE marginal.
IF EXISTS(SELECT 1 FROM ref.corpus_sintetico_versao_v9 WHERE codigo=N'V9_SEED42_30000_CSHARP_V1')
BEGIN
 IF NOT EXISTS(
  SELECT 1 FROM ref.corpus_sintetico_versao_v9
  WHERE codigo=N'V9_SEED42_30000_CSHARP_V1'
    AND origem=N'SINTETICO_IBGE' AND status=N'EVIDENCIA_NAO_OPERACIONAL'
    AND gerador_versao=N'JORNADA_SYNTH_CORPUS_CSHARP_V1'
    AND gerador_semente=42 AND pessoas=30000
    AND corpus_sha256='75171EEDD0BAB7C4D19C7CB37DC620EE0E249199A406F4C8A9DFAC51839ADD13'
    AND observacoes_sha256='28BBB9F4C63C97C353E7F29CDBC77091A4773B2F4C596E46103EDADD93635D17'
    AND gabarito_sha256='982658206DF534E8F129E2C3C803E2DA9679EC96D385669CC054D6CA890754D2'
    AND manifesto_sha256='2F6ED064113D4E58B01D013B5C0A3ED99DE098FBAE4BA7910D0D62B33168E613'
    AND referencia_ibge_codigo=N'CENSO2022_NOMES_BRASIL_V1'
    AND referencia_ibge_sha256='E3CC61BCC7FCA353BB134EE32DE4AA50A8DA708827EAC3750EFC0E6EBCA5E885'
    AND referencia_ibge_tf_materializado_sha256='89211544D4C97236F34B165BF35E5A130063C11679EFFB7985BF003105B19243'
 )
  THROW 52391, 'Corpus sintetico V9 ja registrado com origem/sha divergentes.', 1;
END
ELSE
BEGIN
 INSERT ref.corpus_sintetico_versao_v9(
  codigo,origem,status,gerador_versao,gerador_semente,pessoas,
  corpus_sha256,observacoes_sha256,gabarito_sha256,manifesto_sha256,
  referencia_ibge_codigo,referencia_ibge_sha256,referencia_ibge_tf_materializado_sha256)
 VALUES (
  N'V9_SEED42_30000_CSHARP_V1',N'SINTETICO_IBGE',N'EVIDENCIA_NAO_OPERACIONAL',
  N'JORNADA_SYNTH_CORPUS_CSHARP_V1',42,30000,
  '75171EEDD0BAB7C4D19C7CB37DC620EE0E249199A406F4C8A9DFAC51839ADD13',
  '28BBB9F4C63C97C353E7F29CDBC77091A4773B2F4C596E46103EDADD93635D17',
  '982658206DF534E8F129E2C3C803E2DA9679EC96D385669CC054D6CA890754D2',
  '2F6ED064113D4E58B01D013B5C0A3ED99DE098FBAE4BA7910D0D62B33168E613',
  N'CENSO2022_NOMES_BRASIL_V1',
  'E3CC61BCC7FCA353BB134EE32DE4AA50A8DA708827EAC3750EFC0E6EBCA5E885',
  '89211544D4C97236F34B165BF35E5A130063C11679EFFB7985BF003105B19243'
 );
END;
GO

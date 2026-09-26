SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/*
  Derivado nominal IBGE imutavel (#497): a fonte em ref.frequencia_nome_versao
  e' independente do modelo operacional. Duas referencias (NOME/TODOS e
  NOME_MAE/FEMININO) sao versionadas por conteudo/método/seed/contagem/contrato.
  Nao substitui u condicionado a blocking ou m do modelo.
  Aditiva, sem promover Jornada.SolutionSchema ate o fechamento #411.
*/
IF OBJECT_ID(N'ref.ibge_u_referencia',N'U') IS NULL
BEGIN
 CREATE TABLE ref.ibge_u_referencia(
   ibge_u_referencia_id BIGINT IDENTITY(1,1) NOT NULL
     CONSTRAINT pk_ibge_u_referencia PRIMARY KEY,
   frequencia_nome_versao_id BIGINT NOT NULL
     CONSTRAINT fk_ibge_u_frequencia FOREIGN KEY
       REFERENCES ref.frequencia_nome_versao(frequencia_nome_versao_id),
   conteudo_origem_sha256 BINARY(32) NOT NULL,
   metodo_versao NVARCHAR(80) NOT NULL,
   construcao_versao NVARCHAR(100) NOT NULL,
   canal_versao NVARCHAR(100) NOT NULL,
   comparador_versao NVARCHAR(60) NOT NULL,
   recorte_prenome NVARCHAR(12) NOT NULL,
   seed INT NOT NULL,
   pares INT NOT NULL,
   vocabulario_prenomes INT NOT NULL,
   vocabulario_sobrenomes INT NOT NULL,
   ocorrencias_prenomes BIGINT NOT NULL,
   ocorrencias_sobrenomes BIGINT NOT NULL,
   colisoes_prenome DECIMAL(30,12) NOT NULL,
   colisoes_sobrenome DECIMAL(30,12) NOT NULL,
   colisoes_nome_completo DECIMAL(30,12) NOT NULL,
   resultado_sha256 BINARY(32) NULL,
   status NVARCHAR(16) NOT NULL
     CONSTRAINT df_ibge_u_status DEFAULT(N'CARREGANDO'),
   criado_em DATETIMEOFFSET(7) NOT NULL
     CONSTRAINT df_ibge_u_criado DEFAULT(SYSDATETIMEOFFSET()),
   publicado_em DATETIMEOFFSET(7) NULL,
   CONSTRAINT ck_ibge_u_recorte CHECK(recorte_prenome IN(N'TODOS',N'FEMININO')),
   CONSTRAINT ck_ibge_u_pares CHECK(pares>0 AND vocabulario_prenomes>0 AND vocabulario_sobrenomes>0 AND ocorrencias_prenomes>0 AND ocorrencias_sobrenomes>0),
   CONSTRAINT ck_ibge_u_analiticas CHECK(
     colisoes_prenome BETWEEN 0 AND 1 AND colisoes_sobrenome BETWEEN 0 AND 1
     AND colisoes_nome_completo BETWEEN 0 AND 1),
   CONSTRAINT ck_ibge_u_estado CHECK(
     (status=N'CARREGANDO' AND publicado_em IS NULL AND resultado_sha256 IS NULL)
     OR(status=N'PRONTA' AND publicado_em IS NOT NULL AND resultado_sha256 IS NOT NULL)),
   CONSTRAINT uq_ibge_u_chave UNIQUE(
     frequencia_nome_versao_id,conteudo_origem_sha256,metodo_versao,
     construcao_versao,canal_versao,comparador_versao,
     recorte_prenome,seed,pares)
 );
END;
GO
IF OBJECT_ID(N'ref.ibge_u_referencia_estado',N'U') IS NULL
BEGIN
 CREATE TABLE ref.ibge_u_referencia_estado(
   ibge_u_referencia_id BIGINT NOT NULL
     CONSTRAINT fk_ibge_u_estado_versao FOREIGN KEY
       REFERENCES ref.ibge_u_referencia(ibge_u_referencia_id),
   estado NVARCHAR(20) NOT NULL,
   suporte BIGINT NOT NULL,
   probabilidade DECIMAL(30,12) NOT NULL,
   erro_padrao DECIMAL(30,12) NOT NULL,
   CONSTRAINT pk_ibge_u_referencia_estado PRIMARY KEY(ibge_u_referencia_id,estado),
   CONSTRAINT ck_ibge_u_estado_nome CHECK(estado IN(N'EXACT',N'HIGH',N'MEDIUM',N'LOW')),
   CONSTRAINT ck_ibge_u_estado_valores CHECK(
     suporte>=0 AND probabilidade BETWEEN 0 AND 1
     AND erro_padrao BETWEEN 0 AND 1)
 );
END;
GO
CREATE OR ALTER TRIGGER ref.tr_ibge_u_estado_immutavel
ON ref.ibge_u_referencia_estado AFTER INSERT,UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
  SELECT 1 FROM (
    SELECT ibge_u_referencia_id FROM inserted
    UNION SELECT ibge_u_referencia_id FROM deleted) ids
  JOIN ref.ibge_u_referencia v
    ON v.ibge_u_referencia_id=ids.ibge_u_referencia_id
  WHERE v.status=N'PRONTA'
 )
  THROW 52082,'Estados da referencia nominal u publicada sao imutaveis.',1;
END;
GO
CREATE OR ALTER TRIGGER ref.tr_ibge_u_versao_immutavel
ON ref.ibge_u_referencia AFTER UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(
   SELECT 1 FROM deleted d LEFT JOIN inserted i
     ON i.ibge_u_referencia_id=d.ibge_u_referencia_id
   WHERE i.ibge_u_referencia_id IS NULL OR d.status=N'PRONTA'
 )
  THROW 52083,'Referencia nominal u publicada nao pode ser editada/excluida.',1;
 IF EXISTS(
   SELECT 1 FROM inserted i JOIN deleted d
     ON d.ibge_u_referencia_id=i.ibge_u_referencia_id
   WHERE d.status<>N'CARREGANDO' OR i.status<>N'PRONTA'
     OR i.frequencia_nome_versao_id<>d.frequencia_nome_versao_id
     OR i.conteudo_origem_sha256<>d.conteudo_origem_sha256
     OR i.metodo_versao<>d.metodo_versao
     OR i.construcao_versao<>d.construcao_versao
     OR i.canal_versao<>d.canal_versao
     OR i.comparador_versao<>d.comparador_versao
     OR i.recorte_prenome<>d.recorte_prenome
     OR i.seed<>d.seed OR i.pares<>d.pares
     OR i.vocabulario_prenomes<>d.vocabulario_prenomes
     OR i.vocabulario_sobrenomes<>d.vocabulario_sobrenomes
     OR i.ocorrencias_prenomes<>d.ocorrencias_prenomes
     OR i.ocorrencias_sobrenomes<>d.ocorrencias_sobrenomes
     OR i.colisoes_prenome<>d.colisoes_prenome
     OR i.colisoes_sobrenome<>d.colisoes_sobrenome
     OR i.colisoes_nome_completo<>d.colisoes_nome_completo
 )
  THROW 52084,'Metadados do bootstrap IBGE nao podem ser alterados.',1;
 IF EXISTS(
   SELECT 1 FROM inserted i
   OUTER APPLY(
     SELECT COUNT(*) n, SUM(suporte) support,
            SUM(probabilidade) probability_sum
     FROM ref.ibge_u_referencia_estado e
     WHERE e.ibge_u_referencia_id=i.ibge_u_referencia_id
   ) totals
   WHERE i.status=N'PRONTA'
     AND (totals.n<>4 OR totals.support<>CONVERT(BIGINT,i.pares)
       OR totals.probability_sum NOT BETWEEN 0.999999999998 AND 1.000000000002
       OR EXISTS(
         SELECT v.estado FROM (VALUES(N'EXACT'),(N'HIGH'),(N'MEDIUM'),(N'LOW')) v(estado)
         WHERE NOT EXISTS(
           SELECT 1 FROM ref.ibge_u_referencia_estado e
           WHERE e.ibge_u_referencia_id=i.ibge_u_referencia_id AND e.estado=v.estado)
       ))
 )
  THROW 52085,'Bootstrap IBGE incompleto: suporte/estados/probabilidade invalidos.',1;
END;
GO
CREATE OR ALTER VIEW ref.v_ibge_u_referencia_pronta AS
SELECT u.ibge_u_referencia_id,u.frequencia_nome_versao_id,
       v.codigo referencia_ibge,u.conteudo_origem_sha256,
       u.metodo_versao,u.construcao_versao,u.canal_versao,
       u.comparador_versao,u.recorte_prenome,u.seed,u.pares,
       u.vocabulario_prenomes,u.vocabulario_sobrenomes,
       u.ocorrencias_prenomes,u.ocorrencias_sobrenomes,
       u.colisoes_prenome,u.colisoes_sobrenome,u.colisoes_nome_completo,
       u.resultado_sha256,u.publicado_em
FROM ref.ibge_u_referencia u
JOIN ref.frequencia_nome_versao v
  ON v.frequencia_nome_versao_id=u.frequencia_nome_versao_id
WHERE u.status=N'PRONTA' AND v.status=N'ATIVA'
  AND u.conteudo_origem_sha256=v.conteudo_sha256;
GO

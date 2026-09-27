/*
 Gate 6: carregamento externo, sob responsabilidade da Solução de Apoio às Secretarias.
 Execute somente após disponibilizar e conferir o arquivo v5 exato no contrato de
 armazenamento acordado com a Jornada receptora. Este registro cria RASCUNHO;
 não ativa o contrato nem concede aprovação de governança.
*/
-- SQLCMD não herda as opções de sessão exigidas por índices filtrados/computados.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM ref.gestor WHERE codigo=N'SEHAB')
    THROW 52020,'Gestor externo não cadastrado; cadastrar pelo procedimento governado.',1;
IF EXISTS (
    SELECT 1 FROM ref.gestor_pessoa_versao v JOIN ref.gestor g ON g.gestor_id=v.gestor_id
    WHERE g.codigo=N'SEHAB' AND v.versao=5
      AND (v.pessoa_schema_ref<>N'config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json'
       OR v.pessoa_schema_sha256<>0xCA606BD59BA75301741579445E95E9C7C7357D95293B327354E69340BC639844))
    THROW 52021,'Contrato SEHAB v5 preexistente com referência/hash divergente.',1;
INSERT ref.gestor_pessoa_versao
    (gestor_id,versao,vigencia_inicio,pessoa_schema_ref,pessoa_schema_sha256,status,ativado_em)
SELECT g.gestor_id,5,'2026-09-21',
       N'config/contracts/gestores/SEHAB/pessoa/v5/pessoa.schema.json',
       0xCA606BD59BA75301741579445E95E9C7C7357D95293B327354E69340BC639844,
       N'RASCUNHO',NULL
FROM ref.gestor g
WHERE g.codigo=N'SEHAB'
  AND NOT EXISTS (SELECT 1 FROM ref.gestor_pessoa_versao v WHERE v.gestor_id=g.gestor_id AND v.versao=5);
COMMIT;

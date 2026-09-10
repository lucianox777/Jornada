SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- Fechamento fail-closed da consolidação do schema operacional v3.70.
-- O marcador só é promovido quando todo o conjunto estrutural introduzido pela
-- identidade progressiva, composição e blocking dinâmico estiver materializado.
DECLARE @missing TABLE(objeto SYSNAME NOT NULL);

INSERT @missing(objeto)
SELECT v.objeto
FROM (VALUES
 (N'identidade.cpf_ancora'),
 (N'identidade.pessoa_origem_progressiva'),
 (N'identidade.pessoa_origem_progressiva_evento'),
 (N'identidade.composicao_uuid_reserva'),
 (N'identidade.composicao_plano'),
 (N'identidade.composicao_aplicacao'),
 (N'identidade.composicao_historico_aplicado'),
 (N'identidade.composicao_recomposicao_plano'),
 (N'identidade.composicao_publicacao'),
 (N'identidade.blocking_chave'),
 (N'identidade.linkage_ruleset'),
 (N'identidade.linkage_ruleset_passe'),
 (N'identidade.linkage_ruleset_passe_campo')
) v(objeto)
WHERE OBJECT_ID(v.objeto, N'U') IS NULL;

IF EXISTS(SELECT 1 FROM @missing)
BEGIN
    DECLARE @lista NVARCHAR(MAX)=(SELECT STRING_AGG(objeto,N', ') FROM @missing);
    DECLARE @mensagem NVARCHAR(2048)=CONCAT(N'Schema v3.70 incompleto. Objetos ausentes: ',@lista);
    THROW 51700, @mensagem, 1;
END;

IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.SolutionSchema', @value=N'3.70';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.SolutionSchema', @value=N'3.70';

IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.BaseNormativa')
    EXEC sys.sp_updateextendedproperty @name=N'Jornada.BaseNormativa', @value=N'3.62';
ELSE
    EXEC sys.sp_addextendedproperty @name=N'Jornada.BaseNormativa', @value=N'3.62';
GO

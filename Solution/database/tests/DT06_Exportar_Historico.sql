-- DT-06: extrair ANTES e DEPOIS da reexecução do runner; comparar bytes de saída.
-- Usar sqlcmd -h -1 -W -s '|' -i database/tests/DT06_Exportar_Historico.sql -o <arquivo_privado>.
SET NOCOUNT ON;
IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
    THROW 51371, 'DT06: captura exige histórico já aplicado.', 1;
IF NOT EXISTS(SELECT 1 FROM ref.gestor WHERE codigo=N'DT06_SENTINELA' AND nome=N'Gestor sintético DT06')
    THROW 51372, 'DT06: sentinela sintética ausente (somente teste).', 1;
SELECT CONCAT(N'LEDGER|',migration_name,N'|',sha256,N'|',CONVERT(nvarchar(33),applied_at,126))
  FROM jornada.schema_migration ORDER BY migration_name;
SELECT CONCAT(N'SENTINELA|',COUNT_BIG(*)) FROM ref.gestor WHERE codigo=N'DT06_SENTINELA';
GO

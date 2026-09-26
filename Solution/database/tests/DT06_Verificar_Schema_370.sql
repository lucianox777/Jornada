-- DT-06: verificador SQL read-only após baseline ou upgrade (sem reset).
SET NOCOUNT ON;
IF OBJECT_ID(N'jornada.schema_migration',N'U') IS NULL
    THROW 51366, 'DT06: ledger ausente.', 1;
IF (SELECT COUNT(*) FROM jornada.schema_migration) <> 51
    THROW 51367, 'DT06: conjunto de migrations incompleto ou inesperado.', 1;
IF EXISTS(SELECT 1 FROM jornada.schema_migration WHERE LEN(sha256)<>64 OR sha256 LIKE '%[^0-9a-f]%')
    THROW 51368, 'DT06: checksum inválido.', 1;
IF COALESCE(CONVERT(nvarchar(32),(SELECT value FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.SolutionSchema')),N'') <> N'3.70'
    THROW 51369, 'DT06: schema esperado 3.70 não confirmado.', 1;
IF OBJECT_ID(N'gold.pessoa',N'U') IS NULL OR OBJECT_ID(N'identidade.identity_map',N'U') IS NULL OR OBJECT_ID(N'bronze.entrega',N'U') IS NULL
    THROW 51370, 'DT06: objetos estruturais obrigatórios ausentes.', 1;
SELECT N'DT06_VERIFY_OK' AS resultado, (SELECT COUNT(*) FROM jornada.schema_migration) AS migrations;
GO

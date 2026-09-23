-- Leitura somente de evidência agregada do último ensaio sintético.
-- Executar SEM limpeza, inclusive quando o TEST do Calibrador reprovar.
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;

IF ISNULL(CONVERT(NVARCHAR(32),(
    SELECT value FROM sys.extended_properties
    WHERE class=0 AND name=N'Jornada.EnvironmentProfile')),N'')<>N'Development'
    THROW 51850,'Diagnostico sintetico permitido somente em Development.',1;
IF OBJECT_ID(N'identidade.modelo_linkage',N'U') IS NULL
    THROW 51851,'Modelo de linkage nao instalado neste banco.',1;

SELECT DB_NAME() AS banco_dev, SYSUTCDATETIME() AS coletado_utc;
SELECT COUNT_BIG(*) AS referencias_ibge_linhas FROM ref.frequencia_nome;
SELECT COUNT_BIG(*) AS referencias_ibge_versoes FROM ref.frequencia_nome_versao;

-- #446: o Worker persiste no registro FALHOU somente métricas agregadas
-- (classes de FP, denominadores, VAL/TEST, limiares congelados), sem CPF/PII.
SELECT TOP(5) versao,status,gerado_em,falha_resumo
FROM identidade.modelo_linkage
WHERE status=N'FALHOU'
ORDER BY gerado_em DESC,versao DESC;

SELECT status,COUNT_BIG(*) AS lotes
FROM ingestao.lote GROUP BY status ORDER BY status;
SELECT erro_codigo,COUNT_BIG(*) AS lotes
FROM ingestao.lote
WHERE erro_codigo IS NOT NULL
GROUP BY erro_codigo ORDER BY lotes DESC,erro_codigo;

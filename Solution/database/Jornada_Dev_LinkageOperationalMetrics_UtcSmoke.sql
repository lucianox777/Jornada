-- Regressao #424: dia/semana sao derivados do instante UTC, nao da data local.
-- SELECT read-only sobre fixture em memoria, nenhum dado persistente e nenhuma PII.
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @cases TABLE (
  origem DATETIMEOFFSET(7) NOT NULL,
  data_utc_esperada DATE NOT NULL,
  semana_utc_esperada DATE NOT NULL
);
INSERT @cases(origem,data_utc_esperada,semana_utc_esperada) VALUES
 ('2026-09-27T23:30:00-03:00','20260928','20260928'), -- Domingo local, segunda UTC.
 ('2026-09-28T00:15:00+02:00','20260927','20260921'), -- Segunda local, domingo UTC.
 ('2026-09-21T00:30:00+02:00','20260920','20260914'), -- Virada anterior.
 ('2026-09-21T00:30:00-03:00','20260921','20260921'), -- Segunda em ambos.
 ('2026-09-28T02:30:00+00:00','20260928','20260928'); -- Mesmo instante da primeira.

IF EXISTS (
 SELECT 1 FROM @cases c
 CROSS APPLY (VALUES (CONVERT(date,SWITCHOFFSET(c.origem,'+00:00')))) AS d(data_utc)
 CROSS APPLY (VALUES (
   DATEADD(DAY,-(DATEDIFF(DAY,CONVERT(date,'19000101',112),d.data_utc)%7),d.data_utc)
 )) AS w(semana_inicio_utc)
 WHERE d.data_utc<>c.data_utc_esperada
    OR w.semana_inicio_utc<>c.semana_utc_esperada
)
 THROW 51882,'Regressao #424: agrupamento diario/semanal nao respeita UTC.',1;

-- Prova da agregacao: instantes equivalentes com fusos distintos formam mesmo bucket.
IF (SELECT COUNT_BIG(*) FROM @cases
    WHERE CONVERT(date,SWITCHOFFSET(origem,'+00:00'))=CONVERT(date,'20260928',112))<>2
 THROW 51883,'Regressao #424: instantes equivalentes separados em dias diferentes.',1;

PRINT 'LINKAGE UTC DAY/WEEK BOUNDARY SMOKE: OK';

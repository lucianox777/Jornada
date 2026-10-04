from __future__ import annotations

import csv
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit("uso: dev-console-apply-births.py <pessoas_verdade.csv> <saida.sql>")

source = pathlib.Path(sys.argv[1])
target = pathlib.Path(sys.argv[2])
rows = list(csv.DictReader(source.open("r", encoding="utf-8-sig", newline="")))
if len(rows) != 30000:
    raise SystemExit(f"esperado 30000 pessoas no corpus demografico; encontrado={len(rows)}")

lines = [
    "SET NOCOUNT ON;",
    "SET XACT_ABORT ON;",
    "SET ANSI_NULLS ON;",
    "SET QUOTED_IDENTIFIER ON;",
    "SET ANSI_PADDING ON;",
    "SET ANSI_WARNINGS ON;",
    "SET ARITHABORT ON;",
    "SET CONCAT_NULL_YIELDS_NULL ON;",
    "SET NUMERIC_ROUNDABORT OFF;",
    "CREATE TABLE #birth(n bigint NOT NULL PRIMARY KEY, nascimento date NOT NULL);",
]

for offset in range(0, len(rows), 500):
    chunk = rows[offset:offset + 500]
    lines.append("INSERT #birth(n,nascimento) VALUES")
    values = []
    for index, row in enumerate(chunk, start=offset + 1):
        birth = row["data_nascimento"].strip()
        values.append(f"({index},CONVERT(date,'{birth}',23))")
    lines.append(",\n".join(values) + ";")

lines.extend([
    """
;WITH truth AS (
  SELECT TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10)) n,vc.pessoa_uuid
  FROM silver.pessoa_observacao o
  JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=o.pessoa_observacao_id
  WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%' AND vc.status=N'RESOLVIDO'
)
UPDATE g
SET data_nascimento=b.nascimento,atualizado_em=SYSUTCDATETIME()
FROM gold.pessoa g
JOIN truth t ON t.pessoa_uuid=g.pessoa_uuid
JOIN #birth b ON b.n=t.n;
""",
    """
UPDATE o
SET data_nascimento=b.nascimento
FROM silver.pessoa_observacao o
JOIN #birth b ON b.n=TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10))
WHERE o.codigo_pessoa_origem LIKE N'SCALE-SEHAB-%';
""",
    """
UPDATE o
SET data_nascimento=
 CASE
  WHEN b.n%29=0 AND DAY(b.nascimento)<=12 AND DAY(b.nascimento)<>MONTH(b.nascimento)
   THEN DATEFROMPARTS(YEAR(b.nascimento),DAY(b.nascimento),MONTH(b.nascimento))
  WHEN b.n%31=0 AND DAY(b.nascimento) BETWEEN 2 AND 27
   THEN DATEADD(DAY,CASE WHEN DAY(b.nascimento)%10 IN(0,9) THEN -1 ELSE 1 END,b.nascimento)
  ELSE b.nascimento END
FROM silver.pessoa_observacao o
JOIN #birth b ON b.n=TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10))
WHERE o.codigo_pessoa_origem LIKE N'SCALE-SMADS-%';
""",
    """
;WITH p AS (
 SELECT o.pessoa_observacao_id,
        TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10)) n,
        ((TRY_CONVERT(bigint,RIGHT(o.codigo_pessoa_origem,10))-1)%30000)+1 truth_n
 FROM silver.pessoa_observacao o
 WHERE o.codigo_pessoa_origem LIKE N'SCALE-PEND-%'
)
UPDATE o
SET data_nascimento=
 CASE
  WHEN p.n%10=0 THEN DATEADD(DAY,CONVERT(int,p.n%365),CONVERT(date,'1900-01-01'))
  WHEN p.n%29=0 AND DAY(b.nascimento)<=12 AND DAY(b.nascimento)<>MONTH(b.nascimento)
   THEN DATEFROMPARTS(YEAR(b.nascimento),DAY(b.nascimento),MONTH(b.nascimento))
  WHEN p.n%31=0 AND DAY(b.nascimento) BETWEEN 2 AND 27
   THEN DATEADD(DAY,CASE WHEN DAY(b.nascimento)%10 IN(0,9) THEN -1 ELSE 1 END,b.nascimento)
  ELSE b.nascimento END
FROM silver.pessoa_observacao o
JOIN p ON p.pessoa_observacao_id=o.pessoa_observacao_id
JOIN #birth b ON b.n=p.truth_n;
""",
])

target.parent.mkdir(parents=True, exist_ok=True)
target.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(target)

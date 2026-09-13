from pathlib import Path

replacements = {
    "Solution/src/Jornada.Api/SqlApiServices.cs": [
        ("reader.GetString(5),\n                    reader.GetInt32(6),", "reader.NullableString(5),\n                    reader.GetInt32(6),"),
        ("        string NomeMae,\n        int FontesDistintas,", "        string? NomeMae,\n        int FontesDistintas,"),
    ],
    "Solution/src/Jornada.Processor.Worker/BlockingProjectionPersistence.cs": [
        ("currentMother = reader.GetString(1);", "currentMother = reader.IsDBNull(1) ? null : reader.GetString(1);"),
    ],
    "Solution/src/Jornada.Linkage.Parameters.Worker/PostgreSqlCandidateSampler.cs": [
        ("mask != plan.Match(date, reader.GetString(1), reader.GetString(3)))", "mask != plan.Match(date, reader.GetString(1), reader.IsDBNull(3) ? null : reader.GetString(3)))"),
    ],
    "Solution/src/Jornada.Linkage.Parameters.Worker/PostgreSqlCandidateUniverse.cs": [
        ("mask != plan.Match(candidateDate, reader.GetString(1), reader.GetString(3)))", "mask != plan.Match(candidateDate, reader.GetString(1), reader.IsDBNull(3) ? null : reader.GetString(3)))"),
    ],
}

for filename, edits in replacements.items():
    path = Path(filename)
    text = path.read_text(encoding="utf-8")
    for old, new in edits:
        count = text.count(old)
        expected = 2 if filename.endswith("BlockingProjectionPersistence.cs") and old == "currentMother = reader.GetString(1);" else 1
        if count != expected:
            raise SystemExit(f"{filename}: esperado {expected}, encontrado {count}: {old}")
        text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")

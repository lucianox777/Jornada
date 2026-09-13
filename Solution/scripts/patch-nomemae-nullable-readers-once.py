from pathlib import Path

replacements = {
    "Solution/src/Jornada.Processor.Worker/SqlProcessorRepository.cs": [
        ("reader.GetString(2));", "reader.IsDBNull(2) ? null : reader.GetString(2));"),
        ("silverReader.GetString(2));", "silverReader.IsDBNull(2) ? null : silverReader.GetString(2));"),
    ],
    "Solution/src/Jornada.Processor.Worker/PostgreSqlIdentityMapRepository.cs": [
        ("return new IdentityCore(reader.GetString(0), ReadDate(reader, 1), reader.GetString(2));", "return new IdentityCore(reader.GetString(0), ReadDate(reader, 1), reader.IsDBNull(2) ? null : reader.GetString(2));"),
        ("return new IdentityCore(silverReader.GetString(0), ReadDate(silverReader, 1), silverReader.GetString(2));", "return new IdentityCore(silverReader.GetString(0), ReadDate(silverReader, 1), silverReader.IsDBNull(2) ? null : silverReader.GetString(2));"),
    ],
    "Solution/src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs": [
        ("reader.GetString(3)));", "reader.IsDBNull(3) ? null : reader.GetString(3)));"),
    ],
    "Solution/src/Jornada.Linkage.Runner/PostgreSqlProbabilisticIdentityLinkage.cs": [
        ("reader.GetString(3)));", "reader.IsDBNull(3) ? null : reader.GetString(3)));"),
    ],
    "Solution/src/Jornada.Linkage.Runner/BlockingProjectionCandidateLoader.cs": [
        ("reader.GetString(3)));", "reader.IsDBNull(3) ? null : reader.GetString(3)));"),
    ],
}

for filename, edits in replacements.items():
    path = Path(filename)
    text = path.read_text(encoding="utf-8")
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            raise SystemExit(f"{filename}: esperado 1 ocorrência, encontrado {count}: {old}")
        text = text.replace(old, new)
    path.write_text(text, encoding="utf-8")

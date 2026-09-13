from pathlib import Path

replacements = {
    "Solution/src/Jornada.Processor.Worker/SqlProcessorRepository.Persistence.cs": [
        (
            "var maeCmp = IdentityComparison.NormalizeText(person.NomeMae) ?? person.NomeMae.ToUpperInvariant();",
            "var maeCmp = IdentityComparison.NormalizeText(person.NomeMae);",
        ),
        (
            'insert.Parameters.Add(new SqlParameter("@mae", SqlDbType.NVarChar, 500) { Value = person.NomeMae });',
            'insert.Parameters.Add(new SqlParameter("@mae", SqlDbType.NVarChar, 500) { Value = (object?)person.NomeMae ?? DBNull.Value });',
        ),
        (
            'insert.Parameters.Add(new SqlParameter("@mae_cmp", SqlDbType.NVarChar, 500) { Value = maeCmp });',
            'insert.Parameters.Add(new SqlParameter("@mae_cmp", SqlDbType.NVarChar, 500) { Value = (object?)maeCmp ?? DBNull.Value });',
        ),
    ],
    "Solution/src/Jornada.Processor.Worker/PostgreSqlProcessorRepository.cs": [
        (
            "var maeCmp = IdentityComparison.NormalizeText(person.NomeMae) ?? person.NomeMae.ToUpperInvariant();",
            "var maeCmp = IdentityComparison.NormalizeText(person.NomeMae);",
        ),
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

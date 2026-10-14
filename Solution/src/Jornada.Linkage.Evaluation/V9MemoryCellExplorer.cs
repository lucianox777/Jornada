using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Pure in-memory diagnostic candidate explorer. Never resolves or promotes identities.
/// All comparisons are within the same TRAIN or VALIDATION partition and across gestores.
/// </summary>
public static class V9MemoryCellExplorer
{
    public const string Version = "V9_MEMORY_CELL_EXPLORER_V2_NORMALIZED";

    public sealed record Row(int Id, string Partition, string Gestor, string? Person,
        string? Mother, DateTime? Birth, int BasePersonId);

    public sealed record Cell(string Name, Func<Row, string?> Key);

    public sealed record CellResult(string Cell, string Partition, long M, long U,
        long EligibleTruthM, decimal RecallEligible, decimal HypotheticalFdr,
        int P95U, int P99U);

    public static IReadOnlyList<Cell> DefaultCells() =>
    [
        new("PERSON_EXACT_DOB", r => Join(Normal(r.Person), Date(r.Birth))),
        new("PERSON_EXACT_MOTHER", r => Join(Normal(r.Person), Normal(r.Mother))),
        new("PERSON_PREFIX3_DOB", r => Join(Prefix(r.Person), Date(r.Birth))),
        new("PERSON_PREFIX3_MOTHER_PREFIX3", r => Join(Prefix(r.Person), Prefix(r.Mother))),
        new("PERSON_NO_PARTICLES_MOTHER", r => Join(NoParticles(r.Person), NoParticles(r.Mother))),
        new("PERSON_METAPHONE_MOTHER", r => Join(Phonetic(r.Person), Phonetic(r.Mother))),
        new("PERSON_METAPHONE_DOB", r => Join(Phonetic(r.Person), Date(r.Birth)))
    ];

    public static IReadOnlyList<CellResult> Analyze(IReadOnlyList<Row> rows,
        IReadOnlyList<Cell>? cells = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        cells ??= DefaultCells();
        if (cells.Count == 0 || cells.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name)))
            throw new InvalidDataException("Células diagnósticas inválidas.");
        var ids = new HashSet<int>();
        foreach (var r in rows)
        {
            if (r is null || r.Id < 0 || !ids.Add(r.Id)
                || r.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(r.Gestor) || r.BasePersonId < 0)
                throw new InvalidDataException("Observação inválida ou TEST proibido.");
        }
        var output = new List<CellResult>();
        foreach (var partition in new[] { "TRAIN", "VALIDATION" })
        {
            var eligible = rows.Where(r => r.Partition == partition
                && V9MinimumEvidenceEligibility.EvaluatePersonRequired(r.Person, r.Mother, r.Birth).Eligible)
                .ToArray();
            long truth = 0;
            for (var i = 0; i < eligible.Length; i++)
                for (var j = i + 1; j < eligible.Length; j++)
                    if (eligible[i].Gestor != eligible[j].Gestor
                        && eligible[i].BasePersonId == eligible[j].BasePersonId) truth++;
            foreach (var cell in cells)
            {
                long m = 0, u = 0;
                var degree = eligible.ToDictionary(r => r.Id, _ => 0);
                foreach (var group in eligible.Select(r => (Row: r, Key: cell.Key(r)))
                    .Where(x => x.Key is not null)
                    .GroupBy(x => x.Key!, StringComparer.Ordinal))
                {
                    var members = group.Select(x => x.Row).ToArray();
                    for (var i = 0; i < members.Length; i++)
                        for (var j = i + 1; j < members.Length; j++)
                        {
                            var a = members[i]; var b = members[j];
                            if (a.Gestor == b.Gestor) continue;
                            if (a.BasePersonId == b.BasePersonId) m++;
                            else { u++; degree[a.Id]++; degree[b.Id]++; }
                        }
                }
                var sorted = degree.Values.Order().ToArray();
                static int Percentile(int[] values, int pct)
                    => values.Length == 0 ? 0 : values[(int)Math.Ceiling(values.Length * pct / 100.0) - 1];
                output.Add(new CellResult(cell.Name, partition, m, u, truth,
                    truth == 0 ? 0m : (decimal)m / truth,
                    m + u == 0 ? 0m : (decimal)u / (m + u),
                    Percentile(sorted, 95), Percentile(sorted, 99)));
            }
        }
        return output.AsReadOnly();
    }

    private static string? Normal(string? value) => V9MinimumEvidenceEligibility.HasName(value)
        ? PersonNameBasicNormalization.Project(value)?.UpperNoDiacritics : null;

    private static string? NoParticles(string? value) => V9MinimumEvidenceEligibility.HasName(value)
        ? PersonNameBasicNormalization.Project(value)?.WithoutPortugueseParticles : null;

    private static string? Phonetic(string? value) => V9MinimumEvidenceEligibility.HasName(value)
        ? MetaphoneBr.Encode(value) : null;

    private static string? Prefix(string? value)
    {
        if (!V9MinimumEvidenceEligibility.HasName(value)) return null;
        var letters = new string((Normal(value) ?? string.Empty).Where(char.IsLetter).ToArray());
        return letters.Length >= 3 ? letters[..3] : null;
    }

    private static string? Date(DateTime? value)
        => V9MinimumEvidenceEligibility.HasBirth(value)
            ? value!.Value.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) : null;

    private static string? Join(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)
            ? null : left.Trim().ToUpperInvariant() + "\u001f" + right.Trim().ToUpperInvariant();
}

using Jornada.Contracts;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Candidate-only, additive three-attribute blocking prototype. Its output can be
/// UNIONed with the published dynamic ruleset; it never authorizes identity decisions.
/// </summary>
public static class CombinedIdentityCandidatePlanner
{
    public const string MethodVersion = "COMBINED_IDENTITY_CANDIDATES_V1";

    public static IReadOnlyList<BlockingCandidatePassLookup> Plan(IdentityObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!string.IsNullOrWhiteSpace(observation.Cpf))
            throw new InvalidOperationException("CPF observations must use the deterministic route.");
        if (observation.DataNascimento is not { } birth)
            return Array.Empty<BlockingCandidatePassLookup>();

        var projected = BlockingProjectionKeyProjector.Project(
            observation.NomeCompleto, observation.NomeMae, birth);
        var byFeature = projected.GroupBy(k => k.Feature, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(k => k.Value).Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        if (!byFeature.ContainsKey(BlockingFeatureNames.FullName) ||
            !byFeature.ContainsKey(BlockingFeatureNames.MotherFullName))
            return Array.Empty<BlockingCandidatePassLookup>();

        var passes = new List<BlockingCandidatePassLookup>();
        void Add(string id, string personFeature, string motherFeature, IReadOnlyList<string> years,
            IReadOnlyList<string> months, IReadOnlyList<string> days)
        {
            if (!byFeature.TryGetValue(personFeature, out var person) ||
                !byFeature.TryGetValue(motherFeature, out var mother)) return;
            passes.Add(new BlockingCandidatePassLookup(id, new[]
            {
                new BlockingCandidateClause(personFeature, person),
                new BlockingCandidateClause(motherFeature, mother),
                new BlockingCandidateClause(BlockingFeatureNames.BirthYear, years),
                new BlockingCandidateClause(BlockingFeatureNames.BirthMonth, months),
                new BlockingCandidateClause(BlockingFeatureNames.BirthDay, days)
            }));
        }
        static string N(int value, int width) => value.ToString("D" + width,
            System.Globalization.CultureInfo.InvariantCulture);
        var year = new[] { N(birth.Year, 4) };
        var month = new[] { N(birth.Month, 2) };
        var day = new[] { N(birth.Day, 2) };
        Add("combined-exact", BlockingFeatureNames.FullName,
            BlockingFeatureNames.MotherFullName, year, month, day);

        // Valid day/month transpose; no invented impossible dates.
        if (birth.Day <= 12 && birth.Day != birth.Month &&
            DateTime.DaysInMonth(birth.Year, birth.Day) >= birth.Month)
            Add("combined-day-month-transpose", BlockingFeatureNames.FullName,
                BlockingFeatureNames.MotherFullName, year,
                new[] { N(birth.Day, 2) }, new[] { N(birth.Month, 2) });

        var neighborYears = new[] { birth.Year - 1, birth.Year + 1 }
            .Where(y => y is >= 1 and <= 9999 && birth.Day <= DateTime.DaysInMonth(y, birth.Month))
            .Select(y => N(y, 4)).ToArray();
        if (neighborYears.Length > 0)
            Add("combined-neighbor-year", BlockingFeatureNames.FullName,
                BlockingFeatureNames.MotherFullName, neighborYears, month, day);

        // Alternative name representations are separate indexable passes.
        Add("combined-name-phonetic", BlockingFeatureNames.FullNamePhoneticPtBr,
            BlockingFeatureNames.MotherFullName, year, month, day);
        Add("combined-mother-phonetic", BlockingFeatureNames.FullName,
            BlockingFeatureNames.MotherFullNamePhoneticPtBr, year, month, day);
        return passes;
    }
}

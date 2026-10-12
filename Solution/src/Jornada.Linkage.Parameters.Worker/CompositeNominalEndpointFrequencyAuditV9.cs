using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Aggregate endpoint-appearance counts of the SQL-conditioned *candidate
/// pair sample*. NOT a population distribution, not a term frequency
/// reference, and not a calibrated correction weight. No nominal keys
/// are retained in this exported DTO.
/// </summary>
public sealed record CompositeNominalEndpointUnitAuditV9(
    string Attribute,
    NominalFrequencyUnitV9 Unit,
    long CandidatePairs,
    long ObservedEndpoints,
    long MissingEndpoints,
    long DistinctObservedValues,
    long MaximumEndpointAppearances,
    long PairUnitAgreements);

public sealed record CompositeNominalEndpointFrequencyAuditV9(
    string ContractVersion,
    string Status,
    IReadOnlyList<CompositeNominalEndpointUnitAuditV9> Units);

/// <summary>
/// Detects whether FULL, FIRST+LAST and marginal units are present in a
/// blocking-conditioned sample without falsely calling their appearance
/// rates a population frequency. Every row is aggregate-only.
/// </summary>
public static class CompositeNominalEndpointFrequencyAuditBuilderV9
{
    public const string Version = "NOMINAL_ENDPOINT_FREQUENCY_AUDIT_V9_1";
    public const string Status = "DIAGNOSTIC_ONLY_ENDPOINT_APPEARANCES_NOT_POPULATION_TF";
    private static readonly NominalFrequencyUnitV9[] Units =
        Enum.GetValues<NominalFrequencyUnitV9>();

    public static CompositeNominalEndpointFrequencyAuditV9 Analyze(
        IReadOnlyList<IdentityTrainingPair> conditionedUnmatchedPairs)
    {
        ArgumentNullException.ThrowIfNull(conditionedUnmatchedPairs);
        if (conditionedUnmatchedPairs.Count == 0)
            throw new InvalidOperationException("TF V9 não pode diagnosticar corpus U vazio.");
        var results = new List<CompositeNominalEndpointUnitAuditV9>(8);
        AnalyzeAttribute("NOME_V9", static pair => pair.LeftName,
            static pair => pair.RightName);
        AnalyzeAttribute("NOME_MAE_V9", static pair => pair.LeftMotherName,
            static pair => pair.RightMotherName);
        return new CompositeNominalEndpointFrequencyAuditV9(
            Version, Status, Array.AsReadOnly(results.ToArray()));

        void AnalyzeAttribute(
            string attribute,
            Func<IdentityTrainingPair, string?> left,
            Func<IdentityTrainingPair, string?> right)
        {
            foreach (var unit in Units)
            {
                // Transient, in-memory histogram. Do not return, log,
                // persist or hash raw names or joint value keys.
                var histogram = new Dictionary<string, long>(StringComparer.Ordinal);
                long observed = 0;
                long missing = 0;
                long agreements = 0;
                foreach (var pair in conditionedUnmatchedPairs)
                {
                    if (pair is null)
                        throw new InvalidOperationException("Par TF V9 nulo.");
                    var a = Key(left(pair), unit);
                    var b = Key(right(pair), unit);
                    if (a is null) missing++;
                    else
                    {
                        observed++;
                        histogram.TryGetValue(a, out var count);
                        checked { histogram[a] = count + 1; }
                    }
                    if (b is null) missing++;
                    else
                    {
                        observed++;
                        histogram.TryGetValue(b, out var count);
                        checked { histogram[b] = count + 1; }
                    }
                    if (a is not null && b is not null &&
                        string.Equals(a, b, StringComparison.Ordinal))
                        agreements++;
                }
                results.Add(new CompositeNominalEndpointUnitAuditV9(
                    attribute, unit, conditionedUnmatchedPairs.Count,
                    observed, missing, histogram.Count,
                    histogram.Count == 0 ? 0 : histogram.Values.Max(),
                    agreements));
            }
        }
    }

    private static string? Key(string? name, NominalFrequencyUnitV9 unit)
    {
        var projected = BrazilianNameComponents.Project(name);
        if (projected is null)
            return null;
        var tokens = projected.NormalizedFull.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstIndex = projected.TitlePrefix is null ? 0 : 1;
        var first = firstIndex >= tokens.Length ? null :
            tokens[firstIndex].Trim('.', ',');
        if (string.IsNullOrWhiteSpace(first))
            first = null;

        return unit switch
        {
            NominalFrequencyUnitV9.FullName => projected.NormalizedFull,
            NominalFrequencyUnitV9.FirstLastJoint =>
                first is not null && projected.LastContentSurname is not null
                    ? string.Concat(first, "\u001F", projected.LastContentSurname)
                    : null,
            NominalFrequencyUnitV9.FirstName => first,
            NominalFrequencyUnitV9.LastContentSurname => projected.LastContentSurname,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unidade TF V9 desconhecida.")
        };
    }
}

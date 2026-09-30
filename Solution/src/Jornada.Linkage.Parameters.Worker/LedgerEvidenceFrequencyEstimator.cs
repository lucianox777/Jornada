using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum LedgerEvidenceState { Exact, Disagree, Missing }

public sealed record LedgerEvidenceFrequency(
    string Attribute,
    long MatchExact,
    long MatchDisagree,
    long MatchMissing,
    long NonMatchExact,
    long NonMatchDisagree,
    long NonMatchMissing,
    decimal? MExact,
    decimal? UExact,
    decimal? ExactLogLikelihoodRatio);

/// <summary>
/// Mede evidência complementar do Ledger no mesmo universo M/U entregue ao calibrador.
/// MISSING é observado para suporte, mas permanece neutro no scorer. Probabilidades/LLR
/// só existem quando há pares comparáveis (ambos os lados presentes).
/// </summary>
public static class LedgerEvidenceFrequencyEstimator
{
    public const string MethodVersion = "LEDGER_EVIDENCE_FREQUENCY_FS_V1";

    public static IReadOnlyList<LedgerEvidenceFrequency> Estimate(
        IReadOnlyList<IdentityTrainingPair> matchedPairs,
        IReadOnlyList<IdentityTrainingPair> unmatchedPairs,
        decimal smoothingAlpha)
    {
        ArgumentNullException.ThrowIfNull(matchedPairs);
        ArgumentNullException.ThrowIfNull(unmatchedPairs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(smoothingAlpha);

        var attributes = matchedPairs.Concat(unmatchedPairs)
            .SelectMany(static p => Values(p.LeftResolutionValues).Concat(Values(p.RightResolutionValues)))
            .Select(static v => ResolutionSourceField.Canonicalize(v.Attribute))
            .Where(PersonResolutionAttributeCatalog.IsEligible)
            .Where(static code => code != PersonResolutionAttributeCatalog.FullName
                && code != PersonResolutionAttributeCatalog.MotherName
                && code != PersonResolutionAttributeCatalog.BirthDate)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();

        return attributes.Select(attribute =>
        {
            var m = Count(matchedPairs, attribute);
            var u = Count(unmatchedPairs, attribute);
            var mComparable = m.Exact + m.Disagree;
            var uComparable = u.Exact + u.Disagree;
            if (mComparable == 0 || uComparable == 0)
                return new LedgerEvidenceFrequency(attribute, m.Exact, m.Disagree, m.Missing,
                    u.Exact, u.Disagree, u.Missing, null, null, null);

            var mExact = (m.Exact + smoothingAlpha) / (mComparable + 2m * smoothingAlpha);
            var uExact = (u.Exact + smoothingAlpha) / (uComparable + 2m * smoothingAlpha);
            var llr = Convert.ToDecimal(Math.Log(Convert.ToDouble(mExact / uExact)));
            return new LedgerEvidenceFrequency(attribute, m.Exact, m.Disagree, m.Missing,
                u.Exact, u.Disagree, u.Missing, mExact, uExact, llr);
        }).ToArray();
    }

    public static LedgerEvidenceState Compare(
        IdentityTrainingPair pair,
        string attribute)
    {
        var left = CanonicalValues(pair.LeftResolutionValues, attribute);
        var right = CanonicalValues(pair.RightResolutionValues, attribute);
        if (left.Count == 0 || right.Count == 0)
            return LedgerEvidenceState.Missing;
        return left.Overlaps(right) ? LedgerEvidenceState.Exact : LedgerEvidenceState.Disagree;
    }

    private static (long Exact, long Disagree, long Missing) Count(
        IEnumerable<IdentityTrainingPair> pairs,
        string attribute)
    {
        long exact = 0, disagree = 0, missing = 0;
        foreach (var pair in pairs)
        {
            switch (Compare(pair, attribute))
            {
                case LedgerEvidenceState.Exact: exact++; break;
                case LedgerEvidenceState.Disagree: disagree++; break;
                default: missing++; break;
            }
        }
        return (exact, disagree, missing);
    }

    private static HashSet<string> CanonicalValues(
        IReadOnlyList<ResolutionSourceValue>? values,
        string attribute)
    {
        var source = Values(values)
            .Where(v => string.Equals(
                ResolutionSourceField.Canonicalize(v.Attribute), attribute, StringComparison.Ordinal))
            .ToArray();
        if (source.Length == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        if (!PersonResolutionAttributeCatalog.TryGet(attribute, out var contract))
            return new HashSet<string>(StringComparer.Ordinal);

        return source
            .Select(value => CanonicalizeEvidenceValue(contract.Semantic, value.Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? CanonicalizeEvidenceValue(ResolutionAttributeSemantic semantic, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return semantic switch
            {
                ResolutionAttributeSemantic.Phone => ContactCanonicalization.NormalizeBrazilianPhoneV2(value),
                ResolutionAttributeSemantic.Email => ContactCanonicalization.NormalizeEmailV2(value),
                ResolutionAttributeSemantic.PersonName => IdentityComparison.NormalizeText(value),
                _ => value.Trim()
            };
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IEnumerable<ResolutionSourceValue> Values(
        IReadOnlyList<ResolutionSourceValue>? values) =>
        values ?? Array.Empty<ResolutionSourceValue>();
}

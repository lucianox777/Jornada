using System.Globalization;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Additional, opt-in exploratory risk-preference suggestion. Does not change
/// the Pareto V3 ideal-distance winner or any operational eligibility gate.
/// CP estimates are descriptive until identity-cluster assumptions are proved.
/// </summary>
public static class V9ExploratoryRiskPreferenceV1
{
    public const string Version = "V9_EXPLORATORY_CP_RISK_BAND_V1";
    public const decimal BootstrapTolerancePercentagePoints = 0.05m;

    // The new policy is explicit in new frozen plans. Existing plans without
    // this property retain their historical hashes and V3-only behavior.
    public sealed record Policy(decimal FdrUpperTolerancePp = BootstrapTolerancePercentagePoints);

    public sealed record Result(
        string Version, decimal FdrUpperTolerancePp,
        double? MinimumConditionalClusterCpUpper,
        double? RiskBandUpperExclusive,
        IReadOnlyList<string> RiskBandOptionIds,
        string? SuggestedOptionId,
        string Status);

    public static void ValidatePolicy(Policy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.FdrUpperTolerancePp is < 0m or > 100m)
            throw new InvalidDataException("Tolerancia exploratoria FDR exige 0 a 100 pontos percentuais.");
    }

    private static bool AllowedUncertifiedReason(string reason) => reason is
        "RECALL_ABAIXO_DA_POLITICA" or "P95_FISICO_NAO_ATESTADO"
        or "P95_ACIMA_DO_LIMITE" or "EVIDENCIA_ESTATISTICA_INSUFICIENTE"
        or "LIMITE_FDR_ACIMA_DA_POLITICA" or "POLITICA_DE_RISCO_NAO_DECLARADA"
        or "FANOUT_AUSENTE_OU_ACIMA_DA_POLITICA";

    private static string? BaseOptionId(string configurationId)
    {
        var separator = configurationId.IndexOf(':');
        if (separator <= 0
            || !int.TryParse(configurationId.AsSpan(0, separator), NumberStyles.None,
                CultureInfo.InvariantCulture, out var length)
            || length <= 0 || length > configurationId.Length - separator - 2)
            return null;
        var last = separator + 1 + length;
        if (configurationId[last] != '|') return null;
        var physicalId = configurationId.Substring(separator + 1, length);
        // The adaptive SQL calibrator prefixes each physical option with the
        // SHA-256 of its blocking ruleset. The model plan stores only the
        // declared option ID; parse the physical prefix after the length
        // envelope, never by splitting on arbitrary user-provided colons.
        if (physicalId.Length > 65 && physicalId[64] == ':'
            && V9BootstrapPareto2D.Hash(physicalId[..64]))
            return physicalId[65..];
        return physicalId;
    }

    private static int ActiveTfUnits(V9FsAdaptiveOptionConfiguration option) =>
        option.UnitTfOptions is { } units
            ? units.Count(u => u.Weight > 0m)
            : option.TfWeight > 0m ? 1 : 0;

    /// <summary>
    /// A single minimum CP reference prevents non-transitive pairwise tolerance.
    /// Only structurally sound observed candidates with a valid numeric CP are
    /// ranked; statistical / latency gates remain independent and unchanged.
    /// </summary>
    public static Result Evaluate(Policy policy, V9BootstrapPareto2D.Result pareto,
        IReadOnlyList<V9FsAdaptiveOptionConfiguration> modelOptions, bool wholeSearch = true)
    {
        ValidatePolicy(policy);
        ArgumentNullException.ThrowIfNull(pareto);
        ArgumentNullException.ThrowIfNull(modelOptions);
        if (!wholeSearch)
            return new(Version, policy.FdrUpperTolerancePp, null, null, [],
                null, "PARTIAL_SEARCH_NO_GLOBAL_RISK_SUGGESTION");

        var models = modelOptions.Where(x => x is not null && x.EligibleForSelection)
            .ToDictionary(x => x.OptionId, StringComparer.Ordinal);

        var candidates = pareto.Table.Select(entry =>
        {
            var p = entry.Option;
            var id = BaseOptionId(p.OptionId);
            return (Entry: entry, Option: p, BaseId: id,
                Model: id is null || !models.TryGetValue(id, out var model) ? null : model);
        }).Where(x =>
            x.Model is not null
            && x.Entry.ExclusionReasons.All(AllowedUncertifiedReason)
            && V9BootstrapPareto2D.Hash(x.Option.ConfigurationFingerprintSha256)
            && double.IsFinite(x.Option.GlobalRecall) && x.Option.GlobalRecall is >= 0 and <= 1
            && x.Option.IdentityClusterFdrUpper is { } cp && double.IsFinite(cp)
            && cp is > 0 and <= 1
            && x.Option.AcceptedFdr is { } fdr && double.IsFinite(fdr)
            && fdr is >= 0 and <= 1 && cp >= fdr
            && x.Option.GlobalEligibleTruth > 0 && x.Option.TrueAccepted >= 0
            && x.Option.FalseAccepted >= 0
            && x.Option.TrueAccepted <= x.Option.GlobalEligibleTruth
            && x.Option.TrueAccepted <= long.MaxValue - x.Option.FalseAccepted
            && x.Option.TrueAccepted + x.Option.FalseAccepted > 0
            && Math.Abs(x.Option.GlobalRecall
                - (double)x.Option.TrueAccepted / x.Option.GlobalEligibleTruth) <= 1e-10
            && Math.Abs(fdr - (double)x.Option.FalseAccepted
                / (x.Option.TrueAccepted + x.Option.FalseAccepted)) <= 1e-10)
            .ToArray();

        if (candidates.Length == 0)
            return new(Version, policy.FdrUpperTolerancePp, null, null, [],
                null, "NO_VALID_EXPLORATORY_CP_OBSERVATION");

        var minimumCp = candidates.Min(x => (decimal)x.Option.IdentityClusterFdrUpper!.Value);
        var tolerance = policy.FdrUpperTolerancePp / 100m;
        var ceiling = Math.Min(1m, minimumCp + tolerance);
        // Strictly below the boundary (and always include the minimum).
        // With a zero tolerance, only exact minimum-CP configurations enter.
        var band = candidates.Where(x =>
        {
            var cp = (decimal)x.Option.IdentityClusterFdrUpper!.Value;
            return cp == minimumCp || (tolerance > 0m && cp < ceiling);
        }).OrderByDescending(x => x.Option.GlobalRecall)
          .ThenBy(x => ActiveTfUnits(x.Model!))
          .ThenBy(x => x.Option.OptionId, StringComparer.Ordinal)
          .ToArray();

        return new(Version, policy.FdrUpperTolerancePp, (double)minimumCp,
            (double)ceiling, Array.AsReadOnly(band.Select(x => x.Option.OptionId).ToArray()),
            band[0].Option.OptionId, "EXPLORATORY_RISK_PREFERENCE_NOT_CERTIFIED");
    }
}

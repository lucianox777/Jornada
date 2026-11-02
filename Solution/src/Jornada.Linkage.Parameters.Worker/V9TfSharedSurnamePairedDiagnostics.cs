using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Physical VALIDATION diagnostics only. No TEST, Pareto, finalist or activation output.
/// All rows are joined by the exact SQL-ruleset SHA, option, margin and grouping.
/// </summary>
public static class V9TfSharedSurnamePairedDiagnostics
{
    private const string SharedPrefix = "TF_SHARED_PL_ML_";
    private const string TwoPrefix = "TF_TWO_PL_ML_";

    public sealed record Metrics(double Recall, double? Fdr, long FalseAcceptedSources, long PendingSources);

    public sealed record Row(
        string RulesetSha256, string DiagnosticOptionId, string BilateralOptionId,
        decimal ConflictMargin, string CandidateGrouping, string CandidatePairsSha256,
        long SharedSurnameCandidatePairs, bool SharedSurnameCountMeasured,
        Metrics Shared, Metrics Bilateral,
        double DeltaRecall, double? DeltaFdr,
        long DeltaFalseAcceptedSources, long DeltaPendingSources,
        long? SharedFalseCreations, long? BilateralFalseCreations,
        bool FalseCreationsMeasured, bool EligibleForSelection);

    /// <summary>Checks the exact four diagnostic arms against their selectable counterparts.</summary>
    public static void ValidateOptions(IReadOnlyList<V9FsAdaptiveOptionConfiguration> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var diagnostics = options.Where(x => !x.EligibleForSelection).ToArray();
        if (diagnostics.Length == 0) return; // Generic non-TF studies keep their existing contract.
        if (options.Count != 45 || diagnostics.Length != 4
            || options.Count(x => x.EligibleForSelection) != 41
            || options.Count(x => x.EligibleForSelection && x.OptionId == "T0.95_TF0") != 1
            || diagnostics.Any(x => !x.OptionId.StartsWith(SharedPrefix, StringComparison.Ordinal)))
            throw new InvalidDataException("TF V9 exige 41 opções selecionáveis (inclusive TF0) e 4 diagnósticos exclusivos.");
        foreach (var diagnostic in diagnostics)
        {
            var bilateralId = TwoPrefix + diagnostic.OptionId[SharedPrefix.Length..];
            var bilateral = options.SingleOrDefault(x => x.OptionId == bilateralId && x.EligibleForSelection)
                ?? throw new InvalidDataException("Diagnóstico SHARED sem par bilateral elegível.");
            if (diagnostic.SharedSurnameMode != V9SharedSurnameModeV9.PersonSideOnlyWhenShared
                || bilateral.SharedSurnameMode != V9SharedSurnameModeV9.Bilateral
                || V9FellegiSunterAdaptiveCalibrationAdapter.ConfigurationFingerprintSha256(
                    diagnostic with { OptionId = bilateralId,
                        SharedSurnameMode = V9SharedSurnameModeV9.Bilateral, EligibleForSelection = true })
                  != bilateral.FingerprintSha256)
                throw new InvalidDataException("Par TF SHARED/BILATERAL diverge além da política de sobrenome.");
        }
    }

    private static (string Ruleset, string Option) ParseQualifiedOption(string qualified)
    {
        if (qualified is null || qualified.Length <= 65 || qualified[64] != ':'
            || !qualified.AsSpan(0, 64).ToString().All(Uri.IsHexDigit))
            throw new InvalidDataException("Identificador TF sem SHA físico de ruleset.");
        return (qualified[..64], qualified[65..]);
    }

    public static IReadOnlyList<Row> Build(
        IReadOnlyList<V9FsAdaptiveOptionConfiguration> options,
        IReadOnlyList<V9PhysicalSourceResolveMetric> measurements,
        IReadOnlyDictionary<string, long> sharedSurnamePairsByOption,
        IReadOnlyDictionary<string, string> candidatePairsSha256ByRuleset)
    {
        ValidateOptions(options);
        var diagnosticIds = options.Where(x => !x.EligibleForSelection)
            .Select(x => x.OptionId).ToHashSet(StringComparer.Ordinal);
        if (diagnosticIds.Count == 0) return [];
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(sharedSurnamePairsByOption);
        ArgumentNullException.ThrowIfNull(candidatePairsSha256ByRuleset);
        var keyed = new Dictionary<(string Option, decimal Margin, string Grouping), V9PhysicalSourceResolveMetric>();
        foreach (var row in measurements)
        {
            _ = ParseQualifiedOption(row.OptionId);
            if (!keyed.TryAdd((row.OptionId, row.ConflictMargin, row.CandidateGrouping), row))
                throw new InvalidDataException("Medição física TF duplicada.");
        }
        var result = new List<Row>();
        foreach (var diagnostic in measurements.Where(x =>
            diagnosticIds.Contains(ParseQualifiedOption(x.OptionId).Option))
            .OrderBy(x => x.OptionId, StringComparer.Ordinal)
            .ThenBy(x => x.ConflictMargin).ThenBy(x => x.CandidateGrouping, StringComparer.Ordinal))
        {
            var (ruleset, id) = ParseQualifiedOption(diagnostic.OptionId);
            var bilateralId = TwoPrefix + id[SharedPrefix.Length..];
            var bilateralQualified = ruleset + ":" + bilateralId;
            if (!candidatePairsSha256ByRuleset.TryGetValue(ruleset, out var candidateSha)
                || candidateSha is not { Length: 64 }
                || !candidateSha.All(Uri.IsHexDigit))
                throw new InvalidDataException("SHA das arestas físicas SQL ausente para o par TF.");
            if (!keyed.TryGetValue((bilateralQualified, diagnostic.ConflictMargin,
                    diagnostic.CandidateGrouping), out var bilateral)
                || !sharedSurnamePairsByOption.TryGetValue(diagnostic.OptionId, out var sharedPairs)
                || !sharedSurnamePairsByOption.TryGetValue(bilateralQualified, out var bilateralPairs)
                || sharedPairs < 0 || bilateralPairs < 0 || sharedPairs != bilateralPairs)
                throw new InvalidDataException("Par TF não foi medido no mesmo universo SQL, com contagem física compatível.");
            var shared = new Metrics(diagnostic.GlobalSourceRecall, diagnostic.FdrAfterResolve,
                diagnostic.FalseAcceptedSourceObservations, diagnostic.Unresolved);
            var control = new Metrics(bilateral.GlobalSourceRecall, bilateral.FdrAfterResolve,
                bilateral.FalseAcceptedSourceObservations, bilateral.Unresolved);
            result.Add(new Row(ruleset, id, bilateralId, diagnostic.ConflictMargin,
                diagnostic.CandidateGrouping, candidateSha, sharedPairs, true, shared, control,
                shared.Recall - control.Recall,
                shared.Fdr.HasValue && control.Fdr.HasValue ? shared.Fdr.Value - control.Fdr.Value : null,
                shared.FalseAcceptedSources - control.FalseAcceptedSources,
                shared.PendingSources - control.PendingSources,
                // False links are NOT the same as false identity creations.
                // A creation ledger is unavailable to this non-publishing preview.
                null, null, false, false));
        }
        if (result.Count == 0)
            throw new InvalidDataException("Nenhum dos quatro diagnósticos TF recebeu medição física.");
        // A full ruleset must contain all four diagnostic arms for every
        // observed (margin, grouping) cell, not only one conveniently available pair.
        foreach (var group in result.GroupBy(x => (x.RulesetSha256, x.ConflictMargin, x.CandidateGrouping)))
            if (group.Count() != diagnosticIds.Count)
                throw new InvalidDataException("Célula física com diagnósticos TF incompletos.");
        return result.AsReadOnly();
    }
}

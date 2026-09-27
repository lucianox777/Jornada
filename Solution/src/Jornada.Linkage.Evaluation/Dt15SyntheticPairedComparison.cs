using Jornada.Contracts;

namespace Jornada.Linkage.Evaluation;

/// <summary>
/// DT-15: read-only, same-corpus synthetic ACTIVE×DRAFT FS evaluation.
/// This artifact measures aggregate *frozen model* decisions on identical
/// deterministic validation/test scenario IDs, not a production authorization
/// or a per-observation transition ledger.
/// </summary>
public static class Dt15SyntheticPairedComparison
{
    public const string MethodVersion = "DT15_SYNTHETIC_FS_PAIRED_V1";
    public const string Comparable = "COMPARAVEL_APENAS_FS_SINTETICO_AGREGADO";
    public const string NotComparable = "NAO_COMPARAVEL";
    public const string Incomplete = "INCOMPLETO";

    public static Dt15SyntheticPairedDossier Compare(
        Dt15SyntheticRunFacts active, Dt15SyntheticRunFacts draft)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(draft);

        Dt15SyntheticPairedDossier Failed(string state, string reason) =>
            new(MethodVersion, state, "ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE",
                reason, active.Reference, draft.Reference, active.Corpus, null, null, null, null,
                Safeguards);

        if (active.Reference.ModelId == draft.Reference.ModelId
            || active.Reference.ModelId == Guid.Empty || draft.Reference.ModelId == Guid.Empty
            || active.Reference.Status != "ATIVO" || draft.Reference.Status != "RASCUNHO")
            return Failed(NotComparable, "Exigir IDs distintos: um ATIVO-base e um RASCUNHO.");

        if (active.EnvironmentProfile != "Development" || draft.EnvironmentProfile != "Development"
            || !string.Equals(active.Reference.AlgorithmVersion, draft.Reference.AlgorithmVersion, StringComparison.Ordinal)
            || !string.Equals(active.Reference.NormalizationVersion, draft.Reference.NormalizationVersion, StringComparison.Ordinal))
            return Failed(NotComparable, "Ambiente, algoritmo ou normalização não comparáveis.");

        if (!ValidSha(active.Reference.SnapshotSha256) || !ValidSha(draft.Reference.SnapshotSha256)
            || !ValidSha(active.Reference.RuleSetSha256) || !ValidSha(draft.Reference.RuleSetSha256))
            return Failed(Incomplete, "Fingerprint de modelo/ruleset ausente ou inválido.");

        if (!active.Corpus.Equals(draft.Corpus)
            || !ValidSha(active.Corpus.GenerationManifestSha256)
            || !ValidSha(active.Corpus.ObservationsSha256)
            || !ValidSha(active.Corpus.TruthSha256)
            || !ValidSha(active.Corpus.BridgeManifestSha256)
            || !ValidSha(active.Corpus.CorpusFingerprintSha256))
            return Failed(NotComparable, "Corpus, seed ou arquivos de verdade divergentes ou não verificáveis.");

        if (active.Partition is null || draft.Partition is null
            || !active.Partition.Equals(draft.Partition)
            || active.Partition.Seed < 0
            || active.Partition.ValidationBasisPoints is < 1 or > 4900
            || active.Partition.TestBasisPoints is < 1 or > 4900
            || active.Partition.ValidationBasisPoints + active.Partition.TestBasisPoints >= 10000)
            return Failed(NotComparable, "Partições TRAIN/VALIDATION/TEST diferentes ou inválidas.");

        if (active.TrueInterSourcePairs != draft.TrueInterSourcePairs
            || active.PossibleNonMatchPairs != draft.PossibleNonMatchPairs
            || active.TrueInterSourcePairs <= 0 || active.PossibleNonMatchPairs <= 0)
            return Failed(NotComparable, "Universos de pares verdadeiros/não-vínculos diferentes.");

        var a = active.Decision;
        var d = draft.Decision;
        if (a is null || d is null
            || a.Status is not ("EVALUATED" or "NO_SAFE_ORACLE_CANDIDATE")
            || d.Status is not ("EVALUATED" or "NO_SAFE_ORACLE_CANDIDATE")
            || a.ModelValidation is null || a.ModelTest is null
            || d.ModelValidation is null || d.ModelTest is null)
            return Failed(Incomplete, "Falta avaliação completa da política congelada FS em VALIDATION/TEST.");

        if (!string.Equals(a.CalibrationPolicyVersion, d.CalibrationPolicyVersion, StringComparison.Ordinal)
            || a.ValidationScenarioCount != d.ValidationScenarioCount
            || a.TestScenarioCount != d.TestScenarioCount
            || a.ScenarioCount != d.ScenarioCount
            || a.ValidationScenarioCount <= 0 || a.TestScenarioCount <= 0
            || a.ModelValidation.Total != a.ValidationScenarioCount
            || d.ModelValidation.Total != d.ValidationScenarioCount
            || a.ModelTest.Total != a.TestScenarioCount || d.ModelTest.Total != d.TestScenarioCount
            // Positive truth support stays constant: a false resolution can
            // increment FP *and* FN for a positive scenario. TN+FP is NOT
            // a stable negative denominator under this scorer contract.
            || a.ModelValidation.TruePositive + a.ModelValidation.FalseNegative !=
               d.ModelValidation.TruePositive + d.ModelValidation.FalseNegative
            || a.ModelTest.TruePositive + a.ModelTest.FalseNegative !=
               d.ModelTest.TruePositive + d.ModelTest.FalseNegative)
            return Failed(NotComparable, "Cenários, partições ou denominadores FS diferentes.");

        var aSlices = active.QualitySlices.ToDictionary(
            x => (x.Partition, x.Stratum), x => x);
        var dSlices = draft.QualitySlices.ToDictionary(
            x => (x.Partition, x.Stratum), x => x);
        if (aSlices.Count == 0 || aSlices.Count != dSlices.Count
            || aSlices.Keys.Any(k => !dSlices.TryGetValue(k, out var other)
                || aSlices[k].Total != other.Total
                || aSlices[k].TruePositive + aSlices[k].FalseNegative !=
                   other.TruePositive + other.FalseNegative))
            return Failed(NotComparable, "Estratos de VALIDATION/TEST diferentes ou sem denominador verificável.");

        var slices = aSlices.OrderBy(x => x.Key.Partition, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Stratum, StringComparer.Ordinal)
            .Select(x => new Dt15SyntheticSliceComparison(
                x.Key.Partition, x.Key.Stratum,
                x.Value, dSlices[x.Key],
                dSlices[x.Key].TruePositive - x.Value.TruePositive,
                dSlices[x.Key].FalsePositive - x.Value.FalsePositive,
                dSlices[x.Key].FalseNegative - x.Value.FalseNegative))
            .ToArray();

        return new Dt15SyntheticPairedDossier(
            MethodVersion, Comparable, "ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE",
            "Mesmo corpus sintético/seed e mesmas partições. Comparação agregada do FS congelado, " +
            "com candidate sets próprios de cada ruleset; não há matriz por observação nem decisão master.",
            active.Reference, draft.Reference, active.Corpus,
            new Dt15SyntheticBlockingComparison(
                active.TrueInterSourcePairs, active.PossibleNonMatchPairs,
                active.TruePairsRetained, draft.TruePairsRetained,
                active.CandidateUnionPairs, draft.CandidateUnionPairs,
                active.BlockingRecall, draft.BlockingRecall,
                draft.BlockingRecall - active.BlockingRecall,
                active.ReductionRatio, draft.ReductionRatio,
                draft.ReductionRatio - active.ReductionRatio),
            Outcome("VALIDATION", a.ModelValidation, d.ModelValidation),
            Outcome("TEST", a.ModelTest, d.ModelTest),
            slices, Safeguards);
    }

    public static Dt15SyntheticRunFacts FromReport(
        SyntheticEvaluationReport report, Dt15PartitionContract partition)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new Dt15SyntheticRunFacts(
            new Dt15SyntheticModelReference(
                report.Model.ModelId, report.Model.Version, report.Model.Status,
                report.Model.AlgorithmVersion, report.Model.NormalizationVersion,
                report.Model.ModelSnapshotSha256, report.Model.RuleSetFingerprintSha256),
            new Dt15SyntheticCorpusReference(
                report.Input.GeneratorSeed, report.Input.CorpusInputFingerprintSha256,
                report.Input.GenerationManifestSha256, report.Input.ObservationsSha256,
                report.Input.BridgeTruthSha256, report.Input.BridgeManifestSha256,
                report.Input.MaterializedObservationCount),
            report.EnvironmentProfile, partition,
            report.Blocking.TrueInterSourcePairs, report.Blocking.PossibleNonMatchPairs,
            report.Blocking.TruePairsRetainedByUnion, report.Blocking.CandidateUnionPairs,
            report.Blocking.TrueMatchRecall, report.Blocking.ReductionRatio,
            report.DecisionOracle, report.DecisionOracle.ModelQuality);
    }

    private static Dt15SyntheticDecisionComparison Outcome(
        string partition, SyntheticDecisionObjective a, SyntheticDecisionObjective d) =>
        new(partition, a, d, d.TruePositive - a.TruePositive,
            d.FalsePositive - a.FalsePositive,
            d.FalseNegative - a.FalseNegative,
            d.Inconclusive - a.Inconclusive);

    private static bool ValidSha(string? hash) =>
        hash is { Length: 64 } && hash.All(static ch =>
            ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static readonly string[] Safeguards =
    [
        "Synthetic Development SQL marker is mandatory before reading truth.",
        "ACTIVE and DRAFT are scored separately by the existing C# FS evaluator on identical input file hashes.",
        "The same frozen partition seed/basis points and scenario denominators are required; no cross-corpus historical deltas.",
        "Candidate-set differences are measured; missing truth due to blocking remains in the scenario denominator.",
        "No score, LLR, person UUID, raw name, CPF or individual scenario is exported.",
        "This dossier does not write Gold, identity links, model state, publication ledger or approval.",
        "SYNTHETIC FS aggregate evidence is neither municipal statistical validation nor a production decision.",
        "Detailed per-observation decision transitions, real SQL latency and signed master authorization remain pending."
    ];
}

public sealed record Dt15PartitionContract(int Seed, int ValidationBasisPoints, int TestBasisPoints);

public sealed record Dt15SyntheticModelReference(
    Guid ModelId, int Version, string Status, string AlgorithmVersion,
    string NormalizationVersion, string SnapshotSha256, string RuleSetSha256);

public sealed record Dt15SyntheticCorpusReference(
    ulong Seed, string CorpusFingerprintSha256, string GenerationManifestSha256,
    string ObservationsSha256, string TruthSha256, string BridgeManifestSha256,
    long MaterializedObservationCount);

public sealed record Dt15SyntheticRunFacts(
    Dt15SyntheticModelReference Reference, Dt15SyntheticCorpusReference Corpus,
    string EnvironmentProfile, Dt15PartitionContract? Partition,
    long TrueInterSourcePairs, long PossibleNonMatchPairs, long TruePairsRetained,
    long CandidateUnionPairs, decimal BlockingRecall, decimal ReductionRatio,
    SyntheticThresholdOracle? Decision, IReadOnlyList<SyntheticDecisionQualitySlice> QualitySlices);

public sealed record Dt15SyntheticBlockingComparison(
    long TrueInterSourcePairs, long PossibleNonMatchPairs,
    long ActiveTruePairsRetained, long DraftTruePairsRetained,
    long ActiveCandidatePairs, long DraftCandidatePairs,
    decimal ActiveRecall, decimal DraftRecall, decimal RecallDelta,
    decimal ActiveReduction, decimal DraftReduction, decimal ReductionDelta);

public sealed record Dt15SyntheticDecisionComparison(
    string Partition, SyntheticDecisionObjective Active, SyntheticDecisionObjective Draft,
    long TruePositiveDelta, long FalsePositiveDelta, long FalseNegativeDelta, long InconclusiveDelta);

public sealed record Dt15SyntheticSliceComparison(
    string Partition, string Stratum,
    SyntheticDecisionQualitySlice Active, SyntheticDecisionQualitySlice Draft,
    long TruePositiveDelta, long FalsePositiveDelta, long FalseNegativeDelta);

public sealed record Dt15SyntheticPairedDossier(
    string MethodVersion, string Status, string Purpose, string Explanation,
    Dt15SyntheticModelReference Active, Dt15SyntheticModelReference Draft,
    Dt15SyntheticCorpusReference Corpus,
    Dt15SyntheticBlockingComparison? Blocking,
    Dt15SyntheticDecisionComparison? Validation,
    Dt15SyntheticDecisionComparison? Test,
    IReadOnlyList<Dt15SyntheticSliceComparison>? Strata,
    IReadOnlyList<string> Safeguards);

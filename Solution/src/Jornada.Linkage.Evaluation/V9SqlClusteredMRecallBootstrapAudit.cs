namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Re-samples distinct SYNTHETIC BASE identities, never edges, to describe
/// within-frame uncertainty of eligible-M blocking recall. This is NOT a
/// production confidence guarantee, U-network variance, or calibrated FS.
/// </summary>
public static class V9SqlClusteredMRecallBootstrapAudit
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_CLUSTERED_BASE_M_RECALL_BOOTSTRAP_V1";
    public const string Status =
        "EXPLORATORY_M_ONLY_BASE_CLUSTER_BOOTSTRAP_NOT_U_NETWORK_CI_OR_ACTIVATION";
    public const int Replicates = 1000;

    public sealed record Contribution(
        string Partition, string SyntheticBaseId,
        long EligibleM, long BaselineRecoveredM, long ElevenRecoveredM);

    public sealed record Split(
        string Partition, int ContributingBasePeople,
        long EligibleM, long BaselineRecoveredM, long ElevenRecoveredM,
        decimal BaselineRecallObserved, decimal ElevenRecallObserved,
        decimal BaselineBootstrapP025, decimal BaselineBootstrapP975,
        decimal ElevenBootstrapP025, decimal ElevenBootstrapP975,
        decimal? ProbabilityElevenMeets95InBootstrapNotGate,
        int BootstrapReplicates);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        ulong SourceSeed, string SourceInputFingerprint,
        bool IsIndependentPairConfidenceInterval,
        bool ModelsNetworkDependenceAmongU,
        bool MayPromoteModelOrBlockingPolicy,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Contribution> contributions,
        V9SqlElevenPassResidualAudit.Report eleven,
        ulong sourceSeed, string sourceInputFingerprint)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(eleven);
        if (eleven.Version != V9SqlElevenPassResidualAudit.Version
            || eleven.PartitionScope != "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN"
            || eleven.MayPromoteBlockingPolicy
            || eleven.Splits.Count != 2
            || string.IsNullOrWhiteSpace(sourceInputFingerprint)
            || contributions.Count == 0)
            throw new InvalidDataException("Bootstrap M não recebeu frame SQL e fonte congelados.");

        var seenBases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in contributions)
        {
            ArgumentNullException.ThrowIfNull(c);
            if (c.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(c.SyntheticBaseId)
                || !seenBases.Add(c.SyntheticBaseId)
                || c.EligibleM <= 0
                || c.BaselineRecoveredM < 0
                || c.ElevenRecoveredM < c.BaselineRecoveredM
                || c.ElevenRecoveredM > c.EligibleM)
                throw new InvalidDataException(
                    "Contribuições de base inválidas, duplicadas ou contaminadas por TEST.");
        }

        var summaries = new List<Split>(2);
        foreach (var (part,index) in new[] { ("TRAIN",0), ("VALIDATION",1) })
        {
            var rows = contributions.Where(x => x.Partition == part)
                .OrderBy(x => x.SyntheticBaseId, StringComparer.Ordinal)
                .ToArray();
            if (rows.Length < 2)
                throw new InvalidDataException("Bootstrap precisa de pelo menos duas bases por split.");
            var expected = eleven.Splits.Single(x => x.Partition == part);
            long eligible = rows.Sum(x => x.EligibleM);
            long baseline = rows.Sum(x => x.BaselineRecoveredM);
            long recovered = rows.Sum(x => x.ElevenRecoveredM);
            if (eligible != expected.EligibleM
                || baseline != expected.BaselineM
                || recovered != expected.ElevenUnionM)
                throw new InvalidDataException(
                    "Clusters M não conciliam exatamente com censo SQL e gabarito pré-blocking.");

            var seed = unchecked((int)(sourceSeed ^ (sourceSeed >> 32))
                ^ (index == 0 ? 104729 : 130363));
            var random = new Random(seed);
            var bootstrapBaseline = new decimal[Replicates];
            var bootstrapEleven = new decimal[Replicates];
            int meets95 = 0;
            for (var rep = 0; rep < Replicates; rep++)
            {
                long denominator = 0, numeratorBaseline = 0, numeratorEleven = 0;
                // Only base identities are sampled, never observation edges.
                for (var draw = 0; draw < rows.Length; draw++)
                {
                    var basePerson = rows[random.Next(rows.Length)];
                    checked
                    {
                        denominator += basePerson.EligibleM;
                        numeratorBaseline += basePerson.BaselineRecoveredM;
                        numeratorEleven += basePerson.ElevenRecoveredM;
                    }
                }
                if (denominator <= 0 || numeratorBaseline > numeratorEleven
                    || numeratorEleven > denominator)
                    throw new InvalidDataException("Amostra bootstrap M impossível.");
                bootstrapBaseline[rep] = decimal.Divide(numeratorBaseline, denominator);
                bootstrapEleven[rep] = decimal.Divide(numeratorEleven, denominator);
                if (20 * numeratorEleven >= 19 * denominator)
                    meets95++;
            }
            Array.Sort(bootstrapBaseline);
            Array.Sort(bootstrapEleven);
            var observedBaseline = decimal.Divide(baseline, eligible);
            var observedEleven = decimal.Divide(recovered, eligible);
            summaries.Add(new Split(part, rows.Length,
                eligible, baseline, recovered,
                observedBaseline, observedEleven,
                bootstrapBaseline[24], bootstrapBaseline[974],
                bootstrapEleven[24], bootstrapEleven[974],
                decimal.Divide(meets95, Replicates), Replicates));
        }
        return new Report(Version, Status, "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            sourceSeed, sourceInputFingerprint,
            false, false, false,
            Array.AsReadOnly(summaries.ToArray()));
    }
}

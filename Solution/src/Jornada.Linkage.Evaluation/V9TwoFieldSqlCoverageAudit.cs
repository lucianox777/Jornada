namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Read-only, TRAIN/VALIDATION-only census of the two-field eligibility contract
/// against the actual deduplicated 14-pass physical SQL candidate union.
/// It does not enlarge the SQL frame, run FS, infer FDR, or select a policy.
/// </summary>
public static class V9TwoFieldSqlCoverageAudit
{
    public const string Version = "V9_TWO_FIELD_SQL_FOURTEEN_PASS_COVERAGE_V1";
    public const string Status = "DIAGNOSTIC_LEGACY_SQL_DOB_FRAME_NO_TEST_NO_PROMOTION";
    private static readonly string[] Combinations =
        ["NONE", "P", "M", "D", "PM", "PD", "MD", "PMD"];

    public sealed record Observation(
        int Id, string Base, string Partition, string Gestor,
        string? Person, string? Mother, DateOnly? Birth, string? Cpf);

    public sealed record Availability(
        string Partition, string Combination, long Observations,
        bool ProbabilisticAbstention);

    public sealed record Split(
        string Partition, long NoCpfObservations, long EligibleObservations,
        long AbstainingObservations, long AllNoCpfTruthM,
        long EligibleTwoFieldTruthM, long IneligibleTruthM,
        long EligibleTruthMWithMissingBirth,
        long LegacySqlM, long LegacySqlU,
        long LegacySqlEligibleM, long LegacySqlEligibleU,
        long LegacySqlIneligibleM, long LegacySqlIneligibleU,
        long UnrecoveredEligibleM, decimal? RecallAgainstAllNoCpfTruthM,
        decimal? RecallAgainstTwoFieldEligibleM,
        decimal? TwoFieldEligibilityCeilingAgainstAllTruthM,
        long AdditionalEligibleMNeededFor95);

    public sealed record Report(
        string Version, string Status, string Scope,
        bool MayPromoteModelOrBlockingPolicy, bool FdrMeasured,
        IReadOnlyList<Availability> AvailabilityByCombination,
        IReadOnlyList<Split> Splits);

    public static Report Analyze(
        IReadOnlyList<Observation> observations,
        IEnumerable<V9SqlNamePrefix3BirthAudit.Pair> physicalCandidates,
        int hardPairCap)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(physicalCandidates);
        if (hardPairCap is < 1 or > 1_000_000)
            throw new InvalidDataException("Cap SQL físico inválido.");

        var all = new Dictionary<int, Observation>();
        var evidence = new Dictionary<int, V9MinimumEvidenceEligibility.Result>();
        foreach (var row in observations)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (row.Id < 0 || row.Partition is not ("TRAIN" or "VALIDATION")
                || string.IsNullOrWhiteSpace(row.Base)
                || string.IsNullOrWhiteSpace(row.Gestor)
                || !all.TryAdd(row.Id, row))
                throw new InvalidDataException("Observação duplicada, inválida ou vazamento TEST.");

            evidence.Add(row.Id, V9MinimumEvidenceEligibility.Evaluate(
                row.Person, row.Mother,
                row.Birth?.ToDateTime(TimeOnly.MinValue)));
        }

        var noCpf = all.Values.Where(x => x.Cpf is null).ToArray();
        var availability = new List<Availability>(16);
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        foreach (var combination in Combinations)
        {
            availability.Add(new Availability(part, combination,
                noCpf.LongCount(x => x.Partition == part
                    && evidence[x.Id].Combination == combination),
                combination is "NONE" or "P" or "M" or "D"));
        }

        // Build independent ground truth BEFORE considering SQL candidates;
        // truth M includes all no-CPF observations, even those lacking DOB.
        var eligibleTruth = new HashSet<(int, int)>();
        var truthCount = new Dictionary<string, (long All, long Eligible, long NoDob)>(
            StringComparer.Ordinal) { ["TRAIN"] = (0, 0, 0),
                                     ["VALIDATION"] = (0, 0, 0) };
        foreach (var group in noCpf.GroupBy(x => (x.Partition, x.Base)))
        {
            var rows = group.ToArray();
            for (var i = 0; i < rows.Length; i++)
            for (var j = i + 1; j < rows.Length; j++)
            {
                var a = rows[i];
                var b = rows[j];
                if (a.Gestor == b.Gestor)
                    continue;
                var old = truthCount[group.Key.Partition];
                checked { old.All++; }
                if (evidence[a.Id].Eligible && evidence[b.Id].Eligible)
                {
                    checked { old.Eligible++; }
                    if (!V9MinimumEvidenceEligibility.HasBirth(
                            a.Birth?.ToDateTime(TimeOnly.MinValue))
                        || !V9MinimumEvidenceEligibility.HasBirth(
                            b.Birth?.ToDateTime(TimeOnly.MinValue)))
                    {
                        checked { old.NoDob++; }
                    }
                    if (!eligibleTruth.Add((Math.Min(a.Id, b.Id),
                                            Math.Max(a.Id, b.Id))))
                        throw new InvalidDataException("Gabarito M duplicado.");
                }
                truthCount[group.Key.Partition] = old;
            }
        }

        // Union is by physical pair (not by pass); overlaps cannot inflate recall.
        var sql = new Dictionary<(int, int), V9SqlNamePrefix3BirthAudit.Pair>();
        foreach (var pair in physicalCandidates)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.A >= pair.B
                || pair.Partition is not ("TRAIN" or "VALIDATION")
                || !all.TryGetValue(pair.A, out var a)
                || !all.TryGetValue(pair.B, out var b)
                || a.Partition != pair.Partition || b.Partition != pair.Partition
                || a.Cpf is not null || b.Cpf is not null
                || a.Gestor == b.Gestor
                || a.Base.Equals(b.Base, StringComparison.Ordinal) != pair.Match)
                throw new InvalidDataException("Par SQL fora do gabarito ou frame intergestor sem CPF.");

            if (sql.TryGetValue((pair.A, pair.B), out var prior))
            {
                if (prior != pair)
                    throw new InvalidDataException("Rótulo SQL divergente entre passes.");
            }
            else
            {
                sql.Add((pair.A, pair.B), pair);
                if (sql.Count > hardPairCap)
                    throw new InvalidDataException("União SQL ultrapassou cap; nunca truncar.");
            }
        }

        var result = new List<Split>(2);
        foreach (var part in new[] { "TRAIN", "VALIDATION" })
        {
            var observationsInSplit = noCpf.Where(x => x.Partition == part).ToArray();
            var eligibleObs = observationsInSplit.LongCount(x => evidence[x.Id].Eligible);
            var t = truthCount[part];
            long m = 0, u = 0, eligibleM = 0, eligibleU = 0;
            foreach (var pair in sql.Values.Where(x => x.Partition == part))
            {
                var bothEligible = evidence[pair.A].Eligible && evidence[pair.B].Eligible;
                if (pair.Match)
                {
                    m++;
                    if (bothEligible)
                    {
                        eligibleM++;
                        if (!eligibleTruth.Contains((pair.A, pair.B)))
                            throw new InvalidDataException("SQL M ausente do gabarito elegível.");
                    }
                }
                else
                {
                    u++;
                    if (bothEligible) eligibleU++;
                }
            }
            if (eligibleM > t.Eligible || m > t.All
                || t.NoDob > t.Eligible || eligibleObs > observationsInSplit.Length)
                throw new InvalidDataException("Denominadores de elegibilidade incompatíveis.");
            // SQL still requires DOB on both sides; do not claim the PM/no-DOB
            // cases were evaluated by the current physical SQL frame.
            if (sql.Values.Any(x => x.Partition == part && x.Match
                    && eligibleTruth.Contains((x.A, x.B))
                    && (!V9MinimumEvidenceEligibility.HasBirth(
                            all[x.A].Birth?.ToDateTime(TimeOnly.MinValue))
                        || !V9MinimumEvidenceEligibility.HasBirth(
                            all[x.B].Birth?.ToDateTime(TimeOnly.MinValue)))))
                throw new InvalidDataException("SQL legado recuperou M sem DOB utilizável.");

            var minimum = (19 * t.Eligible + 19) / 20;
            result.Add(new Split(part, observationsInSplit.LongLength, eligibleObs,
                observationsInSplit.LongLength - eligibleObs,
                t.All, t.Eligible, t.All - t.Eligible, t.NoDob,
                m, u, eligibleM, eligibleU, m - eligibleM, u - eligibleU,
                t.Eligible - eligibleM,
                t.All == 0 ? null : decimal.Divide(eligibleM, t.All),
                t.Eligible == 0 ? null : decimal.Divide(eligibleM, t.Eligible),
                t.All == 0 ? null : decimal.Divide(t.Eligible, t.All),
                Math.Max(0, minimum - eligibleM)));
        }
        return new Report(Version, Status, "TRAIN_VALIDATION_NO_CPF_ALL_DOB_STATES",
            false, false, Array.AsReadOnly(availability.ToArray()),
            Array.AsReadOnly(result.ToArray()));
    }
}

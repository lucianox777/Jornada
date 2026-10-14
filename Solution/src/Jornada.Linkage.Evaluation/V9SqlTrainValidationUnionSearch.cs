namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Evaluates FULL DISTINCT unions of pre-registered extra SQL candidate passes,
/// always on the immutable ACTIVE+C1 physical baseline. Rule selection is
/// TRAIN-constrained / VALIDATION-gated; TEST is never accepted as input.
/// This is diagnostic, not a production ruleset, FS threshold or a policy gate.
/// </summary>
public static class V9SqlTrainValidationUnionSearch
{
    public const string Version = "V9_SQL_TRAIN_VALIDATION_SIX_PASS_DEDUP_UNION_SEARCH_V2";
    public const string Status = "DIAGNOSTIC_ONLY_NO_TEST_NO_POLICY_PUBLICATION";
    public const long RequiredRecallNumerator = 19;
    public const long RequiredRecallDenominator = 20;
    public const int MaxPassesInUnion = 4;
    public const int MaxEvaluatedUnions = 2048;

    public sealed record Pair(int A, int B, string Partition, bool IsReferenceMatch);
    public sealed record Extension(string PassId, IReadOnlyList<Pair> ExtraPairs);
    public sealed record Combination(
        string Signature, IReadOnlyList<string> PassIds, int PassCount,
        long TrainAddedM, long TrainAddedU, long ValidationAddedM, long ValidationAddedU,
        long TrainUnionM, long TrainUnionU, long ValidationUnionM, long ValidationUnionU,
        decimal TrainRecallAgainstEligibleM, decimal ValidationRecallAgainstEligibleM,
        bool MeetsBoth95PercentRecallFloors);

    public sealed record AllSixCeiling(
        string Interpretation, int PassesInCeiling,
        long TrainUnionM, long TrainUnionU, long ValidationUnionM, long ValidationUnionU,
        decimal TrainRecall, decimal ValidationRecall,
        long TrainEligibleStillMissing, long ValidationEligibleStillMissing,
        long TrainAdditionalMNeededFor95, long ValidationAdditionalMNeededFor95,
        bool AllSixPassesMeetBoth95PercentFloors,
        bool AllSixPassesProve95ImpossibleWithinRegisteredPassFamily);

    public sealed record Report(
        string Version, string Status, string PartitionScope,
        int RegisteredPassCount, int EvaluatedUnionCount,
        int PassingUnionCount, int CandidatesWithinPairCap,
        long TrainEligibleM, long ValidationEligibleM,
        long BaselineTrainM, long BaselineTrainU,
        long BaselineValidationM, long BaselineValidationU,
        long MinimumTrainMFor95Percent, long MinimumValidationMFor95Percent,
        string? AdvisoryBestSignatureNotPublished,
        bool MayPromoteBlockingPolicy,
        IReadOnlyList<Combination> Combinations)
    {
        public AllSixCeiling? AllSixRegisteredPassesCeiling { get; init; }
    }

    public static Report Analyze(
        IReadOnlyList<Pair> baselinePairs,
        IReadOnlyList<Extension> extensions,
        long trainEligibleM,
        long validationEligibleM,
        int pairHardCap)
    {
        ArgumentNullException.ThrowIfNull(baselinePairs);
        ArgumentNullException.ThrowIfNull(extensions);
        if (trainEligibleM <= 0 || validationEligibleM <= 0
            || pairHardCap < 1 || extensions.Count != 6
            || extensions.Any(x => x is null || string.IsNullOrWhiteSpace(x.PassId)
                || x.ExtraPairs is null)
            || extensions.Select(x => x.PassId).Distinct(StringComparer.Ordinal).Count()
                != extensions.Count)
            throw new InvalidDataException("Frame TRAIN/VALIDATION ou seis passes pré-registrados inválidos.");

        static void Guard(Pair pair)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.A < 0 || pair.B <= pair.A
                || pair.Partition is not ("TRAIN" or "VALIDATION"))
                throw new InvalidDataException("Vazamento de TEST ou candidato SQL não canônico.");
        }

        var baseline = new Dictionary<(int A, int B), Pair>();
        long bmTrain = 0, buTrain = 0, bmVal = 0, buVal = 0;
        foreach (var p in baselinePairs)
        {
            Guard(p);
            if (!baseline.TryAdd((p.A, p.B), p))
                throw new InvalidDataException("Par duplicado no baseline físico SQL.");
            if (p.Partition == "TRAIN")
            {
                if (p.IsReferenceMatch) bmTrain++; else buTrain++;
            }
            else
            {
                if (p.IsReferenceMatch) bmVal++; else buVal++;
            }
        }
        if (baseline.Count > pairHardCap || bmTrain <= 0 || buTrain <= 0
            || bmVal <= 0 || buVal <= 0
            || bmTrain > trainEligibleM || bmVal > validationEligibleM)
            throw new InvalidDataException("Baseline SQL sem M/U bilateral ou acima do cap.");

        // Validate overlap and preserve each pass's own set, *not* the sum
        // of marginal gains; the same extra pair can occur in many passes.
        var extraByPass = new SortedDictionary<string, IReadOnlyDictionary<(int A, int B), Pair>>(
            StringComparer.Ordinal);
        foreach (var pass in extensions)
        {
            var dict = new Dictionary<(int A, int B), Pair>();
            foreach (var p in pass.ExtraPairs)
            {
                Guard(p);
                if (baseline.ContainsKey((p.A, p.B))
                    || !dict.TryAdd((p.A, p.B), p))
                    throw new InvalidDataException("Pass SQL contém candidato baseline ou duplicado.");
            }
            extraByPass.Add(pass.PassId, dict);
        }

        var registered = extraByPass.Keys.ToArray();

        // Union of ALL six passes is a deterministic set-theoretic UPPER
        // BOUND on M recall for every allowed 1..4-pass subset. Evaluate it
        // even when 5/6 passes are not candidates for publication. It proves
        // infeasibility within this exact six-pass family when <95%, not
        // impossibility for unseen passes or another reference population.
        var allExtra = new Dictionary<(int A, int B), Pair>();
        foreach (var pass in registered)
        foreach (var (key, pair) in extraByPass[pass])
        {
            if (allExtra.TryGetValue(key, out var prior))
            {
                if (prior != pair)
                    throw new InvalidDataException(
                        "Passes SQL do limite superior discordam do gabarito do mesmo par.");
            }
            else allExtra.Add(key, pair);
        }
        if ((long)baseline.Count + allExtra.Count > pairHardCap)
            throw new InvalidDataException(
                "União dos seis passes ultrapassa cap físico; limite de cobertura não demonstrável.");

        long allTrainM = bmTrain, allTrainU = buTrain;
        long allValidationM = bmVal, allValidationU = buVal;
        foreach (var pair in allExtra.Values)
        {
            if (pair.Partition == "TRAIN")
            {
                if (pair.IsReferenceMatch) allTrainM++;
                else allTrainU++;
            }
            else
            {
                if (pair.IsReferenceMatch) allValidationM++;
                else allValidationU++;
            }
        }
        if (allTrainM > trainEligibleM || allValidationM > validationEligibleM)
            throw new InvalidDataException("Limite M de seis passes excede M elegível.");
        var trainRequired = Ceil95(trainEligibleM);
        var validationRequired = Ceil95(validationEligibleM);
        var allMeet = Meets95(allTrainM, trainEligibleM)
            && Meets95(allValidationM, validationEligibleM);
        var ceiling = new AllSixCeiling(
            "REGISTERED_SIX_PASS_FAMILY_CEILING_NOT_A_GLOBAL_BLOCKING_UPPER_BOUND",
            registered.Length, allTrainM, allTrainU,
            allValidationM, allValidationU,
            decimal.Divide(allTrainM, trainEligibleM),
            decimal.Divide(allValidationM, validationEligibleM),
            trainEligibleM - allTrainM, validationEligibleM - allValidationM,
            Math.Max(0, trainRequired - allTrainM),
            Math.Max(0, validationRequired - allValidationM),
            allMeet, !allMeet);

        var results = new List<Combination>();
        for (var width = 1; width <= Math.Min(MaxPassesInUnion, registered.Length); width++)
        foreach (var selected in Combinations(registered, width))
        {
            if (results.Count >= MaxEvaluatedUnions)
                throw new InvalidDataException("Busca de regras excedeu cap; nunca truncar.");
            var added = new Dictionary<(int A, int B), Pair>();
            foreach (var pass in selected)
            foreach (var (key, pair) in extraByPass[pass])
            {
                if (added.TryGetValue(key, out var prior))
                {
                    if (prior != pair)
                        throw new InvalidDataException(
                            "Passes SQL discordam sobre rótulo ou partição do mesmo par.");
                }
                else
                    added.Add(key, pair);
            }
            if ((long)baseline.Count + added.Count > pairHardCap)
                throw new InvalidDataException(
                    "União SQL excedeu cap físico; nunca amostrar/truncar candidatos.");

            long mt = 0, ut = 0, mv = 0, uv = 0;
            foreach (var pair in added.Values)
            {
                if (pair.Partition == "TRAIN")
                {
                    if (pair.IsReferenceMatch) mt++; else ut++;
                }
                else
                {
                    if (pair.IsReferenceMatch) mv++; else uv++;
                }
            }
            var trainM = bmTrain + mt;
            var valM = bmVal + mv;
            if (trainM > trainEligibleM || valM > validationEligibleM)
                throw new InvalidDataException("União M excede denominador independente.");
            bool meets = Meets95(trainM, trainEligibleM)
                && Meets95(valM, validationEligibleM);
            var signature = string.Join("||", selected);
            results.Add(new Combination(
                signature, Array.AsReadOnly(selected), selected.Length,
                mt, ut, mv, uv,
                trainM, buTrain + ut, valM, buVal + uv,
                decimal.Divide(trainM, trainEligibleM),
                decimal.Divide(valM, validationEligibleM), meets));
        }
        if (results.Count != 56
            || results.Select(x => x.Signature).Distinct(StringComparer.Ordinal).Count()
                != results.Count
            || results.Any(x => x.TrainUnionM > allTrainM
                || x.TrainUnionU > allTrainU
                || x.ValidationUnionM > allValidationM
                || x.ValidationUnionU > allValidationU)
            || (!allMeet && results.Any(x => x.MeetsBoth95PercentRecallFloors)))
            throw new InvalidDataException("Combinações de seis passes até quatro incompletas.");

        // Advisory only, with VALIDATION as a hard gate, not a means to
        // retune TEST or a justification to publish a ruleset.
        var passing = results.Where(x => x.MeetsBoth95PercentRecallFloors)
            .OrderBy(x => x.TrainUnionU)
            .ThenBy(x => x.ValidationUnionU)
            .ThenByDescending(x => x.TrainRecallAgainstEligibleM)
            .ThenBy(x => x.PassCount)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ToArray();

        return new Report(Version, Status, "TRAIN_AND_VALIDATION_ONLY_TEST_FORBIDDEN",
            registered.Length, results.Count, passing.Length, results.Count,
            trainEligibleM, validationEligibleM, bmTrain, buTrain, bmVal, buVal,
            trainRequired, validationRequired,
            passing.FirstOrDefault()?.Signature,
            false, Array.AsReadOnly(results.ToArray()))
        {
            AllSixRegisteredPassesCeiling = ceiling
        };
    }

    private static bool Meets95(long recoveredM, long eligibleM) =>
        checked(recoveredM * RequiredRecallDenominator)
            >= checked(eligibleM * RequiredRecallNumerator);

    private static long Ceil95(long eligibleM) =>
        checked((eligibleM * RequiredRecallNumerator
            + RequiredRecallDenominator - 1) / RequiredRecallDenominator);

    private static IEnumerable<string[]> Combinations(string[] sorted, int width)
    {
        var chosen = new string[width];
        return Expand(0, 0);
        IEnumerable<string[]> Expand(int start, int depth)
        {
            if (depth == width)
            {
                yield return chosen.ToArray();
                yield break;
            }
            for (var i = start; i <= sorted.Length - (width-depth); i++)
            {
                chosen[depth] = sorted[i];
                foreach (var item in Expand(i+1, depth+1))
                    yield return item;
            }
        }
    }
}

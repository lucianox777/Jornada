namespace Jornada.Linkage.Evaluation;

/// <summary>
/// Contrato de gabarito agregado para DC-SYN-01. A população primária e o challenge
/// adversarial são universos disjuntos; challenge nunca estima prevalência populacional.
/// </summary>
public static class SyntheticEvaluationTruthContract
{
    public const string Version = "DC_SYN_01_STRATIFIED_TRUTH_V1";
    public const string ChallengePartition = "CHALLENGE";

    public static readonly IReadOnlySet<string> PopulationPartitions =
        new HashSet<string>(["TRAIN", "VALIDATION", "TEST"], StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AdditionalReservedFamilies =
        new HashSet<string>(
        [
            "CHALLENGE_MISSING_MOTHER_V1",
            "CHALLENGE_LATE_CPF_V1",
            "CHALLENGE_CORRELATED_ERROR_V1",
            "CHALLENGE_LEAVE_TRUTH_OUT_V1"
        ], StringComparer.Ordinal);

    public static bool IsAdditionalReservedFamily(string? family) =>
        family is not null && AdditionalReservedFamilies.Contains(family);
}

public enum SyntheticEvaluationDecision
{
    Abstained = 0,
    Linked = 1,
    Conflict = 2
}

public sealed record SyntheticEvaluationTruthCase(
    string CaseId,
    string Partition,
    string? ReservedFamily,
    int Wave,
    bool HasCpf,
    bool MotherPresent,
    bool ExactHomonym,
    bool TruthPositive,
    bool CandidateRetrieved,
    SyntheticEvaluationDecision Decision,
    bool DecisionCorrect);

public sealed record SyntheticEvaluationCounts(
    long Cases,
    long TruthPositive,
    long CandidateRetrievedPositive,
    long TruePositive,
    long FalsePositive,
    long FalseNegative,
    long Abstentions,
    long Conflicts,
    long Linked);

public sealed record SyntheticEvaluationRates(
    decimal? BlockingRecall,
    decimal? PrecisionPpv,
    decimal? DecisionRecall,
    decimal Coverage);

public sealed record SyntheticEvaluationSlice(
    string Partition,
    int Wave,
    string CpfStratum,
    string MotherStratum,
    string HomonymStratum,
    SyntheticEvaluationCounts Counts,
    SyntheticEvaluationRates Rates);

public sealed record SyntheticStratifiedEvaluationReport(
    string MethodVersion,
    long PopulationCases,
    long ReservedChallengeCases,
    IReadOnlyList<SyntheticEvaluationSlice> PopulationByStratum,
    IReadOnlyList<SyntheticEvaluationSlice> ChallengeByStratum);

public static class SyntheticStratifiedEvaluator
{
    public const string MethodVersion = "DC_SYN_01_STRATIFIED_METRICS_V1";

    public static SyntheticStratifiedEvaluationReport Evaluate(IEnumerable<SyntheticEvaluationTruthCase> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cases = source.ToArray();
        Validate(cases);

        var population = cases.Where(x => x.ReservedFamily is null).ToArray();
        var challenge = cases.Where(x => x.ReservedFamily is not null).ToArray();

        return new SyntheticStratifiedEvaluationReport(
            MethodVersion,
            population.LongLength,
            challenge.LongLength,
            Slice(population),
            Slice(challenge));
    }

    private static void Validate(IReadOnlyList<SyntheticEvaluationTruthCase> cases)
    {
        if (cases.Any(x => string.IsNullOrWhiteSpace(x.CaseId) || x.Wave <= 0))
            throw new ArgumentException("CaseId e Wave positiva são obrigatórios.", nameof(cases));

        var duplicate = cases.GroupBy(x => x.CaseId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Caso sintético duplicado: {duplicate.Key}.");

        foreach (var item in cases)
        {
            if (item.ReservedFamily is null)
            {
                if (!SyntheticEvaluationTruthContract.PopulationPartitions.Contains(item.Partition))
                    throw new InvalidDataException("População sintética deve pertencer a TRAIN, VALIDATION ou TEST.");
            }
            else
            {
                if (item.Partition != SyntheticEvaluationTruthContract.ChallengePartition)
                    throw new InvalidDataException("Challenge adversarial deve usar partição CHALLENGE separada.");
                if (!SyntheticEvaluationTruthContract.IsAdditionalReservedFamily(item.ReservedFamily))
                    throw new InvalidDataException($"Família challenge não reservada: {item.ReservedFamily}.");
            }

            if (item.Decision == SyntheticEvaluationDecision.Linked && !item.CandidateRetrieved)
                throw new InvalidDataException("Decisão LINKED sem candidato recuperado é impossível.");
            if (item.Decision != SyntheticEvaluationDecision.Linked && item.DecisionCorrect)
                throw new InvalidDataException("DecisionCorrect só é válido para decisão LINKED.");
        }
    }

    private static IReadOnlyList<SyntheticEvaluationSlice> Slice(
        IEnumerable<SyntheticEvaluationTruthCase> cases) =>
        cases.GroupBy(x => new
            {
                x.Partition,
                x.Wave,
                Cpf = x.HasCpf ? "CPF_PRESENT" : "CPF_ABSENT",
                Mother = x.MotherPresent ? "MOTHER_PRESENT" : "MOTHER_ABSENT",
                Homonym = x.ExactHomonym ? "EXACT_HOMONYM" : "NON_EXACT_HOMONYM"
            })
            .OrderBy(group => group.Key.Partition, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Wave)
            .ThenBy(group => group.Key.Cpf, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Mother, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Homonym, StringComparer.Ordinal)
            .Select(group => BuildSlice(
                group.Key.Partition, group.Key.Wave, group.Key.Cpf,
                group.Key.Mother, group.Key.Homonym, group))
            .ToArray();

    private static SyntheticEvaluationSlice BuildSlice(
        string partition,
        int wave,
        string cpf,
        string mother,
        string homonym,
        IEnumerable<SyntheticEvaluationTruthCase> source)
    {
        var rows = source.ToArray();
        var truthPositive = rows.LongCount(x => x.TruthPositive);
        var retrievedPositive = rows.LongCount(x => x.TruthPositive && x.CandidateRetrieved);
        var linked = rows.LongCount(x => x.Decision == SyntheticEvaluationDecision.Linked);
        var tp = rows.LongCount(x => x.TruthPositive
            && x.Decision == SyntheticEvaluationDecision.Linked && x.DecisionCorrect);
        var fp = rows.LongCount(x => x.Decision == SyntheticEvaluationDecision.Linked
            && (!x.TruthPositive || !x.DecisionCorrect));
        var fn = checked(truthPositive - tp);
        var abstentions = rows.LongCount(x => x.Decision == SyntheticEvaluationDecision.Abstained);
        var conflicts = rows.LongCount(x => x.Decision == SyntheticEvaluationDecision.Conflict);

        var counts = new SyntheticEvaluationCounts(
            rows.LongLength, truthPositive, retrievedPositive, tp, fp, fn,
            abstentions, conflicts, linked);
        var rates = new SyntheticEvaluationRates(
            RatioOrNull(retrievedPositive, truthPositive),
            RatioOrNull(tp, linked),
            RatioOrNull(tp, truthPositive),
            rows.LongLength == 0 ? 0m : decimal.Divide(linked, rows.LongLength));

        return new SyntheticEvaluationSlice(partition, wave, cpf, mother, homonym, counts, rates);
    }

    private static decimal? RatioOrNull(long numerator, long denominator) =>
        denominator == 0 ? null : decimal.Divide(numerator, denominator);
}

/// <summary>
/// Recompõe as métricas diretamente do gabarito, sem chamar o agregador ou suas funções
/// de razão. Serve como conferência aritmética independente do relatório produzido.
/// </summary>
public static class SyntheticStratifiedMetricConference
{
    public const string Version = "DC_SYN_01_STRATIFIED_CONFERENCE_V1";

    public static void Confer(
        IEnumerable<SyntheticEvaluationTruthCase> source,
        SyntheticStratifiedEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);
        if (report.MethodVersion != SyntheticStratifiedEvaluator.MethodVersion)
            throw new InvalidDataException("Versão de métrica desconhecida.");

        var cases = source.ToArray();
        var populationCount = cases.LongCount(x => x.ReservedFamily is null);
        var challengeCount = cases.LongCount(x => x.ReservedFamily is not null);
        if (report.PopulationCases != populationCount || report.ReservedChallengeCases != challengeCount)
            throw new InvalidDataException("Universos população/challenge divergem do gabarito.");

        ConferUniverse(cases.Where(x => x.ReservedFamily is null), report.PopulationByStratum, "população");
        ConferUniverse(cases.Where(x => x.ReservedFamily is not null), report.ChallengeByStratum, "challenge");
    }

    private static void ConferUniverse(
        IEnumerable<SyntheticEvaluationTruthCase> source,
        IReadOnlyList<SyntheticEvaluationSlice> slices,
        string label)
    {
        var expected = source.GroupBy(Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (expected.Count != slices.Count)
            throw new InvalidDataException($"Quantidade de estratos divergente em {label}.");

        foreach (var slice in slices)
        {
            var key = Key(slice);
            if (!expected.TryGetValue(key, out var rows))
                throw new InvalidDataException($"Estrato inesperado em {label}: {key}.");

            long truth = 0, retrieved = 0, tp = 0, fp = 0, abstain = 0, conflict = 0, linked = 0;
            foreach (var row in rows)
            {
                if (row.TruthPositive) truth++;
                if (row.TruthPositive && row.CandidateRetrieved) retrieved++;
                if (row.Decision == SyntheticEvaluationDecision.Linked)
                {
                    linked++;
                    if (row.TruthPositive && row.DecisionCorrect) tp++;
                    else fp++;
                }
                else if (row.Decision == SyntheticEvaluationDecision.Abstained) abstain++;
                else if (row.Decision == SyntheticEvaluationDecision.Conflict) conflict++;
            }

            var expectedCounts = new SyntheticEvaluationCounts(
                rows.LongLength, truth, retrieved, tp, fp, truth - tp, abstain, conflict, linked);
            if (slice.Counts != expectedCounts)
                throw new InvalidDataException($"Contagens divergentes em {key}.");

            decimal? blocking = truth == 0 ? null : (decimal)retrieved / truth;
            decimal? ppv = linked == 0 ? null : (decimal)tp / linked;
            decimal? recall = truth == 0 ? null : (decimal)tp / truth;
            var coverage = rows.LongLength == 0 ? 0m : (decimal)linked / rows.LongLength;
            var expectedRates = new SyntheticEvaluationRates(blocking, ppv, recall, coverage);
            if (slice.Rates != expectedRates)
                throw new InvalidDataException($"Razões divergentes em {key}.");
        }
    }

    private static string Key(SyntheticEvaluationTruthCase item) =>
        string.Join("|",
            item.Partition,
            item.Wave,
            item.HasCpf ? "CPF_PRESENT" : "CPF_ABSENT",
            item.MotherPresent ? "MOTHER_PRESENT" : "MOTHER_ABSENT",
            item.ExactHomonym ? "EXACT_HOMONYM" : "NON_EXACT_HOMONYM");

    private static string Key(SyntheticEvaluationSlice item) =>
        string.Join("|", item.Partition, item.Wave, item.CpfStratum,
            item.MotherStratum, item.HomonymStratum);
}

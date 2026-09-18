namespace Jornada.Linkage.Parameters.Worker;

public sealed record NominalDfFrozenCandidateEvaluation(
    DfThresholdCandidate Candidate,
    CalibrationEvaluation Validation,
    CalibrationEvaluation Test);

public sealed record NominalDfBenchmarkCalibrationResult(
    IReadOnlyList<DfThresholdCandidate> ValidationCandidates,
    IReadOnlyList<CalibrationEvaluation> ValidationEvaluations,
    IReadOnlyList<NominalDfFrozenCandidateEvaluation> FrozenFrontier,
    long ValidationObservationCount,
    long TestObservationCount,
    long ValidationFrequencyCensoredCount,
    long TestFrequencyCensoredCount,
    long PublishedFirstNameOccurrences,
    decimal ReferenceExactUProbability,
    string ReferenceSource,
    string ReferenceSourceVersion,
    string ReferenceFingerprintSha256,
    IbgeGeographicScope ReferenceGeographicScope,
    string? ReferenceGeographicCode,
    string BenchmarkGeneratorVersion,
    string CalibrationAlgorithmVersion,
    int BenchmarkSeed,
    long BenchmarkMinimumSupportOccurrences,
    int TrainBasisPoints,
    int ValidationBasisPoints,
    int TestBasisPoints,
    string EvidenceAlgorithmVersion,
    string SimilarityAlgorithmVersion,
    string TermFrequencyAlgorithmVersion);

/// <summary>
/// Calibra a fronteira DF exclusivamente em VALIDATION e aplica a fronteira congelada
/// em TEST. TRAIN nunca participa da geração de thresholds, da seleção de Pareto nem
/// da avaliação independente final.
///
/// A primeira versão usa somente FirstName porque essa é a projeção nominal com
/// semântica de frequência publicável já disponível sem inferir sobrenome de nome
/// completo. TEST não retroalimenta a seleção.
/// </summary>
public static class NominalDfBenchmarkCalibrator
{
    public const string AlgorithmVersion = "NOMINAL_DF_BENCHMARK_CALIBRATION_V1";

    public static NominalDfBenchmarkCalibrationResult Calibrate(
        IbgeTypedNameFrequencySnapshot snapshot,
        IEnumerable<IbgeNominalBenchmarkPair> pairs,
        IbgeNominalBenchmarkOptions benchmarkOptions,
        decimal tfWeight = 1m,
        decimal tfMinimumUValue = 0m,
        int maxSimilarityValues = 32,
        int maxTfValues = 32)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentNullException.ThrowIfNull(benchmarkOptions);
        ArgumentOutOfRangeException.ThrowIfLessThan(tfWeight, 0m);
        ArgumentOutOfRangeException.ThrowIfLessThan(tfMinimumUValue, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tfMinimumUValue, 1m);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSimilarityValues);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTfValues);

        if (benchmarkOptions.TrainBasisPoints +
            benchmarkOptions.ValidationBasisPoints +
            benchmarkOptions.TestBasisPoints != 10_000)
            throw new ArgumentException("As partições do benchmark devem totalizar 10.000 basis points.", nameof(benchmarkOptions));

        var materialized = pairs.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("O benchmark DF não pode ser vazio.", nameof(pairs));

        var duplicatePairId = materialized
            .GroupBy(static pair => pair.PairId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicatePairId is not null)
            throw new ArgumentException($"PairId duplicado no benchmark: {duplicatePairId.Key}.", nameof(pairs));

        EnsureNoPersonPartitionLeakage(materialized);

        var validationPairs = materialized
            .Where(static pair => pair.Partition == BenchmarkPartition.Validation)
            .ToArray();
        var testPairs = materialized
            .Where(static pair => pair.Partition == BenchmarkPartition.Test)
            .ToArray();

        EnsurePartitionHasBothClasses(validationPairs, BenchmarkPartition.Validation);
        EnsurePartitionHasBothClasses(testPairs, BenchmarkPartition.Test);

        var positiveFirstNameOccurrences = snapshot.Entries
            .Where(static entry =>
                entry.StatisticKind == IbgeNameStatisticKind.FirstName &&
                entry.Occurrences > 0)
            .GroupBy(static entry => entry.Name, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => checked(group.Sum(item => item.Occurrences)),
                StringComparer.Ordinal);

        if (positiveFirstNameOccurrences.Count == 0)
            throw new ArgumentException(
                "Snapshot IBGE não contém frequências positivas de primeiro nome.",
                nameof(snapshot));

        var publishedFirstNameOccurrences = checked(positiveFirstNameOccurrences.Values.Sum());
        var firstNameProbabilities = positiveFirstNameOccurrences.ToDictionary(
            static pair => pair.Key,
            pair => (decimal)pair.Value / publishedFirstNameOccurrences,
            StringComparer.Ordinal);

        decimal referenceExactUProbability = 0m;
        foreach (var probability in firstNameProbabilities.Values)
            referenceExactUProbability += probability * probability;

        var validationObservations = BuildObservations(
            validationPairs,
            firstNameProbabilities,
            referenceExactUProbability,
            tfWeight,
            tfMinimumUValue);
        var testObservations = BuildObservations(
            testPairs,
            firstNameProbabilities,
            referenceExactUProbability,
            tfWeight,
            tfMinimumUValue);

        var validationCandidates = DfThresholdSearch.GenerateCandidates(
            validationObservations,
            maxSimilarityValues,
            maxTfValues);

        if (validationCandidates.Count == 0)
            throw new InvalidOperationException(
                "VALIDATION não contém evidência DF com frequência suficiente para gerar thresholds.");

        var validationEvaluations = validationCandidates
            .Select(candidate => DfThresholdSearch.Evaluate(candidate, validationObservations))
            .ToArray();

        var validationFrontier = CalibrationCandidatePareto.NonDominated(validationEvaluations);
        var candidateById = validationCandidates.ToDictionary(
            static candidate => candidate.CandidateId,
            StringComparer.Ordinal);

        var frozenFrontier = validationFrontier
            .Select(validation =>
            {
                var candidate = candidateById[validation.CandidateId];
                var test = DfThresholdSearch.Evaluate(candidate, testObservations);
                return new NominalDfFrozenCandidateEvaluation(candidate, validation, test);
            })
            .ToArray();

        var similarityAlgorithmVersion = validationObservations[0].Evidence.SimilarityAlgorithmVersion;
        if (validationObservations.Any(observation =>
                !string.Equals(
                    observation.Evidence.SimilarityAlgorithmVersion,
                    similarityAlgorithmVersion,
                    StringComparison.Ordinal)))
            throw new InvalidOperationException("VALIDATION contém versões de similaridade incompatíveis.");

        return new NominalDfBenchmarkCalibrationResult(
            validationCandidates,
            validationEvaluations,
            frozenFrontier,
            validationObservations.LongLength,
            testObservations.LongLength,
            validationObservations.LongCount(static observation => observation.Evidence.FrequencyCensored),
            testObservations.LongCount(static observation => observation.Evidence.FrequencyCensored),
            publishedFirstNameOccurrences,
            referenceExactUProbability,
            snapshot.Source,
            snapshot.SourceVersion,
            snapshot.FingerprintSha256,
            snapshot.GeographicScope,
            snapshot.GeographicCode,
            IbgeNominalBenchmarkOptions.GeneratorVersion,
            AlgorithmVersion,
            benchmarkOptions.Seed,
            benchmarkOptions.MinimumSupportOccurrences,
            benchmarkOptions.TrainBasisPoints,
            benchmarkOptions.ValidationBasisPoints,
            benchmarkOptions.TestBasisPoints,
            NominalDfEvidenceCalculator.AlgorithmVersion,
            similarityAlgorithmVersion,
            SplinkCompatibleTermFrequency.AlgorithmVersion);
    }

    private static DfCalibrationObservation[] BuildObservations(
        IReadOnlyList<IbgeNominalBenchmarkPair> pairs,
        IReadOnlyDictionary<string, decimal> firstNameProbabilities,
        decimal referenceExactUProbability,
        decimal tfWeight,
        decimal tfMinimumUValue)
    {
        var result = new DfCalibrationObservation[pairs.Count];

        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            var leftKey = NormalizeSnapshotKey(pair.Left.FirstName);
            var rightKey = NormalizeSnapshotKey(pair.Right.FirstName);
            var hasLeft = firstNameProbabilities.TryGetValue(leftKey, out var leftFrequency);
            var hasRight = firstNameProbabilities.TryGetValue(rightKey, out var rightFrequency);

            var evidence = NominalDfEvidenceCalculator.Evaluate(
                pair.Left.FirstName,
                pair.Right.FirstName,
                hasLeft ? leftFrequency : null,
                hasRight ? rightFrequency : null,
                referenceExactUProbability,
                tfWeight,
                tfMinimumUValue,
                frequencyCensored: !hasLeft || !hasRight);

            result[index] = new DfCalibrationObservation(pair.IsTrueMatch, evidence);
        }

        return result;
    }

    private static string NormalizeSnapshotKey(string value) =>
        value.Trim().ToUpperInvariant();

    private static void EnsurePartitionHasBothClasses(
        IReadOnlyCollection<IbgeNominalBenchmarkPair> pairs,
        BenchmarkPartition partition)
    {
        if (pairs.Count == 0)
            throw new ArgumentException($"O benchmark não contém pares em {partition}.");

        if (!pairs.Any(static pair => pair.IsTrueMatch))
            throw new ArgumentException($"{partition} precisa conter ao menos um MATCH conhecido.");

        if (!pairs.Any(static pair => !pair.IsTrueMatch))
            throw new ArgumentException($"{partition} precisa conter ao menos um NON_MATCH conhecido.");
    }

    private static void EnsureNoPersonPartitionLeakage(
        IReadOnlyCollection<IbgeNominalBenchmarkPair> pairs)
    {
        var partitionByPerson = new Dictionary<string, BenchmarkPartition>(StringComparer.Ordinal);

        foreach (var pair in pairs)
        {
            EnsurePersonPartition(pair.Left.BasePersonId, pair.Partition, pair.PairId, partitionByPerson);
            EnsurePersonPartition(pair.Right.BasePersonId, pair.Partition, pair.PairId, partitionByPerson);
        }
    }

    private static void EnsurePersonPartition(
        string basePersonId,
        BenchmarkPartition partition,
        string pairId,
        IDictionary<string, BenchmarkPartition> partitionByPerson)
    {
        if (string.IsNullOrWhiteSpace(basePersonId))
            throw new ArgumentException($"{pairId}: BasePersonId é obrigatório.");

        if (partitionByPerson.TryGetValue(basePersonId, out var existing) && existing != partition)
            throw new ArgumentException(
                $"{basePersonId} aparece em mais de uma partição ({existing}/{partition}); benchmark com vazamento.");

        partitionByPerson[basePersonId] = partition;
    }
}

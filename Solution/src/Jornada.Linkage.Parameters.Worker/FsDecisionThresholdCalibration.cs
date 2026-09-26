using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

public enum FsDecisionCalibrationPartition
{
    Train,
    Validation,
    Test
}

public sealed record FsDecisionRankedCandidate(
    Guid PessoaUuid,
    decimal Posterior,
    decimal LogOdds,
    bool DemographicExactCollisionRisk = false);

public sealed record FsDecisionCalibrationScenario(
    string ScenarioId,
    Guid BasePersonUuid,
    Guid? ExpectedResolvedUuid,
    FsDecisionCalibrationPartition Partition,
    IReadOnlyList<FsDecisionRankedCandidate> RankedCandidates);

public sealed record FsDecisionThresholdCandidate(
    string CandidateId,
    decimal Threshold,
    decimal ConflictMarginLogOdds,
    decimal DualThresholdConflictFloor);

public sealed record FsDecisionThresholdFrozenEvaluation(
    FsDecisionThresholdCandidate Candidate,
    CalibrationEvaluation Validation,
    CalibrationEvaluation Test);

public sealed record FsDecisionThresholdCalibrationResult(
    string Version,
    int Seed,
    int ValidationBasisPoints,
    int TestBasisPoints,
    IReadOnlyList<FsDecisionThresholdCandidate> Candidates,
    IReadOnlyList<CalibrationEvaluation> ValidationEvaluations,
    IReadOnlyList<FsDecisionThresholdFrozenEvaluation> FrozenFrontier,
    FsDecisionThresholdFrozenEvaluation? Selected,
    int ValidationPositiveScenarios,
    int ValidationNegativeScenarios,
    int TestPositiveScenarios,
    int TestNegativeScenarios)
{
    public bool HasSelectedCandidate => Selected is not null;
    // Limite discreto congelado na calibração; default 0 preserva o safety gate legado.
    public int MaxFpValidationBasisPoints { get; init; }
    public int MaxFpTestBasisPoints { get; init; }
    public long MaxFpValidationAbsolute { get; init; }
    public long MaxFpTestAbsolute { get; init; }
    public bool TestSafetyPassed => Selected is not null
        && Selected.Test.FalsePositive <= MaxFpTestAbsolute;

    // Diagnostics aggregate ONLY frozen TEST decisions. They never participate in
    // threshold selection, Pareto ordering, conflict-floor calibration or promotion.
    // Null means VALIDATION did not select a candidate, so TEST was not audited.
    public long? TestWrongPersonFalsePositive { get; init; }
    public long? TestLeaveTruthOutFalsePositive { get; init; }
}

/// <summary>
/// Calibração operacional dos parâmetros de decisão do FS V6.
///
/// A grade é derivada somente de fronteiras observadas em VALIDATION. TEST nunca gera
/// threshold, margem, fronteira ou desempate. O calibrador preserva a fronteira de Pareto
/// FP/FN e não cria custo relativo entre esses erros. A seleção operacional pré-HML aplica
/// limite de falso vínculo calibrado de forma explícita, sem usar TEST para escolher.
/// Entre candidatos dentro do orçamento de VALIDATION, minimiza FN e inconclusivos.
/// A contagem permitida usa teto discreto sobre TODOS os cenários rotulados, pois
/// FalsePositive inclui tanto pessoa errada em positivos como leave-truth-out negativos.
/// O teto pode exceder a taxa nominal em partições pequenas; persistimos a taxa efetiva.
/// </summary>
public static class FsDecisionThresholdCalibrator
{
    public const string Version = "FS_DECISION_THRESHOLD_PARETO_V1";
    public const string LeaveTruthOutVersion = "CPF_LABELED_LEAVE_TRUTH_OUT_V1";

    public static FsDecisionThresholdCalibrationResult Calibrate(
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> baseParameters,
        IReadOnlyList<FsDecisionCalibrationScenario> scenarios,
        int seed,
        int validationBasisPoints,
        int testBasisPoints,
        int maxThresholdValues = 48,
        int maxMarginValues = 48,
        int maxConflictFloorValues = 64,
        int maxFpValidationBasisPoints = 0,
        int maxFpTestBasisPoints = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        ArgumentNullException.ThrowIfNull(baseParameters);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(validationBasisPoints);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(testBasisPoints);
        if (validationBasisPoints + testBasisPoints >= 10_000)
            throw new ArgumentException("VALIDATION + TEST deve deixar uma partição TRAIN não vazia.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThresholdValues);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarginValues);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConflictFloorValues);
        if (maxFpValidationBasisPoints is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(maxFpValidationBasisPoints));
        if (maxFpTestBasisPoints is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(maxFpTestBasisPoints));

        var validation = scenarios
            .Where(static x => x.Partition == FsDecisionCalibrationPartition.Validation)
            .OrderBy(static x => x.ScenarioId, StringComparer.Ordinal)
            .ToArray();
        var test = scenarios
            .Where(static x => x.Partition == FsDecisionCalibrationPartition.Test)
            .OrderBy(static x => x.ScenarioId, StringComparer.Ordinal)
            .ToArray();

        EnsureBothScenarioClasses(validation, "VALIDATION");
        EnsureBothScenarioClasses(test, "TEST");
        // Não usar somente positivos como denominador: FalsePositive também conta
        // as resoluções indevidas dos cenários negativos leave-truth-out.
        var maxFpValidation = FalsePositiveBudget(validation.Length, maxFpValidationBasisPoints);
        var maxFpTest = FalsePositiveBudget(test.Length, maxFpTestBasisPoints);

        var thresholdValues = Thin(
            BoundaryValues(
                validation
                    .Where(static x => x.RankedCandidates.Count > 0)
                    .Select(static x => x.RankedCandidates[0].Posterior),
                upperBound: 1m),
            maxThresholdValues);

        var marginValues = Thin(
            BoundaryValues(
                validation
                    .Where(static x => x.RankedCandidates.Count > 1)
                    .Select(static x => Math.Max(
                        0m,
                        x.RankedCandidates[0].LogOdds - x.RankedCandidates[1].LogOdds)),
                lowerSeed: 0m),
            maxMarginValues);

        if (thresholdValues.Count == 0)
            throw new InvalidOperationException("VALIDATION não produziu score top-1 para calibrar T_LINKAGE.");
        if (marginValues.Count == 0)
            marginValues = new[] { 0m };

        var candidates = new List<FsDecisionThresholdCandidate>(
            checked(thresholdValues.Count * marginValues.Count));
        foreach (var threshold in thresholdValues)
        foreach (var margin in marginValues)
            candidates.Add(new FsDecisionThresholdCandidate(
                CandidateId(threshold, margin, threshold),
                threshold,
                margin,
                threshold));

        var validationEvaluations = candidates
            .Select(candidate => Evaluate(algorithmVersion, baseParameters, candidate, validation))
            .ToArray();
        var frontierEvaluations = CalibrationCandidatePareto.NonDominated(validationEvaluations);
        var candidateById = candidates.ToDictionary(static x => x.CandidateId, StringComparer.Ordinal);

        var frozen = frontierEvaluations
            .Select(validationEvaluation =>
            {
                var candidate = candidateById[validationEvaluation.CandidateId];
                var testEvaluation = Evaluate(algorithmVersion, baseParameters, candidate, test);
                return new FsDecisionThresholdFrozenEvaluation(
                    candidate,
                    validationEvaluation,
                    testEvaluation);
            })
            .OrderBy(static x => x.Validation.FalsePositive)
            .ThenBy(static x => x.Validation.FalseNegative)
            .ThenBy(static x => x.Validation.Inconclusive)
            .ThenByDescending(static x => x.Candidate.Threshold)
            .ThenByDescending(static x => x.Candidate.ConflictMarginLogOdds)
            .ThenBy(static x => x.Candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();

        // A restrição é fixada antes de avaliar TEST; o Pareto continua minimizando
        // FN entre os candidatos admissíveis, sem buscar zero FP artificialmente.
        var provisionalSelected = frozen
            .Where(x => x.Validation.FalsePositive <= maxFpValidation)
            .OrderBy(static x => x.Validation.FalseNegative)
            .ThenBy(static x => x.Validation.Inconclusive)
            .ThenByDescending(static x => x.Candidate.Threshold)
            .ThenByDescending(static x => x.Candidate.ConflictMarginLogOdds)
            .ThenBy(static x => x.Candidate.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();

        FsDecisionThresholdFrozenEvaluation? selected = null;
        if (provisionalSelected is not null)
        {
            // T_LINKAGE não pode ser reutilizado como piso da guarda de ambiguidade:
            // aumentar T pode fazer o segundo candidato cair abaixo do corte e transformar
            // um CONFLITO em RESOLVIDO. Depois de selecionar T/margem por Pareto, calibramos
            // um piso independente usando somente VALIDATION. Entre pisos que produzem
            // exatamente a mesma matriz de decisão observada, escolhemos o menor (mais
            // conservador fora dos pontos observados). TEST continua sem retroalimentação.
            var floorVariants = ConflictFloorValues(
                    validation,
                    provisionalSelected.Candidate.Threshold,
                    maxConflictFloorValues)
                .Select(floor => new FsDecisionThresholdCandidate(
                    CandidateId(
                        provisionalSelected.Candidate.Threshold,
                        provisionalSelected.Candidate.ConflictMarginLogOdds,
                        floor),
                    provisionalSelected.Candidate.Threshold,
                    provisionalSelected.Candidate.ConflictMarginLogOdds,
                    floor))
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Validation = Evaluate(algorithmVersion, baseParameters, candidate, validation)
                })
                .Where(x => SameDecisionMatrix(x.Validation, provisionalSelected.Validation))
                .OrderBy(static x => x.Candidate.DualThresholdConflictFloor)
                .ThenBy(static x => x.Candidate.CandidateId, StringComparer.Ordinal)
                .ToArray();

            var canonical = floorVariants.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Nenhum piso de conflito reproduziu a decisão VALIDATION selecionada.");

            selected = new FsDecisionThresholdFrozenEvaluation(
                canonical.Candidate,
                canonical.Validation,
                Evaluate(algorithmVersion, baseParameters, canonical.Candidate, test));
        }

        // The TEST split stays frozen and is used once for the safety gate. The
        // partitioned counts explain *which kind* of false link failed the gate,
        // without returning CPF, UUID, scenario IDs, names or TEST-derived thresholds.
        long? wrongPersonFp = null;
        long? leaveTruthOutFp = null;
        if (selected is not null)
        {
            if (selected.Test.FalsePositive == 0)
            {
                wrongPersonFp = 0;
                leaveTruthOutFp = 0;
            }
            else
            {
                wrongPersonFp = Evaluate(
                    algorithmVersion, baseParameters, selected.Candidate,
                    test.Where(static x => x.ExpectedResolvedUuid is not null).ToArray()).FalsePositive;
                leaveTruthOutFp = Evaluate(
                    algorithmVersion, baseParameters, selected.Candidate,
                    test.Where(static x => x.ExpectedResolvedUuid is null).ToArray()).FalsePositive;
                if (wrongPersonFp + leaveTruthOutFp != selected.Test.FalsePositive)
                    throw new InvalidOperationException("Auditoria agregada do safety gate TEST diverge da matriz congelada.");
            }
        }

        return new FsDecisionThresholdCalibrationResult(
            Version,
            seed,
            validationBasisPoints,
            testBasisPoints,
            Array.AsReadOnly(candidates.ToArray()),
            Array.AsReadOnly(validationEvaluations),
            Array.AsReadOnly(frozen),
            selected,
            validation.Count(static x => x.ExpectedResolvedUuid is not null),
            validation.Count(static x => x.ExpectedResolvedUuid is null),
            test.Count(static x => x.ExpectedResolvedUuid is not null),
            test.Count(static x => x.ExpectedResolvedUuid is null))
        {
            TestWrongPersonFalsePositive = wrongPersonFp,
            TestLeaveTruthOutFalsePositive = leaveTruthOutFp,
            MaxFpValidationBasisPoints = maxFpValidationBasisPoints,
            MaxFpTestBasisPoints = maxFpTestBasisPoints,
            MaxFpValidationAbsolute = maxFpValidation,
            MaxFpTestAbsolute = maxFpTest
        };
    }

    public static IReadOnlyDictionary<string, decimal> ApplySelected(
        IReadOnlyDictionary<string, decimal> parameters,
        FsDecisionThresholdCalibrationResult result,
        long maxFpTest = 0)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(result);

        var selected = result.Selected
            ?? throw new InvalidOperationException(
                $"Calibração FS sem candidato Pareto dentro do limite VALIDATION: " +
                $"maxFp={result.MaxFpValidationAbsolute}; bp={result.MaxFpValidationBasisPoints}; " +
                $"cenarios={result.ValidationPositiveScenarios + result.ValidationNegativeScenarios}.");
        if (maxFpTest < 0 || maxFpTest != result.MaxFpTestAbsolute)
            throw new InvalidOperationException(
                "Limite TEST divergente do orçamento congelado no resultado da calibração.");
        if (selected.Test.FalsePositive > maxFpTest)
            throw new InvalidOperationException(
                $"Candidato FS congelado falhou em TEST: falsePositive={selected.Test.FalsePositive}; " +
                $"limitePermitido={maxFpTest}; " +
                $"fpPessoaErrada={result.TestWrongPersonFalsePositive}; " +
                $"fpLeaveTruthOut={result.TestLeaveTruthOutFalsePositive}; " +
                $"testPositivos={result.TestPositiveScenarios}; testNegativos={result.TestNegativeScenarios}; " +
                $"validationFP={selected.Validation.FalsePositive}; validationFN={selected.Validation.FalseNegative}; " +
                $"testFN={selected.Test.FalseNegative}; " +
                $"threshold={selected.Candidate.Threshold.ToString("G29", CultureInfo.InvariantCulture)}; " +
                $"margemLogOdds={selected.Candidate.ConflictMarginLogOdds.ToString("G29", CultureInfo.InvariantCulture)}; " +
                $"pisoConflito={selected.Candidate.DualThresholdConflictFloor.ToString("G29", CultureInfo.InvariantCulture)}. " +
                "Nenhum threshold foi promovido.");

        var output = new Dictionary<string, decimal>(parameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.Threshold] = selected.Candidate.Threshold,
            // V6 usa log-odds. CONFLICT_MARGIN é mantido apenas para o contrato legado/core,
            // dentro de seu domínio histórico; o valor efetivo fica em LOG_ODDS.
            [LinkageParameterCatalog.ConflictMargin] = Math.Clamp(selected.Candidate.ConflictMarginLogOdds, 0.000001m, 0.999999m),
            [LinkageParameterCatalog.LogOddsConflictMargin] = selected.Candidate.ConflictMarginLogOdds,
            [LinkageParameterCatalog.DualThresholdConflictGuard] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloor] = selected.Candidate.DualThresholdConflictFloor,
            ["FS_DECISION_THRESHOLD_PARETO_V1"] = 1m,
            ["FS_DECISION_CALIBRATION_CONFLICT_FLOOR_CANONICALIZATION_V1"] = 1m,
            ["FS_DECISION_CALIBRATION_SEED"] = result.Seed,
            ["FS_DECISION_CALIBRATION_VALIDATION_BP"] = result.ValidationBasisPoints,
            ["FS_DECISION_CALIBRATION_TEST_BP"] = result.TestBasisPoints,
            ["FS_DECISION_CALIBRATION_TRAIN_BP"] = 10_000 - result.ValidationBasisPoints - result.TestBasisPoints,
            ["FS_DECISION_CALIBRATION_BASE_PERSON_SPLIT_V1"] = 1m,
            ["FS_DECISION_CALIBRATION_CANDIDATES"] = result.Candidates.Count,
            ["FS_DECISION_CALIBRATION_FRONTIER"] = result.FrozenFrontier.Count,
            ["FS_DECISION_CALIBRATION_VALIDATION_POSITIVE"] = result.ValidationPositiveScenarios,
            ["FS_DECISION_CALIBRATION_VALIDATION_NEGATIVE"] = result.ValidationNegativeScenarios,
            ["FS_DECISION_CALIBRATION_TEST_POSITIVE"] = result.TestPositiveScenarios,
            ["FS_DECISION_CALIBRATION_TEST_NEGATIVE"] = result.TestNegativeScenarios,
            ["FS_DECISION_CALIBRATION_VALIDATION_FP"] = selected.Validation.FalsePositive,
            ["FS_DECISION_CALIBRATION_VALIDATION_FN"] = selected.Validation.FalseNegative,
            ["FS_DECISION_CALIBRATION_VALIDATION_INCONCLUSIVE"] = selected.Validation.Inconclusive,
            ["FS_DECISION_CALIBRATION_TEST_FP"] = selected.Test.FalsePositive,
            ["FS_DECISION_CALIBRATION_TEST_FN"] = selected.Test.FalseNegative,
            ["FS_DECISION_CALIBRATION_TEST_INCONCLUSIVE"] = selected.Test.Inconclusive,
            ["FS_DECISION_CALIBRATION_RATE_GATE_V1"] = 1m,
            ["FS_DECISION_CALIBRATION_MAX_FP_VALIDATION_BP"] = result.MaxFpValidationBasisPoints,
            ["FS_DECISION_CALIBRATION_MAX_FP_TEST_BP"] = result.MaxFpTestBasisPoints,
            ["FS_DECISION_CALIBRATION_VALIDATION_FP_LIMIT"] = result.MaxFpValidationAbsolute,
            ["FS_DECISION_CALIBRATION_TEST_FP_LIMIT"] = result.MaxFpTestAbsolute,
            ["FS_DECISION_CALIBRATION_VALIDATION_DENOMINATOR"] = result.ValidationPositiveScenarios + result.ValidationNegativeScenarios,
            ["FS_DECISION_CALIBRATION_TEST_DENOMINATOR"] = result.TestPositiveScenarios + result.TestNegativeScenarios,
            ["FS_DECISION_CALIBRATION_VALIDATION_FP_OBSERVED_BP"] =
                10_000m * selected.Validation.FalsePositive / (result.ValidationPositiveScenarios + result.ValidationNegativeScenarios),
            ["FS_DECISION_CALIBRATION_TEST_FP_OBSERVED_BP"] =
                10_000m * selected.Test.FalsePositive / (result.TestPositiveScenarios + result.TestNegativeScenarios),
            ["FS_DECISION_CALIBRATION_VALIDATION_FP_EFFECTIVE_CAP_BP"] =
                10_000m * result.MaxFpValidationAbsolute / (result.ValidationPositiveScenarios + result.ValidationNegativeScenarios),
            ["FS_DECISION_CALIBRATION_TEST_FP_EFFECTIVE_CAP_BP"] =
                10_000m * result.MaxFpTestAbsolute / (result.TestPositiveScenarios + result.TestNegativeScenarios),
            ["FS_DECISION_CALIBRATION_TEST_FP_WRONG_PERSON"] = result.TestWrongPersonFalsePositive ?? 0L,
            ["FS_DECISION_CALIBRATION_TEST_FP_LEAVE_TRUTH_OUT"] = result.TestLeaveTruthOutFalsePositive ?? 0L,
            ["FS_DECISION_CALIBRATION_LEAVE_TRUTH_OUT_V1"] = 1m
        };

        return output;
    }

    /// <summary>
    /// Arredondamento discreto explícito. Em partições pequenas a proporção
    /// efetiva pode superar os basis points nominais; ambas são auditadas.
    /// </summary>
    public static long FalsePositiveBudget(int labeledScenarios, int maxFpBasisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(labeledScenarios);
        if (maxFpBasisPoints is < 0 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(maxFpBasisPoints));
        return ((long)labeledScenarios * maxFpBasisPoints + 9_999L) / 10_000L;
    }

    public static FsDecisionCalibrationPartition Partition(
        Guid basePersonUuid,
        int seed,
        int validationBasisPoints,
        int testBasisPoints)
    {
        if (validationBasisPoints <= 0 || testBasisPoints <= 0 ||
            validationBasisPoints + testBasisPoints >= 10_000)
            throw new ArgumentOutOfRangeException(
                nameof(validationBasisPoints),
                "VALIDATION e TEST devem ser positivos e somar menos de 10.000 basis points.");

        var material = Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{seed}:{basePersonUuid:D}"));
        var hash = SHA256.HashData(material);
        // SQL Server usa os mesmos três primeiros bytes como inteiro positivo
        // (HASHBYTES sobre ASCII/UTF-8 equivalente para seed:GUID) para manter o split
        // idêntico antes da geração de pares.
        var bucketValue = (hash[0] << 16) | (hash[1] << 8) | hash[2];
        var bucket = bucketValue % 10_000;
        var trainCut = 10_000 - validationBasisPoints - testBasisPoints;
        if (bucket < trainCut)
            return FsDecisionCalibrationPartition.Train;
        if (bucket < trainCut + validationBasisPoints)
            return FsDecisionCalibrationPartition.Validation;
        return FsDecisionCalibrationPartition.Test;
    }

    public static CalibrationEvaluation EvaluateFrozen(
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> baseParameters,
        FsDecisionThresholdCandidate candidate,
        IReadOnlyList<FsDecisionCalibrationScenario> scenarios)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        ArgumentNullException.ThrowIfNull(baseParameters);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scenarios);
        return Evaluate(algorithmVersion, baseParameters, candidate, scenarios);
    }

    private static CalibrationEvaluation Evaluate(
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> baseParameters,
        FsDecisionThresholdCandidate candidate,
        IReadOnlyList<FsDecisionCalibrationScenario> scenarios)
    {
        var parameters = new Dictionary<string, decimal>(baseParameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.Threshold] = candidate.Threshold,
            [LinkageParameterCatalog.ConflictMargin] = Math.Clamp(candidate.ConflictMarginLogOdds, 0.000001m, 0.999999m),
            [LinkageParameterCatalog.LogOddsConflictMargin] = candidate.ConflictMarginLogOdds,
            [LinkageParameterCatalog.DualThresholdConflictGuard] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloorV2] = 1m,
            [LinkageParameterCatalog.DualThresholdConflictFloor] = candidate.DualThresholdConflictFloor
        };
        var model = LinkageModelPolicy.Create(
            Guid.Empty,
            0,
            algorithmVersion,
            parameters);

        long tp = 0, tn = 0, fp = 0, fn = 0, inconclusive = 0;
        foreach (var scenario in scenarios)
        {
            var ranking = scenario.RankedCandidates
                .Select(static x => new CandidateScore(
                    x.PessoaUuid,
                    x.Posterior,
                    x.LogOdds,
                    x.DemographicExactCollisionRisk))
                .ToArray();
            var decision = ProbabilisticLinkageDecisions.ResolveRanked(
                model,
                ranking,
                "SEM_CANDIDATO_CALIBRACAO");

            if (scenario.ExpectedResolvedUuid is { } expected)
            {
                if (decision.Status == ResolutionStatus.RESOLVIDO &&
                    decision.PessoaUuidResolvido == expected)
                {
                    tp++;
                    continue;
                }

                fn++;
                if (decision.Status == ResolutionStatus.RESOLVIDO)
                    fp++;
                else
                    inconclusive++;
                continue;
            }

            if (decision.Status == ResolutionStatus.RESOLVIDO)
                fp++;
            else
                tn++;
        }

        return new CalibrationEvaluation(
            candidate.CandidateId,
            tp,
            tn,
            fp,
            fn,
            inconclusive,
            scenarios.Count);
    }

    private static void EnsureBothScenarioClasses(
        IReadOnlyCollection<FsDecisionCalibrationScenario> scenarios,
        string partition)
    {
        if (scenarios.Count == 0)
            throw new InvalidOperationException($"{partition} está vazio.");
        if (!scenarios.Any(static x => x.ExpectedResolvedUuid is not null))
            throw new InvalidOperationException($"{partition} não contém cenários positivos.");
        if (!scenarios.Any(static x => x.ExpectedResolvedUuid is null))
            throw new InvalidOperationException($"{partition} não contém cenários leave-truth-out.");
    }

    private static IReadOnlyList<decimal> BoundaryValues(
        IEnumerable<decimal> source,
        decimal? lowerSeed = null,
        decimal? upperBound = null)
    {
        var values = source
            .Where(static x => x >= 0m)
            .Distinct()
            .Order()
            .ToList();

        if (lowerSeed is { } lower && !values.Contains(lower))
            values.Insert(0, lower);

        if (values.Count > 0)
        {
            var last = values[^1];
            var above = last + 0.00000001m;
            if (upperBound is { } upper)
                above = Math.Min(upper, above);
            if (above > last && !values.Contains(above))
                values.Add(above);
        }

        if (upperBound is { } fixedUpper && !values.Contains(fixedUpper))
            values.Add(fixedUpper);

        return values;
    }

    private static IReadOnlyList<decimal> Thin(
        IReadOnlyList<decimal> sorted,
        int maxValues)
    {
        if (sorted.Count <= maxValues)
            return sorted;
        if (maxValues == 1)
            return new[] { sorted[^1] };

        var selected = new SortedSet<decimal>();
        for (var index = 0; index < maxValues; index++)
        {
            var position = (int)Math.Round(
                index * (sorted.Count - 1d) / (maxValues - 1d),
                MidpointRounding.AwayFromZero);
            selected.Add(sorted[position]);
        }
        return selected.ToArray();
    }

    private static IReadOnlyList<decimal> ConflictFloorValues(
        IReadOnlyList<FsDecisionCalibrationScenario> validation,
        decimal linkThreshold,
        int maxValues)
    {
        var values = new SortedSet<decimal> { 0m, linkThreshold };
        foreach (var score in validation
                     .Where(static x => x.RankedCandidates.Count > 1)
                     .Select(static x => x.RankedCandidates[1].Posterior)
                     .Where(score => score <= linkThreshold))
        {
            values.Add(score);
            var justAbove = Math.Min(linkThreshold, score + 0.00000001m);
            values.Add(justAbove);
        }

        return Thin(values.ToArray(), maxValues);
    }

    private static bool SameDecisionMatrix(
        CalibrationEvaluation left,
        CalibrationEvaluation right) =>
        left.TruePositive == right.TruePositive &&
        left.TrueNegative == right.TrueNegative &&
        left.FalsePositive == right.FalsePositive &&
        left.FalseNegative == right.FalseNegative &&
        left.Inconclusive == right.Inconclusive &&
        left.Total == right.Total;

    private static string CandidateId(decimal threshold, decimal margin, decimal conflictFloor)
    {
        var canonical =
            $"{Version}\nT={threshold.ToString("G29", CultureInfo.InvariantCulture)}\nM={margin.ToString("G29", CultureInfo.InvariantCulture)}\nF={conflictFloor.ToString("G29", CultureInfo.InvariantCulture)}\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

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
    decimal LogOdds);

public sealed record FsDecisionCalibrationScenario(
    string ScenarioId,
    Guid BasePersonUuid,
    Guid? ExpectedResolvedUuid,
    FsDecisionCalibrationPartition Partition,
    IReadOnlyList<FsDecisionRankedCandidate> RankedCandidates);

public sealed record FsDecisionThresholdCandidate(
    string CandidateId,
    decimal Threshold,
    decimal ConflictMarginLogOdds);

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
    public bool TestSafetyPassed => Selected is { Test.FalsePositive: 0 };
}

/// <summary>
/// Calibração operacional dos dois parâmetros de decisão do FS V6.
///
/// A grade é derivada somente de fronteiras observadas em VALIDATION. TEST nunca gera
/// threshold, margem, fronteira ou desempate. O calibrador preserva a fronteira de Pareto
/// FP/FN e não cria custo relativo entre esses erros. A seleção operacional pré-HML aplica
/// uma restrição já existente no projeto: falso vínculo resolvido precisa ser zero no
/// corpus de validação. Entre candidatos com os mesmos erros e a mesma inconclusão, o
/// desempate apenas escolhe a representação mais conservadora da mesma decisão observada.
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
        int maxMarginValues = 48)
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
                CandidateId(threshold, margin),
                threshold,
                margin));

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

        // Restrição de promoção pré-HML já documentada: nenhum falso vínculo resolvido
        // no corpus de segurança. Isto não é peso relativo FP/FN do Pareto.
        var selected = frozen
            .Where(static x => x.Validation.FalsePositive == 0)
            .OrderBy(static x => x.Validation.FalseNegative)
            .ThenBy(static x => x.Validation.Inconclusive)
            .ThenByDescending(static x => x.Candidate.Threshold)
            .ThenByDescending(static x => x.Candidate.ConflictMarginLogOdds)
            .ThenBy(static x => x.Candidate.CandidateId, StringComparer.Ordinal)
            .FirstOrDefault();

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
            test.Count(static x => x.ExpectedResolvedUuid is null));
    }

    public static IReadOnlyDictionary<string, decimal> ApplySelected(
        IReadOnlyDictionary<string, decimal> parameters,
        FsDecisionThresholdCalibrationResult result)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(result);

        var selected = result.Selected
            ?? throw new InvalidOperationException(
                "Calibração FS não encontrou candidato Pareto que satisfaça o safety gate de zero falso vínculo em VALIDATION.");
        if (selected.Test.FalsePositive != 0)
            throw new InvalidOperationException(
                $"Candidato FS congelado falhou em TEST: falsePositive={selected.Test.FalsePositive}. Nenhum threshold foi promovido.");

        var output = new Dictionary<string, decimal>(parameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.Threshold] = selected.Candidate.Threshold,
            // V6 usa log-odds. CONFLICT_MARGIN é mantido apenas para o contrato legado/core,
            // dentro de seu domínio histórico; o valor efetivo fica em LOG_ODDS.
            [LinkageParameterCatalog.ConflictMargin] = Math.Min(selected.Candidate.ConflictMarginLogOdds, 0.999999m),
            [LinkageParameterCatalog.LogOddsConflictMargin] = selected.Candidate.ConflictMarginLogOdds,
            ["FS_DECISION_THRESHOLD_PARETO_V1"] = 1m,
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
            ["FS_DECISION_CALIBRATION_ZERO_FP_PROMOTION_GATE"] = 1m,
            ["FS_DECISION_CALIBRATION_LEAVE_TRUTH_OUT_V1"] = 1m
        };

        return output;
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

    private static CalibrationEvaluation Evaluate(
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> baseParameters,
        FsDecisionThresholdCandidate candidate,
        IReadOnlyList<FsDecisionCalibrationScenario> scenarios)
    {
        var parameters = new Dictionary<string, decimal>(baseParameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.Threshold] = candidate.Threshold,
            [LinkageParameterCatalog.ConflictMargin] = Math.Min(candidate.ConflictMarginLogOdds, 0.999999m),
            [LinkageParameterCatalog.LogOddsConflictMargin] = candidate.ConflictMarginLogOdds
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
                .Select(static x => new CandidateScore(x.PessoaUuid, x.Posterior, x.LogOdds))
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

    private static string CandidateId(decimal threshold, decimal margin)
    {
        var canonical =
            $"{Version}\nT={threshold.ToString("G29", CultureInfo.InvariantCulture)}\nM={margin.ToString("G29", CultureInfo.InvariantCulture)}\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

/// <summary>
/// Usa a mesma amostra CPF rotulada já materializada pelo estimador de prior.
/// O CPF não entra no blocking nem no score: serve somente como truth UUID.
/// Para cada observação rotulada são construídos um cenário positivo e um cenário
/// leave-truth-out negativo, ambos mantidos na mesma partição por pessoa-base.
/// </summary>
public static class BlockingDecisionThresholdCalibrationReader
{
    public static async Task<IReadOnlyList<FsDecisionCalibrationScenario>> ReadAsync(
        SqlConnection connection,
        string normalizationVersion,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        string algorithmVersion,
        IReadOnlyDictionary<string, decimal> parameters,
        int seed,
        int validationBasisPoints,
        int testBasisPoints,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithmVersion);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeoutSeconds);

        var canonicalPasses = passes
            .Select(pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(static x => x.PassId, StringComparer.Ordinal)
            .ToArray();
        if (canonicalPasses.Length == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));

        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        var model = LinkageModelPolicy.Create(
            Guid.Empty,
            0,
            algorithmVersion,
            parameters);

        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('tempdb..#candidate_prior_observations') IS NULL
               OR OBJECT_ID('tempdb..#candidate_prior_keys') IS NULL
                THROW 51920, 'Amostra CPF do prior não foi preparada antes da calibração de decisão.', 1;

            WITH requirements AS (
                SELECT observation_id,pass_id,COUNT(DISTINCT feature) AS required_features
                FROM #candidate_prior_keys
                GROUP BY observation_id,pass_id
            ),
            feature_hits AS (
                SELECT DISTINCT pk.observation_id,pk.pass_id,pk.feature,k.pessoa_uuid
                FROM #candidate_prior_keys pk
                JOIN identidade.blocking_chave k
                  ON k.normalizacao_versao=@normalizacao
                 AND k.projection_schema_version=@projection_schema
                 AND k.projection_fingerprint_sha256=@projection_fingerprint
                 AND k.atributo=pk.feature
                 AND k.valor_normalizado=pk.valor
                 AND (pk.current_only=0 OR k.vigencia_fim IS NULL)
                 AND NOT EXISTS (
                     SELECT 1
                     FROM identidade.vinculo_fonte vf_val
                     JOIN silver.pessoa_observacao po_val
                       ON po_val.pessoa_observacao_id=vf_val.pessoa_observacao_id
                     WHERE vf_val.pessoa_uuid=k.pessoa_uuid
                       AND po_val.codigo_pessoa_origem LIKE N'SCALE-VAL-%'
                 )
            ),
            pass_candidates AS (
                SELECT h.observation_id,h.pass_id,h.pessoa_uuid
                FROM feature_hits h
                JOIN requirements r
                  ON r.observation_id=h.observation_id
                 AND r.pass_id=h.pass_id
                GROUP BY h.observation_id,h.pass_id,h.pessoa_uuid
                HAVING COUNT(DISTINCT h.feature)=MAX(r.required_features)
            ),
            candidate_union AS (
                SELECT DISTINCT observation_id,pessoa_uuid
                FROM pass_candidates
            )
            SELECT
                o.observation_id,o.truth_uuid,o.nome,o.nascimento,o.nome_mae,
                g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae
            FROM #candidate_prior_observations o
            LEFT JOIN candidate_union c ON c.observation_id=o.observation_id
            LEFT JOIN gold.pessoa g
              ON g.pessoa_uuid=c.pessoa_uuid
             AND g.estado_identidade=N'REFERENCIA'
            ORDER BY o.observation_id,c.pessoa_uuid;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = projection.SchemaVersion;
        command.Parameters.Add("@projection_fingerprint", SqlDbType.Char, 64).Value = projection.Fingerprint;

        var groups = new Dictionary<long, RawObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var observationId = reader.GetInt64(0);
            if (!groups.TryGetValue(observationId, out var raw))
            {
                raw = new RawObservation(
                    observationId,
                    reader.GetGuid(1),
                    reader.GetString(2),
                    DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    []);
                groups.Add(observationId, raw);
            }

            if (!reader.IsDBNull(5))
            {
                raw.Candidates.Add(new LinkageCandidate(
                    reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        var result = new List<FsDecisionCalibrationScenario>();
        foreach (var raw in groups.Values.OrderBy(static x => x.ObservationId))
        {
            var partition = FsDecisionThresholdCalibrator.Partition(
                raw.TruthUuid,
                seed,
                validationBasisPoints,
                testBasisPoints);
            if (partition == FsDecisionCalibrationPartition.Train)
                continue;

            var observation = new IdentityObservation(
                null,
                "CALIBRACAO_CPF_OCULTO",
                raw.Name,
                raw.BirthDate,
                raw.MotherName);
            var ranked = ProbabilisticLinkageDecisions.Rank(
                model,
                observation,
                raw.Candidates)
                .Select(static x => new FsDecisionRankedCandidate(
                    x.PessoaUuid,
                    x.Score,
                    x.LogOdds))
                .ToArray();

            var prefix = $"{raw.ObservationId}:{raw.TruthUuid:D}";
            result.Add(new FsDecisionCalibrationScenario(
                $"{prefix}:POS",
                raw.TruthUuid,
                raw.TruthUuid,
                partition,
                ranked));

            result.Add(new FsDecisionCalibrationScenario(
                $"{prefix}:NEG_LEAVE_TRUTH_OUT",
                raw.TruthUuid,
                null,
                partition,
                ranked.Where(x => x.PessoaUuid != raw.TruthUuid).ToArray()));
        }

        return result;
    }

    private sealed record RawObservation(
        long ObservationId,
        Guid TruthUuid,
        string Name,
        DateOnly BirthDate,
        string? MotherName,
        List<LinkageCandidate> Candidates);
}

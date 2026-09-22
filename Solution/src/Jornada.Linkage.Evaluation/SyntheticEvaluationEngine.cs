using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Evaluation;

public sealed record SyntheticEvaluationOptions(
    Guid ModelId,
    string GeneratedRoot,
    int MaxCandidatePairs,
    int CommandTimeoutSeconds,
    IReadOnlyList<ulong>? ExpectedSeeds = null);

public sealed record SyntheticEvaluationReport(
    string SchemaVersion,
    string Nature,
    string Purpose,
    string EvaluatorVersion,
    string EnvironmentProfile,
    DateTimeOffset GeneratedAtUtc,
    SyntheticEvaluationInput Input,
    SyntheticEvaluationModel Model,
    SyntheticObservationStrata ObservationStrata,
    SyntheticBlockingEvaluation Blocking,
    SyntheticDistributionRecovery MRecovery,
    SyntheticDistributionRecovery URecovery,
    SyntheticTransportability Transportability,
    SyntheticThresholdOracle DecisionOracle,
    SyntheticMultiSeedContext MultiSeed,
    IReadOnlyList<string> Safeguards);

public sealed record SyntheticEvaluationInput(
    string GeneratedRoot,
    ulong GeneratorSeed,
    string GenerationManifestSha256,
    string ObservationsSha256,
    string BridgeTruthSha256,
    string BridgeManifestSha256,
    string CorpusInputFingerprintSha256,
    string BridgeVersion,
    string GeneratorVersion,
    string RulesetVersion,
    string RngVersion,
    int SourceObservationCount,
    int MaterializedObservationCount,
    int ExcludedObservationCount);

public sealed record SyntheticEvaluationModel(
    Guid ModelId,
    int Version,
    string Status,
    string AlgorithmVersion,
    string NormalizationVersion,
    string? SampleMethod,
    int? MatchedSampleSize,
    int? USampleSize,
    string RuleSetVersion,
    string RuleSetFingerprintSha256,
    string ModelSnapshotSha256,
    string UProbabilitySemantics,
    string NominalNameUSource,
    string NominalMotherNameUSource);

public sealed record SyntheticObservationStrata(
    long CpfPresentCnsPresent,
    long CpfPresentCnsAbsent,
    long CpfAbsentCnsPresent,
    long CpfAbsentCnsAbsent);

public sealed record SyntheticBlockingEvaluation(
    long MaterializedObservations,
    long TrueInterSourcePairs,
    long TruePairsRetainedByUnion,
    decimal TrueMatchRecall,
    long PossibleNonMatchPairs,
    long CandidateUnionPairs,
    long CandidateUnionNonMatchPairs,
    decimal NonMatchRetention,
    decimal ReductionRatio,
    IReadOnlyList<SyntheticBlockingPassEvaluation> Passes);

public sealed record SyntheticBlockingPassEvaluation(
    string PassId,
    IReadOnlyList<string> Fields,
    long CandidatePairs,
    long CandidateNonMatchPairs,
    long TruePairsRetained,
    decimal TrueMatchRecall);

public sealed record SyntheticDistributionRecovery(
    string TruthUniverse,
    long PairCount,
    SyntheticComparisonDistribution TruthRaw,
    SyntheticComparisonDistribution TruthReweighted,
    SyntheticComparisonDistribution Model,
    SyntheticDistributionDistance RawDistance,
    SyntheticDistributionDistance ReweightedDistance);

public sealed record SyntheticComparisonDistribution(
    IReadOnlyDictionary<string, decimal> Name,
    IReadOnlyDictionary<string, decimal> MotherName,
    IReadOnlyDictionary<string, decimal> BirthSemantic);

public sealed record SyntheticDistributionDistance(
    decimal NameTotalVariation,
    decimal MotherNameTotalVariation,
    decimal BirthSemanticTotalVariation);

public sealed record SyntheticTransportability(
    long CpfLabeledPairs,
    long CpfAbsentPairs,
    SyntheticDistributionDistance RawDistance,
    SyntheticDistributionDistance ReweightedDistance);

public sealed record SyntheticDecisionObjective(
    long TruePositive,
    long TrueNegative,
    long FalsePositive,
    long FalseNegative,
    long Inconclusive,
    long Total);

public sealed record SyntheticMultiSeedContext(
    ulong CurrentSeed,
    IReadOnlyList<ulong> ExpectedSeeds);

public sealed record SyntheticDecisionQualitySlice(
    string Partition,
    string Stratum,
    long TruePositive,
    long FalsePositive,
    long FalseNegative,
    long Inconclusive,
    long Total,
    decimal Precision,
    decimal Recall);

public sealed record SyntheticThresholdOracle(
    string Status,
    string CalibrationPolicyVersion,
    int ScenarioCount,
    int ValidationScenarioCount,
    int TestScenarioCount,
    decimal? ModelThreshold,
    decimal? ModelConflictMarginLogOdds,
    decimal? ModelConflictFloor,
    decimal? OracleThreshold,
    decimal? OracleConflictMarginLogOdds,
    decimal? OracleConflictFloor,
    SyntheticDecisionObjective? ModelValidation,
    SyntheticDecisionObjective? ModelTest,
    SyntheticDecisionObjective? OracleValidation,
    SyntheticDecisionObjective? OracleTest,
    decimal? ValidationFrontierL1Distance,
    bool CoordinatesComparable,
    decimal? ThresholdAbsoluteDelta,
    decimal? ConflictMarginAbsoluteDelta,
    decimal? ConflictFloorAbsoluteDelta,
    IReadOnlyList<SyntheticDecisionQualitySlice> ModelQuality);

public sealed class SyntheticEvaluationEngine(SqlConnection connection, int commandTimeoutSeconds)
{
    public const string SchemaVersion = "JORNADA_SYNTHETIC_EVALUATION_V1";
    public const string Nature = "SYNTHETIC_PARAMETER_RECOVERY";
    public const string Purpose = "ENGINEERING_EVIDENCE_ONLY_NOT_PROMOTABLE";
    public const string EvaluatorVersion = "JORNADA_SYNTHETIC_EVALUATOR_V1";
    public const string RequiredEnvironmentProfile = "Development";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SyntheticEvaluationReport> EvaluateAsync(
        SyntheticEvaluationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ModelId == Guid.Empty)
            throw new ArgumentException("ModelId obrigatório.", nameof(options));
        if (options.MaxCandidatePairs < 1_000)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidatePairs deve ser >= 1000.");

        var root = Path.GetFullPath(options.GeneratedRoot);
        var observationsPath = Path.Combine(root, "corpus", "observacoes.csv");
        var generationManifestPath = Path.Combine(root, "corpus", "generation-manifest.json");
        var truthPath = Path.Combine(root, "ingestion", "bridge-truth.jsonl");
        var manifestPath = Path.Combine(root, "ingestion", "bridge-manifest.json");
        foreach (var path in new[] { observationsPath, generationManifestPath, truthPath, manifestPath })
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Artefato sintético obrigatório ausente.", path);
        }

        var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        var generationManifest = await ReadGenerationManifestAsync(generationManifestPath, cancellationToken);
        var expectedSeeds = (options.ExpectedSeeds is { Count: > 0 }
                ? options.ExpectedSeeds
                : new[] { generationManifest.Seed })
            .Distinct()
            .Order()
            .ToArray();
        if (!expectedSeeds.Contains(generationManifest.Seed))
            throw new InvalidDataException(
                $"Seed atual {generationManifest.Seed} não pertence à lista multi-seed esperada.");
        if (!string.Equals(
                generationManifest.InputFingerprintSha256,
                manifest.CorpusInputFingerprintSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(generationManifest.GeneratorVersion, manifest.GeneratorVersion, StringComparison.Ordinal)
            || !string.Equals(generationManifest.RulesetVersion, manifest.RulesetVersion, StringComparison.Ordinal)
            || !string.Equals(generationManifest.RngVersion, manifest.RngVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "generation-manifest e bridge-manifest divergem na proveniência do corpus sintético.");
        }

        var model = await LoadModelAsync(options.ModelId, cancellationToken);
        var environmentProfile = await ReadEnvironmentProfileAsync(cancellationToken);
        if (!string.Equals(environmentProfile, RequiredEnvironmentProfile, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_EVALUATE exige Jornada.EnvironmentProfile={RequiredEnvironmentProfile}; " +
                $"atual={environmentProfile ?? "(ausente)"}.");
        }
        var modelSnapshotSha256 = await ReadModelSnapshotSha256Async(model.ModelId, cancellationToken);

        var materialized = await ReadMaterializedTruthAsync(truthPath, cancellationToken);
        var observations = ReadObservations(observationsPath, materialized);
        if (observations.Count != manifest.MaterializedObservationCount)
        {
            throw new InvalidDataException(
                $"Observações materializadas divergiram do manifesto: corpus={observations.Count}; " +
                $"manifest={manifest.MaterializedObservationCount}.");
        }

        var requiredBlockingFields = model.Passes
            .SelectMany(static pass => pass.Fields)
            .ToHashSet(StringComparer.Ordinal);
        var projected = observations
            .Select(row => new ProjectedObservation(
                row,
                BlockingProjectionKeyProjector.Project(row.Name, row.MotherName, row.BirthDate)
                    .Where(key => requiredBlockingFields.Contains(key.Feature))
                    .GroupBy(static key => key.Feature, StringComparer.Ordinal)
                    .ToDictionary(
                        static group => group.Key,
                        static group => (IReadOnlyList<string>)group.Select(static x => x.Value)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(static x => x, StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal)))
            .ToArray();

        var truePairs = BuildTrueInterSourcePairs(projected);
        var candidate = BuildCandidateUnion(projected, model.Passes, truePairs, options.MaxCandidatePairs);

        var nameContract = LinkageParameterCatalog.NameComparisonContractForAlgorithm(model.AlgorithmVersion);
        var cpfLabeledPairs = truePairs
            .Where(pair => HasSameNonNullCpf(projected[pair.Left].Observation, projected[pair.Right].Observation))
            .ToArray();
        var noCpfPairs = truePairs
            .Where(pair => projected[pair.Left].Observation.Cpf is null
                           && projected[pair.Right].Observation.Cpf is null)
            .ToArray();

        if (cpfLabeledPairs.Length == 0)
            throw new InvalidDataException("Truth materializada não contém pares interfonte CPF-rotulados.");
        if (candidate.NonMatchPairs.Count == 0)
            throw new InvalidDataException("Ruleset não reteve não-vínculos sintéticos; u_truth_candidate_union é vazio.");

        var mRaw = Distribution(projected, cpfLabeledPairs, nameContract, reweighted: false);
        var mWeighted = Distribution(projected, cpfLabeledPairs, nameContract, reweighted: true);
        var uRaw = Distribution(projected, candidate.NonMatchPairs, nameContract, reweighted: false);
        var uWeighted = Distribution(projected, candidate.NonMatchPairs, nameContract, reweighted: true);
        var modelM = ModelDistribution(model.Parameters, "M");
        var modelU = ModelDistribution(model.Parameters, "U");

        var noCpfRaw = noCpfPairs.Length == 0
            ? EmptyDistribution()
            : Distribution(projected, noCpfPairs, nameContract, reweighted: false);
        var noCpfWeighted = noCpfPairs.Length == 0
            ? EmptyDistribution()
            : Distribution(projected, noCpfPairs, nameContract, reweighted: true);

        return new SyntheticEvaluationReport(
            SchemaVersion,
            Nature,
            Purpose,
            EvaluatorVersion,
            environmentProfile!,
            DateTimeOffset.UtcNow,
            new SyntheticEvaluationInput(
                root,
                generationManifest.Seed,
                await HashFileAsync(generationManifestPath, cancellationToken),
                await HashFileAsync(observationsPath, cancellationToken),
                await HashFileAsync(truthPath, cancellationToken),
                await HashFileAsync(manifestPath, cancellationToken),
                manifest.CorpusInputFingerprintSha256,
                manifest.BridgeVersion,
                manifest.GeneratorVersion,
                manifest.RulesetVersion,
                manifest.RngVersion,
                manifest.SourceObservationCount,
                manifest.MaterializedObservationCount,
                manifest.ExcludedObservationCount),
            new SyntheticEvaluationModel(
                model.ModelId,
                model.Version,
                model.Status,
                model.AlgorithmVersion,
                model.NormalizationVersion,
                model.SampleMethod,
                model.MatchedSampleSize,
                model.USampleSize,
                model.RuleSetVersion,
                model.RuleSetFingerprintSha256,
                modelSnapshotSha256,
                LinkageCalibrationAuditExchangePolicy.UProbabilitySemantics,
                LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(
                    model.Parameters.Select(static x => new LinkageCalibrationAuditParameter(x.Key, x.Value)).ToArray(),
                    motherName: false),
                LinkageCalibrationAuditExchangePolicy.ResolveNominalUSource(
                    model.Parameters.Select(static x => new LinkageCalibrationAuditParameter(x.Key, x.Value)).ToArray(),
                    motherName: true)),
            CountStrata(observations),
            new SyntheticBlockingEvaluation(
                projected.LongLength,
                truePairs.LongLength,
                candidate.TruePairsRetained,
                Rate(candidate.TruePairsRetained, truePairs.LongLength),
                PossibleNonMatchPairs(projected),
                candidate.UnionPairCount,
                candidate.NonMatchPairs.Count,
                Rate(candidate.NonMatchPairs.Count, PossibleNonMatchPairs(projected)),
                1m - Rate(candidate.NonMatchPairs.Count, PossibleNonMatchPairs(projected)),
                candidate.Passes),
            new SyntheticDistributionRecovery(
                "MATERIALIZED_INTER_SOURCE_TRUE_PAIRS_WITH_SAME_NON_NULL_CPF",
                cpfLabeledPairs.LongLength,
                mRaw,
                mWeighted,
                modelM,
                Distance(modelM, mRaw),
                Distance(modelM, mWeighted)),
            new SyntheticDistributionRecovery(
                "MATERIALIZED_DEDUPLICATED_BLOCKING_CANDIDATE_UNION_NON_MATCH_PAIRS",
                candidate.NonMatchPairs.Count,
                uRaw,
                uWeighted,
                modelU,
                Distance(modelU, uRaw),
                Distance(modelU, uWeighted)),
            new SyntheticTransportability(
                cpfLabeledPairs.LongLength,
                noCpfPairs.LongLength,
                noCpfPairs.Length == 0 ? ZeroDistance() : Distance(mRaw, noCpfRaw),
                noCpfPairs.Length == 0 ? ZeroDistance() : Distance(mWeighted, noCpfWeighted)),
            EvaluateDecisionOracle(projected, candidate, model),
            new SyntheticMultiSeedContext(generationManifest.Seed, expectedSeeds),
            [
                "synthetic truth is read only after the requested model exists in RASCUNHO",
                "base_person_id is used only inside this evaluator and is never emitted in the report",
                "blocking uses persisted model passes and the shared BlockingProjectionKeyProjector",
                "u truth is conditioned on the deduplicated candidate union in the materialized synthetic observation universe",
                "candidate union is exact or evaluation fails when MaxCandidatePairs is exceeded; no silent sampling",
                "decision oracle reuses FsDecisionThresholdCalibrator, FellegiSunterScoring and the frozen VALIDATION/TEST partitions; TEST never selects coordinates",
                "multi-seed expected membership is explicit and persisted; dispersion is forbidden until every expected seed is present",
                "the report does not validate or activate a model and cannot satisfy issue #31"
            ]);
    }

    private async Task<string?> ReadEnvironmentProfileAsync(CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT CONVERT(nvarchar(32),(
                SELECT value
                FROM sys.extended_properties
                WHERE class=0 AND name=N'Jornada.EnvironmentProfile'));
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        return (await command.ExecuteScalarAsync(cancellationToken)) as string;
    }

    private async Task<string> ReadModelSnapshotSha256Async(
        Guid modelId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            DECLARE @fingerprint BINARY(32);
            EXEC auditoria.sp_calcular_fingerprint_modelo_linkage
                @modelo_id=@model_id,
                @fingerprint=@fingerprint OUTPUT;
            SELECT @fingerprint;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@model_id", System.Data.SqlDbType.UniqueIdentifier).Value = modelId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not byte[] bytes || bytes.Length != 32)
            throw new InvalidDataException("Fingerprint canônico do modelo não retornou SHA-256 válido.");
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<SyntheticModelSnapshot> LoadModelAsync(Guid modelId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT modelo_id,versao,status,algoritmo_versao,normalizacao_versao,amostra_metodo,
                   amostra_m_tamanho,amostra_u_tamanho
            FROM identidade.modelo_linkage
            WHERE modelo_id=@model_id;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@model_id", System.Data.SqlDbType.UniqueIdentifier).Value = modelId;

        Guid id;
        int version;
        string status;
        string algorithm;
        string normalization;
        string? sampleMethod;
        int? matchedSize;
        int? uSize;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Modelo {modelId} não encontrado.");
            id = reader.GetGuid(0);
            version = reader.GetInt32(1);
            status = reader.GetString(2);
            algorithm = reader.GetString(3);
            normalization = reader.GetString(4);
            sampleMethod = reader.IsDBNull(5) ? null : reader.GetString(5);
            matchedSize = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            uSize = reader.IsDBNull(7) ? null : reader.GetInt32(7);
        }

        if (!string.Equals(status, "RASCUNHO", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SYNTHETIC_EVALUATE aceita somente modelo RASCUNHO; modelo {id} está {status}.");
        }

        var parameters = await LoadParametersAsync(modelId, cancellationToken);
        var ruleSet = await LoadRuleSetAsync(modelId, cancellationToken);
        return new SyntheticModelSnapshot(
            id,
            version,
            status,
            algorithm,
            normalization,
            sampleMethod,
            matchedSize,
            uSize,
            parameters,
            ruleSet.Version,
            ruleSet.Fingerprint,
            ruleSet.Passes);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> LoadParametersAsync(
        Guid modelId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "SELECT nome,valor FROM identidade.parametro_linkage WHERE modelo_id=@model_id ORDER BY nome;",
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@model_id", System.Data.SqlDbType.UniqueIdentifier).Value = modelId;
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!values.TryAdd(reader.GetString(0), reader.GetDecimal(1)))
                throw new InvalidDataException("Parâmetro duplicado no modelo.");
        }

        if (values.Count == 0)
            throw new InvalidDataException("Modelo sem parâmetros persistidos.");
        return new ReadOnlyDictionary<string, decimal>(values);
    }

    private async Task<SyntheticRuleSetSnapshot> LoadRuleSetAsync(
        Guid modelId,
        CancellationToken cancellationToken)
    {
        Guid ruleSetId;
        string version;
        string fingerprint;
        await using (var command = new SqlCommand(
                         """
                         SELECT ruleset_id,ruleset_versao,fingerprint_sha256
                         FROM identidade.linkage_ruleset
                         WHERE modelo_id=@model_id
                         ORDER BY criado_em,ruleset_id;
                         """,
                         connection)
                     {
                         CommandTimeout = commandTimeoutSeconds
                     })
        {
            command.Parameters.Add("@model_id", System.Data.SqlDbType.UniqueIdentifier).Value = modelId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("Modelo RASCUNHO sem ruleset.");
            ruleSetId = reader.GetGuid(0);
            version = reader.GetString(1);
            fingerprint = reader.GetString(2);
            if (await reader.ReadAsync(cancellationToken))
                throw new InvalidDataException("Modelo possui mais de um ruleset.");
        }

        await using var passesCommand = new SqlCommand(
            """
            SELECT p.passe_ordem,p.passe_id,c.campo_ordem,c.atributo
            FROM identidade.linkage_ruleset_passe p
            LEFT JOIN identidade.linkage_ruleset_passe_campo c
              ON c.ruleset_id=p.ruleset_id AND c.passe_ordem=p.passe_ordem
            WHERE p.ruleset_id=@ruleset_id
            ORDER BY p.passe_ordem,c.campo_ordem;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        passesCommand.Parameters.Add("@ruleset_id", System.Data.SqlDbType.UniqueIdentifier).Value = ruleSetId;

        var builders = new SortedDictionary<int, (string PassId, List<string> Fields)>();
        await using var passReader = await passesCommand.ExecuteReaderAsync(cancellationToken);
        while (await passReader.ReadAsync(cancellationToken))
        {
            var order = passReader.GetInt32(0);
            var passId = passReader.GetString(1);
            if (!builders.TryGetValue(order, out var builder))
            {
                builder = (passId, []);
                builders.Add(order, builder);
            }
            else if (!string.Equals(builder.PassId, passId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("PassId inconsistente dentro do ruleset.");
            }

            if (!passReader.IsDBNull(3))
                builder.Fields.Add(passReader.GetString(3));
        }

        var passes = builders.Values
            .Select(static item => LinkageBlockingPass.Create(item.PassId, item.Fields))
            .ToArray();
        if (passes.Length == 0)
            throw new InvalidDataException("Ruleset RASCUNHO sem passes.");
        return new SyntheticRuleSetSnapshot(version, fingerprint, passes);
    }

    private static SyntheticCandidateEvaluation BuildCandidateUnion(
        IReadOnlyList<ProjectedObservation> observations,
        IReadOnlyList<LinkageBlockingPass> passes,
        IReadOnlyCollection<PairIndex> truePairs,
        int maxCandidatePairs)
    {
        var union = new HashSet<ulong>();
        var trueSet = truePairs.Select(static pair => Pack(pair.Left, pair.Right)).ToHashSet();
        var passReports = new List<SyntheticBlockingPassEvaluation>(passes.Count);

        foreach (var pass in passes.OrderBy(static x => x.PassId, StringComparer.Ordinal))
        {
            var buckets = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < observations.Count; index++)
            {
                foreach (var signature in Signatures(observations[index].Keys, pass.Fields))
                {
                    if (!buckets.TryGetValue(signature, out var members))
                    {
                        members = [];
                        buckets.Add(signature, members);
                    }
                    members.Add(index);
                }
            }

            var passPairs = new HashSet<ulong>();
            foreach (var members in buckets.Values)
            {
                for (var left = 0; left < members.Count; left++)
                {
                    for (var right = left + 1; right < members.Count; right++)
                    {
                        var key = Pack(members[left], members[right]);
                        passPairs.Add(key);
                        union.Add(key);
                        if (union.Count > maxCandidatePairs)
                        {
                            throw new InvalidOperationException(
                                $"União candidata excedeu MaxCandidatePairs={maxCandidatePairs}; " +
                                "a avaliação falha em vez de amostrar silenciosamente.");
                        }
                    }
                }
            }

            long passTrue = 0;
            long passNonMatch = 0;
            foreach (var packed in passPairs)
            {
                if (trueSet.Contains(packed)) passTrue++;
                else passNonMatch++;
            }
            passReports.Add(new SyntheticBlockingPassEvaluation(
                pass.PassId,
                pass.Fields,
                passPairs.Count,
                passNonMatch,
                passTrue,
                Rate(passTrue, truePairs.Count)));
        }

        long trueRetained = 0;
        var nonMatches = new List<PairIndex>();
        foreach (var packed in union)
        {
            if (trueSet.Contains(packed))
            {
                trueRetained++;
                continue;
            }

            var pair = Unpack(packed);
            if (!string.Equals(
                    observations[pair.Left].Observation.BasePersonId,
                    observations[pair.Right].Observation.BasePersonId,
                    StringComparison.Ordinal))
            {
                nonMatches.Add(pair);
            }
        }

        return new SyntheticCandidateEvaluation(
            union.Count,
            trueRetained,
            union.Select(Unpack)
                .OrderBy(static pair => pair.Left)
                .ThenBy(static pair => pair.Right)
                .ToArray(),
            nonMatches,
            passReports);
    }

    private static IEnumerable<string> Signatures(
        IReadOnlyDictionary<string, IReadOnlyList<string>> keys,
        IReadOnlyList<string> fields)
    {
        var values = new IReadOnlyList<string>[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            if (!keys.TryGetValue(fields[index], out var fieldValues) || fieldValues.Count == 0)
                yield break;
            values[index] = fieldValues;
        }

        var buffer = new string[fields.Count];
        foreach (var signature in Recurse(0))
            yield return signature;

        IEnumerable<string> Recurse(int depth)
        {
            if (depth == values.Length)
            {
                yield return string.Join('', buffer);
                yield break;
            }

            foreach (var value in values[depth])
            {
                buffer[depth] = value;
                foreach (var signature in Recurse(depth + 1))
                    yield return signature;
            }
        }
    }

    private static PairIndex[] BuildTrueInterSourcePairs(IReadOnlyList<ProjectedObservation> observations)
    {
        var result = new List<PairIndex>();
        foreach (var group in observations
                     .Select((item, index) => (item, index))
                     .GroupBy(static x => x.item.Observation.BasePersonId, StringComparer.Ordinal))
        {
            var rows = group.ToArray();
            for (var left = 0; left < rows.Length; left++)
            {
                for (var right = left + 1; right < rows.Length; right++)
                {
                    if (string.Equals(
                            rows[left].item.Observation.Gestor,
                            rows[right].item.Observation.Gestor,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    result.Add(new PairIndex(rows[left].index, rows[right].index));
                }
            }
        }

        return result.ToArray();
    }

    private static long PossibleNonMatchPairs(IReadOnlyList<ProjectedObservation> observations)
    {
        var total = Choose2(observations.Count);
        var samePerson = observations
            .GroupBy(static x => x.Observation.BasePersonId, StringComparer.Ordinal)
            .Sum(static group => Choose2(group.Count()));
        return total - samePerson;
    }

    private static long Choose2(long n) => checked(n * (n - 1) / 2);

    private static SyntheticThresholdOracle EvaluateDecisionOracle(
        IReadOnlyList<ProjectedObservation> observations,
        SyntheticCandidateEvaluation candidate,
        SyntheticModelSnapshot model)
    {
        if (!LinkageParameterCatalog.UsesDecisionEvidence(model.AlgorithmVersion))
            return DecisionOracleNotEvaluable("NOT_EVALUABLE_ALGORITHM", model);

        var required = LinkageParameterCatalog.CoreScoringRequired
            .Concat(LinkageParameterCatalog.DecisionEvidenceRequired)
            .Append(LinkageParameterCatalog.DualThresholdConflictFloor)
            .Append("FS_DECISION_CALIBRATION_SEED")
            .Append("FS_DECISION_CALIBRATION_VALIDATION_BP")
            .Append("FS_DECISION_CALIBRATION_TEST_BP")
            .Distinct(StringComparer.Ordinal)
            .Where(name => !model.Parameters.ContainsKey(name))
            .ToArray();
        if (required.Length > 0)
            return DecisionOracleNotEvaluable("NOT_EVALUABLE_MISSING_CALIBRATION_CONTRACT", model);

        var representatives = observations
            .GroupBy(static x => x.Observation.BasePersonId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(static x => x.Observation.Cpf is not null)
                    .ThenBy(static x => x.Observation.Gestor, StringComparer.Ordinal)
                    .ThenBy(static x => x.Observation.ObservationId, StringComparer.Ordinal)
                    .First().Observation,
                StringComparer.Ordinal);
        var personIds = representatives.Keys.ToDictionary(
            static id => id,
            DeterministicSyntheticPersonGuid,
            StringComparer.Ordinal);

        var neighbours = Enumerable.Range(0, observations.Count)
            .Select(static _ => new HashSet<string>(StringComparer.Ordinal))
            .ToArray();
        foreach (var pair in candidate.UnionPairs)
        {
            var leftPerson = observations[pair.Left].Observation.BasePersonId;
            var rightPerson = observations[pair.Right].Observation.BasePersonId;
            neighbours[pair.Left].Add(rightPerson);
            neighbours[pair.Right].Add(leftPerson);
        }

        var nameContract = LinkageParameterCatalog.NameComparisonContractForAlgorithm(model.AlgorithmVersion);
        var envelopes = new List<SyntheticScenarioEnvelope>();
        for (var index = 0; index < observations.Count; index++)
        {
            var observed = observations[index].Observation;
            FsDecisionCalibrationPartition partition;
            if (string.Equals(observed.Partition, "VALIDATION", StringComparison.OrdinalIgnoreCase))
                partition = FsDecisionCalibrationPartition.Validation;
            else if (string.Equals(observed.Partition, "TEST", StringComparison.OrdinalIgnoreCase))
                partition = FsDecisionCalibrationPartition.Test;
            else
                continue;

            var candidateRows = neighbours[index]
                .Select(id => representatives[id])
                .OrderBy(static x => x.BasePersonId, StringComparer.Ordinal)
                .ToArray();
            var uniqueCandidateCount = candidateRows.Length;
            var ranking = candidateRows
                .Select(row =>
                {
                    var nameState = OptionalNameState(observed.Name, row.Name, nameContract);
                    var motherState = OptionalNameState(observed.MotherName, row.MotherName, nameContract);
                    var score = FellegiSunterScoring.Calculate(
                        model.Parameters,
                        nameState,
                        motherState,
                        Math.Max(1, uniqueCandidateCount),
                        observed.BirthDate,
                        row.BirthDate);
                    var collision =
                        nameState == NameComparisonState.EXACT &&
                        observed.BirthDate == row.BirthDate;
                    return new FsDecisionRankedCandidate(
                        personIds[row.BasePersonId],
                        score.Posterior,
                        score.LogOdds,
                        collision);
                })
                .OrderByDescending(static x => x.LogOdds)
                .ThenBy(static x => x.PessoaUuid)
                .ToArray();

            var truthUuid = personIds[observed.BasePersonId];
            var prefix = observed.ObservationId + ":" + observed.BasePersonId;
            var stratum = ObservationStratum(observed);
            envelopes.Add(new SyntheticScenarioEnvelope(
                new FsDecisionCalibrationScenario(
                    prefix + ":POS",
                    truthUuid,
                    truthUuid,
                    partition,
                    ranking),
                stratum));
            envelopes.Add(new SyntheticScenarioEnvelope(
                new FsDecisionCalibrationScenario(
                    prefix + ":NEG_LEAVE_TRUTH_OUT",
                    truthUuid,
                    null,
                    partition,
                    ranking.Where(x => x.PessoaUuid != truthUuid).ToArray()),
                stratum));
        }

        var scenarios = envelopes.Select(static x => x.Scenario).ToArray();
        var validation = scenarios
            .Where(static x => x.Partition == FsDecisionCalibrationPartition.Validation)
            .ToArray();
        var test = scenarios
            .Where(static x => x.Partition == FsDecisionCalibrationPartition.Test)
            .ToArray();
        if (validation.Length == 0 || test.Length == 0)
        {
            return DecisionOracleNotEvaluable(
                "NOT_EVALUABLE_PARTITION_SUPPORT",
                model,
                scenarios.Length,
                validation.Length,
                test.Length);
        }

        var seed = DecimalToInt(model.Parameters["FS_DECISION_CALIBRATION_SEED"]);
        var validationBp = DecimalToInt(model.Parameters["FS_DECISION_CALIBRATION_VALIDATION_BP"]);
        var testBp = DecimalToInt(model.Parameters["FS_DECISION_CALIBRATION_TEST_BP"]);
        FsDecisionThresholdCalibrationResult oracle;
        try
        {
            oracle = FsDecisionThresholdCalibrator.Calibrate(
                model.AlgorithmVersion,
                model.Parameters,
                scenarios,
                seed,
                validationBp,
                testBp);
        }
        catch (InvalidOperationException)
        {
            return DecisionOracleNotEvaluable(
                "NOT_EVALUABLE_ORACLE_SUPPORT",
                model,
                scenarios.Length,
                validation.Length,
                test.Length);
        }

        var threshold = model.Parameters[LinkageParameterCatalog.Threshold];
        var margin = model.Parameters[LinkageParameterCatalog.LogOddsConflictMargin];
        var floor = model.Parameters[LinkageParameterCatalog.DualThresholdConflictFloor];
        var frozenModel = new FsDecisionThresholdCandidate(
            "PERSISTED_MODEL",
            threshold,
            margin,
            floor);
        var modelValidation = FsDecisionThresholdCalibrator.EvaluateFrozen(
            model.AlgorithmVersion,
            model.Parameters,
            frozenModel,
            validation);
        var modelTest = FsDecisionThresholdCalibrator.EvaluateFrozen(
            model.AlgorithmVersion,
            model.Parameters,
            frozenModel,
            test);

        if (oracle.Selected is null)
        {
            return new SyntheticThresholdOracle(
                "NO_SAFE_ORACLE_CANDIDATE",
                FsDecisionThresholdCalibrator.Version,
                scenarios.Length,
                validation.Length,
                test.Length,
                threshold,
                margin,
                floor,
                null,
                null,
                null,
                Objective(modelValidation),
                Objective(modelTest),
                null,
                null,
                FrontierDistance(modelValidation, oracle.FrozenFrontier),
                false,
                null,
                null,
                null,
                DecisionQuality(model, frozenModel, envelopes));
        }

        var selected = oracle.Selected;
        return new SyntheticThresholdOracle(
            "EVALUATED",
            FsDecisionThresholdCalibrator.Version,
            scenarios.Count,
            validation.Length,
            test.Length,
            threshold,
            margin,
            floor,
            selected.Candidate.Threshold,
            selected.Candidate.ConflictMarginLogOdds,
            selected.Candidate.DualThresholdConflictFloor,
            Objective(modelValidation),
            Objective(modelTest),
            Objective(selected.Validation),
            Objective(selected.Test),
            FrontierDistance(modelValidation, oracle.FrozenFrontier),
            true,
            Math.Abs(threshold - selected.Candidate.Threshold),
            Math.Abs(margin - selected.Candidate.ConflictMarginLogOdds),
            Math.Abs(floor - selected.Candidate.DualThresholdConflictFloor),
            DecisionQuality(model, frozenModel, envelopes));
    }

    private static SyntheticThresholdOracle DecisionOracleNotEvaluable(
        string status,
        SyntheticModelSnapshot model,
        int scenarioCount = 0,
        int validationCount = 0,
        int testCount = 0)
    {
        model.Parameters.TryGetValue(LinkageParameterCatalog.Threshold, out var threshold);
        var hasThreshold = model.Parameters.ContainsKey(LinkageParameterCatalog.Threshold);
        model.Parameters.TryGetValue(LinkageParameterCatalog.LogOddsConflictMargin, out var margin);
        var hasMargin = model.Parameters.ContainsKey(LinkageParameterCatalog.LogOddsConflictMargin);
        model.Parameters.TryGetValue(LinkageParameterCatalog.DualThresholdConflictFloor, out var floor);
        var hasFloor = model.Parameters.ContainsKey(LinkageParameterCatalog.DualThresholdConflictFloor);
        return new SyntheticThresholdOracle(
            status,
            FsDecisionThresholdCalibrator.Version,
            scenarioCount,
            validationCount,
            testCount,
            hasThreshold ? threshold : null,
            hasMargin ? margin : null,
            hasFloor ? floor : null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            Array.Empty<SyntheticDecisionQualitySlice>());
    }

    private static IReadOnlyList<SyntheticDecisionQualitySlice> DecisionQuality(
        SyntheticModelSnapshot model,
        FsDecisionThresholdCandidate candidate,
        IReadOnlyList<SyntheticScenarioEnvelope> envelopes)
    {
        var result = new List<SyntheticDecisionQualitySlice>();
        foreach (var partition in new[]
                 {
                     FsDecisionCalibrationPartition.Validation,
                     FsDecisionCalibrationPartition.Test
                 })
        {
            var partitionRows = envelopes
                .Where(item => item.Scenario.Partition == partition)
                .ToArray();
            if (partitionRows.Length == 0)
                continue;

            AddSlice(partition.ToString().ToUpperInvariant(), "ALL", partitionRows);
            foreach (var stratum in partitionRows
                         .Select(static item => item.Stratum)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                AddSlice(
                    partition.ToString().ToUpperInvariant(),
                    stratum,
                    partitionRows.Where(item => string.Equals(item.Stratum, stratum, StringComparison.Ordinal)).ToArray());
            }

            var cpfPresent = partitionRows
                .Where(static item => item.Stratum.StartsWith("CPF_PRESENT_", StringComparison.Ordinal))
                .ToArray();
            if (cpfPresent.Length > 0)
                AddSlice(partition.ToString().ToUpperInvariant(), "CPF_PRESENT", cpfPresent);

            var cpfAbsent = partitionRows
                .Where(static item => item.Stratum.StartsWith("CPF_ABSENT_", StringComparison.Ordinal))
                .ToArray();
            if (cpfAbsent.Length > 0)
                AddSlice(partition.ToString().ToUpperInvariant(), "CPF_ABSENT", cpfAbsent);
        }

        return result;

        void AddSlice(
            string partitionName,
            string stratum,
            IReadOnlyList<SyntheticScenarioEnvelope> rows)
        {
            var evaluation = FsDecisionThresholdCalibrator.EvaluateFrozen(
                model.AlgorithmVersion,
                model.Parameters,
                candidate,
                rows.Select(static item => item.Scenario).ToArray());
            var precisionDenominator = evaluation.TruePositive + evaluation.FalsePositive;
            var recallDenominator = evaluation.TruePositive + evaluation.FalseNegative;
            result.Add(new SyntheticDecisionQualitySlice(
                partitionName,
                stratum,
                evaluation.TruePositive,
                evaluation.FalsePositive,
                evaluation.FalseNegative,
                evaluation.Inconclusive,
                evaluation.Total,
                precisionDenominator == 0
                    ? 0m
                    : (decimal)evaluation.TruePositive / precisionDenominator,
                recallDenominator == 0
                    ? 0m
                    : (decimal)evaluation.TruePositive / recallDenominator));
        }
    }

    private static string ObservationStratum(SyntheticTruthObservation observation)
        => (observation.Cpf is not null, observation.Cns is not null) switch
        {
            (true, true) => "CPF_PRESENT_CNS_PRESENT",
            (true, false) => "CPF_PRESENT_CNS_ABSENT",
            (false, true) => "CPF_ABSENT_CNS_PRESENT",
            _ => "CPF_ABSENT_CNS_ABSENT"
        };

    private static int DecimalToInt(decimal value)
    {
        if (value != decimal.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("Parâmetro inteiro da calibração de decisão é inválido.");
        return decimal.ToInt32(value);
    }

    private static NameComparisonState? OptionalNameState(
        string? left,
        string? right,
        NameComparisonContract contract)
        => IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null
            ? null
            : IdentityComparison.CompareName(left, right, contract);

    private static Guid DeterministicSyntheticPersonGuid(string basePersonId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            "JORNADA_SYNTHETIC_ORACLE_PERSON_V1|" + basePersonId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static SyntheticDecisionObjective Objective(CalibrationEvaluation value)
        => new(
            value.TruePositive,
            value.TrueNegative,
            value.FalsePositive,
            value.FalseNegative,
            value.Inconclusive,
            value.Total);

    private static decimal FrontierDistance(
        CalibrationEvaluation model,
        IReadOnlyList<FsDecisionThresholdFrozenEvaluation> frontier)
        => frontier.Count == 0
            ? 0m
            : frontier.Min(item =>
                Math.Abs((decimal)model.FalsePositive - item.Validation.FalsePositive) +
                Math.Abs((decimal)model.FalseNegative - item.Validation.FalseNegative) +
                Math.Abs((decimal)model.Inconclusive - item.Validation.Inconclusive));

    private static SyntheticComparisonDistribution Distribution(
        IReadOnlyList<ProjectedObservation> observations,
        IReadOnlyCollection<PairIndex> pairs,
        NameComparisonContract nameContract,
        bool reweighted)
    {
        var nameStates = LinkageParameterCatalog.NameStates.ToDictionary(
            static state => state,
            static _ => 0m,
            StringComparer.Ordinal);
        var motherStates = LinkageParameterCatalog.MotherNameStates.ToDictionary(
            static state => state,
            static _ => 0m,
            StringComparer.Ordinal);
        var birthStates = LinkageParameterCatalog.BirthSemanticStates.ToDictionary(
            static state => state,
            static _ => 0m,
            StringComparer.Ordinal);

        decimal totalName = 0m;
        decimal totalMother = 0m;
        decimal totalBirth = 0m;
        foreach (var pair in pairs)
        {
            var left = observations[pair.Left].Observation;
            var right = observations[pair.Right].Observation;
            var weight = reweighted
                ? (left.EvaluationWeight + right.EvaluationWeight) / 2m
                : 1m;

            var name = IdentityComparison.CompareName(left.Name, right.Name, nameContract).ToString();
            nameStates[name] += weight;
            totalName += weight;

            var mother = IdentityComparison.NormalizeText(left.MotherName) is null
                         || IdentityComparison.NormalizeText(right.MotherName) is null
                ? "MISSING"
                : IdentityComparison.CompareName(left.MotherName, right.MotherName, nameContract).ToString();
            motherStates[mother] += weight;
            totalMother += weight;

            var birth = BirthDateSemanticEvidence.Classify(left.BirthDate, right.BirthDate);
            birthStates[birth] += weight;
            totalBirth += weight;
        }

        return new SyntheticComparisonDistribution(
            Normalize(nameStates, totalName),
            Normalize(motherStates, totalMother),
            Normalize(birthStates, totalBirth));
    }

    private static SyntheticComparisonDistribution ModelDistribution(
        IReadOnlyDictionary<string, decimal> parameters,
        string prefix)
    {
        var name = ReadModelDistribution(parameters, $"{prefix}_NOME", LinkageParameterCatalog.NameStates);
        var motherStates = parameters.ContainsKey($"{prefix}_NOME_MAE_MISSING")
            ? LinkageParameterCatalog.MotherNameStates
            : LinkageParameterCatalog.NameStates;
        var mother = ReadModelDistribution(parameters, $"{prefix}_NOME_MAE", motherStates);
        var birth = ReadModelDistribution(
            parameters,
            $"{prefix}_NASCIMENTO_SEMANTICO",
            LinkageParameterCatalog.BirthSemanticStates);
        return new SyntheticComparisonDistribution(name, mother, birth);
    }

    private static IReadOnlyDictionary<string, decimal> ReadModelDistribution(
        IReadOnlyDictionary<string, decimal> parameters,
        string prefix,
        IReadOnlyList<string> states)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            var key = $"{prefix}_{state}";
            if (!parameters.TryGetValue(key, out var value))
                throw new InvalidDataException($"Modelo não possui parâmetro obrigatório {key} para avaliação sintética.");
            result[state] = value;
        }
        return new ReadOnlyDictionary<string, decimal>(result);
    }

    private static IReadOnlyDictionary<string, decimal> Normalize(
        Dictionary<string, decimal> values,
        decimal total)
    {
        if (total <= 0m)
            return new ReadOnlyDictionary<string, decimal>(
                values.ToDictionary(static x => x.Key, static _ => 0m, StringComparer.Ordinal));

        return new ReadOnlyDictionary<string, decimal>(
            values.ToDictionary(static x => x.Key, x => x.Value / total, StringComparer.Ordinal));
    }

    private static SyntheticDistributionDistance Distance(
        SyntheticComparisonDistribution left,
        SyntheticComparisonDistribution right)
        => new(
            TotalVariation(left.Name, right.Name),
            TotalVariation(left.MotherName, right.MotherName),
            TotalVariation(left.BirthSemantic, right.BirthSemantic));

    private static SyntheticDistributionDistance ZeroDistance() => new(0m, 0m, 0m);

    private static decimal TotalVariation(
        IReadOnlyDictionary<string, decimal> left,
        IReadOnlyDictionary<string, decimal> right)
    {
        var keys = left.Keys.Union(right.Keys, StringComparer.Ordinal);
        return 0.5m * keys.Sum(key =>
            Math.Abs(
                (left.TryGetValue(key, out var l) ? l : 0m)
                - (right.TryGetValue(key, out var r) ? r : 0m)));
    }

    private static SyntheticComparisonDistribution EmptyDistribution() => new(
        new ReadOnlyDictionary<string, decimal>(
            LinkageParameterCatalog.NameStates.ToDictionary(static x => x, static _ => 0m, StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, decimal>(
            LinkageParameterCatalog.MotherNameStates.ToDictionary(static x => x, static _ => 0m, StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, decimal>(
            LinkageParameterCatalog.BirthSemanticStates.ToDictionary(static x => x, static _ => 0m, StringComparer.Ordinal)));

    private static bool HasSameNonNullCpf(SyntheticTruthObservation left, SyntheticTruthObservation right)
        => left.Cpf is not null
           && right.Cpf is not null
           && string.Equals(left.Cpf, right.Cpf, StringComparison.Ordinal);

    private static SyntheticObservationStrata CountStrata(IReadOnlyCollection<SyntheticTruthObservation> observations)
        => new(
            observations.LongCount(static x => x.Cpf is not null && x.Cns is not null),
            observations.LongCount(static x => x.Cpf is not null && x.Cns is null),
            observations.LongCount(static x => x.Cpf is null && x.Cns is not null),
            observations.LongCount(static x => x.Cpf is null && x.Cns is null));

    private static decimal Rate(long numerator, long denominator)
        => denominator <= 0 ? 0m : (decimal)numerator / denominator;

    private static ulong Pack(int left, int right)
    {
        var a = Math.Min(left, right);
        var b = Math.Max(left, right);
        return ((ulong)(uint)a << 32) | (uint)b;
    }

    private static PairIndex Unpack(ulong value)
        => new((int)(value >> 32), (int)(value & uint.MaxValue));

    private static IReadOnlyList<SyntheticTruthObservation> ReadObservations(
        string path,
        IReadOnlyDictionary<string, SyntheticBridgeTruthRow> materialized)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = reader.ReadLine()
            ?? throw new InvalidDataException("observacoes.csv vazio.");
        var header = ParseCsvLine(headerLine);
        var index = header
            .Select((name, position) => (name, position))
            .ToDictionary(static x => x.name, static x => x.position, StringComparer.Ordinal);

        var required = new[]
        {
            "observacao_id", "base_person_id", "particao", "gestor", "nome", "nome_mae",
            "data_nascimento", "cpf", "cns", "evaluation_weight"
        };
        foreach (var column in required)
        {
            if (!index.ContainsKey(column))
                throw new InvalidDataException($"observacoes.csv sem coluna {column}.");
        }

        var result = new List<SyntheticTruthObservation>(materialized.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            var fields = ParseCsvLine(line);
            var observationId = Field(fields, index["observacao_id"]);
            if (!materialized.TryGetValue(observationId, out var bridge))
                continue;
            if (!seen.Add(observationId))
                throw new InvalidDataException($"observacao_id duplicado no corpus: {observationId}.");

            var basePersonId = Field(fields, index["base_person_id"]);
            if (!string.Equals(basePersonId, bridge.BasePersonId, StringComparison.Ordinal))
                throw new InvalidDataException($"Truth bridge/corpus divergiu para {observationId}.");

            var birthText = Field(fields, index["data_nascimento"]);
            if (!DateOnly.TryParseExact(
                    birthText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var birth))
            {
                throw new InvalidDataException(
                    $"Observação materializada {observationId} não possui data válida.");
            }

            var weightText = Field(fields, index["evaluation_weight"]);
            if (!decimal.TryParse(
                    weightText,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var weight)
                || weight <= 0m)
            {
                throw new InvalidDataException(
                    $"evaluation_weight inválido em {observationId}.");
            }

            result.Add(new SyntheticTruthObservation(
                observationId,
                basePersonId,
                Field(fields, index["particao"]),
                Field(fields, index["gestor"]),
                Field(fields, index["nome"]),
                NullIfEmpty(Field(fields, index["nome_mae"])),
                birth,
                NullIfEmpty(Field(fields, index["cpf"])),
                NullIfEmpty(Field(fields, index["cns"])),
                weight));
        }

        if (seen.Count != materialized.Count)
        {
            var missing = materialized.Keys.Where(key => !seen.Contains(key)).Take(5).ToArray();
            throw new InvalidDataException(
                $"Nem toda truth MATERIALIZADA existe em observacoes.csv; faltantes={materialized.Count - seen.Count}; " +
                $"exemplos={string.Join(",", missing)}.");
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, SyntheticBridgeTruthRow>> ReadMaterializedTruthAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SyntheticBridgeTruthRow>(StringComparer.Ordinal);
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var row = JsonSerializer.Deserialize<SyntheticBridgeTruthRow>(line, JsonOptions)
                ?? throw new InvalidDataException("Linha inválida em bridge-truth.jsonl.");
            if (!string.Equals(row.Status, "MATERIALIZADA", StringComparison.Ordinal))
                continue;
            if (string.IsNullOrWhiteSpace(row.ObservationId) || string.IsNullOrWhiteSpace(row.BasePersonId))
                throw new InvalidDataException("Truth materializada sem identificadores internos.");
            if (!result.TryAdd(row.ObservationId, row))
                throw new InvalidDataException($"ObservationId duplicado no sidecar: {row.ObservationId}.");
        }

        if (result.Count == 0)
            throw new InvalidDataException("bridge-truth.jsonl não contém linhas MATERIALIZADA.");
        return result;
    }

    private static async Task<SyntheticGenerationManifest> ReadGenerationManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SyntheticGenerationManifest>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("generation-manifest.json inválido.");
    }

    private static async Task<SyntheticBridgeManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SyntheticBridgeManifest>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("bridge-manifest.json inválido.");
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        if (quoted)
            throw new InvalidDataException("CSV contém aspas não fechadas.");
        result.Add(current.ToString());
        return result;
    }

    private static string Field(IReadOnlyList<string> fields, int index)
        => index < fields.Count
            ? fields[index]
            : throw new InvalidDataException("Linha CSV possui menos campos que o cabeçalho.");

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private sealed record SyntheticModelSnapshot(
        Guid ModelId,
        int Version,
        string Status,
        string AlgorithmVersion,
        string NormalizationVersion,
        string? SampleMethod,
        int? MatchedSampleSize,
        int? USampleSize,
        IReadOnlyDictionary<string, decimal> Parameters,
        string RuleSetVersion,
        string RuleSetFingerprintSha256,
        IReadOnlyList<LinkageBlockingPass> Passes);

    private sealed record SyntheticRuleSetSnapshot(
        string Version,
        string Fingerprint,
        IReadOnlyList<LinkageBlockingPass> Passes);

    private sealed record SyntheticGenerationManifest(
        [property: JsonPropertyName("generator_version")] string GeneratorVersion,
        [property: JsonPropertyName("ruleset_version")] string RulesetVersion,
        [property: JsonPropertyName("rng_version")] string RngVersion,
        [property: JsonPropertyName("seed")] ulong Seed,
        [property: JsonPropertyName("input_fingerprint_sha256")] string InputFingerprintSha256);

    private sealed record SyntheticBridgeManifest(
        string BridgeVersion,
        string GeneratorVersion,
        string RulesetVersion,
        string RngVersion,
        int SourceObservationCount,
        int MaterializedObservationCount,
        int ExcludedObservationCount,
        string CorpusInputFingerprintSha256);

    private sealed record SyntheticBridgeTruthRow(
        string Status,
        string ObservationId,
        string BasePersonId);

    private sealed record SyntheticTruthObservation(
        string ObservationId,
        string BasePersonId,
        string Partition,
        string Gestor,
        string Name,
        string? MotherName,
        DateOnly BirthDate,
        string? Cpf,
        string? Cns,
        decimal EvaluationWeight);

    private sealed record ProjectedObservation(
        SyntheticTruthObservation Observation,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Keys);

    private readonly record struct PairIndex(int Left, int Right);

    private sealed record SyntheticScenarioEnvelope(
        FsDecisionCalibrationScenario Scenario,
        string Stratum);

    private sealed record SyntheticCandidateEvaluation(
        long UnionPairCount,
        long TruePairsRetained,
        IReadOnlyList<PairIndex> UnionPairs,
        IReadOnlyList<PairIndex> NonMatchPairs,
        IReadOnlyList<SyntheticBlockingPassEvaluation> Passes);
}

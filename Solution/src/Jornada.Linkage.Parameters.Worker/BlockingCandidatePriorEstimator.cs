using System.Data;
using System.Globalization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record BlockingCandidatePriorEstimate(
    long ObservationSampleSize,
    long ObservationsWithCandidates,
    long TruthCandidatePairs,
    long FalseCandidatePairs,
    long TotalCandidatePairs,
    decimal CandidateRecall,
    decimal? MatchProbability);

/// <summary>
/// Estima, somente sobre observações com verdade CPF conhecida, a prevalência de match
/// dentro do conjunto candidato produzido pelo ruleset. O CPF é usado apenas como rótulo:
/// as chaves candidatas são reprojetadas dos demais atributos com o mesmo plano físico.
/// </summary>
public static class BlockingCandidatePriorEstimator
{
    public const string MethodVersion = "CPF_LABELED_BLOCKING_CANDIDATE_PAIR_PRIOR_V1";

    public static IReadOnlyDictionary<string, decimal> AppendDiagnostics(
        IReadOnlyDictionary<string, decimal> parameters,
        BlockingCandidatePriorEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(estimate);

        var result = new Dictionary<string, decimal>(parameters, StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.CandidatePairPriorDiagnosticV1] = 1m,
            ["DIAG_CANDIDATE_PRIOR_OBSERVATION_SAMPLE_SIZE"] = estimate.ObservationSampleSize,
            ["DIAG_CANDIDATE_PRIOR_OBSERVATIONS_WITH_CANDIDATES"] = estimate.ObservationsWithCandidates,
            ["DIAG_CANDIDATE_PRIOR_TRUTH_PAIRS"] = estimate.TruthCandidatePairs,
            ["DIAG_CANDIDATE_PRIOR_FALSE_PAIRS"] = estimate.FalseCandidatePairs,
            ["DIAG_CANDIDATE_PRIOR_TOTAL_PAIRS"] = estimate.TotalCandidatePairs,
            ["DIAG_CANDIDATE_PRIOR_CANDIDATE_RECALL"] = estimate.CandidateRecall,
            ["DIAG_CANDIDATE_PRIOR_BOTH_CLASSES_OBSERVED"] =
                estimate.TruthCandidatePairs > 0 && estimate.FalseCandidatePairs > 0 ? 1m : 0m,
            ["DIAG_CANDIDATE_PRIOR_ACTIVE_SCORE_CHANGED"] = 0m
        };

        if (estimate.ObservationSampleSize > 0)
            result["DIAG_CANDIDATE_PRIOR_MEAN_CANDIDATES_PER_OBSERVATION"] =
                decimal.Divide(estimate.TotalCandidatePairs, estimate.ObservationSampleSize);

        if (estimate.MatchProbability is { } empirical)
        {
            result["DIAG_CANDIDATE_PRIOR_AVAILABLE"] = 1m;
            result["DIAG_CANDIDATE_PRIOR_MATCH_PROBABILITY"] = empirical;

            if (parameters.TryGetValue(LinkageParameterCatalog.PriorMatchProbability, out var active))
            {
                result["DIAG_CANDIDATE_PRIOR_ACTIVE_PRIOR_PROBABILITY"] = active;
                result["DIAG_CANDIDATE_PRIOR_DELTA_LOG_ODDS_VS_ACTIVE"] =
                    Convert.ToDecimal(Logit(empirical) - Logit(active), CultureInfo.InvariantCulture);
            }
        }
        else
        {
            result["DIAG_CANDIDATE_PRIOR_AVAILABLE"] = 0m;
        }

        return result;
    }

    private static double Logit(decimal probability)
    {
        var p = Math.Clamp(Convert.ToDouble(probability, CultureInfo.InvariantCulture), 0.000000001d, 0.999999999d);
        return Math.Log(p / (1d - p));
    }

    public static async Task<BlockingCandidatePriorEstimate> EstimateAsync(
        SqlConnection connection,
        string normalizationVersion,
        IReadOnlyCollection<LinkageBlockingPass> passes,
        int observationSampleSize,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizationVersion);
        ArgumentNullException.ThrowIfNull(passes);
        if (passes.Count == 0)
            throw new ArgumentException("Ao menos um passe de blocking é obrigatório.", nameof(passes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(observationSampleSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeoutSeconds);

        var canonicalPasses = passes
            .Select(pass => LinkageBlockingPass.Create(pass.PassId, pass.Fields))
            .OrderBy(pass => pass.PassId, StringComparer.Ordinal)
            .ToArray();

        var observations = await LoadLabeledObservationsAsync(
            connection,
            observationSampleSize,
            commandTimeoutSeconds,
            cancellationToken);
        if (observations.Count == 0)
            return new BlockingCandidatePriorEstimate(0, 0, 0, 0, 0, 0m, null);

        await CreatePriorKeysTableAsync(connection, commandTimeoutSeconds, cancellationToken);
        var keyRows = BuildPriorKeyRows(observations, canonicalPasses);
        if (keyRows.Rows.Count == 0)
            return new BlockingCandidatePriorEstimate(observations.Count, 0, 0, 0, 0, 0m, null);

        using (var bulk = new SqlBulkCopy(connection))
        {
            bulk.DestinationTableName = "#candidate_prior_keys";
            bulk.BatchSize = 10_000;
            bulk.BulkCopyTimeout = commandTimeoutSeconds;
            await bulk.WriteToServerAsync(keyRows, cancellationToken);
        }

        var projection = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        await using var command = new SqlCommand(
            """
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
                (SELECT COUNT_BIG(*) FROM #candidate_prior_observations) AS observation_sample_size,
                (SELECT COUNT_BIG(DISTINCT observation_id) FROM candidate_union) AS observations_with_candidates,
                (SELECT COUNT_BIG(*)
                   FROM candidate_union c
                   JOIN #candidate_prior_observations o ON o.observation_id=c.observation_id
                  WHERE c.pessoa_uuid=o.truth_uuid) AS truth_candidate_pairs,
                (SELECT COUNT_BIG(*) FROM candidate_union) AS total_candidate_pairs;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@normalizacao", SqlDbType.NVarChar, 80).Value = normalizationVersion;
        command.Parameters.Add("@projection_schema", SqlDbType.NVarChar, 120).Value = projection.SchemaVersion;
        command.Parameters.Add("@projection_fingerprint", SqlDbType.Char, 64).Value = projection.Fingerprint;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Estimador de prior não retornou agregados.");

        var sampleSize = reader.GetInt64(0);
        var withCandidates = reader.GetInt64(1);
        var truthPairs = reader.GetInt64(2);
        var totalPairs = reader.GetInt64(3);
        var falsePairs = totalPairs - truthPairs;
        if (falsePairs < 0)
            throw new InvalidOperationException("Estimador de prior produziu contagem negativa de pares não-match.");

        var recall = sampleSize == 0 ? 0m : decimal.Divide(truthPairs, sampleSize);
        var prior = totalPairs == 0 ? null : decimal.Divide(truthPairs, totalPairs);
        return new BlockingCandidatePriorEstimate(
            sampleSize,
            withCandidates,
            truthPairs,
            falsePairs,
            totalPairs,
            recall,
            prior);
    }

    private static async Task<List<LabeledObservation>> LoadLabeledObservationsAsync(
        SqlConnection connection,
        int sampleSize,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('tempdb..#candidate_prior_observations') IS NOT NULL
                DROP TABLE #candidate_prior_observations;

            CREATE TABLE #candidate_prior_observations(
                observation_id bigint NOT NULL PRIMARY KEY,
                truth_uuid uniqueidentifier NOT NULL,
                nome nvarchar(500) NOT NULL,
                nascimento date NOT NULL,
                nome_mae nvarchar(500) NULL
            );

            ;WITH latest AS (
                SELECT
                    po.pessoa_observacao_id,
                    vf.pessoa_uuid,
                    po.gestor_id,
                    po.nome_completo,
                    po.data_nascimento,
                    po.nome_mae,
                    ROW_NUMBER() OVER (
                        PARTITION BY vf.pessoa_uuid,po.gestor_id
                        ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC) AS rn
                FROM identidade.vinculo_fonte vf
                JOIN silver.pessoa_observacao po
                  ON po.pessoa_observacao_id=vf.pessoa_observacao_id
                WHERE vf.ativo=1
                  AND vf.status=N'RESOLVIDO'
                  AND vf.metodo_resolucao=N'CPF_DETERMINISTICO'
                  AND po.cpf IS NOT NULL
                  AND po.codigo_pessoa_origem NOT LIKE N'SCALE-VAL-%'
            )
            INSERT #candidate_prior_observations(observation_id,truth_uuid,nome,nascimento,nome_mae)
            SELECT TOP (@sample_size)
                   pessoa_observacao_id,pessoa_uuid,nome_completo,data_nascimento,nome_mae
            FROM latest
            WHERE rn=1
            ORDER BY HASHBYTES('SHA2_256',CONCAT(CONVERT(nvarchar(36),pessoa_uuid),N':',CONVERT(nvarchar(20),gestor_id))),
                     pessoa_uuid,gestor_id,pessoa_observacao_id;

            SELECT observation_id,truth_uuid,nome,nascimento,nome_mae
            FROM #candidate_prior_observations
            ORDER BY observation_id;

            SELECT a.pessoa_observacao_id,a.atributo_codigo,a.valor
            FROM silver.pessoa_atributo_observacao a
            JOIN #candidate_prior_observations o
              ON o.observation_id=a.pessoa_observacao_id
            ORDER BY a.pessoa_observacao_id,a.atributo_codigo,a.atributo_instancia_chave,a.pessoa_atributo_observacao_id;
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("@sample_size", SqlDbType.Int).Value = sampleSize;

        var result = new List<LabeledObservation>();
        var byId = new Dictionary<long, LabeledObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var observation = new LabeledObservation(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                []);
            result.Add(observation);
            byId.Add(observation.ObservationId, observation);
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var observationId = reader.GetInt64(0);
                if (!byId.TryGetValue(observationId, out var observation))
                    continue;
                observation.DynamicValues.Add(new ResolutionSourceValue(
                    reader.GetString(1),
                    reader.GetString(2)));
            }
        }

        return result;
    }

    private static async Task CreatePriorKeysTableAsync(
        SqlConnection connection,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            IF OBJECT_ID('tempdb..#candidate_prior_keys') IS NOT NULL
                DROP TABLE #candidate_prior_keys;
            CREATE TABLE #candidate_prior_keys(
                observation_id bigint NOT NULL,
                pass_id nvarchar(160) NOT NULL,
                feature nvarchar(80) NOT NULL,
                valor nvarchar(500) NOT NULL,
                current_only bit NOT NULL
            );
            CREATE INDEX IX_candidate_prior_keys
                ON #candidate_prior_keys(feature,valor,observation_id,pass_id);
            """,
            connection)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DataTable BuildPriorKeyRows(
        IReadOnlyCollection<LabeledObservation> observations,
        IReadOnlyCollection<LinkageBlockingPass> passes)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        table.Columns.Add("observation_id", typeof(long));
        table.Columns.Add("pass_id", typeof(string));
        table.Columns.Add("feature", typeof(string));
        table.Columns.Add("valor", typeof(string));
        table.Columns.Add("current_only", typeof(bool));

        var plan = BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan;
        foreach (var observation in observations)
        {
            var sourceValues = new List<ResolutionSourceValue>
            {
                new(PersonResolutionAttributeCatalog.FullName, observation.Name),
                new(PersonResolutionAttributeCatalog.BirthDate, observation.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            };
            if (!string.IsNullOrWhiteSpace(observation.MotherName))
                sourceValues.Add(new ResolutionSourceValue(PersonResolutionAttributeCatalog.MotherName, observation.MotherName));
            sourceValues.AddRange(observation.DynamicValues);

            var valuesByFeature = ResolutionProjectionExecutor.Project(plan, sourceValues)
                .GroupBy(static key => key.Feature, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Select(static key => key.Value).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            foreach (var pass in passes)
            {
                if (pass.Fields.Any(field =>
                        !valuesByFeature.TryGetValue(field, out var values) || values.Length == 0))
                    continue;

                foreach (var feature in pass.Fields)
                {
                    var currentOnly = BlockingFeatureTemporalCatalog.Get(feature)
                        == BlockingFeatureTemporalSemantics.StableIdentityDatum;
                    foreach (var value in valuesByFeature[feature])
                        table.Rows.Add(observation.ObservationId, pass.PassId, feature, value, currentOnly);
                }
            }
        }

        return table;
    }

    private sealed record LabeledObservation(
        long ObservationId,
        Guid TruthUuid,
        string Name,
        DateOnly BirthDate,
        string? MotherName,
        List<ResolutionSourceValue> DynamicValues);
}

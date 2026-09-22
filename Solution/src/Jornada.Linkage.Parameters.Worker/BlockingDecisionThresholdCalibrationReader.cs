using System.Data;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

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
                    x.LogOdds,
                    x.DemographicExactCollisionRisk))
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

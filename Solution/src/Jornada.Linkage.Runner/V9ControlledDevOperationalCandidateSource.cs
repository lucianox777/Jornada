using System.Collections.ObjectModel;
using System.Data;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// A read-only DEV candidate source, NOT a second scorer. Executes the original
/// production BlockingProjectionCandidateQueryBuilder against real Gold UUIDs.
/// No synthetic TEST UUID, direct signature-join fallback, model refit or Gold
/// mutation is possible here. The DEV scorer still has to reconcile complete-link
/// groups with real identities before publication is permitted.
/// </summary>
public static class V9ControlledDevOperationalCandidateSource
{
    public sealed record GoldCandidate(Guid PessoaUuid, string NomeCompleto,
        DateOnly DataNascimento, string NomeMae);

    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<GoldCandidate>>> ReadAsync(
        SqlConnection connection,
        V9OperationalPinnedCohortRuntime pinnedWinner,
        Guid attemptId,
        Guid runId,
        IReadOnlyList<V9FrozenRunObservation> frozenRows,
        int maximumCandidatesPerObservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(pinnedWinner);
        ArgumentNullException.ThrowIfNull(frozenRows);
        if (attemptId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_ATTEMPT_MISSING");
        if (runId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_RUN_MISSING");
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidatesPerObservation, 1);

        // Source IDs are globally ordered immutable run_item IDs, never
        // page-local indexes. The entire SQL run_item membership is checked
        // below before any operational candidate/Gold query.
        if (frozenRows.Any(x => x is null || x.PessoaObservacaoId <= 0
                || x.Observation is null || x.Observation.Cpf is not null)
            || !frozenRows.Select(x => x.PessoaObservacaoId)
                .SequenceEqual(frozenRows.Select(x => x.PessoaObservacaoId).OrderBy(x => x))
            || frozenRows.Select(x => x.PessoaObservacaoId).Distinct().Count() != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID");

        // This is the same physical, read-only gate as controlled DEV RunAsync,
        // before executing even the first candidate query.
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            connection, attemptId, cancellationToken);

        // Reconcile EXACT SQL membership, not a page-local or caller-truncated
        // sample. A single missing/extra row is fatal before candidate scoring.
        await using (var items = new SqlCommand("""
            SELECT pessoa_observacao_id
              FROM identidade.linkage_run_item
             WHERE linkage_run_id=@run_id
             ORDER BY pessoa_observacao_id;
            """, connection))
        {
            items.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            var index = 0;
            await using var ids = await items.ExecuteReaderAsync(cancellationToken);
            while (await ids.ReadAsync(cancellationToken))
            {
                if (ids.IsDBNull(0) || index >= frozenRows.Count
                    || ids.GetInt64(0) != frozenRows[index].PessoaObservacaoId)
                    throw new InvalidDataException("RUNNER_V9_DEV_RUN_UNIVERSE_MISMATCH");
                index++;
            }
            if (index != frozenRows.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_RUN_UNIVERSE_MISMATCH");
        }

        var winner = pinnedWinner.FrozenWinner;
        if (pinnedWinner.ModelId !=
                V9OperationalSqlModelConverter.ModelIdFromWinnerSha(pinnedWinner.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_WINNER_MODEL_MISMATCH");
        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), pinnedWinner.WinnerSha256);

        var result = new Dictionary<long, IReadOnlyList<GoldCandidate>>(frozenRows.Count);
        foreach (var row in frozenRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 120;
            var candidateSql = planner.BuildCandidateUuidQuery(command,
                row.Observation, planner.ProjectionSchemaVersion,
                planner.ProjectionFingerprintSha256);
            command.CommandText = """
                SELECT c.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae,
                       g.estado_identidade
                  FROM (
                """ + candidateSql + """
                ) AS c LEFT JOIN gold.pessoa AS g
                  ON g.pessoa_uuid=c.pessoa_uuid
                 ORDER BY c.pessoa_uuid;
                """;

            var candidates = new List<GoldCandidate>();
            var seen = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2)
                    || reader.IsDBNull(3) || reader.IsDBNull(4))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_MISSING");
                var id = reader.GetGuid(0);
                if (id == Guid.Empty || !seen.Add(id))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_CANDIDATE_DUPLICATE");
                if (reader.GetString(4) != "REFERENCIA")
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_STALE");
                if (candidates.Count >= maximumCandidatesPerObservation)
                    throw new InvalidDataException("RUNNER_V9_DEV_CANDIDATE_GRAPH_TRUNCATED");
                var name = reader.GetString(1);
                var mother = reader.GetString(3);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(mother))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_INVALID");
                candidates.Add(new GoldCandidate(id, name,
                    DateOnly.FromDateTime(reader.GetDateTime(2)), mother));
            }
            if (!result.TryAdd(row.PessoaObservacaoId,
                    Array.AsReadOnly(candidates.ToArray())))
                throw new InvalidDataException("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID");
        }
        return new ReadOnlyDictionary<long, IReadOnlyList<GoldCandidate>>(result);
    }
}

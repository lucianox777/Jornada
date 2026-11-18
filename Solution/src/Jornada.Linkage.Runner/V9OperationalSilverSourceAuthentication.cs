using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only, fail-closed verification of the supplied Silver observation
/// attributes, manager code and active resolved Gold link in one SNAPSHOT.
/// Only core IdentityObservation fields are authenticated; additional dynamic
/// attributes are rejected until their physical provenance is implemented.
/// </summary>
public static class V9OperationalSilverSourceAuthentication
{
    public static async Task VerifyAsync(
        SqlConnection connection, SqlTransaction snapshotTransaction,
        IReadOnlyCollection<V9OperationalCohortGraphPreview.Source> frozenCohort,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshotTransaction);
        ArgumentNullException.ThrowIfNull(frozenCohort);
        ct.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open
            || !ReferenceEquals(snapshotTransaction.Connection, connection)
            || snapshotTransaction.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_OPERATIONAL_SNAPSHOT_REQUIRED");
        if (frozenCohort.Count == 0)
            throw new InvalidDataException("RUNNER_V9_COHORT_EMPTY");

        var seenIds = new HashSet<long>();
        const string sql = """
            SELECT o.cpf, o.cpf_ausente_motivo, o.nome_completo,
                   o.data_nascimento, o.nome_mae, gestor.codigo
            FROM silver.pessoa_observacao o
            INNER JOIN ref.gestor gestor ON gestor.gestor_id = o.gestor_id
            INNER JOIN identidade.vinculo_fonte v
                ON v.pessoa_observacao_id = o.pessoa_observacao_id
               AND v.pessoa_uuid = @gold_uuid
               AND v.ativo = 1 AND v.status = N'RESOLVIDO'
            INNER JOIN gold.pessoa g ON g.pessoa_uuid = v.pessoa_uuid
                AND g.estado_identidade = N'REFERENCIA'
            WHERE o.pessoa_observacao_id = @observation_id;
            """;
        foreach (var source in frozenCohort.OrderBy(s => s.ObservationId))
        {
            ct.ThrowIfCancellationRequested();
            if (source is null || source.Observation is null
                || source.ObservationId <= 0 || source.GoldUuid == Guid.Empty
                || string.IsNullOrWhiteSpace(source.Manager)
                || !seenIds.Add(source.ObservationId)
                || !string.IsNullOrWhiteSpace(source.Observation.Cpf)
                || source.Observation.ResolutionAttributes is { Count: > 0 })
                throw new InvalidDataException("RUNNER_V9_SOURCE_AUTH_INVALID_INPUT");

            await using var cmd = new SqlCommand(sql, connection, snapshotTransaction) {
                CommandTimeout = 0
            };
            cmd.Parameters.Add("@observation_id", SqlDbType.BigInt).Value = source.ObservationId;
            cmd.Parameters.Add("@gold_uuid", SqlDbType.UniqueIdentifier).Value = source.GoldUuid;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidDataException("RUNNER_V9_SOURCE_AUTH_LINK_MISSING");
            // A source must have exactly one matching active resolved link.
            // Multiple matching links are ambiguous, not silently selected.
            var cpfIsNull = reader.IsDBNull(0);
            var reason = reader.IsDBNull(1) ? null : reader.GetString(1);
            var name = reader.IsDBNull(2) ? null : reader.GetString(2);
            var birth = reader.IsDBNull(3) ? (DateOnly?)null
                : DateOnly.FromDateTime(reader.GetDateTime(3));
            var mother = reader.IsDBNull(4) ? null : reader.GetString(4);
            var manager = reader.GetString(5);
            if (!cpfIsNull
                || !string.Equals(reason, source.Observation.CpfAusenteMotivo, StringComparison.Ordinal)
                || !string.Equals(name, source.Observation.NomeCompleto, StringComparison.Ordinal)
                || birth != source.Observation.DataNascimento
                || !string.Equals(mother, source.Observation.NomeMae, StringComparison.Ordinal)
                || !string.Equals(manager, source.Manager, StringComparison.Ordinal))
                throw new InvalidDataException("RUNNER_V9_SOURCE_AUTH_ATTRIBUTE_MISMATCH");
            if (await reader.ReadAsync(ct))
                throw new InvalidDataException("RUNNER_V9_SOURCE_AUTH_AMBIGUOUS_LINK");
        }
    }
}

using System.Data;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

/// <summary>
/// Governance boundary of the Processor V9 integration rehearsal.
/// The normal Processor still calls RefreshGoldPersonAsync with no permit
/// and ALWAYS executes legacy V2. The sole permit constructor is this
/// internal disposable-only entry, accessible to friend integration tests.
/// </summary>
internal sealed partial class SqlProcessorRepository
{
    private sealed record V9ProcessorRehearsalPermit(
        int MaximumPrefixLength, string ProjectionFingerprintSha256);

    /// <summary>
    /// Execute the actual Processor Gold recomposition path with an immutable
    /// V9 projection, within the caller's exclusive rollback-only SQL test
    /// transaction. This is NOT a production activation nor a publication API.
    /// Requires two physically resolved Silver observations with distinct
    /// historical names and mothers; their source links stay untouched.
    /// </summary>
    internal static async Task<int> RehearseV9GoldHistoryRefreshAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid pessoaUuid, int maximumPrefixLength,
        string expectedProjectionFingerprintSha256, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open || transaction.Connection != connection)
            throw new InvalidOperationException(
                "Reensaio V9 exige conexão e transação SQL do mesmo banco.");
        if (pessoaUuid == Guid.Empty)
            throw new ArgumentException("UUID diagnóstico requerido.", nameof(pessoaUuid));

        // Rejection BEFORE calling sp_recompor_gold_pessoa is intentional:
        // an invalid projection claim must not modify even disposable Gold.
        V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(maximumPrefixLength);
        var fingerprint = V9CalibrationBlockingProjection.ComputeSchemaFingerprint(
            maximumPrefixLength);
        if (string.IsNullOrWhiteSpace(expectedProjectionFingerprintSha256)
            || !string.Equals(fingerprint, expectedProjectionFingerprintSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Gate Processor V9: fingerprint da projeção não pertence ao plano fixado.");

        await using (var gate = new SqlCommand("SELECT DB_NAME();", connection, transaction))
            if (!string.Equals((string?)await gate.ExecuteScalarAsync(ct),
                    "JornadaHarness", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Gate Processor V9 proibido fora de JornadaHarness descartável.");

        // Do not accept fabricated caller-side alias lists. History must be
        // read from the same published/deterministic current-source VIEW the
        // real Processor uses. Probabilistic results from an unpublished run
        // do not qualify, irrespective of their score.
        await using (var provenance = new SqlCommand("""
            SELECT COUNT_BIG(*) AS linked,
                   COUNT(DISTINCT po.nome_cmp) AS names,
                   COUNT(DISTINCT po.nome_mae_cmp) AS mothers,
                   COUNT(DISTINCT CONVERT(char(10),po.data_nascimento,23)) AS births
              FROM identidade.v_vinculo_corrente vc
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=vc.pessoa_observacao_id
             WHERE vc.pessoa_uuid=@id AND vc.status=N'RESOLVIDO'
               AND vc.linkage_run_id IS NULL;
            """, connection, transaction))
        {
            provenance.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = pessoaUuid;
            await using var reader = await provenance.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)
                || reader.GetInt64(0) != 2 || reader.GetInt32(1) != 2
                || reader.GetInt32(2) != 2 || reader.GetInt32(3) != 1)
                throw new InvalidDataException(
                    "Gate Processor V9 exige dois vínculos físicos correntes e distintos sem run probabilístico publicado.");
        }

        // Same private method used by RefreshGoldRepresentationsAsync in
        // the real Processor path: recomposes Gold, then V9 only by permit.
        // This call NEVER commits the transaction or touches Serving.
        return await RefreshGoldPersonAsync(connection, transaction,
            pessoaUuid, ct, new V9ProcessorRehearsalPermit(
                maximumPrefixLength, fingerprint));
    }
}

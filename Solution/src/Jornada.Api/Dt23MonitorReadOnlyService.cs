using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

/// <summary>
/// DT23 read-only Monitor projection. Persisted metadata is NOT proof that the
/// independent labels, post-blocking universe and sampling groups were audited.
/// Never promote a database declaration of VERDE to a certified Monitor badge.
/// </summary>
internal static class Dt23MonitorReadOnlyService
{
    internal sealed record Provenance(string SourceKind, bool Verified);
    internal sealed record Preview(
        string PolicyVersion,
        DateTimeOffset? CutoffUtc,
        string State,
        IReadOnlyList<string> Reasons,
        Provenance Provenance);

    internal static Preview Unverified(string version, DateTimeOffset? cutoffUtc) =>
        new(version, cutoffUtc, "INDETERMINADO",
            ["Metadados DT23 observados; sem coletor de verdade independente auditável. Diagnóstico não autoriza calibração."],
            new Provenance("UNVERIFIED", false));

    /// <summary>
    /// Read the newest declared policy and its newest cutoff, never the evidence
    /// or personally identifying counters. Absence/SQL failure is unknown, not green.
    /// Uses a separate bounded query to avoid blocking Monitor heartbeat/readiness.
    /// </summary>
    internal static async Task<Preview?> GetAsync(IOperationalSqlAdapter sql, CancellationToken ct)
    {
        try
        {
            // Bound both connection acquisition and the SQL command; the optional
            // diagnostic must not hold the Monitor request indefinitely.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var connection = await sql.OpenAsync(timeout.Token);
            await using var command = new SqlCommand("""
                SELECT TOP (1) p.versao, d.data_corte
                FROM ref.dt23_politica_suficiencia AS p
                OUTER APPLY (
                    SELECT TOP (1) s.data_corte
                    FROM ref.dt23_diagnostico_suficiencia AS s
                    WHERE s.politica_id = p.politica_id
                    ORDER BY s.data_corte DESC, s.registrado_em DESC, s.diagnostico_id DESC
                ) AS d
                ORDER BY p.criada_em DESC, p.politica_id DESC;
                """, connection) { CommandTimeout = 3 };

            await using var reader = await command.ExecuteReaderAsync(timeout.Token);
            if (!await reader.ReadAsync(timeout.Token)) return null;

            var cutoff = reader.IsDBNull(1) ? (DateTimeOffset?)null :
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
            return Unverified(reader.GetString(0), cutoff);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The internal timeout is not a caller cancellation.
            return null;
        }
        catch (SqlException) when (!ct.IsCancellationRequested)
        {
            // DT23 schema is optional while the migration is pending; a failed
            // diagnostic must not take down the existing operational Monitor.
            return null;
        }
        catch (TimeoutException) when (!ct.IsCancellationRequested)
        {
            // A timed-out optional diagnostic is unavailable, never certified.
            return null;
        }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested)
        {
            // Connection/reader state failures must not interrupt core monitoring.
            return null;
        }
    }
}

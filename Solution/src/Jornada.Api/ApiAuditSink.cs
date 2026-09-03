using System.Data;
using Jornada.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal interface IApiAuditSink
{
    Task PersistAsync(HttpContext http, Guid correlationId, long elapsedMs, CancellationToken ct);
}

/// <summary>
/// Persistência SQL da trilha de auditoria. O middleware depende da abstração IApiAuditSink para
/// permitir teste de pipeline totalmente em memória sem abrir conexão com banco.
/// </summary>
internal sealed class SqlApiAuditSink(SqlConnectionFactory connections) : IApiAuditSink
{
    public async Task PersistAsync(HttpContext http, Guid correlationId, long elapsedMs, CancellationToken ct)
    {
        var context = http.Items.TryGetValue(ApiContextItems.AccessContext, out var value) ? value as AccessContext : null;
        var resourceCode = http.Items.TryGetValue(ApiContextItems.ResourceCode, out var rp) ? rp as string : null;
        var personIds = http.Items.TryGetValue(ApiContextItems.PersonIds, out var pp) && pp is IReadOnlyCollection<Guid> ids
            ? ids.Distinct().Take(1000).ToArray()
            : Array.Empty<Guid>();
        var singlePerson = personIds.Length == 1 ? personIds[0] : (Guid?)null;
        var agentHash = http.Items.TryGetValue(ApiContextItems.AgentCpfHash, out var ah) ? ah as byte[] : null;
        var agentHashVersion = http.Items.TryGetValue(ApiContextItems.AgentCpfHashVersion, out var av) && av is short ver ? ver : (short?)null;

        var endpoint = http.GetEndpoint() as RouteEndpoint;
        var route = endpoint?.RoutePattern.RawText ?? http.Request.Path.Value ?? "/";
        if (route.Length > 300) route = route[..300];
        var method = http.Request.Method.Length > 10 ? http.Request.Method[..10] : http.Request.Method;
        var duration = (int)Math.Min(int.MaxValue, Math.Max(0, elapsedMs));

        await using var connection = await connections.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT controle.api_evento(
                    credencial_id,gestor_id,correlation_id,rota,metodo,status_http,duracao_ms,bytes_recebidos,
                    pessoa_uuid,recurso_codigo,agente_cpf_hash,agente_hash_versao,ocorrido_em)
                OUTPUT INSERTED.api_evento_id
                SELECT @credencial_id,g.gestor_id,@correlation_id,@rota,@metodo,@status_http,@duracao_ms,@bytes,
                       @pessoa_uuid,@recurso_codigo,@agente_cpf_hash,@agente_hash_versao,SYSDATETIMEOFFSET()
                FROM (VALUES(1)) x(n)
                LEFT JOIN ref.gestor g ON g.codigo=@gestor;
                """;
            command.Parameters.Add(new SqlParameter("@credencial_id", SqlDbType.UniqueIdentifier) { Value = (object?)context?.CredentialId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@gestor", SqlDbType.NVarChar, 30) { Value = (object?)context?.GestorCodigo ?? DBNull.Value });
            command.Parameters.AddWithValue("@correlation_id", correlationId);
            command.Parameters.Add(new SqlParameter("@rota", SqlDbType.NVarChar, 300) { Value = route });
            command.Parameters.Add(new SqlParameter("@metodo", SqlDbType.NVarChar, 10) { Value = method });
            command.Parameters.AddWithValue("@status_http", http.Response.StatusCode);
            command.Parameters.AddWithValue("@duracao_ms", duration);
            command.Parameters.Add(new SqlParameter("@bytes", SqlDbType.BigInt) { Value = (object?)http.Request.ContentLength ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@pessoa_uuid", SqlDbType.UniqueIdentifier) { Value = (object?)singlePerson ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@recurso_codigo", SqlDbType.NVarChar, 80) { Value = (object?)resourceCode ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@agente_cpf_hash", SqlDbType.Binary, 32) { Value = (object?)agentHash ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@agente_hash_versao", SqlDbType.SmallInt) { Value = (object?)agentHashVersion ?? DBNull.Value });
            var eventId = Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);

            if (personIds.Length > 0)
            {
                var idsJson = System.Text.Json.JsonSerializer.Serialize(personIds);
                await using var targets = connection.CreateCommand();
                targets.Transaction = tx;
                targets.CommandText = """
                    INSERT controle.api_evento_pessoa(api_evento_id,pessoa_uuid)
                    SELECT @evento,pessoa_uuid
                    FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$');
                    """;
                targets.Parameters.AddWithValue("@evento", eventId);
                targets.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = idsJson });
                await targets.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

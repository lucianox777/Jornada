using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed record JornadaUuidLookupResult(
    Guid RequestedUuid,
    Guid CanonicalUuid,
    bool Redirected);

internal static class JornadaUuidLookup
{
    public static async Task<JornadaUuidLookupResult?> ResolveAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid requestedUuid,
        CancellationToken ct)
    {
        if (requestedUuid == Guid.Empty)
            return null;

        // UUID_JORNADA é retroalimentação interna. A resolução é somente leitura:
        // nunca cria identidade, nunca cria âncora e nunca escolhe sucessor arbitrário.
        // Uma referência canônica corrente é válida por si mesma. Um initial_uuid só
        // redireciona quando o estado autoritativo é REFERENCIA e existe canonical_uuid.
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT TOP(1) resolved_uuid, redirected
            FROM (
                SELECT p.canonical_uuid AS resolved_uuid, CAST(0 AS bit) AS redirected, 0 AS ord
                FROM identidade.pessoa_origem_progressiva p
                WHERE p.canonical_uuid=@uuid AND p.estado='REFERENCIA'

                UNION ALL

                SELECT p.canonical_uuid AS resolved_uuid,
                       CAST(CASE WHEN p.canonical_uuid=@uuid THEN 0 ELSE 1 END AS bit) AS redirected,
                       1 AS ord
                FROM identidade.pessoa_origem_progressiva p
                WHERE p.initial_uuid=@uuid
                  AND p.estado='REFERENCIA'
                  AND p.canonical_uuid IS NOT NULL
            ) q
            WHERE q.resolved_uuid IS NOT NULL
            ORDER BY ord;
            """;
        command.Parameters.Add(new SqlParameter("@uuid", SqlDbType.UniqueIdentifier) { Value = requestedUuid });

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var canonical = reader.GetGuid(0);
        var redirected = reader.GetBoolean(1);
        if (canonical == Guid.Empty)
            throw new InvalidDataException("Estado progressivo retornou UUID canônico vazio.");

        if (await reader.ReadAsync(ct))
            throw new InvalidDataException("UUID Jornada possui resolução autoritativa ambígua.");

        return new JornadaUuidLookupResult(requestedUuid, canonical, redirected);
    }
}

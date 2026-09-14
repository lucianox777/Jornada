using System.Data;
using Microsoft.Data.SqlClient;

namespace Jornada.Processor.Worker;

internal sealed record JornadaUuidLookupResult(
    Guid RequestedUuid,
    Guid CanonicalUuid,
    bool Redirected);

internal static class JornadaUuidLookup
{
    private const int MaxRedirectDepth = 32;

    public static async Task<JornadaUuidLookupResult?> ResolveAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid requestedUuid,
        CancellationToken ct)
    {
        if (requestedUuid == Guid.Empty)
            return null;

        // UUID_JORNADA referencia identidade.pessoa, não pessoa_origem_progressiva.
        // Isso é essencial para observações v4 sem codigoPessoaOrigem: elas podem receber uma
        // identidade por CPF sem jamais possuir origem progressiva. A consulta é somente leitura,
        // nunca cria identidade/âncora e só segue o sucessor explícito de uma fusão.
        var current = requestedUuid;
        var visited = new HashSet<Guid>();
        var redirected = false;

        for (var depth = 0; depth < MaxRedirectDepth; depth++)
        {
            if (!visited.Add(current))
                throw new InvalidDataException("Ciclo detectado na cadeia de redirecionamento de UUID Jornada.");

            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                SELECT status,pessoa_uuid_sucessor
                FROM identidade.pessoa
                WHERE pessoa_uuid=@uuid;
                """;
            command.Parameters.Add(new SqlParameter("@uuid", SqlDbType.UniqueIdentifier) { Value = current });

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                if (current == requestedUuid)
                    return null;
                throw new InvalidDataException("Cadeia de fusão de UUID Jornada aponta para identidade inexistente.");
            }

            var status = reader.GetString(0);
            var successor = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
            if (await reader.ReadAsync(ct))
                throw new InvalidDataException("UUID Jornada duplicado na autoridade de identidade.");

            if (string.Equals(status, "ATIVO", StringComparison.Ordinal))
                return new JornadaUuidLookupResult(requestedUuid, current, redirected);

            if (string.Equals(status, "FUNDIDO", StringComparison.Ordinal) && successor.HasValue)
            {
                if (successor.Value == Guid.Empty)
                    throw new InvalidDataException("Fusão de UUID Jornada aponta para UUID vazio.");
                current = successor.Value;
                redirected = true;
                continue;
            }

            // SEPARADO e INATIVO não possuem sucessor automaticamente escolhível. FUNDIDO sem
            // sucessor também é estado inválido para retroalimentação. Falha fechada.
            return null;
        }

        throw new InvalidDataException("Cadeia de redirecionamento de UUID Jornada excede o limite operacional.");
    }
}

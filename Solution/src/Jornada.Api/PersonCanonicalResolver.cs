using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Api;

internal interface IPersonCanonicalResolver
{
    Task<Guid?> ResolveAsync(Guid pessoaUuid, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, Guid>> ResolveManyAsync(IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct);
}

internal sealed class SqlPersonCanonicalResolver(SqlConnectionFactory connections) : IPersonCanonicalResolver
{
    public async Task<Guid?> ResolveAsync(Guid pessoaUuid, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT identidade.fn_pessoa_uuid_canonico(@uuid);";
        command.Parameters.AddWithValue("@uuid", pessoaUuid);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid uuid ? uuid : null;
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> ResolveManyAsync(IReadOnlyCollection<Guid> pessoaUuids, CancellationToken ct)
    {
        if (pessoaUuids.Count == 0) return new Dictionary<Guid, Guid>();

        var unique = pessoaUuids.Distinct().ToArray();
        var idsJson = JsonSerializer.Serialize(unique);
        var result = new Dictionary<Guid, Guid>(unique.Length);

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT j.pessoa_uuid, identidade.fn_pessoa_uuid_canonico(j.pessoa_uuid) AS pessoa_uuid_canonico
            FROM OPENJSON(@ids) WITH (pessoa_uuid UNIQUEIDENTIFIER '$') j;
            """;
        command.Parameters.Add(new SqlParameter("@ids", SqlDbType.NVarChar, -1) { Value = idsJson });

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(1)) result[reader.GetGuid(0)] = reader.GetGuid(1);
        }
        return result;
    }
}

using System.Data;
using System.Data.Common;

namespace Jornada.Linkage.Parameters.Worker;

public sealed record IbgeNominalUReferenceInfo(
    long Id,
    string Code,
    string Source,
    string ContentSha256);

/// <summary>
/// Leitura canônica da referência nominal IBGE já internalizada no SQL Server.
/// Para nome da pessoa usa prenomes TODOS; para nome da mãe o chamador pode usar
/// prenomes FEMININO. Sobrenomes permanecem na distribuição nacional TODOS.
/// </summary>
public static class IbgeNominalUReferenceReader
{
    public static async Task<IbgeNominalUReferenceInfo> ReadActiveReferenceAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT frequencia_nome_versao_id,codigo,fonte,conteudo_sha256
            FROM ref.frequencia_nome_versao
            WHERE status='ATIVA'
            ORDER BY frequencia_nome_versao_id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Nenhuma referência IBGE de frequências está ATIVA.");

        var info = new IbgeNominalUReferenceInfo(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            Convert.ToHexString((byte[])reader.GetValue(3)).ToLowerInvariant());

        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Mais de uma referência de frequências está ATIVA; calibração nominal recusada fail-closed.");

        return info;
    }

    public static async Task<IReadOnlyList<IbgeTypedNameFrequencyEntry>> ReadBrazilPublishedMarginalsAsync(
        DbConnection connection,
        long referenceId,
        string firstNameSex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (firstNameSex is not ("TODOS" or "MASCULINO" or "FEMININO"))
            throw new ArgumentOutOfRangeException(nameof(firstNameSex), "Sexo nominal deve ser TODOS, MASCULINO ou FEMININO.");

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT tipo,valor_normalizado,SUM(CONVERT(bigint,frequencia)) AS ocorrencias
            FROM ref.frequencia_nome
            WHERE frequencia_nome_versao_id=@id
              AND escopo_geografico='BRASIL'
              AND periodo_nascimento='TODOS'
              AND uf_codigo='00'
              AND municipio_codigo='0000000'
              AND (
                    (tipo='NOME' AND sexo=@first_name_sex)
                 OR (tipo='SOBRENOME' AND sexo='TODOS')
              )
            GROUP BY tipo,valor_normalizado
            ORDER BY tipo,valor_normalizado;
            """;

        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.DbType = DbType.Int64;
        id.Value = referenceId;
        command.Parameters.Add(id);

        var sex = command.CreateParameter();
        sex.ParameterName = "@first_name_sex";
        sex.DbType = DbType.String;
        sex.Value = firstNameSex;
        command.Parameters.Add(sex);

        var result = new List<IbgeTypedNameFrequencyEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = reader.GetString(0) switch
            {
                "NOME" => IbgeNameStatisticKind.FirstName,
                "SOBRENOME" => IbgeNameStatisticKind.Surname,
                var unexpected => throw new InvalidOperationException($"Tipo IBGE inesperado no recorte nacional: {unexpected}.")
            };
            var occurrences = reader.GetInt64(2);
            if (occurrences > 0)
                result.Add(new IbgeTypedNameFrequencyEntry(kind, reader.GetString(1), occurrences));
        }

        if (!result.Any(entry => entry.StatisticKind == IbgeNameStatisticKind.FirstName) ||
            !result.Any(entry => entry.StatisticKind == IbgeNameStatisticKind.Surname))
            throw new InvalidOperationException(
                $"Recorte IBGE nacional publicado não contém simultaneamente NOME ({firstNameSex}) e SOBRENOME (TODOS).");

        return result;
    }
}

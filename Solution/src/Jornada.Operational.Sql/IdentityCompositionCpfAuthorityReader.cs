using System.Data;
using System.Data.Common;

namespace Jornada.Operational.Sql;

/// <summary>
/// Autoridade CPF por origem para composição. Uma origem só recebe CpfAnchorUuid quando a sua
/// atribuição factual corrente está explicitamente resolvida por CPF_DETERMINISTICO e coincide com
/// identidade.cpf_ancora. A mera presença de CPF na origem não torna a origem titular da âncora:
/// isso preserva a possibilidade de corrigir uma observação com CPF atribuído incorretamente sem
/// transferir a relação permanente CPF→UUID.
/// </summary>
public sealed class IdentityCompositionCpfAuthorityReader : IIdentityCompositionCpfAuthorityReader
{
    private readonly bool postgres;

    public IdentityCompositionCpfAuthorityReader(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<IReadOnlyDictionary<Guid, Guid?>> LoadAnchorUuidsAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyCollection<IdentityCompositionOriginSnapshot> origins,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(origins);
        if (origins.Count == 0) throw new InvalidOperationException("Componente de composição vazio.");

        var result = new Dictionary<Guid, Guid?>();
        foreach (var origin in origins.OrderBy(x => x.InitialUuid))
        {
            if (origin.SourceId <= 0 || origin.InitialUuid == Guid.Empty || !result.TryAdd(origin.InitialUuid, null))
                throw new InvalidOperationException("Origem inválida ou duplicada na leitura de autoridade CPF.");

            var observation = await ReadLatestObservationAsync(
                connection, transaction, origin.SourceId, cancellationToken);
            if (observation is null || observation.Value.Cpf is null) continue;

            var anchor = await ReadAnchorAsync(
                connection, transaction, observation.Value.Cpf, cancellationToken);
            var link = await ReadCurrentLinkAsync(
                connection, transaction, observation.Value.ObservationId, cancellationToken);

            if (link is null || !string.Equals(link.Value.Method, "CPF_DETERMINISTICO", StringComparison.Ordinal))
                continue;
            if (link.Value.Status != "RESOLVIDO" || link.Value.PersonUuid is null || link.Value.PersonUuid == Guid.Empty)
                throw new InvalidOperationException("Atribuição CPF determinística corrente está incompleta.");
            if (anchor is null)
                throw new InvalidOperationException("Atribuição CPF determinística não possui âncora permanente.");
            if (anchor.Value != link.Value.PersonUuid.Value)
                throw new InvalidOperationException("Atribuição CPF determinística diverge da âncora permanente.");

            result[origin.InitialUuid] = anchor.Value;
        }
        return result;
    }

    private async Task<(long ObservationId, string? Cpf)?> ReadLatestObservationAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sourceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_observacao_id,cpf FROM silver.pessoa_observacao WHERE pessoa_origem_id=@source ORDER BY versao_interna DESC,pessoa_observacao_id DESC LIMIT 1 FOR UPDATE;"
            : "SELECT TOP(1) pessoa_observacao_id,cpf FROM silver.pessoa_observacao WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_origem_id=@source ORDER BY versao_interna DESC,pessoa_observacao_id DESC;";
        Add(command, "@source", DbType.Int64, sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var id = reader.GetInt64(0);
        var cpf = reader.IsDBNull(1) ? null : reader.GetString(1);
        if (id <= 0 || (cpf is not null && cpf.Length != 11))
            throw new InvalidOperationException("Observação corrente inválida para autoridade CPF.");
        return (id, cpf);
    }

    private async Task<Guid?> ReadAnchorAsync(
        DbConnection connection,
        DbTransaction transaction,
        string cpf,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf FOR UPDATE;"
            : "SELECT pessoa_uuid FROM identidade.cpf_ancora WITH(UPDLOCK,HOLDLOCK) WHERE cpf=@cpf;";
        Add(command, "@cpf", DbType.AnsiStringFixedLength, cpf);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var value = reader.GetGuid(0);
        if (value == Guid.Empty) throw new InvalidOperationException("Âncora CPF possui UUID inválido.");
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("CPF possui mais de uma âncora permanente.");
        return value;
    }

    private async Task<(Guid? PersonUuid, string Method, string Status)?> ReadCurrentLinkAsync(
        DbConnection connection,
        DbTransaction transaction,
        long observationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_uuid,metodo_resolucao,status FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@observation AND ativo ORDER BY vinculo_fonte_id FOR UPDATE;"
            : "SELECT pessoa_uuid,metodo_resolucao,status FROM identidade.vinculo_fonte WITH(UPDLOCK,HOLDLOCK) WHERE pessoa_observacao_id=@observation AND ativo=1 ORDER BY vinculo_id;";
        Add(command, "@observation", DbType.Int64, observationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var value = (
            reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Observação possui mais de um vínculo factual corrente.");
        return value;
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Autoridade CPF exige transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

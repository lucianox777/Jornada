using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>Leitura transacional de históricos efetivados. Não reconstrói estado temporal nem publica projeções.</summary>
public sealed class IdentityCompositionAppliedHistoryStore
{
    private readonly bool postgres;

    public IdentityCompositionAppliedHistoryStore(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<ImmutableArray<IdentityCompositionHistory>> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        IReadOnlyCollection<Guid> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(references);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Histórico aplicado exige transação ativa.");

        var ids = references.Order().ToArray();
        if (ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Referências históricas inválidas ou duplicadas.");
        if (ids.Length == 0)
            return ImmutableArray<IdentityCompositionHistory>.Empty;

        var rows = new List<IdentityCompositionAppliedHistoryRow>();
        foreach (var reference in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = postgres
                ? "SELECT h.decision_id,h.reference_uuid,h.members_json,h.members_hash,h.registrado_em,a.decision_id FROM identidade.composicao_historico_aplicado h LEFT JOIN identidade.composicao_aplicacao a ON a.decision_id=h.decision_id AND a.estado='APLICADA' WHERE h.reference_uuid=@reference ORDER BY h.decision_id FOR UPDATE OF h;"
                : "SELECT h.decision_id,h.reference_uuid,h.members_json,h.members_hash,h.registrado_em,a.decision_id FROM identidade.composicao_historico_aplicado h WITH(UPDLOCK,HOLDLOCK) LEFT JOIN identidade.composicao_aplicacao a WITH(HOLDLOCK) ON a.decision_id=h.decision_id AND a.estado='APLICADA' WHERE h.reference_uuid=@reference ORDER BY h.decision_id;";
            Add(command, "@reference", DbType.Guid, reference);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new IdentityCompositionAppliedHistoryRow(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    ReadUtcTimestamp(reader, 4),
                    !reader.IsDBNull(5)));
            }
        }

        return IdentityCompositionAppliedHistory.ValidateBatch(rows);
    }

    private static DateTimeOffset ReadUtcTimestamp(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt when dt.Kind == DateTimeKind.Utc => new DateTimeOffset(dt),
            DateTime dt when dt.Kind == DateTimeKind.Unspecified => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            DateTime dt => new DateTimeOffset(dt.ToUniversalTime()),
            _ => throw new InvalidOperationException("Timestamp de histórico aplicado possui tipo inesperado.")
        };
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

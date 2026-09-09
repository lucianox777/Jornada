using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>Leitura de históricos efetivados. Não reconstrói estado temporal nem publica projeções.</summary>
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
        DbConnection connection, DbTransaction transaction, IReadOnlyCollection<Guid> references,
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
        if (ids.Length == 0) return ImmutableArray<IdentityCompositionHistory>.Empty;
        var rows = new List<IdentityCompositionAppliedHistoryRow>();
        foreach (var reference in ids)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = postgres
                ? "SELECT h.decision_id,h.reference_uuid,h.members_json,h.members_hash,h.registrado_em,a.decision_id FROM identidade.composicao_historico_aplicado h LEFT JOIN identidade.composicao_aplicacao a ON a.decision_id=h.decision_id WHERE h.reference_uuid=@reference ORDER BY h.decision_id FOR UPDATE OF h;"
                : "SELECT h.decision_id,h.reference_uuid,h.members_json,h.members_hash,h.registrado_em,a.decision_id FROM identidade.composicao_historico_aplicado h WITH(UPDLOCK,HOLDLOCK) LEFT JOIN identidade.composicao_aplicacao a WITH(HOLDLOCK) ON a.decision_id=h.decision_id WHERE h.reference_uuid=@reference ORDER BY h.decision_id;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@reference";
            parameter.DbType = DbType.Guid;
            parameter.Value = reference;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new IdentityCompositionAppliedHistoryRow(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), !reader.IsDBNull(5)));
            }
        }
        return IdentityCompositionAppliedHistory.ValidateBatch(rows);
    }
}

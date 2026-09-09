using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public interface IIdentityCompositionProjectionScopeReader
{
    Task<ImmutableArray<IdentityCompositionProjectionScope>> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPlan composition,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Carrega o escopo factual autoritativo das origens alteradas pela composição.
/// A leitura é somente leitura de negócio, mas usa locks transacionais para impedir que
/// novas observações/fatos sejam anexados ao mesmo escopo durante o fechamento.
/// </summary>
public sealed class IdentityCompositionProjectionScopeReader : IIdentityCompositionProjectionScopeReader
{
    private readonly bool postgres;

    public IdentityCompositionProjectionScopeReader(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<ImmutableArray<IdentityCompositionProjectionScope>> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPlan composition,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(composition);
        if (composition.DecisionId == Guid.Empty || string.IsNullOrWhiteSpace(composition.RequestHash) ||
            composition.Changes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Plano de composição aplicado ausente ou incompleto.");

        var initialUuids = composition.Changes.Select(x => x.InitialUuid).Order().ToArray();
        if (initialUuids.Any(x => x == Guid.Empty) || initialUuids.Distinct().Count() != initialUuids.Length)
            throw new InvalidOperationException("Plano de composição contém origem vazia ou duplicada.");

        var scopes = ImmutableArray.CreateBuilder<IdentityCompositionProjectionScope>(initialUuids.Length);
        foreach (var initialUuid in initialUuids)
        {
            var sourceId = await LoadAndLockSourceAsync(connection, transaction, initialUuid, cancellationToken)
                ?? throw new InvalidOperationException("Origem alterada não existe no estado progressivo autoritativo.");

            // Primeiro trava todas as observações da origem. Isso fecha também a criação de novos
            // registros filhos enquanto os writers respeitam as FKs existentes.
            var observationIds = await LoadAndLockPersonObservationsAsync(
                connection, transaction, sourceId, cancellationToken);

            var recordIds = await LoadAndLockRecordsAsync(
                connection, transaction, sourceId, cancellationToken);

            if (recordIds.Count != recordIds.Distinct().Count())
                throw new InvalidOperationException("Escopo factual autoritativo contém registro duplicado.");

            // Se há fatos, todos devem apontar para observações pertencentes à mesma origem.
            // A própria consulta faz o join; a checagem abaixo protege regressões de schema/consulta.
            if (recordIds.Count > 0 && observationIds.Count == 0)
                throw new InvalidOperationException("Escopo factual contém registros sem observação de pessoa da origem.");

            scopes.Add(new IdentityCompositionProjectionScope(
                initialUuid,
                sourceId,
                recordIds.Order().ToImmutableArray(),
                true));
        }

        var result = scopes.ToImmutable();
        // Reutiliza o planejador puro como gate de fechamento exato antes de qualquer publicação.
        _ = IdentityCompositionRecompositionPlanner.Prepare(composition, result);
        return result;
    }

    private async Task<long?> LoadAndLockSourceAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid initialUuid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? """
              SELECT pop.pessoa_origem_id
              FROM identidade.pessoa_origem_progressiva pop
              JOIN silver.pessoa_origem po ON po.pessoa_origem_id=pop.pessoa_origem_id
              WHERE pop.initial_uuid=@uuid
              FOR UPDATE OF pop,po;
              """
            : """
              SELECT pop.pessoa_origem_id
              FROM identidade.pessoa_origem_progressiva pop WITH(UPDLOCK,HOLDLOCK)
              JOIN silver.pessoa_origem po WITH(UPDLOCK,HOLDLOCK) ON po.pessoa_origem_id=pop.pessoa_origem_id
              WHERE pop.initial_uuid=@uuid;
              """;
        Add(command, "@uuid", DbType.Guid, initialUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var sourceId = reader.GetInt64(0);
        if (sourceId <= 0)
            throw new InvalidOperationException("Origem factual autoritativa inválida.");
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("UUID inicial está associado a mais de uma origem factual.");
        return sourceId;
    }

    private async Task<IReadOnlyList<long>> LoadAndLockPersonObservationsAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sourceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? """
              SELECT pessoa_observacao_id
              FROM silver.pessoa_observacao
              WHERE pessoa_origem_id=@source
              ORDER BY pessoa_observacao_id
              FOR UPDATE;
              """
            : """
              SELECT pessoa_observacao_id
              FROM silver.pessoa_observacao WITH(UPDLOCK,HOLDLOCK)
              WHERE pessoa_origem_id=@source
              ORDER BY pessoa_observacao_id;
              """;
        Add(command, "@source", DbType.Int64, sourceId);
        var result = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            if (id <= 0 || (result.Count > 0 && id <= result[^1]))
                throw new InvalidOperationException("Observações de pessoa inválidas ou não determinísticas.");
            result.Add(id);
        }
        return result;
    }

    private async Task<IReadOnlyList<long>> LoadAndLockRecordsAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sourceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? """
              SELECT ro.registro_observacao_id
              FROM silver.registro_observacao ro
              JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
              WHERE po.pessoa_origem_id=@source
              ORDER BY ro.registro_observacao_id
              FOR UPDATE OF ro,po;
              """
            : """
              SELECT ro.registro_observacao_id
              FROM silver.registro_observacao ro WITH(UPDLOCK,HOLDLOCK)
              JOIN silver.pessoa_observacao po WITH(UPDLOCK,HOLDLOCK) ON po.pessoa_observacao_id=ro.pessoa_observacao_id
              WHERE po.pessoa_origem_id=@source
              ORDER BY ro.registro_observacao_id;
              """;
        Add(command, "@source", DbType.Int64, sourceId);
        var result = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            if (id <= 0 || (result.Count > 0 && id <= result[^1]))
                throw new InvalidOperationException("Registros factuais inválidos ou não determinísticos.");
            result.Add(id);
        }
        return result;
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Fechamento factual exige transação ativa na conexão informada.");
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

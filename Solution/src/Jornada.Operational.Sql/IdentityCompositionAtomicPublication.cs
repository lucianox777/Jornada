using System.Data;
using System.Data.Common;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public sealed record IdentityCompositionPublicationReceipt(
    Guid DecisionId,
    string RecompositionPlanHash,
    string FactualRevalidationVersion,
    string PublicationVersion,
    string CommandHash,
    string PublishedBy,
    DateTimeOffset PublishedAt,
    int MutationCount,
    string State);

public sealed record IdentityCompositionPublicationResult(
    IdentityCompositionPublicationReceipt Receipt,
    bool Replay);

/// <summary>
/// Fronteira explícita de publicação factual da composição. A unidade de trabalho é fornecida pelo
/// chamador: releitura autoritativa, revalidação, mutação de Gold/Serving e recibo PUBLICADA usam a
/// mesma transação. A referência estrutural nunca é usada como destino factual.
/// </summary>
public sealed class IdentityCompositionAtomicPublication
{
    private readonly bool postgres;
    private readonly IdentityCompositionRecompositionPlanStore recompositionStore;
    private readonly IIdentityCompositionFactualAuthorityReader factualAuthority;

    public IdentityCompositionAtomicPublication(
        IOperationalDatabaseAdapter database,
        IIdentityCompositionFactualAuthorityReader? factualAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
        recompositionStore = new IdentityCompositionRecompositionPlanStore(database);
        this.factualAuthority = factualAuthority ?? new IdentityCompositionFactualAuthorityReader(database);
    }

    public async Task<IdentityCompositionPublicationResult> PublishAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionRecompositionPlan recomposition,
        string publishedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(recomposition);
        if (string.IsNullOrWhiteSpace(publishedBy) || publishedBy.Length > 200)
            throw new ArgumentException("Publicador inválido.", nameof(publishedBy));
        if (publishedAt == default || publishedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante de publicação deve estar em UTC.", nameof(publishedAt));

        var persistedPlan = await recompositionStore.ReadAsync(
            connection, transaction, recomposition.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Publicação exige plano de recomposição persistido.");
        IdentityCompositionRecompositionPlanStore.ValidateContent(persistedPlan, recomposition);

        var snapshots = await factualAuthority.LoadAsync(connection, transaction, recomposition, cancellationToken);
        var factualPlan = IdentityCompositionFactualRevalidationPlanner.Prepare(
            recomposition, persistedPlan.PlanHash, snapshots);
        var command = IdentityCompositionPublicationPlanner.Prepare(factualPlan);
        var commandHash = HashCommand(command);

        var existing = await ReadAsync(connection, transaction, recomposition.DecisionId, cancellationToken);
        if (existing is not null)
        {
            ValidateReplay(existing, command, commandHash);
            await VerifyProjectionsAsync(connection, transaction, command, cancellationToken);
            return new IdentityCompositionPublicationResult(existing, Replay: true);
        }

        foreach (var mutation in command.Mutations)
            await ApplyMutationAsync(connection, transaction, mutation, publishedAt, cancellationToken);

        await VerifyProjectionsAsync(connection, transaction, command, cancellationToken);
        await InsertReceiptAsync(
            connection, transaction, command, commandHash, publishedBy, publishedAt, cancellationToken);

        var receipt = await ReadAsync(connection, transaction, recomposition.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Recibo PUBLICADA não ficou visível na transação.");
        ValidateReplay(receipt, command, commandHash);
        return new IdentityCompositionPublicationResult(receipt, Replay: false);
    }

    public async Task<IdentityCompositionPublicationReceipt?> ReadAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        if (decisionId == Guid.Empty) throw new ArgumentException("Decisão inválida.", nameof(decisionId));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT decision_id,recomposition_plan_hash,factual_revalidation_version,publication_version,command_hash,published_by,published_at,mutation_count,state FROM identidade.composicao_publicacao WHERE decision_id=@decision FOR UPDATE;"
            : "SELECT decision_id,recomposition_plan_hash,factual_revalidation_version,publication_version,command_hash,published_by,published_at,mutation_count,state FROM identidade.composicao_publicacao WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision;";
        Add(command, "@decision", DbType.Guid, decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var receipt = new IdentityCompositionPublicationReceipt(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetInt32(7), reader.GetString(8));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Mais de um recibo PUBLICADA para a mesma decisão.");
        ValidateReceipt(receipt);
        return receipt;
    }

    private async Task ApplyMutationAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPublicationMutation mutation,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        var totalGold = 0;
        totalGold += await UpdateProjectionAsync(connection, transaction, "gold.beneficio_concedido", mutation, publishedAt, cancellationToken);
        totalGold += await UpdateProjectionAsync(connection, transaction, "gold.servico_prestado", mutation, publishedAt, cancellationToken);
        if (totalGold != 1)
            throw new InvalidOperationException("Registro afetado deve possuir exatamente uma projeção Gold materializada.");

        var serving = await UpdateProjectionAsync(connection, transaction, "serving.registro_integrado", mutation, publishedAt, cancellationToken);
        if (serving != 1)
            throw new InvalidOperationException("Registro afetado deve possuir exatamente uma projeção Serving materializada.");
    }

    private async Task<int> UpdateProjectionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IdentityCompositionPublicationMutation mutation,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET pessoa_uuid=@uuid,estado_atribuicao_identidade=@state,atualizado_em=@at WHERE registro_observacao_id=@record;";
        Add(command, "@uuid", DbType.Guid, mutation.PessoaUuid);
        Add(command, "@state", DbType.String, mutation.AssignmentState);
        Add(command, "@at", DbType.DateTimeOffset, publishedAt);
        Add(command, "@record", DbType.Int64, mutation.RegistroObservacaoId);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected is < 0 or > 1)
            throw new InvalidOperationException("Projeção factual não é unívoca por registro de observação.");
        return affected;
    }

    private async Task VerifyProjectionsAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPublicationCommand publication,
        CancellationToken cancellationToken)
    {
        foreach (var mutation in publication.Mutations)
        {
            var gold = await CountMatchingAsync(connection, transaction, "gold.beneficio_concedido", mutation, cancellationToken)
                     + await CountMatchingAsync(connection, transaction, "gold.servico_prestado", mutation, cancellationToken);
            var serving = await CountMatchingAsync(connection, transaction, "serving.registro_integrado", mutation, cancellationToken);
            if (gold != 1 || serving != 1)
                throw new InvalidOperationException("Projeções factuais não correspondem ao comando revalidado.");
        }
    }

    private static async Task<int> CountMatchingAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IdentityCompositionPublicationMutation mutation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE registro_observacao_id=@record AND estado_atribuicao_identidade=@state AND ((@uuid IS NULL AND pessoa_uuid IS NULL) OR pessoa_uuid=@uuid);";
        Add(command, "@record", DbType.Int64, mutation.RegistroObservacaoId);
        Add(command, "@state", DbType.String, mutation.AssignmentState);
        Add(command, "@uuid", DbType.Guid, mutation.PessoaUuid);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task InsertReceiptAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPublicationCommand publication,
        string commandHash,
        string publishedBy,
        DateTimeOffset publishedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "INSERT INTO identidade.composicao_publicacao(decision_id,recomposition_plan_hash,factual_revalidation_version,publication_version,command_hash,published_by,published_at,mutation_count,state) VALUES(@decision,@plan_hash,@factual_version,@publication_version,@command_hash,@by,@at,@count,'PUBLICADA');"
            : "INSERT identidade.composicao_publicacao(decision_id,recomposition_plan_hash,factual_revalidation_version,publication_version,command_hash,published_by,published_at,mutation_count,state) VALUES(@decision,@plan_hash,@factual_version,@publication_version,@command_hash,@by,@at,@count,'PUBLICADA');";
        Add(command, "@decision", DbType.Guid, publication.DecisionId);
        Add(command, "@plan_hash", DbType.String, publication.RecompositionPlanHash);
        Add(command, "@factual_version", DbType.String, publication.FactualRevalidationVersion);
        Add(command, "@publication_version", DbType.String, IdentityCompositionPublicationPlanner.Version);
        Add(command, "@command_hash", DbType.String, commandHash);
        Add(command, "@by", DbType.String, publishedBy);
        Add(command, "@at", DbType.DateTimeOffset, publishedAt);
        Add(command, "@count", DbType.Int32, publication.Mutations.Length);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Recibo PUBLICADA não foi persistido exatamente uma vez.");
    }

    private static string HashCommand(IdentityCompositionPublicationCommand command)
    {
        var json = JsonSerializer.Serialize(new
        {
            command.DecisionId,
            command.RecompositionPlanHash,
            command.FactualRevalidationVersion,
            PublicationVersion = IdentityCompositionPublicationPlanner.Version,
            Mutations = command.Mutations.OrderBy(x => x.RegistroObservacaoId).ToArray()
        });
        return IdentityCompositionCanonical.HashUtf8(json);
    }

    private static void ValidateReplay(
        IdentityCompositionPublicationReceipt receipt,
        IdentityCompositionPublicationCommand command,
        string commandHash)
    {
        ValidateReceipt(receipt);
        if (receipt.DecisionId != command.DecisionId ||
            !string.Equals(receipt.RecompositionPlanHash, command.RecompositionPlanHash, StringComparison.Ordinal) ||
            !string.Equals(receipt.FactualRevalidationVersion, command.FactualRevalidationVersion, StringComparison.Ordinal) ||
            !string.Equals(receipt.PublicationVersion, IdentityCompositionPublicationPlanner.Version, StringComparison.Ordinal) ||
            !string.Equals(receipt.CommandHash, commandHash, StringComparison.Ordinal) ||
            receipt.MutationCount != command.Mutations.Length)
            throw new InvalidOperationException("Replay de publicação diverge do recibo PUBLICADA.");
    }

    private static void ValidateReceipt(IdentityCompositionPublicationReceipt receipt)
    {
        if (receipt.DecisionId == Guid.Empty || !IsHash(receipt.RecompositionPlanHash) || !IsHash(receipt.CommandHash) ||
            string.IsNullOrWhiteSpace(receipt.FactualRevalidationVersion) ||
            string.IsNullOrWhiteSpace(receipt.PublicationVersion) ||
            string.IsNullOrWhiteSpace(receipt.PublishedBy) || receipt.PublishedBy.Length > 200 ||
            receipt.PublishedAt == default || receipt.PublishedAt.Offset != TimeSpan.Zero || receipt.MutationCount < 0 ||
            !string.Equals(receipt.State, "PUBLICADA", StringComparison.Ordinal))
            throw new InvalidOperationException("Recibo PUBLICADA inválido.");
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Publicação exige transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

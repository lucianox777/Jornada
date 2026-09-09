using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public sealed record IdentityCompositionRecompositionPlanReceipt(
    Guid DecisionId,
    string CompositionRequestHash,
    string RecompositionVersion,
    string PlanJson,
    string PlanHash,
    DateTimeOffset RegisteredAt,
    string State);

public sealed record IdentityCompositionRecompositionPlanRegistration(
    IdentityCompositionRecompositionPlanReceipt Receipt,
    bool Replay);

/// <summary>
/// Persiste, de forma append-only e idempotente, o plano determinístico de recomposição para uma
/// composição já APLICADA. PLANEJADA não significa PUBLICADA e não autoriza qualquer escrita em
/// Gold, Serving, vinculo_fonte ou identity_map.
/// </summary>
public sealed class IdentityCompositionRecompositionPlanStore
{
    private readonly bool postgres;
    private readonly IdentityCompositionApplicationStore application;

    public IdentityCompositionRecompositionPlanStore(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
        application = new IdentityCompositionApplicationStore(database);
    }

    public async Task<IdentityCompositionRecompositionPlanReceipt?> ReadAsync(
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
            ? "SELECT decision_id,composition_request_hash,recomposition_version,plan_json,plan_hash,registrado_em,estado FROM identidade.composicao_recomposicao_plano WHERE decision_id=@decision FOR UPDATE;"
            : "SELECT decision_id,composition_request_hash,recomposition_version,plan_json,plan_hash,registrado_em,estado FROM identidade.composicao_recomposicao_plano WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision;";
        Add(command, "@decision", DbType.Guid, decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var receipt = new IdentityCompositionRecompositionPlanReceipt(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.GetString(6));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Mais de um plano de recomposição para a mesma decisão.");
        ValidateReceipt(receipt);
        return receipt;
    }

    public async Task<IdentityCompositionRecompositionPlanRegistration> RegisterAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionRecompositionPlan plan,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(plan);
        if (registeredAt == default || registeredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante de registro deve estar em UTC.", nameof(registeredAt));

        var planJson = IdentityCompositionCanonical.SerializeRecompositionPlan(plan);
        var planHash = IdentityCompositionCanonical.HashUtf8(planJson);
        var applied = await application.ReadAppliedAsync(connection, transaction, plan.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Plano de recomposição exige composição APLICADA.");
        if (!string.Equals(applied.RequestHash, plan.CompositionRequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Plano de recomposição não pertence ao recibo APLICADA.");

        var existing = await ReadAsync(connection, transaction, plan.DecisionId, cancellationToken);
        if (existing is not null)
        {
            ValidateContent(existing, plan, planJson, planHash);
            return new IdentityCompositionRecompositionPlanRegistration(existing, Replay: true);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "INSERT INTO identidade.composicao_recomposicao_plano(decision_id,composition_request_hash,recomposition_version,plan_json,plan_hash,registrado_em,estado) VALUES(@decision,@request_hash,@version,@json,@hash,@at,'PLANEJADA');"
            : "INSERT identidade.composicao_recomposicao_plano(decision_id,composition_request_hash,recomposition_version,plan_json,plan_hash,registrado_em,estado) VALUES(@decision,@request_hash,@version,@json,@hash,@at,'PLANEJADA');";
        Add(command, "@decision", DbType.Guid, plan.DecisionId);
        Add(command, "@request_hash", DbType.String, plan.CompositionRequestHash);
        Add(command, "@version", DbType.String, IdentityCompositionRecompositionPlanner.Version);
        Add(command, "@json", DbType.String, planJson);
        Add(command, "@hash", DbType.String, planHash);
        Add(command, "@at", DbType.DateTimeOffset, registeredAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Plano de recomposição não foi persistido exatamente uma vez.");

        var receipt = await ReadAsync(connection, transaction, plan.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Plano de recomposição não ficou visível na transação.");
        ValidateContent(receipt, plan, planJson, planHash);
        return new IdentityCompositionRecompositionPlanRegistration(receipt, Replay: false);
    }

    public static void ValidateContent(
        IdentityCompositionRecompositionPlanReceipt receipt,
        IdentityCompositionRecompositionPlan plan,
        string? canonicalJson = null,
        string? canonicalHash = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateReceipt(receipt);
        canonicalJson ??= IdentityCompositionCanonical.SerializeRecompositionPlan(plan);
        canonicalHash ??= IdentityCompositionCanonical.HashUtf8(canonicalJson);
        if (receipt.DecisionId != plan.DecisionId ||
            !string.Equals(receipt.CompositionRequestHash, plan.CompositionRequestHash, StringComparison.Ordinal) ||
            !string.Equals(receipt.RecompositionVersion, IdentityCompositionRecompositionPlanner.Version, StringComparison.Ordinal) ||
            !string.Equals(receipt.PlanJson, canonicalJson, StringComparison.Ordinal) ||
            !string.Equals(receipt.PlanHash, canonicalHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Plano de recomposição persistido diverge do conteúdo canônico.");
    }

    private static void ValidateReceipt(IdentityCompositionRecompositionPlanReceipt receipt)
    {
        if (receipt.DecisionId == Guid.Empty || !IsHash(receipt.CompositionRequestHash) || !IsHash(receipt.PlanHash) ||
            string.IsNullOrWhiteSpace(receipt.RecompositionVersion) || receipt.RecompositionVersion.Length > 80 ||
            string.IsNullOrWhiteSpace(receipt.PlanJson) || receipt.RegisteredAt == default ||
            receipt.RegisteredAt.Offset != TimeSpan.Zero || !string.Equals(receipt.State, "PLANEJADA", StringComparison.Ordinal))
            throw new InvalidOperationException("Recibo de plano de recomposição inválido.");
        if (!string.Equals(IdentityCompositionCanonical.HashUtf8(receipt.PlanJson), receipt.PlanHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Hash do plano de recomposição persistido é inválido.");
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Persistência de recomposição exige transação ativa na conexão informada.");
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

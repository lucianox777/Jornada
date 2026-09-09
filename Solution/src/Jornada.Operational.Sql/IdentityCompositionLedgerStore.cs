using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>
/// Recibo imutável de preparação. Não representa autorização nem aplicação da composição.
/// </summary>
public sealed record IdentityCompositionPreparedReceipt(
    Guid DecisionId,
    string RequestHash,
    string PlanHash,
    string ReservationsHash,
    string RequestJson,
    string PlanJson,
    string ReservationsJson,
    string RequesterReference,
    Guid? CorrelationId,
    string State);

/// <summary>
/// Fronteira tipada para o ledger de composição. Todas as operações desta classe exigem uma
/// transação externa ativa para poderem participar, futuramente, da mesma unidade de consistência
/// da leitura autoritativa e da publicação. Nenhum método aplica composição, altera âncora CPF,
/// vínculo factual, estado progressivo ou Gold/Serving.
/// </summary>
public sealed class IdentityCompositionLedgerStore
{
    private readonly bool postgres;

    public IdentityCompositionLedgerStore(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<Guid> ReserveAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid decisionId,
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        if (decisionId == Guid.Empty) throw new ArgumentException("Decisão inválida.", nameof(decisionId));
        if (reservationId == Guid.Empty) throw new ArgumentException("Reserva inválida.", nameof(reservationId));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT identidade.reservar_uuid_composicao(@decision,@reservation);"
            : "DECLARE @uuid uniqueidentifier; EXEC identidade.sp_reservar_uuid_composicao @decision_id=@decision,@reserva_id=@reservation,@uuid_resultado=@uuid OUTPUT; SELECT @uuid;";
        Add(command, "@decision", DbType.Guid, decisionId);
        Add(command, "@reservation", DbType.Guid, reservationId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid uuid && uuid != Guid.Empty
            ? uuid
            : throw new InvalidOperationException("Ledger não devolveu UUID reservado válido.");
    }

    public async Task<IdentityCompositionPreparedReceipt> RegisterPreparedAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionDecision decision,
        IdentityCompositionPlan plan,
        IEnumerable<Guid> reservedUuids,
        string requesterReference,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reservedUuids);
        if (decision.DecisionId == Guid.Empty || plan.DecisionId != decision.DecisionId)
            throw new InvalidOperationException("Plano e decisão não pertencem ao mesmo decision_id.");
        if (string.IsNullOrWhiteSpace(requesterReference))
            throw new ArgumentException("Referência opaca do solicitante é obrigatória.", nameof(requesterReference));
        if (requesterReference.Length > 120)
            throw new ArgumentOutOfRangeException(nameof(requesterReference), "Referência do solicitante excede 120 caracteres.");

        var requestJson = IdentityCompositionCanonical.SerializeDecision(decision);
        var expectedRequestHash = IdentityCompositionCanonical.HashUtf8(requestJson);
        if (!string.Equals(plan.RequestHash, expectedRequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Plano não corresponde à serialização canônica da decisão.");
        var planJson = IdentityCompositionCanonical.SerializePlan(plan);
        var reservationsJson = IdentityCompositionCanonical.SerializeReservations(reservedUuids);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT identidade.registrar_plano_composicao(@decision,@request,@plan,@reservations,@requester,@correlation);"
            : "DECLARE @hash char(64); EXEC identidade.sp_registrar_plano_composicao @decision_id=@decision,@request_json=@request,@plan_json=@plan,@reservas_json=@reservations,@solicitante_referencia=@requester,@correlation_id=@correlation,@request_hash=@hash OUTPUT; SELECT @hash;";
        Add(command, "@decision", DbType.Guid, decision.DecisionId);
        Add(command, "@request", DbType.String, requestJson);
        Add(command, "@plan", DbType.String, planJson);
        Add(command, "@reservations", DbType.String, reservationsJson);
        Add(command, "@requester", DbType.String, requesterReference);
        Add(command, "@correlation", DbType.Guid, correlationId);
        var returnedHash = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (!string.Equals(returnedHash, expectedRequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Ledger devolveu hash diferente do request canônico.");

        return await ReadPreparedAsync(connection, transaction, decision.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Registro preparado não ficou visível na transação.");
    }

    /// <summary>
    /// Lê e trava o recibo da decisão na transação do chamador. Ausência é retornada como null;
    /// erro, timeout ou truncamento continuam sendo exceções e nunca equivalem a ausência.
    /// </summary>
    public async Task<IdentityCompositionPreparedReceipt?> ReadPreparedAsync(
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
            ? "SELECT decision_id,request_hash,plan_hash,reservas_hash,request_json,plan_json,reservas_json,solicitante_referencia,correlation_id,estado FROM identidade.composicao_plano WHERE decision_id=@decision FOR UPDATE;"
            : "SELECT decision_id,request_hash,plan_hash,reservas_hash,request_json,plan_json,reservas_json,solicitante_referencia,correlation_id,estado FROM identidade.composicao_plano WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision;";
        Add(command, "@decision", DbType.Guid, decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var receipt = new IdentityCompositionPreparedReceipt(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.GetString(9));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Ledger retornou mais de um recibo para a mesma decisão.");
        ValidateReceipt(receipt);
        return receipt;
    }

    public async Task<IReadOnlyList<Guid>> ReadReservedUuidsAsync(
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
            ? "SELECT pessoa_uuid FROM identidade.composicao_uuid_reserva WHERE decision_id=@decision ORDER BY pessoa_uuid FOR UPDATE;"
            : "SELECT pessoa_uuid FROM identidade.composicao_uuid_reserva WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision ORDER BY pessoa_uuid;";
        Add(command, "@decision", DbType.Guid, decisionId);
        var values = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetGuid(0);
            if (value == Guid.Empty || values.Contains(value))
                throw new InvalidOperationException("Ledger contém reserva inválida ou duplicada.");
            values.Add(value);
        }
        return values;
    }

    public static void ValidatePreparedContent(
        IdentityCompositionPreparedReceipt receipt,
        IdentityCompositionDecision decision,
        IdentityCompositionPlan plan,
        IEnumerable<Guid> reservedUuids)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reservedUuids);
        ValidateReceipt(receipt);
        if (receipt.DecisionId != decision.DecisionId || plan.DecisionId != decision.DecisionId)
            throw new InvalidOperationException("decision_id divergente no recibo preparado.");

        var requestJson = IdentityCompositionCanonical.SerializeDecision(decision);
        var planJson = IdentityCompositionCanonical.SerializePlan(plan);
        var reservationsJson = IdentityCompositionCanonical.SerializeReservations(reservedUuids);
        if (!string.Equals(receipt.RequestJson, requestJson, StringComparison.Ordinal) ||
            !string.Equals(receipt.PlanJson, planJson, StringComparison.Ordinal) ||
            !string.Equals(receipt.ReservationsJson, reservationsJson, StringComparison.Ordinal) ||
            !string.Equals(receipt.RequestHash, IdentityCompositionCanonical.HashUtf8(requestJson), StringComparison.Ordinal) ||
            !string.Equals(receipt.PlanHash, IdentityCompositionCanonical.HashUtf8(planJson), StringComparison.Ordinal) ||
            !string.Equals(receipt.ReservationsHash, IdentityCompositionCanonical.HashUtf8(reservationsJson), StringComparison.Ordinal))
            throw new InvalidOperationException("Conteúdo recebido diverge do recibo PREPARADA persistido.");
    }

    private static void ValidateReceipt(IdentityCompositionPreparedReceipt receipt)
    {
        if (receipt.DecisionId == Guid.Empty ||
            !string.Equals(receipt.State, "PREPARADA", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(receipt.RequesterReference) ||
            !IsHash(receipt.RequestHash) || !IsHash(receipt.PlanHash) || !IsHash(receipt.ReservationsHash))
            throw new InvalidOperationException("Recibo de preparação inválido ou em estado não suportado.");
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Ledger exige transação ativa na conexão informada.");
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

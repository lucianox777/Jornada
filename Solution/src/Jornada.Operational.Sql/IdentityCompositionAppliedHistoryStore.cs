using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>Leitura de históricos efetivados. Não reconstrói estado temporal nem publica projeções.</summary>
public sealed class IdentityCompositionAppliedHistoryStore
{
    private readonly bool postgres;
    private readonly IdentityCompositionLedgerStore ledger;
    private readonly IdentityCompositionApplicationStore application;
    public IdentityCompositionAppliedHistoryStore(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        ledger = new IdentityCompositionLedgerStore(database);
        application = new IdentityCompositionApplicationStore(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<ImmutableArray<IdentityCompositionHistory>> LoadAsync(DbConnection connection,
        DbTransaction transaction, IReadOnlyCollection<Guid> references, CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(references);
        var ids = references.Order().ToArray();
        if (ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Referências históricas inválidas ou duplicadas.");
        if (ids.Length == 0) return ImmutableArray<IdentityCompositionHistory>.Empty;
        var decisions = new SortedSet<Guid>();
        foreach (var reference in ids)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = postgres
                ? "SELECT decision_id FROM identidade.composicao_historico_aplicado WHERE reference_uuid=@reference ORDER BY decision_id FOR UPDATE;"
                : "SELECT decision_id FROM identidade.composicao_historico_aplicado WITH(UPDLOCK,HOLDLOCK) WHERE reference_uuid=@reference ORDER BY decision_id;";
            Add(command, "@reference", DbType.Guid, reference);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) decisions.Add(reader.GetGuid(0));
        }
        var result = new List<IdentityCompositionHistory>();
        foreach (var decisionId in decisions)
        {
            var all = await LoadDecisionAsync(connection, transaction, decisionId, cancellationToken);
            result.AddRange(all.Where(h => ids.Contains(h.ReferenceUuid)));
        }
        return IdentityCompositionAppliedHistory.ValidateBatch(result.Select(h =>
        {
            var json = IdentityCompositionCanonical.SerializeHistoryMembers(h.MemberInitialUuids);
            return new IdentityCompositionAppliedHistoryRow(h.CompositionId, h.ReferenceUuid, json,
                IdentityCompositionCanonical.HashUtf8(json), DateTimeOffset.UnixEpoch, true);
        }));
    }

    /// <summary>Valida o conjunto inteiro da decisão, inclusive ausências e linhas extras.</summary>
    public async Task<ImmutableArray<IdentityCompositionHistory>> LoadDecisionAsync(DbConnection connection,
        DbTransaction transaction, Guid decisionId, CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        if (decisionId == Guid.Empty) throw new ArgumentException("Decisão inválida.", nameof(decisionId));
        var prepared = await ledger.ReadPreparedAsync(connection, transaction, decisionId, cancellationToken)
            ?? throw new InvalidOperationException("Histórico sem plano PREPARADA.");
        var receipt = await application.ReadAppliedAsync(connection, transaction, decisionId, cancellationToken)
            ?? throw new InvalidOperationException("Histórico sem recibo APLICADA.");
        IdentityCompositionDecision decision;
        IdentityCompositionPlan plan;
        try
        {
            decision = JsonSerializer.Deserialize<IdentityCompositionDecision>(prepared.RequestJson)
                ?? throw new InvalidOperationException("Decisão preparada vazia.");
            plan = JsonSerializer.Deserialize<IdentityCompositionPlan>(prepared.PlanJson)
                ?? throw new InvalidOperationException("Plano preparado vazio.");
        }
        catch (JsonException ex) { throw new InvalidOperationException("Payload preparado inválido.", ex); }
        var reserved = await ledger.ReadReservedUuidsAsync(connection, transaction, decisionId, cancellationToken);
        IdentityCompositionLedgerStore.ValidatePreparedContent(prepared, decision, plan, reserved);
        IdentityCompositionApplicationStore.ValidateAppliedContent(receipt, prepared, plan);
        var rows = new List<IdentityCompositionAppliedHistoryRow>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = postgres
                ? "SELECT decision_id,reference_uuid,members_json,members_hash,registrado_em FROM identidade.composicao_historico_aplicado WHERE decision_id=@decision ORDER BY reference_uuid FOR UPDATE;"
                : "SELECT decision_id,reference_uuid,members_json,members_hash,registrado_em FROM identidade.composicao_historico_aplicado WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision ORDER BY reference_uuid;";
            Add(command, "@decision", DbType.Guid, decisionId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new IdentityCompositionAppliedHistoryRow(reader.GetGuid(0), reader.GetGuid(1),
                    reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), true));
        }
        var actual = IdentityCompositionAppliedHistory.ValidateBatch(rows);
        if (actual.Length != receipt.RegisteredHistories || actual.Length != plan.HistoryToAppend.Length ||
            rows.Any(r => r.RegisteredAt != receipt.AppliedAt))
            throw new InvalidOperationException("Histórico aplicado diverge das contagens ou instante do recibo.");
        var expected = plan.HistoryToAppend.OrderBy(h => h.ReferenceUuid).ThenBy(h => h.CompositionId).ToArray();
        for (var i = 0; i < expected.Length; i++)
        {
            if (actual[i].CompositionId != expected[i].CompositionId ||
                actual[i].ReferenceUuid != expected[i].ReferenceUuid ||
                !actual[i].MemberInitialUuids.SequenceEqual(expected[i].MemberInitialUuids))
                throw new InvalidOperationException("Histórico aplicado diverge do plano PREPARADA canônico.");
        }
        return actual;
    }

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Histórico aplicado exige transação ativa.");
    }
    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.DbType = type;
        parameter.Value = value; command.Parameters.Add(parameter);
    }
}

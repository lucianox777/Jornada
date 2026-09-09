using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;

internal static class IdentityCompositionHistorySmoke
{
    public static async Task RunAsync(IOperationalDatabaseAdapter database, Guid reference, Guid member,
        Guid decisionId, IdentityCompositionPlan expectedPlan)
    {
        var store = new IdentityCompositionAppliedHistoryStore(database);
        var authoritative = new IdentityCompositionAuthoritativeReader(database,
            new IdentityCompositionCpfAuthorityReader(database));
        await using var connection = await database.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var history = await store.LoadDecisionAsync(connection, transaction, decisionId);
        Require(history.Length == expectedPlan.HistoryToAppend.Length, "Histórico aplicado incompleto.");
        var byReference = await store.LoadAsync(connection, transaction, new[] { reference });
        Require(byReference.Any(h => h.CompositionId == decisionId && h.ReferenceUuid == reference),
            "Leitura por referência não encontrou o histórico aplicado.");
        var version = await VersionAsync(connection, transaction, member);
        var probe = new IdentityCompositionDecision(Guid.NewGuid(), IdentityCompositionOperation.REASSOCIACAO,
            ImmutableArray.Create(new IdentityCompositionAssignment(member, version, reference, ProgressiveIdentityStatus.REFERENCIA)),
            "evidence:synthetic-history-read", "HISTORY_SMOKE_V1", DateTimeOffset.UtcNow);
        var closed = await authoritative.LoadClosedReadSetAsync(connection, transaction, probe, Array.Empty<Guid>());
        IdentityCompositionHistoryClosure.RequireComplete(closed.History, closed.Members.Select(m => m.InitialUuid));
        Require(closed.Members.Any(m => m.InitialUuid == reference) && closed.Members.Any(m => m.InitialUuid == member),
            "Fechamento não incluiu os membros históricos.");
        Require(closed.History.Any(h => h.CompositionId == decisionId && h.ReferenceUuid == reference),
            "Fechamento não incorporou o histórico efetivamente aplicado.");
        var resolved = IdentityHistoricalResolver.Resolve(byReference.Single(h => h.CompositionId == decisionId), closed.Members);
        Require(resolved.State == HistoricalReferenceState.UNIVOCA && resolved.CanonicalUuid == reference,
            "Fusão histórica não resolveu para o destino corrente esperado.");
        await transaction.RollbackAsync();
        Console.WriteLine("IDENTITY HISTORY CLOSURE: OK (applied receipt, canonical history, authoritative members; no publication)");
    }

    private static async Task<long> VersionAsync(DbConnection connection, DbTransaction transaction, Guid initial)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT versao FROM identidade.pessoa_origem_progressiva WHERE initial_uuid=@initial;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@initial"; parameter.DbType = DbType.Guid;
        parameter.Value = initial; command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

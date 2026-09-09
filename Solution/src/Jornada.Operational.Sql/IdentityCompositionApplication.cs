using System.Data;
using System.Data.Common;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

public sealed record IdentityCompositionAppliedReceipt(
    Guid DecisionId,
    string RequestHash,
    string PlanHash,
    string ReservationsHash,
    string ApplierReference,
    DateTimeOffset AppliedAt,
    int AppliedChanges,
    int RegisteredHistories,
    string State);

public sealed record IdentityCompositionApplicationResult(
    IdentityCompositionAppliedReceipt Receipt,
    bool Replay);

/// <summary>
/// Escrita transacional da aplicação progressiva de um plano de composição já validado.
/// Não abre nem confirma transação própria e não altera vínculo_fonte, identity_map, CPF, Gold ou Serving.
/// </summary>
public sealed class IdentityCompositionApplicationStore
{
    private readonly bool postgres;

    public IdentityCompositionApplicationStore(IOperationalDatabaseAdapter database)
    {
        ArgumentNullException.ThrowIfNull(database);
        postgres = database.Provider switch
        {
            OperationalDatabaseProviders.PostgreSql => true,
            OperationalDatabaseProviders.SqlServer => false,
            _ => throw new ArgumentException("Provider operacional não suportado.", nameof(database))
        };
    }

    public async Task<IdentityCompositionAppliedReceipt?> ReadAppliedAsync(
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
            ? "SELECT decision_id,request_hash,plan_hash,reservas_hash,aplicador_referencia,aplicado_em,alteracoes_aplicadas,historicos_registrados,estado FROM identidade.composicao_aplicacao WHERE decision_id=@decision FOR UPDATE;"
            : "SELECT decision_id,request_hash,plan_hash,reservas_hash,aplicador_referencia,aplicado_em,alteracoes_aplicadas,historicos_registrados,estado FROM identidade.composicao_aplicacao WITH(UPDLOCK,HOLDLOCK) WHERE decision_id=@decision;";
        Add(command, "@decision", DbType.Guid, decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var receipt = new IdentityCompositionAppliedReceipt(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8));
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Mais de um recibo APLICADA para a mesma decisão.");
        ValidateReceipt(receipt);
        return receipt;
    }

    public async Task<int> ApplyProgressiveChangesAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPreApplicationValidation validation,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(validation);
        if (appliedAt == default || appliedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante de aplicação deve estar em UTC.", nameof(appliedAt));
        var decision = validation.Decision;
        var plan = validation.Replanned;
        if (plan.DecisionId != decision.DecisionId || plan.Changes.IsDefaultOrEmpty)
            throw new InvalidOperationException("Plano revalidado não possui alterações aplicáveis.");
        ValidateAuditText(decision);
        var reserved = validation.AuthoritativeReadSet.ReservedNewUuids.ToHashSet();
        var count = 0;
        foreach (var change in plan.Changes.OrderBy(x => x.InitialUuid))
        {
            if (change.NewVersion != checked(change.ExpectedVersion + 1))
                throw new InvalidOperationException("Versão nova da composição é inconsistente.");
            var current = await LoadCurrentAsync(connection, transaction, change.InitialUuid, cancellationToken)
                ?? throw new InvalidOperationException("Origem da alteração não existe mais.");
            if (current.Version != change.ExpectedVersion || current.Status != change.BeforeStatus || current.CanonicalUuid != change.BeforeUuid)
                throw new InvalidOperationException("Estado progressivo mudou após a revalidação autoritativa.");
            if (change.AfterStatus == ProgressiveIdentityStatus.REFERENCIA && change.AfterUuid is null ||
                change.AfterStatus == ProgressiveIdentityStatus.INDEFINIDA && change.AfterUuid is not null ||
                change.AfterStatus == ProgressiveIdentityStatus.PROVISORIA)
                throw new InvalidOperationException("Estado final de composição inválido.");

            var outcome = change.AfterStatus == ProgressiveIdentityStatus.INDEFINIDA
                ? "INDEFINIDA"
                : change.AfterUuid is { } target && reserved.Contains(target)
                    ? "NOVA_IDENTIDADE"
                    : "ASSOCIACAO_EXISTENTE";
            var eventTarget = outcome == "ASSOCIACAO_EXISTENTE" ? change.AfterUuid : null;
            var universe = outcome == "NOVA_IDENTIDADE" ? $"COMPOSICAO:{decision.DecisionId:D}" : null;
            await InsertResolutionEventAsync(
                connection, transaction, current.SourceId, change, decision,
                outcome, eventTarget, universe, appliedAt, cancellationToken);
            await UpdateProgressiveAsync(
                connection, transaction, current.SourceId, change, appliedAt, cancellationToken);
            count++;
        }
        return count;
    }

    public async Task<int> AppendAppliedHistoryAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPlan plan,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(plan);
        if (appliedAt == default || appliedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante de aplicação deve estar em UTC.", nameof(appliedAt));
        var count = 0;
        foreach (var history in plan.HistoryToAppend.OrderBy(x => x.ReferenceUuid))
        {
            if (history.CompositionId != plan.DecisionId || history.ReferenceUuid == Guid.Empty)
                throw new InvalidOperationException("Histórico proposto não pertence ao plano aplicado.");
            var json = IdentityCompositionCanonical.SerializeHistoryMembers(history.MemberInitialUuids);
            var hash = IdentityCompositionCanonical.HashUtf8(json);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = postgres
                ? "INSERT INTO identidade.composicao_historico_aplicado(decision_id,reference_uuid,members_json,members_hash,registrado_em) VALUES(@decision,@reference,@members,@hash,@at);"
                : "INSERT identidade.composicao_historico_aplicado(decision_id,reference_uuid,members_json,members_hash,registrado_em) VALUES(@decision,@reference,@members,@hash,@at);";
            Add(command, "@decision", DbType.Guid, plan.DecisionId);
            Add(command, "@reference", DbType.Guid, history.ReferenceUuid);
            Add(command, "@members", DbType.String, json);
            Add(command, "@hash", DbType.String, hash);
            Add(command, "@at", DbType.DateTimeOffset, appliedAt);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Histórico aplicado não foi persistido exatamente uma vez.");
            count++;
        }
        return count;
    }

    public async Task<IdentityCompositionAppliedReceipt> RegisterAppliedAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionPreparedReceipt prepared,
        IdentityCompositionPlan plan,
        string applierReference,
        DateTimeOffset appliedAt,
        int appliedChanges,
        int registeredHistories,
        CancellationToken cancellationToken = default)
    {
        ValidateTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateApplier(applierReference);
        if (prepared.DecisionId != plan.DecisionId || appliedChanges != plan.Changes.Length ||
            registeredHistories != plan.HistoryToAppend.Length || appliedChanges <= 0)
            throw new InvalidOperationException("Contagens ou identidade da aplicação divergem do plano revalidado.");
        if (appliedAt == default || appliedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Instante de aplicação deve estar em UTC.", nameof(appliedAt));

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "INSERT INTO identidade.composicao_aplicacao(decision_id,request_hash,plan_hash,reservas_hash,aplicador_referencia,aplicado_em,alteracoes_aplicadas,historicos_registrados,estado) VALUES(@decision,@request_hash,@plan_hash,@reservations_hash,@applier,@at,@changes,@histories,'APLICADA');"
            : "INSERT identidade.composicao_aplicacao(decision_id,request_hash,plan_hash,reservas_hash,aplicador_referencia,aplicado_em,alteracoes_aplicadas,historicos_registrados,estado) VALUES(@decision,@request_hash,@plan_hash,@reservations_hash,@applier,@at,@changes,@histories,'APLICADA');";
        Add(command, "@decision", DbType.Guid, prepared.DecisionId);
        Add(command, "@request_hash", DbType.String, prepared.RequestHash);
        Add(command, "@plan_hash", DbType.String, prepared.PlanHash);
        Add(command, "@reservations_hash", DbType.String, prepared.ReservationsHash);
        Add(command, "@applier", DbType.String, applierReference.Trim());
        Add(command, "@at", DbType.DateTimeOffset, appliedAt);
        Add(command, "@changes", DbType.Int32, appliedChanges);
        Add(command, "@histories", DbType.Int32, registeredHistories);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Recibo APLICADA não foi persistido exatamente uma vez.");
        var receipt = await ReadAppliedAsync(connection, transaction, prepared.DecisionId, cancellationToken)
            ?? throw new InvalidOperationException("Recibo APLICADA não ficou visível na transação.");
        ValidateAppliedContent(receipt, prepared, plan);
        return receipt;
    }

    public static void ValidateAppliedContent(
        IdentityCompositionAppliedReceipt applied,
        IdentityCompositionPreparedReceipt prepared,
        IdentityCompositionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateReceipt(applied);
        if (applied.DecisionId != prepared.DecisionId || plan.DecisionId != prepared.DecisionId ||
            !string.Equals(applied.RequestHash, prepared.RequestHash, StringComparison.Ordinal) ||
            !string.Equals(applied.PlanHash, prepared.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(applied.ReservationsHash, prepared.ReservationsHash, StringComparison.Ordinal) ||
            applied.AppliedChanges != plan.Changes.Length || applied.RegisteredHistories != plan.HistoryToAppend.Length)
            throw new InvalidOperationException("Recibo APLICADA diverge do PREPARADA correspondente.");
    }

    private async Task<CurrentOrigin?> LoadCurrentAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid initialUuid,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "SELECT pessoa_origem_id,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WHERE initial_uuid=@initial FOR UPDATE;"
            : "SELECT pessoa_origem_id,canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WITH(UPDLOCK,HOLDLOCK) WHERE initial_uuid=@initial;";
        Add(command, "@initial", DbType.Guid, initialUuid);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var sourceId = reader.GetInt64(0);
        var canonical = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var state = reader.GetString(2);
        var version = reader.GetInt64(3);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("UUID inicial está associado a mais de uma origem.");
        if (sourceId <= 0 || canonical == Guid.Empty || version < 0 ||
            !Enum.TryParse<ProgressiveIdentityStatus>(state, false, out var status) || !Enum.IsDefined(status))
            throw new InvalidOperationException("Estado progressivo corrente inválido.");
        return new CurrentOrigin(sourceId, canonical, status, version);
    }

    private async Task InsertResolutionEventAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sourceId,
        IdentityCompositionChange change,
        IdentityCompositionDecision decision,
        string outcome,
        Guid? eventTarget,
        string? universe,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = postgres
            ? "INSERT INTO identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,@version,'RESOLUCAO',@state,@canonical,@expected,@outcome,@target,@evidence,@policy,NULL,@universe,TRUE,@at);"
            : "INSERT identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,@version,'RESOLUCAO',@state,@canonical,@expected,@outcome,@target,@evidence,@policy,NULL,@universe,1,@at);";
        Add(command, "@event", DbType.Guid, Guid.NewGuid());
        Add(command, "@source", DbType.Int64, sourceId);
        Add(command, "@version", DbType.Int64, change.NewVersion);
        Add(command, "@state", DbType.String, change.AfterStatus.ToString());
        Add(command, "@canonical", DbType.Guid, change.AfterUuid);
        Add(command, "@expected", DbType.Int64, change.ExpectedVersion);
        Add(command, "@outcome", DbType.String, outcome);
        Add(command, "@target", DbType.Guid, eventTarget);
        Add(command, "@evidence", DbType.String, decision.EvidenceReference);
        Add(command, "@policy", DbType.String, decision.PolicyVersion);
        Add(command, "@universe", DbType.String, universe);
        Add(command, "@at", DbType.DateTimeOffset, appliedAt);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Evento progressivo de composição não foi persistido exatamente uma vez.");
    }

    private async Task UpdateProgressiveAsync(
        DbConnection connection,
        DbTransaction transaction,
        long sourceId,
        IdentityCompositionChange change,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        var external = change.AfterUuid is { } target && target != change.InitialUuid ? target : (Guid?)null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE identidade.pessoa_origem_progressiva SET canonical_uuid=@canonical,estado=@state,versao=@new_version,ultima_resolucao_em=@at,ultimo_destino_externo_uuid=@external,atualizado_em=@at WHERE pessoa_origem_id=@source AND initial_uuid=@initial AND versao=@expected;";
        Add(command, "@canonical", DbType.Guid, change.AfterUuid);
        Add(command, "@state", DbType.String, change.AfterStatus.ToString());
        Add(command, "@new_version", DbType.Int64, change.NewVersion);
        Add(command, "@at", DbType.DateTimeOffset, appliedAt);
        Add(command, "@external", DbType.Guid, external);
        Add(command, "@source", DbType.Int64, sourceId);
        Add(command, "@initial", DbType.Guid, change.InitialUuid);
        Add(command, "@expected", DbType.Int64, change.ExpectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Atualização progressiva de composição não afetou exatamente uma origem.");
    }

    private static void ValidateAuditText(IdentityCompositionDecision decision)
    {
        if (string.IsNullOrWhiteSpace(decision.EvidenceReference) || decision.EvidenceReference.Length > 255 ||
            string.IsNullOrWhiteSpace(decision.PolicyVersion) || decision.PolicyVersion.Length > 120)
            throw new InvalidOperationException("Evidência ou política excede o contrato persistente da identidade progressiva.");
    }

    private static void ValidateApplier(string applierReference)
    {
        if (string.IsNullOrWhiteSpace(applierReference) || applierReference.Trim().Length > 120)
            throw new ArgumentException("Referência opaca do aplicador é obrigatória e limitada a 120 caracteres.", nameof(applierReference));
    }

    private static void ValidateReceipt(IdentityCompositionAppliedReceipt receipt)
    {
        ValidateApplier(receipt.ApplierReference);
        if (receipt.DecisionId == Guid.Empty || !string.Equals(receipt.State, "APLICADA", StringComparison.Ordinal) ||
            receipt.AppliedAt == default || receipt.AppliedAt.Offset != TimeSpan.Zero ||
            receipt.AppliedChanges <= 0 || receipt.RegisteredHistories < 0 ||
            !IsHash(receipt.RequestHash) || !IsHash(receipt.PlanHash) || !IsHash(receipt.ReservationsHash))
            throw new InvalidOperationException("Recibo APLICADA inválido.");
    }

    private static bool IsHash(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(transaction.Connection, connection) || connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Aplicação de composição exige transação ativa na conexão informada.");
    }

    private static void Add(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record CurrentOrigin(long SourceId, Guid? CanonicalUuid, ProgressiveIdentityStatus Status, long Version);
}

/// <summary>
/// Orquestra PREPARADA -> APLICADA dentro da transação do chamador.
/// Replay de decisão já aplicada devolve o recibo existente e não repete eventos ou alterações.
/// </summary>
public sealed class IdentityCompositionApplicationService
{
    private readonly IdentityCompositionLedgerStore ledger;
    private readonly IdentityCompositionPreApplicationService preApplication;
    private readonly IdentityCompositionApplicationStore application;

    public IdentityCompositionApplicationService(
        IdentityCompositionLedgerStore ledger,
        IdentityCompositionPreApplicationService preApplication,
        IdentityCompositionApplicationStore application)
    {
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.preApplication = preApplication ?? throw new ArgumentNullException(nameof(preApplication));
        this.application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public async Task<IdentityCompositionApplicationResult> ApplyAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid decisionId,
        string applierReference,
        CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty) throw new ArgumentException("Decisão inválida.", nameof(decisionId));
        if (string.IsNullOrWhiteSpace(applierReference) || applierReference.Trim().Length > 120)
            throw new ArgumentException("Referência opaca do aplicador é obrigatória e limitada a 120 caracteres.", nameof(applierReference));

        // O lock do PREPARADA serializa primeira aplicação e replay concorrente pelo mesmo decision_id.
        var prepared = await ledger.ReadPreparedAsync(connection, transaction, decisionId, cancellationToken)
            ?? throw new InvalidOperationException("Plano PREPARADA inexistente.");
        var existing = await application.ReadAppliedAsync(connection, transaction, decisionId, cancellationToken);
        if (existing is not null)
        {
            var storedPlan = DeserializePlan(prepared.PlanJson);
            IdentityCompositionApplicationStore.ValidateAppliedContent(existing, prepared, storedPlan);
            return new IdentityCompositionApplicationResult(existing, Replay: true);
        }

        // Relê PREPARADA e o componente autoritativo na mesma transação e exige replanning idêntico.
        var validation = await preApplication.ValidateAsync(connection, transaction, decisionId, cancellationToken);
        var appliedAt = DateTimeOffset.UtcNow;
        var changes = await application.ApplyProgressiveChangesAsync(
            connection, transaction, validation, appliedAt, cancellationToken);
        var histories = await application.AppendAppliedHistoryAsync(
            connection, transaction, validation.Replanned, appliedAt, cancellationToken);
        var receipt = await application.RegisterAppliedAsync(
            connection, transaction, validation.Receipt, validation.Replanned,
            applierReference, appliedAt, changes, histories, cancellationToken);
        return new IdentityCompositionApplicationResult(receipt, Replay: false);
    }

    private static IdentityCompositionPlan DeserializePlan(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<IdentityCompositionPlan>(json)
                ?? throw new InvalidOperationException("Plano vazio no ledger.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException("Plano inválido no ledger.", ex);
        }
    }
}

using System.Data.Common;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Operational.Sql;

/// <summary>
/// Fonte de leitura autoritativa para pré-aplicação. A implementação deve carregar e travar o
/// componente completo dentro da transação recebida, incluindo autoridade CPF já admitida.
/// A lista da decisão é somente uma semente; nunca é prova de fechamento do componente.
/// </summary>
public interface IIdentityCompositionAuthoritativeReader
{
    Task<IdentityCompositionReadSet> LoadClosedReadSetAsync(
        DbConnection connection,
        DbTransaction transaction,
        IdentityCompositionDecision decision,
        IReadOnlyList<Guid> reservedUuids,
        CancellationToken cancellationToken = default);
}

public sealed record IdentityCompositionPreApplicationValidation(
    IdentityCompositionPreparedReceipt Receipt,
    IdentityCompositionDecision Decision,
    IdentityCompositionPlan PreparedPlan,
    IdentityCompositionPlan Replanned,
    IdentityCompositionReadSet AuthoritativeReadSet);

/// <summary>
/// Valida um plano PREPARADA contra estado autoritativo corrente. Não possui operação Apply e
/// deliberadamente não altera identidade, vínculos, CPF, eventos progressivos ou Gold/Serving.
/// </summary>
public sealed class IdentityCompositionPreApplicationService
{
    private readonly IdentityCompositionLedgerStore ledger;
    private readonly IIdentityCompositionAuthoritativeReader authoritativeReader;

    public IdentityCompositionPreApplicationService(
        IdentityCompositionLedgerStore ledger,
        IIdentityCompositionAuthoritativeReader authoritativeReader)
    {
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.authoritativeReader = authoritativeReader ?? throw new ArgumentNullException(nameof(authoritativeReader));
    }

    public async Task<IdentityCompositionPreApplicationValidation> ValidateAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid decisionId,
        CancellationToken cancellationToken = default)
    {
        if (decisionId == Guid.Empty) throw new ArgumentException("Decisão inválida.", nameof(decisionId));

        var receipt = await ledger.ReadPreparedAsync(connection, transaction, decisionId, cancellationToken)
            ?? throw new InvalidOperationException("Plano PREPARADA inexistente.");
        var decision = Deserialize<IdentityCompositionDecision>(receipt.RequestJson, "decisão");
        var preparedPlan = Deserialize<IdentityCompositionPlan>(receipt.PlanJson, "plano");
        var reserved = await ledger.ReadReservedUuidsAsync(connection, transaction, decisionId, cancellationToken);

        IdentityCompositionLedgerStore.ValidatePreparedContent(receipt, decision, preparedPlan, reserved);
        var readSet = await authoritativeReader.LoadClosedReadSetAsync(
            connection, transaction, decision, reserved, cancellationToken);
        ArgumentNullException.ThrowIfNull(readSet);
        var replanned = ValidateAuthoritativeState(receipt, decision, preparedPlan, reserved, readSet);

        return new IdentityCompositionPreApplicationValidation(
            receipt, decision, preparedPlan, replanned, readSet);
    }

    public static IdentityCompositionPlan ValidateAuthoritativeState(
        IdentityCompositionPreparedReceipt receipt,
        IdentityCompositionDecision decision,
        IdentityCompositionPlan preparedPlan,
        IEnumerable<Guid> reservedUuids,
        IdentityCompositionReadSet authoritativeReadSet)
    {
        ArgumentNullException.ThrowIfNull(authoritativeReadSet);
        IdentityCompositionLedgerStore.ValidatePreparedContent(
            receipt, decision, preparedPlan, reservedUuids);
        var replanned = IdentityCompositionPlanner.Prepare(authoritativeReadSet, decision);
        var replannedJson = IdentityCompositionCanonical.SerializePlan(replanned);
        if (!string.Equals(replannedJson, receipt.PlanJson, StringComparison.Ordinal))
            throw new InvalidOperationException("Plano PREPARADA tornou-se obsoleto diante do estado autoritativo corrente.");
        return replanned;
    }

    private static T Deserialize<T>(string json, string label)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new InvalidOperationException($"Payload de {label} vazio no ledger.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Payload de {label} inválido no ledger.", ex);
        }
    }
}

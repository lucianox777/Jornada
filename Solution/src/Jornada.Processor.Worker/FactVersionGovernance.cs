namespace Jornada.Processor.Worker;

internal sealed record FactVersionGovernanceSnapshot(
    IntegrationNature Natureza,
    long TipoRegistroId,
    Guid? PessoaUuid,
    long? PessoaOrigemId,
    DateOnly? DataInicioConcessao,
    DateTimeOffset? DataHoraServico);

internal sealed record FactVersionGovernanceDecision(
    IReadOnlyList<string> ConflictReasons)
{
    public bool HasRetificationConflict => ConflictReasons.Count > 0;

    public string CanonicalReason =>
        HasRetificationConflict
            ? "RN_CT_12:" + string.Join(",", ConflictReasons.OrderBy(static x => x, StringComparer.Ordinal))
            : string.Empty;
}

/// <summary>
/// Regra pura da RN-CT-12. idPessoaEntrega não participa da comparação: ele é chave
/// técnica apenas dentro da Entrega. Conflito de Pessoa só é afirmado quando ambas as
/// observações possuem identidade persistente comparável (UUID canônico; ou, na ausência
/// dele em ambos os lados, a mesma origem persistente pode confirmar continuidade, mas
/// origens distintas não são usadas sozinhas para afirmar pessoas distintas).
/// </summary>
internal static class FactVersionGovernance
{
    internal const string ConflictType = "CONFLITO_RETIFICACAO";
    internal const string DuplicateAlertType = "POSSIVEL_DUPLICACAO_FATO";
    internal const string DuplicateExactReason = "RN_CT_12_DUPLICIDADE_EXATA";

    public static FactVersionGovernanceDecision Evaluate(
        FactVersionGovernanceSnapshot previous,
        FactVersionGovernanceSnapshot proposed)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(proposed);

        var reasons = new List<string>(3);

        if (previous.Natureza != proposed.Natureza ||
            previous.TipoRegistroId != proposed.TipoRegistroId)
        {
            reasons.Add("NATUREZA_TIPO");
        }

        if (previous.PessoaUuid is Guid previousUuid &&
            proposed.PessoaUuid is Guid proposedUuid &&
            previousUuid != proposedUuid)
        {
            reasons.Add("PESSOA");
        }

        if (previous.Natureza == proposed.Natureza)
        {
            if (proposed.Natureza == IntegrationNature.BENEFICIO &&
                previous.DataInicioConcessao is DateOnly previousStart &&
                proposed.DataInicioConcessao is DateOnly proposedStart &&
                previousStart != proposedStart)
            {
                reasons.Add("MARCO_INICIAL");
            }
            else if (proposed.Natureza == IntegrationNature.SERVICO &&
                     previous.DataHoraServico is DateTimeOffset previousServiceAt &&
                     proposed.DataHoraServico is DateTimeOffset proposedServiceAt &&
                     previousServiceAt != proposedServiceAt)
            {
                reasons.Add("MARCO_INICIAL");
            }
        }

        return new FactVersionGovernanceDecision(reasons);
    }
}

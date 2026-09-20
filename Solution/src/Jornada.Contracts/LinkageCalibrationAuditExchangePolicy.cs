namespace Jornada.Contracts;

/// <summary>
/// Contrato de interoperabilidade da exportação de auditoria do modelo de Linkage.
/// Não ativa modelo, não define qualidade estatística e não afirma compatibilidade
/// completa com um runtime externo específico.
/// </summary>
public static class LinkageCalibrationAuditExchangePolicy
{
    public const string UProbabilitySemantics = "CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION";

    public static IReadOnlyList<string> UnmappedOrNonBijectiveComparisonStates { get; } =
    [
        "DAY_MONTH_SWAP",
        "CENTURY_SHIFT",
        "ONE_DIGIT_ERROR",
        "TWO_DIGIT_ERROR",
        "PARTIAL_COMPONENT_AGREEMENT"
    ];

    public static bool IsExportableModelStatus(string? status) =>
        string.Equals(status, "ATIVO", StringComparison.Ordinal)
        || string.Equals(status, "VALIDADO", StringComparison.Ordinal);

    public static void EnsureExportableModelStatus(Guid modelId, string? status)
    {
        if (!IsExportableModelStatus(status))
            throw new InvalidOperationException(
                $"Exportação de auditoria aceita somente modelo ATIVO ou VALIDADO; modelo {modelId} está {status ?? "SEM_STATUS"}.");
    }
}

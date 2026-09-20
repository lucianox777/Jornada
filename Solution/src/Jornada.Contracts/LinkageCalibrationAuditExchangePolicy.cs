namespace Jornada.Contracts;

/// <summary>
/// Contrato de interoperabilidade da exportação de auditoria do modelo de Linkage.
/// Não ativa modelo, não define qualidade estatística e não afirma compatibilidade
/// completa com um runtime externo específico.
/// </summary>
public static class LinkageCalibrationAuditExchangePolicy
{
    public const int SchemaVersion = 1;
    public const string Nature = "LINKAGE_CALIBRATION_AUDIT_EXPORT";
    public const string Purpose = "EXTERNAL_REPRODUCIBILITY_READ_ONLY";
    // Semântica do u empírico estimado no universo candidato; fontes nominais podem
    // permanecer em bootstrap IBGE enquanto o suporte condicionado não converge.
    public const string UProbabilitySemantics = "CONDITIONED_ON_DEDUPLICATED_BLOCKING_CANDIDATE_UNION";
    public const string NominalUSourceBlockingConditioned = "BLOCKING_CONDITIONED";
    public const string NominalUSourceIbgeBootstrap = "IBGE_BOOTSTRAP";
    public const string NominalUSourceUndeclared = "NAO_DECLARADO";
    public const string ComparisonStateMappingRule =
        "Do not collapse semantic states silently; an external adapter must declare an explicit mapping.";

    public static IReadOnlyList<string> UnmappedOrNonBijectiveComparisonStates { get; } =
        Array.AsReadOnly(new[]
        {
            "DAY_MONTH_SWAP",
            "CENTURY_SHIFT",
            "ONE_DIGIT_ERROR",
            "TWO_DIGIT_ERROR",
            "PARTIAL_COMPONENT_AGREEMENT"
        });

    public static string ResolveNominalUSource(
        IReadOnlyList<LinkageCalibrationAuditParameter> parameters,
        bool motherName)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var conditionedKey = motherName
            ? "NOMINAL_U_NOME_MAE_SOURCE_BLOCKING_CONDITIONED"
            : "NOMINAL_U_NOME_SOURCE_BLOCKING_CONDITIONED";
        var ibgeKey = motherName
            ? "IBGE_MC_NOMINAL_U_APPLIED_NOME_MAE"
            : "IBGE_MC_NOMINAL_U_APPLIED_NOME";

        var conditioned = parameters.FirstOrDefault(x =>
            string.Equals(x.Name, conditionedKey, StringComparison.Ordinal))?.Value;
        var ibge = parameters.FirstOrDefault(x =>
            string.Equals(x.Name, ibgeKey, StringComparison.Ordinal))?.Value;

        var usesConditioned = conditioned is >= 1m;
        var usesIbge = ibge is >= 1m;
        if (usesConditioned && usesIbge)
            throw new InvalidDataException(
                $"Proveniência nominal inconsistente: {conditionedKey} e {ibgeKey} estão simultaneamente ativos.");

        if (usesConditioned) return NominalUSourceBlockingConditioned;
        if (usesIbge) return NominalUSourceIbgeBootstrap;
        return NominalUSourceUndeclared;
    }

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

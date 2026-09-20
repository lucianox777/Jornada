namespace Jornada.Contracts;

/// <summary>
/// Detector conservador de inconsistência entre observações que declaram o mesmo CPF.
/// O CPF permanece determinístico. Ausência de evidência não é divergência: conflito
/// exige simultaneamente data observada e diferente e nomes observados com comparação LOW.
/// </summary>
public static class CpfIdentityConsistency
{
    public const string PolicyVersion = "CPF_CORE_CONSISTENCY_V2_PARTIAL_EVIDENCE";
    public const string SharedCpfSuspectedReason = "CPF_COMPARTILHADO_SUSPEITO";
    public const string ExistingCoreUnavailableReason = "CPF_NUCLEO_EXISTENTE_INDISPONIVEL";
    public const string IdentifierInConflictReason = "CPF_EM_CONFLITO_IDENTIDADE";

    public static CpfIdentityConsistencyAssessment Evaluate(
        IdentityCore existing,
        IdentityCore incoming)
    {
        static NameComparisonState? CompareOptional(string? left, string? right) =>
            IdentityComparison.NormalizeText(left) is null || IdentityComparison.NormalizeText(right) is null
                ? null
                : IdentityComparison.CompareName(left, right);

        var name = CompareOptional(existing.NomeCompleto, incoming.NomeCompleto);
        var mother = CompareOptional(existing.NomeMae, incoming.NomeMae);
        bool? birthDateMatches =
            existing.DataNascimento.HasValue && incoming.DataNascimento.HasValue
                ? existing.DataNascimento.Value == incoming.DataNascimento.Value
                : null;

        var conflict = birthDateMatches == false && name == NameComparisonState.LOW;
        return new CpfIdentityConsistencyAssessment(
            conflict,
            conflict ? SharedCpfSuspectedReason : null,
            name,
            mother,
            birthDateMatches,
            PolicyVersion);
    }
}

public sealed record IdentityCore(
    string? NomeCompleto,
    DateOnly? DataNascimento,
    string? NomeMae);

public sealed record CpfIdentityConsistencyAssessment(
    bool IsConflict,
    string? Motivo,
    NameComparisonState? Nome,
    NameComparisonState? NomeMae,
    bool? DataNascimentoIgual,
    string PolicyVersion);

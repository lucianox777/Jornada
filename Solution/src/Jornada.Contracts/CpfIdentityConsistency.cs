namespace Jornada.Contracts;

/// <summary>
/// Detector conservador de inconsistência global entre observações que declaram o mesmo CPF.
/// O CPF continua sendo a rota determinística e a regra nunca escolhe outra identidade nem
/// classifica automaticamente uma observação específica como errada. A V1 sinaliza o próprio
/// identificador quando encontra divergência forte em dois sinais independentes: nome LOW e
/// data de nascimento diferente. Variações de nome com a mesma data e erros isolados de data
/// não são, por si só, conflito. O caso não é enviado ao linkage probabilístico.
/// </summary>
public static class CpfIdentityConsistency
{
    public const string PolicyVersion = "CPF_CORE_CONSISTENCY_V1";
    public const string SharedCpfSuspectedReason = "CPF_COMPARTILHADO_SUSPEITO";
    public const string ExistingCoreUnavailableReason = "CPF_NUCLEO_EXISTENTE_INDISPONIVEL";
    public const string IdentifierInConflictReason = "CPF_EM_CONFLITO_IDENTIDADE";

    public static CpfIdentityConsistencyAssessment Evaluate(
        IdentityCore existing,
        IdentityCore incoming)
    {
        var name = IdentityComparison.CompareName(existing.NomeCompleto, incoming.NomeCompleto);
        var mother = IdentityComparison.CompareName(existing.NomeMae, incoming.NomeMae);
        var birthDateMatches = existing.DataNascimento == incoming.DataNascimento;

        var conflict = !birthDateMatches && name == NameComparisonState.LOW;
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
    string NomeCompleto,
    DateOnly DataNascimento,
    string NomeMae);

public sealed record CpfIdentityConsistencyAssessment(
    bool IsConflict,
    string? Motivo,
    NameComparisonState Nome,
    NameComparisonState NomeMae,
    bool DataNascimentoIgual,
    string PolicyVersion);

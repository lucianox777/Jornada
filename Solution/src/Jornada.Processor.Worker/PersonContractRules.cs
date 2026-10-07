using Jornada.Contracts;

namespace Jornada.Processor.Worker;

/// <summary>
/// Regras semânticas do contrato Pessoa corrente após a convergência dos
/// identificadores legados + identificadores[]. O repositório mantém somente
/// a versão corrente do contrato; versões antigas permanecem apenas no histórico Git.
/// </summary>
internal static class PersonContractRules
{
    public const int CurrentSchemaVersion = ContractVersions.CurrentPersonSchemaVersion;

    private static readonly HashSet<string> CpfAbsenceReasons = new(StringComparer.Ordinal)
    {
        "NAO_INFORMADO_ORIGEM",
        "SEM_DOCUMENTACAO_BASE_DECLARADA",
        "COM_DOCUMENTACAO_SEM_CPF_CONHECIDO",
        "EM_REGULARIZACAO"
    };

    public static void ValidateCpfAbsence(int pessoaSchemaVersao, string? cpf, string? cpfAbsenceReason)
    {
        if (pessoaSchemaVersao != CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Contrato Pessoa não suportado: v{pessoaSchemaVersao}. A versão corrente é v{CurrentSchemaVersion}.");

        if (string.IsNullOrWhiteSpace(cpf))
        {
            if (string.IsNullOrWhiteSpace(cpfAbsenceReason) || !CpfAbsenceReasons.Contains(cpfAbsenceReason))
                throw new InvalidDataException(
                    $"Pessoa v{CurrentSchemaVersion} sem CPF exige cpfAusenteMotivo explícito da taxonomia corrente.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(cpfAbsenceReason))
            throw new InvalidDataException(
                $"Pessoa v{CurrentSchemaVersion} com CPF não pode declarar cpfAusenteMotivo.");
    }

    public static bool IsCpfAbsenceReason(string value)
        => CpfAbsenceReasons.Contains(value);
}

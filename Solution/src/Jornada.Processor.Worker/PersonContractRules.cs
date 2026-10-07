namespace Jornada.Processor.Worker;

/// <summary>
/// Regras semânticas do contrato Pessoa corrente. O ambiente em desenvolvimento
/// mantém somente a versão vigente; compatibilidade com contratos históricos não
/// é tratada pelo Processor.
/// </summary>
internal static class PersonContractRules
{
    private static readonly HashSet<string> CpfAbsenceReasons = new(StringComparer.Ordinal)
    {
        "NAO_INFORMADO_ORIGEM",
        "SEM_DOCUMENTACAO_BASE_DECLARADA",
        "COM_DOCUMENTACAO_SEM_CPF_CONHECIDO",
        "EM_REGULARIZACAO"
    };

    public static void ValidateCpfAbsence(string? cpf, string? cpfAbsenceReason)
    {
        if (string.IsNullOrWhiteSpace(cpf))
        {
            if (string.IsNullOrWhiteSpace(cpfAbsenceReason) || !CpfAbsenceReasons.Contains(cpfAbsenceReason))
                throw new InvalidDataException(
                    "Pessoa sem CPF exige cpfAusenteMotivo explícito da taxonomia corrente.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(cpfAbsenceReason))
            throw new InvalidDataException("Pessoa com CPF não pode declarar cpfAusenteMotivo.");
    }

    public static bool IsCpfAbsenceReason(string value)
        => CpfAbsenceReasons.Contains(value);
}

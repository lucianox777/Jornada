namespace Jornada.Processor.Worker;

/// <summary>
/// Regras semânticas que dependem da versão Pessoa após a convergência dos
/// identificadores legados + identificadores[]. Mantê-las fora do JSON Schema
/// evita tratar a ausência do campo legado cpf como se não pudesse existir CPF
/// explícito na coleção de identificadores.
/// </summary>
internal static class PersonV5ContractRules
{
    private static readonly HashSet<string> CpfAbsenceReasons = new(StringComparer.Ordinal)
    {
        "NAO_INFORMADO_ORIGEM",
        "SEM_DOCUMENTACAO_BASE_DECLARADA",
        "COM_DOCUMENTACAO_SEM_CPF_CONHECIDO",
        "EM_REGULARIZACAO"
    };

    public static void ValidateCpfAbsence(int pessoaSchemaVersao, string? cpf, string? cpfAbsenceReason)
    {
        if (pessoaSchemaVersao < 5)
            return;

        if (string.IsNullOrWhiteSpace(cpf))
        {
            if (string.IsNullOrWhiteSpace(cpfAbsenceReason) || !CpfAbsenceReasons.Contains(cpfAbsenceReason))
                throw new InvalidDataException(
                    "Pessoa v5 sem CPF exige cpfAusenteMotivo explícito da taxonomia v5.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(cpfAbsenceReason))
            throw new InvalidDataException("Pessoa v5 com CPF não pode declarar cpfAusenteMotivo.");
    }

    public static bool IsCpfAbsenceReasonV5(string value)
        => CpfAbsenceReasons.Contains(value);
}

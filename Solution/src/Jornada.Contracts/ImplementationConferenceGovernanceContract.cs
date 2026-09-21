using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Contracts;

public static class ImplementationConferenceGovernanceContract
{
    public const int SchemaVersion = 1;
    public const string MethodVersion = "JORNADA_IMPLEMENTATION_CONFERENCE_STATE_VECTOR_V1";
    public const string Scope =
        "SCORER_POLICY_ONLY_STATES_AND_GUARD_INPUTS_PRECOMPUTED_COMPARATORS_OUT_OF_SCOPE";
    public const string DecisionEquivalence = "EXACT_FINAL_OPERATIONAL_DECISION";
    public const string StatisticalValidation = "SEPARATE_ISSUE_31";

    public static IReadOnlyList<string> PrimaryGates { get; } =
        Array.AsReadOnly(new[]
        {
            "PAIR_LLR_WITHIN_FROZEN_TOLERANCE",
            "EXACT_FINAL_OPERATIONAL_DECISION"
        });
}

public sealed record ImplementationConferenceToleranceContract(
    string Version,
    string Status,
    decimal? MaxAbsolutePairLlrDifference)
{
    public bool TryGetFrozen(out decimal tolerance, out string reason)
    {
        tolerance = default;
        if (!string.Equals(Status, "FROZEN", StringComparison.Ordinal))
        {
            reason = "TOLERANCE_NOT_FROZEN";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Version)
            || string.Equals(Version, "UNFROZEN", StringComparison.Ordinal)
            || MaxAbsolutePairLlrDifference is null
            || MaxAbsolutePairLlrDifference < 0m)
        {
            reason = "INVALID_FROZEN_TOLERANCE";
            return false;
        }

        tolerance = MaxAbsolutePairLlrDifference.Value;
        reason = string.Empty;
        return true;
    }
}

public sealed record ImplementationConferenceToleranceConfiguration(
    int SchemaVersion,
    string MethodVersion,
    string Status,
    string Scope,
    string ToleranceVersion,
    decimal? MaxAbsolutePairLlrDifference,
    string DecisionEquivalence,
    IReadOnlyList<string> PrimaryGates,
    IReadOnlyList<string> DiagnosticsOnly,
    string StatisticalValidation,
    string Note)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ImplementationConferenceToleranceConfiguration Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException(
                "Configuração de tolerância da conferência não encontrada.",
                full);

        var config = JsonSerializer.Deserialize<ImplementationConferenceToleranceConfiguration>(
            File.ReadAllText(full),
            Options)
            ?? throw new InvalidDataException(
                "Configuração de tolerância da conferência vazia.");

        config.ValidateEnvelope();
        return config;
    }

    public ImplementationConferenceToleranceContract ToContract()
    {
        ValidateEnvelope();
        return new(
            ToleranceVersion,
            Status,
            MaxAbsolutePairLlrDifference);
    }

    public void ValidateEnvelope()
    {
        if (SchemaVersion != ImplementationConferenceGovernanceContract.SchemaVersion)
            throw new InvalidDataException("schemaVersion da tolerância não suportado.");
        if (!string.Equals(
                MethodVersion,
                ImplementationConferenceGovernanceContract.MethodVersion,
                StringComparison.Ordinal))
            throw new InvalidDataException("methodVersion da tolerância diverge do contrato.");
        if (!string.Equals(
                Scope,
                ImplementationConferenceGovernanceContract.Scope,
                StringComparison.Ordinal))
            throw new InvalidDataException("scope da tolerância diverge do contrato.");
        if (!string.Equals(
                DecisionEquivalence,
                ImplementationConferenceGovernanceContract.DecisionEquivalence,
                StringComparison.Ordinal))
            throw new InvalidDataException("decisionEquivalence inválido.");
        if (!PrimaryGates.SequenceEqual(
                ImplementationConferenceGovernanceContract.PrimaryGates,
                StringComparer.Ordinal))
            throw new InvalidDataException("primaryGates divergentes do contrato corrente.");
        if (!string.Equals(
                StatisticalValidation,
                ImplementationConferenceGovernanceContract.StatisticalValidation,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "A conferência não pode incorporar a validação estatística #31.");
    }
}

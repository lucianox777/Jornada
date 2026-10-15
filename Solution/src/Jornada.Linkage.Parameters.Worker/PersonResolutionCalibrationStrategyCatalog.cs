using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Declarative, diagnostic-only strategies anchored to the canonical Person contract.
/// This catalog does not alter the runtime blocking projector or activate V9.
/// </summary>
public static class PersonResolutionCalibrationStrategyCatalog
{
    public const string Version = "PERSON_RESOLUTION_CALIBRATION_STRATEGIES_V1";

    public sealed record Strategy(
        string AttributeCode,
        string Normalizer,
        IReadOnlyList<string> MissingValues,
        IReadOnlyList<string> BlockingDerivations,
        IReadOnlyList<string> FsComparisonLevels,
        string TfSource);

    private static readonly Strategy[] Definitions =
    [
        new(PersonResolutionContractCatalog.FullName, "PERSON_NAME_PT_BR_V1",
            ["", "NULL"], ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            ["EXACT", "HIGH", "PARTIAL", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME"),
        new(PersonResolutionContractCatalog.MotherName, "PERSON_NAME_PT_BR_V1",
            ["", "NULL"], ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            ["EXACT", "HIGH", "PARTIAL", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME"),
        new(PersonResolutionContractCatalog.BirthDate, "ISO_DATE_V1",
            ["", "NULL"], ["EXACT", "DAY_MONTH", "YEAR"],
            ["EXACT", "PARTIAL", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME"),
        new(PersonResolutionContractCatalog.ContactPhone, "BRAZILIAN_PHONE_V2",
            ["", "NULL"], ["EXACT"],
            ["EXACT", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME"),
        new(PersonResolutionContractCatalog.ContactEmail, "EMAIL_CANONICAL_V1",
            ["", "NULL"], ["EXACT"],
            ["EXACT", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME"),
        new(PersonResolutionContractCatalog.SocialName, "PERSON_NAME_PT_BR_V1",
            ["", "NULL"], ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            ["EXACT", "HIGH", "PARTIAL", "DISAGREE", "MISSING"], "TRAIN_CANDIDATE_FRAME")
    ];

    public static IReadOnlyList<Strategy> All { get; } = Array.AsReadOnly(Definitions);

    public static bool TryGet(string? attributeCode, out Strategy strategy)
    {
        strategy = null!;
        if (!PersonResolutionContractCatalog.TryGet(attributeCode, out var contract)
            || !contract.EligibleForResolution)
            return false;
        var found = Definitions.SingleOrDefault(x =>
            string.Equals(x.AttributeCode, contract.Code, StringComparison.Ordinal));
        if (found is null)
            return false;
        strategy = found;
        return true;
    }

    public static IReadOnlyList<Strategy> ValidateAndList()
    {
        if (Definitions.Select(x => x.AttributeCode).Distinct(StringComparer.Ordinal).Count()
            != Definitions.Length)
            throw new InvalidDataException("Duplicate calibration attribute strategy.");
        foreach (var strategy in Definitions)
        {
            if (!PersonResolutionContractCatalog.TryGet(strategy.AttributeCode, out var contract)
                || !contract.EligibleForResolution || strategy.BlockingDerivations.Count == 0
                || strategy.FsComparisonLevels.Count == 0 || string.IsNullOrWhiteSpace(strategy.Normalizer)
                || strategy.TfSource != "TRAIN_CANDIDATE_FRAME")
                throw new InvalidDataException("Invalid calibration strategy: " + strategy.AttributeCode);
        }
        return All;
    }
}

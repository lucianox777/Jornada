using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

public enum PersonResolutionEvidenceRole
{
    RequiredForProbabilisticEligibility,
    AlternativeEligibility,
    ReinforcementOnly
}

public enum PersonResolutionDependencyPolicy
{
    IndependentEvidence,
    JointNominalV9,
    SharedContactWeak,
    CrossCivilSocialName
}

public enum CalibrationTfSource
{
    None,
    TrainDistinctBase,
    FrozenIbgePublishedMarginal,
    FrozenIbgeSurnameAnyPositionProxy
}

/// <summary>
/// Estratégias declarativas do Calibrador ancoradas no contrato canônico de Pessoa.
/// O catálogo descreve semântica; não altera Rank/Resolve nem ativa V9.
/// </summary>
public static class PersonResolutionCalibrationStrategyCatalog
{
    public const string Version = "PERSON_RESOLUTION_CALIBRATION_STRATEGIES_V2";

    public sealed record TfPolicy(
        string ComparisonLevel,
        NominalFrequencyUnitV9 Unit,
        CalibrationTfSource Source);

    public sealed record Strategy(
        string AttributeCode,
        string Normalizer,
        string MissingPolicyVersion,
        IReadOnlyList<string> BlockingDerivations,
        string ComparisonContract,
        IReadOnlyList<string> FsComparisonLevels,
        IReadOnlyList<TfPolicy> TfPolicies,
        PersonResolutionEvidenceRole EvidenceRole,
        PersonResolutionDependencyPolicy DependencyPolicy,
        IReadOnlyList<string> CrossCompareWith);

    private static readonly IReadOnlyList<string> NominalV9Levels =
        Array.AsReadOnly(Enum.GetNames<CompositeNominalLevelV9>());

    private static readonly IReadOnlyList<TfPolicy> CivilNominalTfPolicies =
        Array.AsReadOnly(new[]
        {
            new TfPolicy(nameof(CompositeNominalLevelV9.ExactFull),
                NominalFrequencyUnitV9.FullName, CalibrationTfSource.TrainDistinctBase),
            new TfPolicy(nameof(CompositeNominalLevelV9.ExactFirstLast),
                NominalFrequencyUnitV9.FirstLastJoint, CalibrationTfSource.TrainDistinctBase),
            new TfPolicy(nameof(CompositeNominalLevelV9.ExactFirst),
                NominalFrequencyUnitV9.FirstName, CalibrationTfSource.FrozenIbgePublishedMarginal),
            new TfPolicy(nameof(CompositeNominalLevelV9.ExactLast),
                NominalFrequencyUnitV9.LastContentSurname, CalibrationTfSource.FrozenIbgeSurnameAnyPositionProxy)
        });

    private static readonly Strategy[] Definitions =
    [
        new(PersonResolutionContractCatalog.FullName, "PERSON_NAME_PT_BR_V1",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            CompositeNominalComparisonV9.ContractVersion, NominalV9Levels, CivilNominalTfPolicies,
            PersonResolutionEvidenceRole.RequiredForProbabilisticEligibility,
            PersonResolutionDependencyPolicy.JointNominalV9,
            [PersonResolutionContractCatalog.FullName]),

        new(PersonResolutionContractCatalog.MotherName, "PERSON_NAME_PT_BR_V1",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            CompositeNominalComparisonV9.ContractVersion, NominalV9Levels, CivilNominalTfPolicies,
            PersonResolutionEvidenceRole.AlternativeEligibility,
            PersonResolutionDependencyPolicy.JointNominalV9,
            [PersonResolutionContractCatalog.MotherName]),

        new(PersonResolutionContractCatalog.BirthDate, "ISO_DATE_V1",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT", "DAY_MONTH", "YEAR"],
            "BIRTH_EXACT_OTHER_MISSING_V1", ["Missing", "Exact", "Other"], [],
            PersonResolutionEvidenceRole.AlternativeEligibility,
            PersonResolutionDependencyPolicy.IndependentEvidence,
            [PersonResolutionContractCatalog.BirthDate]),

        new(PersonResolutionContractCatalog.ContactPhone, "BRAZILIAN_PHONE_V2",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT"],
            "EXACT_DISCRETE_MISSING_V1", ["Missing", "Exact", "Other"], [],
            PersonResolutionEvidenceRole.ReinforcementOnly,
            PersonResolutionDependencyPolicy.SharedContactWeak,
            [PersonResolutionContractCatalog.ContactPhone]),

        new(PersonResolutionContractCatalog.ContactEmail, "EMAIL_CANONICAL_V1",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT"],
            "EXACT_DISCRETE_MISSING_V1", ["Missing", "Exact", "Other"], [],
            PersonResolutionEvidenceRole.ReinforcementOnly,
            PersonResolutionDependencyPolicy.SharedContactWeak,
            [PersonResolutionContractCatalog.ContactEmail]),

        new(PersonResolutionContractCatalog.SocialName, "PERSON_NAME_PT_BR_V1",
            PersonResolutionMissingValuePolicy.Version,
            ["EXACT", "PREFIX_K", "FIRST_NAME", "LAST_SURNAME", "PHONETIC"],
            CompositeNominalComparisonV9.ContractVersion, NominalV9Levels, [],
            PersonResolutionEvidenceRole.ReinforcementOnly,
            PersonResolutionDependencyPolicy.CrossCivilSocialName,
            [PersonResolutionContractCatalog.SocialName, PersonResolutionContractCatalog.FullName])
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
                || !contract.EligibleForResolution
                || strategy.BlockingDerivations.Count == 0
                || strategy.FsComparisonLevels.Count == 0
                || string.IsNullOrWhiteSpace(strategy.Normalizer)
                || !string.Equals(strategy.MissingPolicyVersion,
                    PersonResolutionMissingValuePolicy.Version, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(strategy.ComparisonContract)
                || strategy.FsComparisonLevels.Distinct(StringComparer.Ordinal).Count()
                    != strategy.FsComparisonLevels.Count
                || !strategy.FsComparisonLevels.Contains("Missing", StringComparer.Ordinal)
                || strategy.CrossCompareWith.Count == 0)
                throw new InvalidDataException("Invalid calibration strategy: " + strategy.AttributeCode);

            foreach (var crossCode in strategy.CrossCompareWith)
                if (!PersonResolutionContractCatalog.IsEligible(crossCode))
                    throw new InvalidDataException("Cross comparison references ineligible attribute: " + crossCode);

            if (string.Equals(strategy.ComparisonContract,
                    CompositeNominalComparisonV9.ContractVersion, StringComparison.Ordinal))
            {
                if (!strategy.FsComparisonLevels.SequenceEqual(NominalV9Levels, StringComparer.Ordinal))
                    throw new InvalidDataException("Nominal V9 levels must match CompositeNominalLevelV9 exactly.");

                if (strategy.TfPolicies.Select(x => x.ComparisonLevel)
                    .Distinct(StringComparer.Ordinal).Count() != strategy.TfPolicies.Count)
                    throw new InvalidDataException("Duplicate TF policy for nominal level.");

                foreach (var tf in strategy.TfPolicies)
                {
                    if (!Enum.TryParse<CompositeNominalLevelV9>(tf.ComparisonLevel, out var level))
                        throw new InvalidDataException("TF policy references unknown V9 level.");
                    var required = CompositeNominalTermFrequencyContractV9.RequiredUnit(level);
                    if (required is null || required != tf.Unit)
                        throw new InvalidDataException("TF policy unit is incompatible with V9 comparison level.");
                }
            }
            else if (strategy.TfPolicies.Count != 0)
            {
                throw new InvalidDataException("Non-nominal strategies cannot declare nominal TF policy.");
            }
        }

        var full = Definitions.Single(x => x.AttributeCode == PersonResolutionContractCatalog.FullName);
        var mother = Definitions.Single(x => x.AttributeCode == PersonResolutionContractCatalog.MotherName);
        var birth = Definitions.Single(x => x.AttributeCode == PersonResolutionContractCatalog.BirthDate);
        if (full.EvidenceRole != PersonResolutionEvidenceRole.RequiredForProbabilisticEligibility
            || mother.EvidenceRole != PersonResolutionEvidenceRole.AlternativeEligibility
            || birth.EvidenceRole != PersonResolutionEvidenceRole.AlternativeEligibility)
            throw new InvalidDataException("Eligibility roles must implement person + (mother or birth).");

        return All;
    }
}

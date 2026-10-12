namespace Jornada.Contracts;

/// <summary>
/// The *statistical unit* used to estimate a nominal term frequency.
/// FULL and FIRST+LAST are joint units, never a product of IBGE marginals.
/// </summary>
public enum NominalFrequencyUnitV9
{
    FullName,
    FirstLastJoint,
    FirstName,
    LastContentSurname
}

/// <summary>
/// Source description is metadata supplied by an independent reference
/// certification, NOT proof of sampling representativeness by this class.
/// </summary>
public enum NominalFrequencySourceV9
{
    IndependentlyObservedPopulation,
    IbgePublishedMarginal
}

public sealed record NominalFrequencyReferenceContractV9(
    NominalFrequencyUnitV9 Unit,
    NominalFrequencySourceV9 Source,
    string PopulationFrameFingerprint,
    string BlockingFrameFingerprint,
    long IndependentObservedSupport,
    bool IndependentPopulationValidationApproved);

/// <summary>
/// Fail-closed guard against the V8 error of applying marginal first-name
/// frequencies to full-name EXACT agreement. It does NOT estimate f(value),
/// m/u, LLR, calibrated TF weight or model activation authority.
/// </summary>
public static class CompositeNominalTermFrequencyContractV9
{
    public const string Version = "V9_NOMINAL_TF_UNIT_GUARD_V1";

    public static NominalFrequencyUnitV9? RequiredUnit(CompositeNominalLevelV9 level) =>
        level switch
        {
            CompositeNominalLevelV9.ExactFull => NominalFrequencyUnitV9.FullName,
            CompositeNominalLevelV9.ExactFirstLast => NominalFrequencyUnitV9.FirstLastJoint,
            CompositeNominalLevelV9.ExactFirst => NominalFrequencyUnitV9.FirstName,
            CompositeNominalLevelV9.ExactLast => NominalFrequencyUnitV9.LastContentSurname,
            CompositeNominalLevelV9.Missing
                or CompositeNominalLevelV9.Other
                or CompositeNominalLevelV9.HighFull
                or CompositeNominalLevelV9.HighFirstLast
                or CompositeNominalLevelV9.ReversedFirstLast => null,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Nível V9 inválido.")
        };

    /// <summary>
    /// Gates the *eligibility of a reference contract only*; an upstream
    /// certifier must prove the attested frame and independence. No frequency
    /// or correction is returned. Missing/approximate levels are TF-neutral.
    /// </summary>
    public static void RequireMatchingReference(
        CompositeNominalLevelV9 level,
        NominalFrequencyReferenceContractV9 reference,
        long minimumIndependentSupport)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumIndependentSupport);
        if (!Enum.IsDefined(reference.Unit) || !Enum.IsDefined(reference.Source))
            throw new InvalidOperationException("Contrato de unidade/fonte TF V9 desconhecido.");
        var expectedUnit = RequiredUnit(level)
            ?? throw new InvalidOperationException("Nível nominal V9 não possui ajuste TF calibrável.");
        if (expectedUnit != reference.Unit)
            throw new InvalidOperationException(
                $"TF V9 rejeitada: nível {level} exige unidade {expectedUnit}, recebida {reference.Unit}.");

        // The IBGE published first/surname marginal cannot attest a full
        // name or joint first+last distribution by multiplication.
        if (reference.Source == NominalFrequencySourceV9.IbgePublishedMarginal
            && expectedUnit is NominalFrequencyUnitV9.FullName
                or NominalFrequencyUnitV9.FirstLastJoint)
            throw new InvalidOperationException(
                "Marginais publicadas do IBGE não constituem distribuição conjunta ou nome integral.");

        if (string.IsNullOrWhiteSpace(reference.PopulationFrameFingerprint)
            || string.IsNullOrWhiteSpace(reference.BlockingFrameFingerprint)
            || reference.IndependentObservedSupport < minimumIndependentSupport
            || !reference.IndependentPopulationValidationApproved)
            throw new InvalidOperationException(
                "TF V9 exige referência com frame, blocking, suporte independente e validação populacional aprovada.");
    }
}

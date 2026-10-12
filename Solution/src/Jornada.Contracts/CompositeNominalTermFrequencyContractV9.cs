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
    IbgePublishedMarginal,
    // DC-LK-TF: surname counted in ANY position is allowed only as an
    // explicitly identified INITIAL PROXY for the last content surname.
    // Never mislabel this as the empirically observed last-position rate.
    IbgeSurnameAnyPositionProxy
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
    public const string Version = "V9_NOMINAL_TF_UNIT_GUARD_V2";

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

        // DC-LK-TF, Decisoes_Canonicas_Identidade_Linkage_20260929.md:95:
        // the published surname frequency covers ANY surname position, so
        // it is an authorized initial PROXY for LastContentSurname, NOT the
        // observed last-position distribution. Require explicit proxy source
        // to avoid silently treating the IBGE marginal as empirically exact.
        // Neither IBGE marginal supports full-name or first+last JOINT TF.
        if (reference.Source == NominalFrequencySourceV9.IbgePublishedMarginal
            && expectedUnit != NominalFrequencyUnitV9.FirstName)
            throw new InvalidOperationException(
                "Marginal IBGE de prenome somente é compatível com FirstName; sobrenome exige fonte proxy explícita.");

        if (reference.Source == NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy
            && expectedUnit != NominalFrequencyUnitV9.LastContentSurname)
            throw new InvalidOperationException(
                "Proxy IBGE de sobrenome em qualquer posição só é compatível com LastContentSurname.");

        if (string.IsNullOrWhiteSpace(reference.PopulationFrameFingerprint)
            || string.IsNullOrWhiteSpace(reference.BlockingFrameFingerprint)
            || reference.IndependentObservedSupport < minimumIndependentSupport
            || !reference.IndependentPopulationValidationApproved)
            throw new InvalidOperationException(
                "TF V9 exige referência com frame, blocking, suporte independente e validação populacional aprovada.");
    }
}

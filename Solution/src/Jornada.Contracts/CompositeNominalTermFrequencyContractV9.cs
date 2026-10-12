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

/// <summary>
/// Explicit synthetic evidence provenance for the canonical 30,000-person
/// IBGE bootstrap. This is declarative metadata, not empirical validation.
/// Hypotheses, generator, seed and corpus identity must remain traceable.
/// </summary>
public sealed record NominalSyntheticIbgeBootstrapV9(
    string Provenance,
    int People,
    string CorpusFingerprint,
    string GeneratorVersion,
    ulong GeneratorSeed,
    string GenerationHypothesesFingerprint);

public sealed record NominalFrequencyReferenceContractV9(
    NominalFrequencyUnitV9 Unit,
    NominalFrequencySourceV9 Source,
    string PopulationFrameFingerprint,
    string BlockingFrameFingerprint,
    long IndependentObservedSupport,
    bool IndependentPopulationValidationApproved,
    NominalSyntheticIbgeBootstrapV9? SyntheticBootstrap = null);

/// <summary>
/// Fail-closed guard against the V8 error of applying marginal first-name
/// frequencies to full-name EXACT agreement. It does NOT estimate f(value),
/// m/u, LLR, calibrated TF weight or model activation authority.
/// </summary>
public static class CompositeNominalTermFrequencyContractV9
{
    public const string Version = "V9_NOMINAL_TF_UNIT_GUARD_V3";
    public const string SyntheticIbgeProvenance = "SINTETICO_IBGE";
    public const int CanonicalBootstrapPeople = 30_000;

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
    /// Gates the *eligibility of a reference contract only*; the canonical
    /// SINTETICO_IBGE bootstrap uses a versioned 30k synthetic corpus and
    /// does not require real observed support or population validation.
    /// Real observed references still need independent support/validation.
    /// Declared metadata does NOT prove either source's authenticity.
    /// No frequency or correction is returned. Missing levels are TF-neutral.
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
                "Marginais publicadas do IBGE de prenome somente são compatíveis com FirstName; sobrenome exige fonte proxy explícita.");

        if (reference.Source == NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy
            && expectedUnit != NominalFrequencyUnitV9.LastContentSurname)
            throw new InvalidOperationException(
                "Proxy IBGE de sobrenome em qualquer posição só é compatível com LastContentSurname.");

        // Frame/projection identity is required for either provenance path;
        // it is a traceable declaration, not a certificate of representativity.
        if (string.IsNullOrWhiteSpace(reference.PopulationFrameFingerprint)
            || string.IsNullOrWhiteSpace(reference.BlockingFrameFingerprint))
            throw new InvalidOperationException(
                "TF V9 exige fingerprints de frame e blocking para qualquer referência.");

        if (reference.Source is NominalFrequencySourceV9.IbgePublishedMarginal
            or NominalFrequencySourceV9.IbgeSurnameAnyPositionProxy)
        {
            // DC-LK-TF / canonical 09-10 bootstrap: the 30k synthetic
            // reference has known simulated identities, not real-person
            // independent observations. Do not demand fake observed support
            // or fake population approval just to admit its initial proxy.
            var bootstrap = reference.SyntheticBootstrap;
            if (bootstrap is null
                || !string.Equals(bootstrap.Provenance, SyntheticIbgeProvenance,
                    StringComparison.Ordinal)
                || bootstrap.People != CanonicalBootstrapPeople
                || string.IsNullOrWhiteSpace(bootstrap.CorpusFingerprint)
                || string.IsNullOrWhiteSpace(bootstrap.GeneratorVersion)
                || string.IsNullOrWhiteSpace(bootstrap.GenerationHypothesesFingerprint)
                || reference.IndependentObservedSupport != 0
                || reference.IndependentPopulationValidationApproved)
                throw new InvalidOperationException(
                    "TF V9 bootstrap exige SINTETICO_IBGE de 30.000 pessoas, gerador, seed, " +
                    "hipóteses, corpus e proveniência; sem declarar evidência real fictícia.");
            return;
        }

        if (reference.SyntheticBootstrap is not null
            || reference.IndependentObservedSupport < minimumIndependentSupport
            || !reference.IndependentPopulationValidationApproved)
            throw new InvalidOperationException(
                "TF V9 fonte observada exige suporte real independente e validação populacional; " +
                "não aceita proveniência sintética.");

    }
}

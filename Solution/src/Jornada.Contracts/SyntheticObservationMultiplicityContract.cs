namespace Jornada.Contracts;

/// <summary>
/// Population support of the base corpus, declared independently of measured
/// linkage outcomes. Ingestion waves and arbitrary imported corpora are not covered.
/// This contract makes no claim about independence of generated identities or decisions.
/// </summary>
public static class SyntheticObservationMultiplicityContract
{
    public const string Version = "SYNTHETIC_BASE_OBSERVATION_MULTIPLICITY_V1";
    public const string GeneratorVersion = "JORNADA_SYNTH_CORPUS_CSHARP_V1";
    public const int MaximumObservationsPerBaseIdentity = 4;
    public static IReadOnlyList<int> Counts { get; } = Array.AsReadOnly(new[] { 1, 2, 3, 4 });
    public static IReadOnlyList<ulong> Weights { get; } = Array.AsReadOnly(new ulong[] { 40, 34, 18, 8 });
}

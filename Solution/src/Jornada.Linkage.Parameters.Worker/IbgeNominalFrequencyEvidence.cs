namespace Jornada.Linkage.Parameters.Worker;

public enum NominalFrequencyEvidenceKind
{
    Observed,
    CensoredUpperBound,
    Unavailable
}

public sealed record NominalFrequencyEvidence(
    NominalFrequencyEvidenceKind Kind,
    long? Occurrences,
    long? UpperBoundOccurrences,
    long PopulationSize,
    decimal? Frequency,
    string SourceVersion,
    string SourceFingerprint);

public static class IbgeNominalFrequencyEvidence
{
    /// <summary>
    /// Converte ocorrência IBGE em frequência populacional sem inventar a cauda.
    /// Para valor ausente, o upper bound só é usado quando o chamador afirma que a
    /// semântica/cobertura do snapshot permite interpretar ausência como censura.
    /// </summary>
    public static NominalFrequencyEvidence Resolve(
        IbgeTypedNameFrequencySnapshot snapshot,
        IbgeNameStatisticKind statisticKind,
        string value,
        long populationSize,
        bool absenceMeansCensored = false,
        long? censoredUpperBoundOccurrences = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(populationSize);

        if (snapshot.TryGetOccurrences(statisticKind, value, out var occurrences))
        {
            return new NominalFrequencyEvidence(
                NominalFrequencyEvidenceKind.Observed,
                occurrences,
                null,
                populationSize,
                (decimal)occurrences / populationSize,
                snapshot.SourceVersion,
                snapshot.FingerprintSha256);
        }

        if (absenceMeansCensored)
        {
            var upperBound = censoredUpperBoundOccurrences
                ?? throw new ArgumentNullException(nameof(censoredUpperBoundOccurrences));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(upperBound);

            return new NominalFrequencyEvidence(
                NominalFrequencyEvidenceKind.CensoredUpperBound,
                null,
                upperBound,
                populationSize,
                (decimal)upperBound / populationSize,
                snapshot.SourceVersion,
                snapshot.FingerprintSha256);
        }

        return new NominalFrequencyEvidence(
            NominalFrequencyEvidenceKind.Unavailable,
            null,
            null,
            populationSize,
            null,
            snapshot.SourceVersion,
            snapshot.FingerprintSha256);
    }
}

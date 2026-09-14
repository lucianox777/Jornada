namespace Jornada.Linkage.Parameters.Worker;

public enum GroundTruthSource
{
    Cpf,
    Cns
}

public enum GroundTruthPopulationStratum
{
    WithCpf,
    WithoutCpfWithCns,
    WithoutCpfWithoutCns
}

public enum LinkagePipelineRole
{
    IdentityAnchor,
    LabelSource,
    CandidateGeneration,
    ScoringEvidence
}

public sealed record GroundTruthEligibility(
    bool StructurallyValid,
    int DistinctPersonsObserved,
    int MaximumDistinctPersonsAllowed,
    int BirthDateDistanceDays,
    int MaximumBirthDateDistanceDays)
{
    public bool IsEligible =>
        StructurallyValid &&
        DistinctPersonsObserved > 0 &&
        MaximumDistinctPersonsAllowed > 0 &&
        DistinctPersonsObserved <= MaximumDistinctPersonsAllowed &&
        BirthDateDistanceDays >= 0 &&
        MaximumBirthDateDistanceDays >= 0 &&
        BirthDateDistanceDays <= MaximumBirthDateDistanceDays;
}

public sealed record GroundTruthCoverageDiagnostics(
    GroundTruthSource Source,
    GroundTruthPopulationStratum Stratum,
    long EligiblePopulation,
    long TargetPopulation,
    long PositivePairCount,
    bool StatisticallySufficient,
    bool RepresentativeForTargetStratum)
{
    public decimal Coverage => TargetPopulation <= 0
        ? 0m
        : Math.Clamp((decimal)EligiblePopulation / TargetPopulation, 0m, 1m);

    public bool CanBePreferredForCalibration =>
        PositivePairCount > 0 &&
        StatisticallySufficient &&
        RepresentativeForTargetStratum;
}

/// <summary>
/// Regras metodológicas para uso de identificadores como ground truth do Calibrador.
/// CPF permanece a âncora de identidade da Jornada. CNS pode rotular pares positivos
/// de alta confiança no estrato sem CPF, mas não cria/funde UUID e não participa da
/// decisão que pretende avaliar.
/// </summary>
public static class GroundTruthIsolationPolicy
{
    public static GroundTruthSource SelectPreferredLabelSource(
        GroundTruthCoverageDiagnostics cpf,
        GroundTruthCoverageDiagnostics? cns)
    {
        ArgumentNullException.ThrowIfNull(cpf);

        if (cpf.Source != GroundTruthSource.Cpf)
            throw new ArgumentException("O diagnóstico primário deve ser CPF.", nameof(cpf));

        if (cpf.CanBePreferredForCalibration)
            return GroundTruthSource.Cpf;

        if (cns is not null &&
            cns.Source == GroundTruthSource.Cns &&
            cns.Stratum == GroundTruthPopulationStratum.WithoutCpfWithCns &&
            cns.CanBePreferredForCalibration)
            return GroundTruthSource.Cns;

        return GroundTruthSource.Cpf;
    }

    public static void EnsureNoLabelLeakage(
        GroundTruthSource labelSource,
        IEnumerable<string> candidateGenerationInputs,
        IEnumerable<string> scoringInputs,
        IEnumerable<string>? derivedInputs = null)
    {
        ArgumentNullException.ThrowIfNull(candidateGenerationInputs);
        ArgumentNullException.ThrowIfNull(scoringInputs);

        var forbiddenToken = labelSource.ToString().ToUpperInvariant();
        var allInputs = candidateGenerationInputs
            .Concat(scoringInputs)
            .Concat(derivedInputs ?? Array.Empty<string>());

        var leaking = allInputs
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => value.Contains(forbiddenToken, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (leaking.Length > 0)
            throw new InvalidOperationException(
                $"Label leakage detectado para {labelSource}: {string.Join(", ", leaking)}. " +
                "A fonte do rótulo e qualquer informação derivada dela devem ficar fora do blocking e do score.");
    }

    public static bool CanActAsIdentityAnchor(GroundTruthSource source) =>
        source == GroundTruthSource.Cpf;
}

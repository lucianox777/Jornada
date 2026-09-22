namespace Jornada.Linkage.SyntheticCorpus;

public sealed record SyntheticCorpusOptions(
    int People = 20_000,
    ulong Seed = 42,
    string ErrorProfile = "correlated",
    long MinFrequency = 20,
    double TailOversample = 1.0,
    double CpfBasePrevalence = .22,
    double CnsBasePrevalence = .55,
    double CpfObservationRetention = .55,
    double CnsObservationRetention = .70,
    double CnsInvalidRate = .01,
    double CnsReuseRate = .01,
    double CnsDobConflictRate = .01,
    int Gestores = 4,
    SyntheticBrazilianNameErrorConfig? BrazilianNameErrors = null)
{
    public void Validate()
    {
        if (People <= 0)
            throw new ArgumentOutOfRangeException(nameof(People));
        if (!SyntheticCorpusV2Rules.Profiles.ContainsKey(ErrorProfile))
            throw new ArgumentException($"Perfil de erro desconhecido: {ErrorProfile}.", nameof(ErrorProfile));
        if (MinFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinFrequency));
        if (TailOversample <= 0 || double.IsNaN(TailOversample) || double.IsInfinity(TailOversample))
            throw new ArgumentOutOfRangeException(nameof(TailOversample));
        ValidateProbability(CpfBasePrevalence, nameof(CpfBasePrevalence));
        ValidateProbability(CnsBasePrevalence, nameof(CnsBasePrevalence));
        ValidateProbability(CpfObservationRetention, nameof(CpfObservationRetention));
        ValidateProbability(CnsObservationRetention, nameof(CnsObservationRetention));
        ValidateProbability(CnsInvalidRate, nameof(CnsInvalidRate));
        ValidateProbability(CnsReuseRate, nameof(CnsReuseRate));
        ValidateProbability(CnsDobConflictRate, nameof(CnsDobConflictRate));
        if (Gestores <= 0)
            throw new ArgumentOutOfRangeException(nameof(Gestores));
        BrazilianNameErrors?.Validate(Gestores);
    }

    private static void ValidateProbability(double value, string name)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name, "Probabilidade deve estar em [0,1].");
    }
}

public sealed class SyntheticPerson
{
    public required string BasePersonId { get; init; }
    public required string Partition { get; set; }
    public required string Name { get; init; }
    public required string MotherName { get; init; }
    public required DateOnly BirthDate { get; init; }
    public required string Sex { get; init; }
    public string? Cpf { get; init; }
    public string? Cns { get; set; }
    public string? CnsScenario { get; set; }
    public required double EvaluationWeight { get; init; }
}

public sealed class SyntheticObservation
{
    public required string ObservationId { get; init; }
    public required string BasePersonId { get; init; }
    public required string Partition { get; init; }
    public required string Gestor { get; init; }
    public string? Name { get; set; }
    public string? MotherName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public required string Sex { get; init; }
    public string? Cpf { get; set; }
    public string? Cns { get; set; }
    public required double EvaluationWeight { get; init; }
    public required string Corruptions { get; set; }
}

public sealed record SyntheticEmpiricalM(
    long EligiblePairs,
    long ExactPairs,
    double? MExactEmpirical,
    double? MExactEmpiricalReweighted);

public sealed record SyntheticCorpusGeneration(
    IReadOnlyList<SyntheticPerson> People,
    IReadOnlyList<SyntheticObservation> Observations,
    IReadOnlyDictionary<string, SyntheticEmpiricalM> EmpiricalMExact,
    SyntheticCorpusOptions Options);

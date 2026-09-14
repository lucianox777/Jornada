namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Fotografia dos três estratos relevantes para ground truth. O terceiro estrato é
/// deliberadamente mantido no plano mesmo sem fonte de rótulo elegível: sua presença
/// torna explícita a parcela da população que CPF/CNS não conseguem representar.
/// </summary>
public sealed record GroundTruthPopulationSnapshot(
    long WithCpf,
    long WithoutCpfWithCns,
    long WithoutCpfWithoutCns)
{
    public long TotalPopulation => checked(WithCpf + WithoutCpfWithCns + WithoutCpfWithoutCns);

    public void Validate()
    {
        if (WithCpf < 0 || WithoutCpfWithCns < 0 || WithoutCpfWithoutCns < 0)
            throw new ArgumentOutOfRangeException(nameof(WithCpf), "Populações dos estratos não podem ser negativas.");
    }
}

public sealed record GroundTruthSampleAllocation(
    GroundTruthPopulationStratum Stratum,
    GroundTruthSource? LabelSource,
    long TargetPopulation,
    int RequestedSample,
    decimal PopulationShare)
{
    public bool HasIndependentLabelSource => LabelSource is not null;
}

public sealed record GroundTruthStratifiedSamplePlan(
    int RequestedBudget,
    int AllocatedBudget,
    IReadOnlyList<GroundTruthSampleAllocation> Allocations)
{
    public GroundTruthSampleAllocation For(GroundTruthPopulationStratum stratum) =>
        Allocations.Single(allocation => allocation.Stratum == stratum);
}

/// <summary>
/// Planeja a coleta de ground truth sem transformar um número operacional de pares em
/// critério de suficiência estatística. O orçamento é informado externamente e distribuído
/// proporcionalmente entre os estratos que possuem fonte independente de rótulo.
/// </summary>
public static class GroundTruthStratifiedSamplePlanner
{
    public static GroundTruthStratifiedSamplePlan Plan(
        GroundTruthPopulationSnapshot population,
        int requestedBudget)
    {
        ArgumentNullException.ThrowIfNull(population);
        population.Validate();

        if (requestedBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedBudget), "O orçamento amostral deve ser positivo.");

        var labelable = new[]
        {
            new LabelableStratum(GroundTruthPopulationStratum.WithCpf, GroundTruthSource.Cpf, population.WithCpf),
            new LabelableStratum(GroundTruthPopulationStratum.WithoutCpfWithCns, GroundTruthSource.Cns, population.WithoutCpfWithCns)
        };

        var labelablePopulation = checked(labelable.Sum(static item => item.Population));
        var effectiveBudget = (int)Math.Min((long)requestedBudget, labelablePopulation);
        var allocations = Allocate(labelable, effectiveBudget, labelablePopulation);
        var totalPopulation = population.TotalPopulation;

        var result = new List<GroundTruthSampleAllocation>(3);
        foreach (var item in labelable)
        {
            result.Add(new GroundTruthSampleAllocation(
                item.Stratum,
                item.Source,
                item.Population,
                allocations[item.Stratum],
                Share(item.Population, totalPopulation)));
        }

        result.Add(new GroundTruthSampleAllocation(
            GroundTruthPopulationStratum.WithoutCpfWithoutCns,
            null,
            population.WithoutCpfWithoutCns,
            0,
            Share(population.WithoutCpfWithoutCns, totalPopulation)));

        return new GroundTruthStratifiedSamplePlan(
            requestedBudget,
            result.Sum(static allocation => allocation.RequestedSample),
            result);
    }

    private static Dictionary<GroundTruthPopulationStratum, int> Allocate(
        IReadOnlyList<LabelableStratum> strata,
        int effectiveBudget,
        long labelablePopulation)
    {
        var allocation = strata.ToDictionary(static item => item.Stratum, static _ => 0);
        if (effectiveBudget == 0 || labelablePopulation == 0)
            return allocation;

        var ranked = strata
            .Select(item =>
            {
                var exact = (decimal)effectiveBudget * item.Population / labelablePopulation;
                var floor = (int)Math.Floor(exact);
                return new AllocationRemainder(item, floor, exact - floor);
            })
            .ToArray();

        foreach (var item in ranked)
            allocation[item.Stratum.Stratum] = item.Floor;

        var remaining = effectiveBudget - allocation.Values.Sum();
        foreach (var item in ranked
                     .OrderByDescending(static item => item.Remainder)
                     .ThenBy(static item => item.Stratum.Stratum))
        {
            if (remaining == 0)
                break;

            var current = allocation[item.Stratum.Stratum];
            if ((long)current >= item.Stratum.Population)
                continue;

            allocation[item.Stratum.Stratum] = current + 1;
            remaining--;
        }

        return allocation;
    }

    private static decimal Share(long population, long totalPopulation) =>
        totalPopulation <= 0 ? 0m : (decimal)population / totalPopulation;

    private sealed record LabelableStratum(
        GroundTruthPopulationStratum Stratum,
        GroundTruthSource Source,
        long Population);

    private sealed record AllocationRemainder(
        LabelableStratum Stratum,
        int Floor,
        decimal Remainder);
}

/// <summary>
/// Resultado de uma avaliação estatística externa/versionada. O contrato carrega a decisão,
/// mas não a deduz de um limiar operacional como MinimumIndependentMatchedPairs.
/// </summary>
public sealed record GroundTruthStatisticalAssessment(
    GroundTruthSource Source,
    GroundTruthPopulationStratum Stratum,
    string Method,
    string MethodVersion,
    long AssessedPositivePairCount,
    bool StatisticallySufficient,
    bool RepresentativeForTargetStratum)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Method))
            throw new ArgumentException("Método estatístico é obrigatório.", nameof(Method));
        if (string.IsNullOrWhiteSpace(MethodVersion))
            throw new ArgumentException("Versão do método estatístico é obrigatória.", nameof(MethodVersion));
        if (AssessedPositivePairCount < 0)
            throw new ArgumentOutOfRangeException(nameof(AssessedPositivePairCount));

        if (Source == GroundTruthSource.Cpf && Stratum != GroundTruthPopulationStratum.WithCpf)
            throw new InvalidOperationException("Ground truth CPF deve avaliar o estrato WithCpf.");
        if (Source == GroundTruthSource.Cns && Stratum != GroundTruthPopulationStratum.WithoutCpfWithCns)
            throw new InvalidOperationException("Ground truth CNS deve avaliar o estrato WithoutCpfWithCns.");
    }
}

public static class GroundTruthCoverageDiagnosticsBuilder
{
    public static GroundTruthCoverageDiagnostics Build(
        GroundTruthSource source,
        GroundTruthPopulationStratum stratum,
        long eligiblePopulation,
        long targetPopulation,
        long positivePairCount,
        GroundTruthStatisticalAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        assessment.Validate();

        ArgumentOutOfRangeException.ThrowIfNegative(eligiblePopulation);
        ArgumentOutOfRangeException.ThrowIfNegative(targetPopulation);
        ArgumentOutOfRangeException.ThrowIfNegative(positivePairCount);
        if (eligiblePopulation > targetPopulation)
            throw new InvalidOperationException("População elegível não pode exceder a população-alvo.");
        if (assessment.Source != source || assessment.Stratum != stratum)
            throw new InvalidOperationException("Avaliação estatística não corresponde à fonte/estrato do diagnóstico.");
        if (assessment.AssessedPositivePairCount != positivePairCount)
            throw new InvalidOperationException("Avaliação estatística foi produzida para outra contagem de pares positivos.");

        return new GroundTruthCoverageDiagnostics(
            source,
            stratum,
            eligiblePopulation,
            targetPopulation,
            positivePairCount,
            assessment.StatisticallySufficient,
            assessment.RepresentativeForTargetStratum);
    }
}

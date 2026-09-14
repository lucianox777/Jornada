namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Resultado observado da coleta de ground truth em um estrato. O contrato separa
/// população elegível, quantidade efetivamente amostrada e pares positivos produzidos.
/// Nenhum desses números, isoladamente, implica suficiência estatística.
/// </summary>
public sealed record GroundTruthStratumObservation(
    GroundTruthPopulationStratum Stratum,
    GroundTruthSource? LabelSource,
    long EligiblePopulation,
    int SampledRecords,
    long PositivePairCount)
{
    public void Validate(GroundTruthSampleAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        if (allocation.Stratum != Stratum)
            throw new InvalidOperationException("Observação e alocação pertencem a estratos diferentes.");
        if (allocation.LabelSource != LabelSource)
            throw new InvalidOperationException("Fonte de rótulo observada não corresponde à fonte planejada para o estrato.");

        ArgumentOutOfRangeException.ThrowIfNegative(EligiblePopulation);
        ArgumentOutOfRangeException.ThrowIfNegative(SampledRecords);
        ArgumentOutOfRangeException.ThrowIfNegative(PositivePairCount);

        if (EligiblePopulation > allocation.TargetPopulation)
            throw new InvalidOperationException("População elegível observada não pode exceder a população-alvo do estrato.");
        if (SampledRecords > allocation.RequestedSample)
            throw new InvalidOperationException("Quantidade amostrada não pode exceder a alocação planejada.");
        if (LabelSource is null && (EligiblePopulation != 0 || SampledRecords != 0 || PositivePairCount != 0))
            throw new InvalidOperationException("Estrato sem fonte independente de rótulo não pode produzir observações rotuladas.");
    }
}

public sealed record GroundTruthStratumDiagnostic(
    GroundTruthSampleAllocation Allocation,
    GroundTruthStratumObservation Observation,
    GroundTruthCoverageDiagnostics? Coverage)
{
    public bool IsLabelable => Allocation.HasIndependentLabelSource;
}

/// <summary>
/// Evidência de uma execução de diagnóstico. A parcela não representada é sempre
/// preservada explicitamente para impedir que sucesso em CPF/CNS seja interpretado
/// como representatividade automática de toda a população sem CPF.
/// </summary>
public sealed record GroundTruthDiagnosticRun(
    GroundTruthPopulationSnapshot Population,
    GroundTruthStratifiedSamplePlan SamplePlan,
    IReadOnlyList<GroundTruthStratumDiagnostic> Strata)
{
    public GroundTruthStratumDiagnostic For(GroundTruthPopulationStratum stratum) =>
        Strata.Single(item => item.Allocation.Stratum == stratum);

    public decimal UnrepresentedPopulationShare =>
        For(GroundTruthPopulationStratum.WithoutCpfWithoutCns).Allocation.PopulationShare;

    public IReadOnlyList<GroundTruthCoverageDiagnostics> LabelableDiagnostics =>
        Strata.Where(static item => item.Coverage is not null)
            .Select(static item => item.Coverage!)
            .ToArray();
}

public static class GroundTruthDiagnosticRunBuilder
{
    public static GroundTruthDiagnosticRun Build(
        GroundTruthPopulationSnapshot population,
        GroundTruthStratifiedSamplePlan samplePlan,
        IEnumerable<GroundTruthStratumObservation> observations,
        IEnumerable<GroundTruthStatisticalAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(samplePlan);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(assessments);
        population.Validate();

        var observationByStratum = observations.ToDictionary(static item => item.Stratum);
        var assessmentByStratum = assessments.ToDictionary(static item => item.Stratum);
        var diagnostics = new List<GroundTruthStratumDiagnostic>(samplePlan.Allocations.Count);

        foreach (var allocation in samplePlan.Allocations)
        {
            if (!observationByStratum.TryGetValue(allocation.Stratum, out var observation))
                throw new InvalidOperationException($"Falta observação para o estrato {allocation.Stratum}.");

            observation.Validate(allocation);

            GroundTruthCoverageDiagnostics? coverage = null;
            if (allocation.HasIndependentLabelSource)
            {
                if (!assessmentByStratum.TryGetValue(allocation.Stratum, out var assessment))
                    throw new InvalidOperationException($"Falta avaliação estatística para o estrato rotulável {allocation.Stratum}.");

                coverage = GroundTruthCoverageDiagnosticsBuilder.Build(
                    allocation.LabelSource!.Value,
                    allocation.Stratum,
                    observation.EligiblePopulation,
                    allocation.TargetPopulation,
                    observation.PositivePairCount,
                    assessment);
            }
            else if (assessmentByStratum.ContainsKey(allocation.Stratum))
            {
                throw new InvalidOperationException($"Estrato {allocation.Stratum} não possui fonte independente de rótulo e não pode receber assessment de ground truth.");
            }

            diagnostics.Add(new GroundTruthStratumDiagnostic(allocation, observation, coverage));
        }

        var unexpectedObservation = observationByStratum.Keys.Except(samplePlan.Allocations.Select(static item => item.Stratum)).ToArray();
        if (unexpectedObservation.Length > 0)
            throw new InvalidOperationException($"Observações para estratos não planejados: {string.Join(", ", unexpectedObservation)}.");

        var unexpectedAssessment = assessmentByStratum.Keys.Except(samplePlan.Allocations.Select(static item => item.Stratum)).ToArray();
        if (unexpectedAssessment.Length > 0)
            throw new InvalidOperationException($"Assessments para estratos não planejados: {string.Join(", ", unexpectedAssessment)}.");

        return new GroundTruthDiagnosticRun(population, samplePlan, diagnostics);
    }
}

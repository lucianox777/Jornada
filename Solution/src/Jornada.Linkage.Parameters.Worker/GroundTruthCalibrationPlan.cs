namespace Jornada.Linkage.Parameters.Worker;

public sealed record GroundTruthCalibrationPlan(
    GroundTruthSource LabelSource,
    GroundTruthPopulationStratum PopulationStratum,
    IReadOnlyList<string> CandidateGenerationInputs,
    IReadOnlyList<string> ScoringInputs,
    IReadOnlyList<string> DerivedInputs,
    GroundTruthCoverageDiagnostics Diagnostics)
{
    public void Validate()
    {
        if (!Diagnostics.CanBePreferredForCalibration)
            throw new InvalidOperationException(
                $"Ground truth {LabelSource} não é simultaneamente suficiente e representativo para {PopulationStratum}.");

        if (Diagnostics.Source != LabelSource || Diagnostics.Stratum != PopulationStratum)
            throw new InvalidOperationException("Diagnóstico de ground truth não corresponde ao plano de calibração.");

        GroundTruthIsolationPolicy.EnsureNoLabelLeakage(
            LabelSource,
            CandidateGenerationInputs,
            ScoringInputs,
            DerivedInputs);
    }
}

public static class GroundTruthCalibrationPlanner
{
    public static GroundTruthCalibrationPlan Create(
        GroundTruthCoverageDiagnostics cpf,
        GroundTruthCoverageDiagnostics? cns,
        IEnumerable<string> candidateGenerationInputs,
        IEnumerable<string> scoringInputs,
        IEnumerable<string>? derivedInputs = null)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        ArgumentNullException.ThrowIfNull(candidateGenerationInputs);
        ArgumentNullException.ThrowIfNull(scoringInputs);

        var selected = GroundTruthIsolationPolicy.SelectPreferredLabelSource(cpf, cns);
        var diagnostics = selected switch
        {
            GroundTruthSource.Cpf when cpf.CanBePreferredForCalibration => cpf,
            GroundTruthSource.Cns when cns is not null && cns.CanBePreferredForCalibration => cns,
            _ => throw new InvalidOperationException(
                "Nenhuma fonte de ground truth está simultaneamente suficiente e representativa; calibração bloqueada fail-closed.")
        };

        var plan = new GroundTruthCalibrationPlan(
            selected,
            diagnostics.Stratum,
            candidateGenerationInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scoringInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            (derivedInputs ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
        plan.Validate();
        return plan;
    }
}

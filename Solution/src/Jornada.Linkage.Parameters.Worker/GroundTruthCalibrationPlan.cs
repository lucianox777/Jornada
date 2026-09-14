namespace Jornada.Linkage.Parameters.Worker;

public sealed record GroundTruthCalibrationPlan(
    GroundTruthSource LabelSource,
    GroundTruthPopulationStratum PopulationStratum,
    IReadOnlyList<string> CandidateGenerationInputs,
    IReadOnlyList<string> ScoringInputs,
    IReadOnlyList<string> DerivedInputs,
    GroundTruthCoverageDiagnostics Diagnostics)
{
    public IReadOnlyList<GroundTruthFeatureLineage> FeatureLineages { get; init; } =
        Array.Empty<GroundTruthFeatureLineage>();

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

        if (FeatureLineages.Count > 0)
            GroundTruthFeatureLineagePolicy.EnsureNoLabelLeakage(LabelSource, FeatureLineages);
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

        var diagnostics = SelectDiagnostics(cpf, cns);
        var plan = new GroundTruthCalibrationPlan(
            diagnostics.Source,
            diagnostics.Stratum,
            candidateGenerationInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            scoringInputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            (derivedInputs ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
        plan.Validate();
        return plan;
    }

    /// <summary>
    /// Cria um plano de calibração a partir do catálogo real de projeções.
    /// Candidate generation é derivado dos candidatos de blocking do próprio plano e a
    /// linhagem de candidate generation + scoring é validada semanticamente. Uma feature
    /// de scoring ausente do plano é rejeitada fail-closed, pois sua proveniência não pode
    /// ser demonstrada por este contrato.
    /// </summary>
    public static GroundTruthCalibrationPlan CreateFromProjectionPlan(
        GroundTruthCoverageDiagnostics cpf,
        GroundTruthCoverageDiagnostics? cns,
        ResolutionProjectionPlan projectionPlan,
        IEnumerable<string> scoringInputs,
        IEnumerable<string>? derivedInputs = null)
    {
        ArgumentNullException.ThrowIfNull(cpf);
        ArgumentNullException.ThrowIfNull(projectionPlan);
        ArgumentNullException.ThrowIfNull(scoringInputs);

        var scoring = scoringInputs
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var featuresByName = projectionPlan.Features
            .GroupBy(static feature => feature.Feature, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var unknownScoring = scoring
            .Where(input => !featuresByName.ContainsKey(input))
            .OrderBy(static input => input, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownScoring.Length > 0)
            throw new InvalidOperationException(
                "Features de scoring sem linhagem no ResolutionProjectionPlan: " +
                string.Join(", ", unknownScoring) +
                ". A calibração é bloqueada fail-closed.");

        var diagnostics = SelectDiagnostics(cpf, cns);
        var candidateInputs = projectionPlan.BlockingCandidateFeatures;
        var candidateNames = candidateInputs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scoringNames = scoring.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lineages = ResolutionProjectionGroundTruthLineage.All(projectionPlan)
            .Where(lineage => candidateNames.Contains(lineage.FeatureName) || scoringNames.Contains(lineage.FeatureName))
            .ToArray();

        var plan = new GroundTruthCalibrationPlan(
            diagnostics.Source,
            diagnostics.Stratum,
            candidateInputs,
            scoring,
            (derivedInputs ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics)
        {
            FeatureLineages = lineages
        };
        plan.Validate();
        return plan;
    }

    private static GroundTruthCoverageDiagnostics SelectDiagnostics(
        GroundTruthCoverageDiagnostics cpf,
        GroundTruthCoverageDiagnostics? cns)
    {
        var selected = GroundTruthIsolationPolicy.SelectPreferredLabelSource(cpf, cns);
        return selected switch
        {
            GroundTruthSource.Cpf when cpf.CanBePreferredForCalibration => cpf,
            GroundTruthSource.Cns when cns is not null && cns.CanBePreferredForCalibration => cns,
            _ => throw new InvalidOperationException(
                "Nenhuma fonte de ground truth está simultaneamente suficiente e representativa; calibração bloqueada fail-closed.")
        };
    }
}

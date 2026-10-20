namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Risk envelopes over one predeclared, globally corrected source cohort.</summary>
public static class V9RiskBandFrontier
{
    public const string Version = "V9_POST_RESOLVE_RISK_ENVELOPES_V1";
    public sealed record Band(double MaximumFdr, string? BestConditionalOptionId,
        double? ConditionalRecall, double? ConditionalFdrUpper,
        string? BestEligibleOptionId, IReadOnlyList<string> ConditionalExclusionReasons);
    public sealed record Report(string Version, IReadOnlyList<Band> Bands,
        string Scope = "VALIDATION_EXPLORATORY_NOT_CERTIFICATION", bool TestUsed = false,
        bool AutomaticActivationAllowed = false);

    public static Report Evaluate(string partition, bool usesTestLabels,
        IEnumerable<V9BootstrapPareto2D.CandidateOption> options,
        IReadOnlyCollection<double> ceilings, V9BootstrapPareto2D.SelectionPolicy operationalPolicy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ceilings);
        ArgumentNullException.ThrowIfNull(operationalPolicy);
        if (partition != "VALIDATION" || usesTestLabels)
            throw new InvalidDataException("Faixas exigem VALIDATION; TEST não participa.");
        if (ceilings.Count == 0 || ceilings.Any(x => !double.IsFinite(x) || x is <= 0 or > 1)
            || ceilings.Distinct().Count() != ceilings.Count)
            throw new InvalidDataException("Faixas devem ser probabilidades positivas, distintas e pré-declaradas.");
        var candidates = options.ToArray();
        // Reuse reconciliation, counts, provenance, latency and certification gates.
        var evaluated = V9BootstrapPareto2D.Evaluate(candidates, operationalPolicy).Table;
        var hardInvalid = new HashSet<string>(StringComparer.Ordinal)
        {
            "FINGERPRINT_INVALIDO", "METRICAS_INVALIDAS", "CONTAGENS_OU_DENOMINADOR_INVALIDOS",
            "FDR_INDEFINIDO_SEM_ACEITOS", "METRICAS_INCONSISTENTES", "LIMITE_FDR_INCONSISTENTE"
        };
        var bands = ceilings.OrderBy(x => x).Select(ceiling =>
        {
            // Conditional envelope deliberately retains inadequate support/latency
            // as explicit exclusions. It is never presented as an eligible model.
            var numeric = evaluated.Where(x => !x.ExclusionReasons.Any(hardInvalid.Contains)
                && x.Option.IdentityClusterFdrUpper.HasValue
                && x.Option.IdentityClusterFdrUpper.Value <= ceiling)
                .OrderByDescending(x => x.Option.GlobalRecall)
                .ThenBy(x => x.Option.IdentityClusterFdrUpper)
                .ThenBy(x => x.Option.AcceptedFdr)
                .ThenBy(x => x.Option.ConfigurationFingerprintSha256, StringComparer.Ordinal)
                .ThenBy(x => x.Option.OptionId, StringComparer.Ordinal).ToArray();
            var best = numeric.FirstOrDefault();
            var eligible = ceiling <= operationalPolicy.MaximumAcceptedFdrUpper
                ? numeric.FirstOrDefault(x => x.Eligible) : null;
            var reasons = best?.ExclusionReasons.Where(x => x != "LIMITE_FDR_ACIMA_DA_POLITICA").ToList() ?? [];
            if (ceiling > operationalPolicy.MaximumAcceptedFdrUpper)
                reasons.Add("FAIXA_EXPLORATORIA_ACIMA_DO_RISCO_OPERACIONAL");
            if (best is null) reasons.Add("NENHUMA_CONFIGURACAO_NUMERICAMENTE_SUPORTADA_NA_FAIXA");
            return new Band(ceiling, best?.Option.OptionId, best?.Option.GlobalRecall,
                best?.Option.IdentityClusterFdrUpper, eligible?.Option.OptionId,
                Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray()));
        }).ToArray();
        // Bands reuse the SAME simultaneous configuration bounds. They do not
        // refit, revisit TEST, reset multiplicity or expand operational risk.
        return new(Version, Array.AsReadOnly(bands));
    }
}

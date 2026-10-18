namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Identity-cluster descriptive FDR diagnostics. Neither the point estimate
/// nor leave-one-cluster-out sensitivity is a statistical confidence bound.
/// </summary>
public static class V9IdentityClusterFdr
{
    public sealed record IdentityGroup(string BaseIdentityFingerprint, long TrueAccepted, long FalseAccepted);
    public sealed record Summary(int IndependentGroups, long TrueAccepted, long FalseAccepted,
        double? ObservedFdr, double? MaximumLeaveOneGroupOutFdr, bool Certified);

    public static Summary Describe(IEnumerable<IdentityGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var values = groups.ToArray();
        if (values.Length < 2 || values.Any(x => x is null
            || string.IsNullOrWhiteSpace(x.BaseIdentityFingerprint)
            || x.TrueAccepted < 0 || x.FalseAccepted < 0)
            || values.Select(x => x.BaseIdentityFingerprint)
                .Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("São exigidos grupos independentes e únicos de identidade-base.");

        long tp = 0, fp = 0;
        foreach (var group in values)
        {
            tp = checked(tp + group.TrueAccepted);
            fp = checked(fp + group.FalseAccepted);
        }

        static double? Fdr(long falseAccepted, long trueAccepted) =>
            falseAccepted == 0 && trueAccepted == 0 ? null
                : (double)falseAccepted / ((double)falseAccepted + trueAccepted);

        double? maxLeaveOneOut = null;
        foreach (var group in values)
        {
            var candidate = Fdr(fp - group.FalseAccepted, tp - group.TrueAccepted);
            if (candidate.HasValue)
                maxLeaveOneOut = Math.Max(maxLeaveOneOut ?? 0, candidate.Value);
        }
        return new Summary(values.Length, tp, fp, Fdr(fp, tp), maxLeaveOneOut,
            Certified: false);
    }
    /// <summary>
    /// Conservative one-sided confidence bound on population accepted-link FDR,
    /// treating each independently sampled base identity as ONE observation.
    /// Assumptions: independent identically distributed identity clusters,
    /// complete labeling, and a predeclared finite upper cap on accepted links
    /// per identity (including identities with zero accepts). The cap must be
    /// enforced by the measurement design, not inferred from observed maxima.
    /// Hoeffding bounds the means of FP and total accepts separately; the
    /// union bound gives simultaneous coverage at least 1-alpha. This is a
    /// bound on E[FP per identity]/E[accepted per identity], NOT a finite-frame
    /// realized FDP bound and NOT a certification of an operational model.
    /// </summary>
    public sealed record ClusterMeanRatioBound(
        int IndependentGroups, long FalseAccepted, long TotalAccepted,
        double? ObservedFdr, double? UpperFdr,
        double Alpha, int MaximumAcceptedPerIdentity,
        bool DenominatorSupported, bool OperationallyCertified);

    public static ClusterMeanRatioBound UpperBoundForPopulationMeanRatio(
        IEnumerable<IdentityGroup> groups, int maximumAcceptedPerIdentity,
        double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (maximumAcceptedPerIdentity < 1 || !double.IsFinite(alpha)
            || alpha <= 0 || alpha >= 1)
            throw new ArgumentOutOfRangeException(nameof(alpha),
                "Cap positivo pré-declarado e alpha em (0,1) são obrigatórios.");

        var values = groups.ToArray();
        // Describe checks uniqueness, signs and at least two independent groups.
        var descriptive = Describe(values);
        foreach (var group in values)
        {
            if (group.TrueAccepted > maximumAcceptedPerIdentity
                || group.FalseAccepted > maximumAcceptedPerIdentity - group.TrueAccepted)
                throw new InvalidDataException("Grupo excede cap de vínculos aceitos pré-declarado.");
        }

        var n = (double)values.Length;
        var radius = maximumAcceptedPerIdentity
            * Math.Sqrt(Math.Log(2d / alpha) / (2d * n));
        var upperFalseMean = Math.Min(maximumAcceptedPerIdentity,
            descriptive.FalseAccepted / n + radius);
        var lowerAcceptedMean = Math.Max(0d,
            ((double)descriptive.TrueAccepted + descriptive.FalseAccepted) / n - radius);
        var supported = lowerAcceptedMean > 0d;
        var upper = supported ? Math.Min(1d, upperFalseMean / lowerAcceptedMean) : (double?)null;
        return new ClusterMeanRatioBound(values.Length, descriptive.FalseAccepted,
            checked(descriptive.TrueAccepted + descriptive.FalseAccepted),
            descriptive.ObservedFdr, upper, alpha, maximumAcceptedPerIdentity,
            DenominatorSupported: supported, OperationallyCertified: false);
    }

}

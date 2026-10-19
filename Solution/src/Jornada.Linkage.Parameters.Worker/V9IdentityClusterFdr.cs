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

    /// <summary>
    /// Supplementary ONE-SIDED 1-alpha bound on population E[FP]/E[accepts].
    /// Split alpha over (a) EXACT binomial Clopper-Pearson UCB for the
    /// probability that an entire source-identity cluster has >=1 FP,
    /// and (b) Hoeffding LCB on accepted links per independent identity.
    /// The inequality E[FP] <= predeclaredCap * P(any FP in cluster)
    /// yields an upper ratio after dividing by that positive LCB.
    ///
    /// ASSUMPTIONS (NOT certified here): exchangeable IID base-identity
    /// clusters; complete independent truth labels; a hard cap enforced
    /// by the population measurement protocol, not chosen from observed
    /// maximum; a predeclared count of all compared configurations. Candidate
    /// competition may correlate two identities: that violates IID and means
    /// this number is an assumption-CONDITIONAL sensitivity diagnostic only.
    /// No automatic suggestion, VALIDATE, ACTIVATE, TEST or promotion.
    /// </summary>
    public sealed record ClusterAnyErrorCpRatioBound(
        int IndependentGroups,
        int GroupsWithAnyFalseAccepted,
        long TotalAccepted,
        long FalseAccepted,
        double? ObservedFdr,
        double UpperProbabilityOfAnyGroupErrorClopperPearson,
        double LowerMeanAcceptedPerGroupHoeffding,
        double? UpperFdrConditional,
        double Alpha,
        int ComparedConfigurations,
        int PredeclaredMaximumAcceptedPerIdentity,
        bool DenominatorSupported,
        bool IidClusterAssumptionIndependentlyValidated,
        bool OperationallyCertified);

    public static ClusterAnyErrorCpRatioBound
        UpperBoundForPopulationMeanRatioByAnyErrorCp(
            IEnumerable<IdentityGroup> groups,
            int predeclaredMaximumAcceptedPerIdentity,
            int comparedConfigurations,
            double alpha = .05d)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (predeclaredMaximumAcceptedPerIdentity < 1
            || comparedConfigurations < 1
            || !double.IsFinite(alpha) || alpha is <= 0d or >= 1d)
            throw new ArgumentOutOfRangeException(nameof(alpha),
                "Cap positivo, quantidade de opções pré-declarada e alpha em (0,1) exigidos.");

        var values = groups.ToArray();
        var summary = Describe(values); // uniqueness, signs, group count >= 2
        foreach (var group in values)
            if (group.TrueAccepted > predeclaredMaximumAcceptedPerIdentity
                || group.FalseAccepted
                    > predeclaredMaximumAcceptedPerIdentity - group.TrueAccepted)
                throw new InvalidDataException(
                    "Cap por identidade violado; impossível inferir UCB.");

        var n = values.Length;
        var errors = values.Count(x => x.FalseAccepted > 0);
        // Bonferroni across ALL predeclared compared configurations,
        // followed by a second Bonferroni over numerator and denominator.
        var perComponentAlpha = alpha / (2d * comparedConfigurations);
        var cpUpper = ClopperPearsonBinomialUpper(
            n, errors, perComponentAlpha);
        var total = checked(summary.TrueAccepted + summary.FalseAccepted);
        var acceptedLower = Math.Max(0d, (double)total / n
            - predeclaredMaximumAcceptedPerIdentity
                * Math.Sqrt(Math.Log(1d / perComponentAlpha) / (2d * n)));
        var supported = acceptedLower > 0d;
        var ratio = supported
            ? Math.Min(1d,
                predeclaredMaximumAcceptedPerIdentity * cpUpper / acceptedLower)
            : (double?)null;
        return new ClusterAnyErrorCpRatioBound(
            n, errors, total, summary.FalseAccepted, summary.ObservedFdr,
            cpUpper, acceptedLower, ratio, alpha, comparedConfigurations,
            predeclaredMaximumAcceptedPerIdentity,
            DenominatorSupported: supported,
            IidClusterAssumptionIndependentlyValidated: false,
            OperationallyCertified: false);
    }

    // P_p(X<=k) decreases in p. Its equality to alphaComponent gives
    // the exact one-sided Clopper-Pearson UCB for Bernoulli cluster errors.
    // Compute the binomial tail in the log domain to avoid underflow for
    // thousands of identities; no normal or n_eff approximation is used.
    private static double ClopperPearsonBinomialUpper(
        int trials, int errors, double alphaComponent)
    {
        if (errors == trials) return 1d;
        if (errors == 0)
            return 1d - Math.Exp(Math.Log(alphaComponent) / trials);
        double low = 0d, high = 1d;
        for (int i = 0; i < 70; i++)
        {
            var mid = (low + high) / 2d;
            if (BinomialLowerTail(trials, errors, mid) > alphaComponent)
                low = mid;
            else
                high = mid;
        }
        return high;
    }

    private static double BinomialLowerTail(int n, int k, double p)
    {
        if (p <= 0d) return 1d;
        if (p >= 1d) return k == n ? 1d : 0d;
        var logP = Math.Log(p);
        var logOneMinusP = Math.Log(1d - p);
        var logs = new double[k + 1];
        double logCombination = 0d;
        double maximum = double.NegativeInfinity;
        for (var j = 0; j <= k; j++)
        {
            if (j > 0)
                logCombination += Math.Log(n - j + 1d) - Math.Log(j);
            var logTerm = logCombination + j * logP
                + (n - j) * logOneMinusP;
            logs[j] = logTerm;
            maximum = Math.Max(maximum, logTerm);
        }
        if (maximum < -745d) return 0d;
        double sum = 0d;
        foreach (var value in logs)
            sum += Math.Exp(value - maximum);
        return Math.Min(1d, Math.Exp(maximum) * sum);
    }

}

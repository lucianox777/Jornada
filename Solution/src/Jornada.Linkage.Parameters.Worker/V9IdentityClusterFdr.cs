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
}

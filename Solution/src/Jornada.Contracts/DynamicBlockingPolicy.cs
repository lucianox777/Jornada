using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jornada.Contracts;

/// <summary>
/// Immutable, provider-independent snapshot of the blocking rules used by calibration/evaluation.
/// The optimizer may select a different enabled-pass combination only by emitting a new versioned
/// snapshot. External frequency data is provenance, never individual identity truth.
/// </summary>
public sealed record DynamicBlockingPolicy(
    string PolicyVersion,
    string BlockingPlanVersion,
    BirthBlockingPass EnabledPasses,
    bool UseComponents,
    int YearTolerance,
    string NormalizationVersion,
    string OptimizerVersion,
    string? ExternalNameFrequencySource,
    string? ExternalNameFrequencyVersion,
    string? ExternalNameFrequencyFingerprint)
{
    public const string CurrentPolicyVersion = "DYNAMIC_BLOCKING_POLICY_V1_20260909";
    public const string CurrentOptimizerVersion = "BLOCKING_COMBINATION_OPTIMIZER_V1";
    private const BirthBlockingPass AllPasses = BirthBlockingPass.ExactDate |
        BirthBlockingPass.MonthYearWithInitial |
        BirthBlockingPass.DayYearWithInitial |
        BirthBlockingPass.TransposedDayMonth |
        BirthBlockingPass.NeighborYear;

    public static DynamicBlockingPolicy CreateCurrent(bool useComponents, int yearTolerance) => new DynamicBlockingPolicy(
        CurrentPolicyVersion,
        BirthBlockingPlan.Version,
        useComponents ? AllPasses : BirthBlockingPass.ExactDate,
        useComponents,
        yearTolerance,
        IdentityComparison.NormalizationVersion,
        CurrentOptimizerVersion,
        null,
        null,
        null).Validate();

    public DynamicBlockingPolicy Validate()
    {
        if (string.IsNullOrWhiteSpace(PolicyVersion) ||
            BlockingPlanVersion != BirthBlockingPlan.Version ||
            string.IsNullOrWhiteSpace(NormalizationVersion) ||
            string.IsNullOrWhiteSpace(OptimizerVersion))
            throw new ArgumentException("Metadados da política de blocking são obrigatórios e compatíveis com o plano corrente.");
        if (YearTolerance is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(YearTolerance));
        if (EnabledPasses == BirthBlockingPass.None || (((int)EnabledPasses) & ~((int)AllPasses)) != 0)
            throw new ArgumentOutOfRangeException(nameof(EnabledPasses));
        if (!UseComponents && EnabledPasses != BirthBlockingPass.ExactDate)
            throw new ArgumentException("Sem componentes, somente ExactDate pode permanecer habilitado.");
        if (!UseComponents && YearTolerance != 0)
            throw new ArgumentException("Sem componentes, YearTolerance deve ser zero.");
        var hasExternal = ExternalNameFrequencySource is not null ||
                          ExternalNameFrequencyVersion is not null ||
                          ExternalNameFrequencyFingerprint is not null;
        if (hasExternal)
        {
            if (string.IsNullOrWhiteSpace(ExternalNameFrequencySource) ||
                string.IsNullOrWhiteSpace(ExternalNameFrequencyVersion) ||
                ExternalNameFrequencyFingerprint is null ||
                ExternalNameFrequencyFingerprint.Length != 64 ||
                ExternalNameFrequencyFingerprint.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("Proveniência externa deve ser completa e possuir fingerprint SHA-256.");
        }
        return this;
    }

    public BirthBlockingPass Match(BirthBlockingPlan plan, DateOnly date, string? name, string? mother)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.UseComponents != UseComponents || plan.YearTolerance != YearTolerance)
            throw new InvalidOperationException("Plano de blocking diverge da política versionada.");
        return plan.Match(date, name, mother) & EnabledPasses;
    }

    public string FingerprintSha256()
    {
        Validate();
        var canonical = string.Join("|",
            PolicyVersion,
            BlockingPlanVersion,
            ((int)EnabledPasses).ToString(CultureInfo.InvariantCulture),
            UseComponents ? "1" : "0",
            YearTolerance.ToString(CultureInfo.InvariantCulture),
            NormalizationVersion,
            OptimizerVersion,
            ExternalNameFrequencySource ?? string.Empty,
            ExternalNameFrequencyVersion ?? string.Empty,
            ExternalNameFrequencyFingerprint?.ToLowerInvariant() ?? string.Empty) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

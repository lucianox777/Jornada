using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Explicit compute controls; these never relax a statistical budget.</summary>
public sealed record V9PhysicalAdaptiveExecution(
    int MaximumPrefixLength = 0,
    int EndToEndRepetitions = 0,
    int? TotalComparedConfigurations = null,
    bool FsBlindGroupingOnly = false,
    int? MaximumBlindClusterSize = null,
    int MaximumCandidatePairIncidences = V9PhysicalSqlAdaptiveCalibrationProbe.HardPairIncidenceCap,
    int MaximumProjectedSignatures = V9PhysicalSqlAdaptiveCalibrationProbe.HardPairIncidenceCap)
{
    public const string EndToEndProtocol = "V9_FULL_FRAME_SQL_FS_RANK_RESOLVE_LATENCY_V1|"
        + "warm-process;each-run:project-observations,create-temp-tables,bulk-observations,"
        + "bulk-signatures,complete-TRAIN-VALIDATION-SQL-union,read-all-pairs,read-VALIDATION-edges,"
        + "frozen-FS-TF-rank,blind-complete-link-grouping,source-resolve,cluster-metrics;"
        + "excludes:input-file-read,SQL-connection-open,TRAIN-fitting,outcome-verification;"
        + "nearest-rank-P95;at-least-20-runs;not-production-SLA";

    public static string EndToEndProtocolSha256 => Sha(EndToEndProtocol);

    public static string CurrentEnvironmentSha256(string? sqlEnvironment = null) => Sha(string.Join('|',
        Environment.MachineName, RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture, RuntimeInformation.FrameworkDescription,
        Environment.ProcessorCount, sqlEnvironment));

    public void Validate(bool evaluateFinalResolve, bool includeFsBlindGrouping)
    {
        if (MaximumPrefixLength != 0)
            V9CalibrationBlockingProjection.ValidateMaximumPrefixLength(MaximumPrefixLength);
        if (EndToEndRepetitions != 0 && EndToEndRepetitions is < 20 or > 10_000)
            throw new InvalidDataException("P95 ponta a ponta exige 20..10000 execuções reais, ou zero.");
        if (TotalComparedConfigurations is <= 0 || MaximumBlindClusterSize is < 2
            || MaximumCandidatePairIncidences < 1 || MaximumProjectedSignatures < 1
            || ((EndToEndRepetitions > 0 || FsBlindGroupingOnly)
                && (!evaluateFinalResolve || !includeFsBlindGrouping)))
            throw new InvalidDataException("Configuração física exige Resolve FS blind e multiplicidade/cap positivos.");
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed record V9PhysicalFrozenConfiguration(string OptionId, decimal ConflictMargin,
    V9FittedSnapshot Snapshot);

public sealed record V9PhysicalEndToEndMeasurement(string OptionId, string CandidateGrouping,
    decimal ConflictMargin, string ProtocolSha256, string EnvironmentSha256,
    string OutcomeSha256, V9PhysicalSqlCostQuantiles.Summary Quantiles,
    IReadOnlyList<double> SamplesMilliseconds);

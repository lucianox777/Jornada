using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Parameters.Worker;

// Artificial kernel inputs only. Attestation flags below exercise the Pareto
// algorithm and never constitute measured SQL or statistical certification.
const int repetitions = 3;
#if DEBUG
const string buildConfiguration = "Debug";
#else
const string buildConfiguration = "Release";
#endif
var measurements = new List<object>();
_ = V9BootstrapPareto2D.Evaluate(ParetoInputs(32));
_ = V9TrainBlockingCandidateDiscovery.Discover(TrainInputs(32), DiscoveryOptions(32));

foreach (var count in new[] { 500, 2_000, 8_000 })
{
    var inputs = ParetoInputs(count);
    _ = V9BootstrapPareto2D.Evaluate(inputs);
    var runs = new List<RunMeasurement>();
    for (var repeat = 0; repeat < repetitions; repeat++)
        runs.Add(Measure(() =>
        {
            var result = V9BootstrapPareto2D.Evaluate(inputs);
            if (result.Table.Count != count || result.Table.Count(x => x.OnFrontier) != count)
                throw new InvalidOperationException("Artificial tradeoff frontier was not preserved.");
            return Fingerprint(string.Join("|", result.Table.Select(x => x.Option.OptionId)));
        }));
    measurements.Add(new
    {
        Workload = "actual_csharp_pareto_full_tradeoff_frontier_and_table",
        Configurations = count, WorstCaseDominanceComparisons = (long)count * (count - 1),
        AllConfigurationsOnFrontier = true, Runs = runs,
        MedianElapsedMilliseconds = Median(runs.Select(x => x.ElapsedMilliseconds))
    });
}

foreach (var count in new[] { 300, 1_500, 3_000, 30_000 })
{
    var inputs = TrainInputs(count);
    var options = DiscoveryOptions(count);
    _ = V9TrainBlockingCandidateDiscovery.Discover(inputs, options);
    var runs = new List<RunMeasurement>();
    V9TrainBlockingDiscoveryResult? last = null;
    for (var repeat = 0; repeat < repetitions; repeat++)
        runs.Add(Measure(() =>
        {
            last = V9TrainBlockingCandidateDiscovery.Discover(inputs, options);
            if (last.TrainObservationCount != count || last.EnumeratedUnionCount != 136)
                throw new InvalidOperationException("Discovery workload changed unexpectedly.");
            return last.TrainProjectionFingerprintSha256 + ":" + last.SamplePairFingerprintSha256;
        }));
    measurements.Add(new
    {
        Workload = "actual_csharp_train_preblocking_discovery",
        Observations = count, Partition = "TRAIN", options,
        last!.EligiblePositivePairsBeforeBlocking, last.EligibleNonMatchPairsBeforeBlocking,
        last.SampledNonMatchPairs, last.PairSamplingAttempts,
        last.FeatureCount, last.PairFeatureComparisonCount,
        last.EvaluatedPrimitiveCount, last.RetainedPrimitiveCount, last.EnumeratedUnionCount,
        Runs = runs, MedianElapsedMilliseconds = Median(runs.Select(x => x.ElapsedMilliseconds))
    });
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    Version = "V9_CPU_WORKLOAD_BENCHMARK_V1",
    EvidenceScope = "Synthetic CPU kernel timings only; no SQL, TEST, GPU timing, or statistical certification",
    Framework = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    LogicalProcessors = Environment.ProcessorCount,
    BenchmarkConfiguration = buildConfiguration, Repetitions = repetitions, WarmupsPerScale = 1,
    ParetoVersion = V9BootstrapPareto2D.SelectionVersion,
    DiscoveryVersion = V9TrainBlockingCandidateDiscovery.Version,
    ContractsAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
        typeof(Jornada.Contracts.V9CalibrationBlockingProjection).Assembly.Location))).ToLowerInvariant(),
    ParametersAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
        typeof(V9TrainBlockingCandidateDiscovery).Assembly.Location))).ToLowerInvariant(),
    Measurements = measurements
}, BenchmarkJson.Options));

static double Median(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}

static RunMeasurement Measure(Func<string> action)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var allocated = GC.GetTotalAllocatedBytes(true);
    using var process = Process.GetCurrentProcess();
    var cpu = process.TotalProcessorTime;
    var timer = Stopwatch.StartNew();
    var fingerprint = action();
    timer.Stop();
    process.Refresh();
    return new RunMeasurement(timer.Elapsed.TotalMilliseconds,
        (process.TotalProcessorTime - cpu).TotalMilliseconds,
        GC.GetTotalAllocatedBytes(true) - allocated, process.WorkingSet64, fingerprint);
}

static V9BootstrapPareto2D.CandidateOption[] ParetoInputs(int count) =>
    Enumerable.Range(0, count).Select(i =>
    {
        var tp = 10_000L + i;
        var fp = 1L + i;
        return new V9BootstrapPareto2D.CandidateOption(
            i.ToString("D8", CultureInfo.InvariantCulture), Fingerprint("artificial-cpu-point:" + i),
            tp / 1_000_000d, (double)fp / (tp + fp), 1d,
            tp, fp, 1_000_000, 1_000, true, true);
    }).ToArray();

static V9PhysicalCalibrationObservation[] TrainInputs(int count) =>
    Enumerable.Range(0, count).Select(i => new V9PhysicalCalibrationObservation(
        i + 1, "artificial-base-" + i / 3, "TRAIN", "manager-" + i % 3,
        i % 13 == 0 ? "Variant " + i : "Person " + i / 3,
        i % 7 == 0 ? null : "Mother " + i / 3,
        new DateOnly(1950 + i / 3 % 60, 1 + i / 3 % 12, 1 + i / 3 % 28))).ToArray();

static V9TrainBlockingDiscoveryOptions DiscoveryOptions(int count) => new(
    NonMatchSampleSize: count * 3,
    SearchOptions: new BlockingRuleSetSearchOptions(2, 2, 16, CandidateRuleSetCap: 100_000));

static string Fingerprint(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

internal sealed record RunMeasurement(
    double ElapsedMilliseconds, double ProcessCpuMilliseconds,
    long AllocatedBytes, long WorkingSetBytesAfter, string ResultFingerprint);

internal static class BenchmarkJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}

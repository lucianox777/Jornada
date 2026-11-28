using System.Diagnostics;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Linkage.Runner;

internal static partial class V9Phase4Matrix
{
    private enum VetoMode { None, Direct, Origin }

    private sealed record NewPublication(long Leader, Guid Destination, string Method, long[] Versions, long[] SecureRecords);

    private sealed class ComparativeRound(VetoMode mode)
    {
        public List<NewPublication> NewLinks { get; } = [];
        public VetoMode Mode { get; } = mode;
        public HashSet<long> EligibleLeaders { get; } = [];
        public HashSet<long> SuppressedFollowers { get; } = [];
        public Dictionary<long, Guid?> State { get; set; } = [];
    }

    internal static bool MatchesDirectVeto(IdentityObservation source,
        IReadOnlyList<V9ControlledDevSecureEvidenceScore.RecordScore> scores, Func<long, IdentityObservation> observation)
    {
        var maximum = scores.Max(x => x.LogOdds);
        if (maximum is null || source.DataNascimento is null || BrazilianNameComponents.Project(source.NomeMae) is null)
            return false;
        return scores.Where(x => x.LogOdds == maximum).All(x =>
        {
            var record = observation(x.ObservationId);
            return record.DataNascimento.HasValue && record.DataNascimento != source.DataNascimento &&
                CompositeNominalComparisonV9.Classify(source.NomeCompleto, record.NomeCompleto) == CompositeNominalLevelV9.ExactFull &&
                BrazilianNameComponents.Project(record.NomeMae) is null;
        });
    }

    internal static bool ComparativeSingletonMayPublish(int leaderCandidateCount,
        IEnumerable<ProbabilisticLinkageDecision> originResults)
    {
        if (leaderCandidateCount != 0) return false;
        // The installed DT-10 procedure tests persisted rank fields for every
        // version of this origin before it permits the leader's promotion.
        Require(originResults.All(row => row.MelhorCandidatoUuid is null &&
            row.SegundoCandidatoUuid is null && row.PessoaUuidResolvido is null),
            "COMPARATIVE_DT10_51835");
        return true;
    }

    internal static bool SixArmAdmitted =>
        V9DevE2EFullCommand.ScaleAdmitted && V9DevE2EFullCommand.DiagnosticPopulation == 10000 &&
        Environment.GetEnvironmentVariable("JORNADA_V9_SIX_ARM") == "7235636439" &&
        Environment.GetEnvironmentVariable("SCALE_SEED") == "85229684" &&
        Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") == "1";

    internal static async Task CompareSixAsync(string root, V9FittedSnapshot winner, Guid attempt)
    {
        Require(SixArmAdmitted && winner.ContentSha256 == "6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee", "COMPARATIVE_SCOPE");
        using var preflight = Read(root, "wave-preflight-receipt.json");
        Require(preflight.RootElement.GetProperty("seed").GetInt32() == 85229684 &&
            preflight.RootElement.GetProperty("baseIdentities").GetInt32() == 10000, "COMPARATIVE_COHORT");
        using var input = Read(root, "matrix-frame-private.json");
        var frame = input.RootElement.Deserialize<Frame>()!;
        Require(frame.Attempt == attempt && frame.Rows.Length == preflight.RootElement.GetProperty("sourceVersions").GetInt32(), "COMPARATIVE_FRAME");
        using var census = Read(root, "sql-census.json");
        using var diagnostic = Read(root, "fn-private.json");
        using var actual = Read(root, "actual-graph-receipt.json");
        var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
        var firstWave = frame.Rows.Where(row => row.Wave == 1).ToArray();
        var all = Graph(winner, planner, frame.Rows, indexedSilver: true);
        var first = Graph(winner, planner, firstWave, indexedSilver: true);
        var initial = frame.Rows.Where(row => row.HasCpf).GroupBy(row => row.Origin)
            .ToDictionary(group => group.Key, group => new Link(group.First().Current!.Value, true, "CPF"));
        var gold = Index(frame.Rows, initial, planner, false);
        Require(gold.Count == frame.InitialRefs.Length && frame.InitialRefs.All(reference =>
            gold.TryGetValue(reference.Person, out var entry) && entry.Summary == reference), "COMPARATIVE_INITIAL_GOLD");
        var states = 0;
        await using var output = new StreamWriter(new FileStream(Path.Combine(root, "comparison-private.jsonl"), FileMode.CreateNew));
        foreach (var schedule in new[] { "repeat_full", "per_wave" })
        foreach (var s4b in new[] { "i", "ii" })
        {
            // Use the same collective ID namespace for the three policies, with independent mutable state.
            var official = new List<Dictionary<long, Link>>();
            foreach (var mode in new[] { VetoMode.None, VetoMode.Direct, VetoMode.Origin })
            {
                var links = new Dictionary<long, Link>();
                string? previous = null;
                for (var round = 1; round <= 2; round++)
                {
                    var wave = schedule == "per_wave" && round == 1 ? 1 : 2;
                    var visible = wave == 1 ? firstWave : frame.Rows;
                    var graph = wave == 1 ? first : all;
                    var checkBaseline = schedule == "repeat_full" && s4b == "i" && round == 1 && mode == VetoMode.None;
                    var exp = new ComparativeRound(mode);
                    var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                    var result = Round(winner, planner, visible, graph, links, schedule + "/" + round,
                        s4b, "b1", "records", "r1", "broad", checkBaseline ? census.RootElement : null,
                        checkBaseline ? diagnostic.RootElement : null, checkBaseline ? actual.RootElement : null, exp);
                    var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds;
                    if (mode == VetoMode.None)
                        official.Add(new Dictionary<long, Link>(links));
                    if (mode == VetoMode.Direct)
                        Require(links.Count == official[round - 1].Count && links.All(item =>
                            official[round - 1].TryGetValue(item.Key, out var reference) && reference == item.Value),
                            "COMPARATIVE_DIRECT_STATE");
                    var id = $"{schedule}/{s4b}/{mode}/{round}";
                    await output.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        id, previous, schedule, s4b, mode = mode.ToString(), round, wave, checkBaseline,
                        exp.EligibleLeaders, exp.SuppressedFollowers, exp.NewLinks, exp.State, rows = result.Rows,
                        counters = new { candidatePeople = result.Rows.Sum(row => (long)row.Candidates),
                            silverEdges = graph.Edges, silverDirectionsCompared = checked(2L * graph.Edges),
                            goldRecordScores = result.Rows.Sum(row => (long)row.Fs), instanceBatchRequests = (long?)null },
                        resources = result.Resources, cpuMs
                    }));
                    previous = id;
                    states++;
                }
            }
        }
        Require(states == 24, "COMPARATIVE_COVERAGE");
    }
}

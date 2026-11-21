using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Nonpublishing, noncertifying staging collector for run-scoped differential evidence.
/// Not connected to production DI or RunAsync. Inputs must be obtained from actual
/// runtime observations; this class does not authenticate runtime provenance.
/// </summary>
internal sealed class V9DifferentialRunEvidenceCollector
{
    private readonly SortedSet<long> universe;
    private readonly Dictionary<long, string> decisions = new();
    private readonly List<long[]> groups = new();
    private bool sealedEvidence;

    internal V9DifferentialRunEvidenceCollector(IEnumerable<long> frozenObservationIds)
    {
        ArgumentNullException.ThrowIfNull(frozenObservationIds);
        var ids = frozenObservationIds.ToArray();
        if (ids.Length == 0 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Differential universe must be nonempty, positive and unique.");
        universe = new SortedSet<long>(ids);
    }

    internal void RecordDecision(long observationId, string decision)
    {
        if (sealedEvidence || !universe.Contains(observationId) || string.IsNullOrWhiteSpace(decision)
            || !decisions.TryAdd(observationId, decision))
            throw new InvalidDataException("Invalid, duplicate or out-of-universe decision.");
    }

    internal void RecordGroup(IEnumerable<long> observationIds)
    {
        if (sealedEvidence)
            throw new InvalidOperationException("Differential evidence is sealed.");
        ArgumentNullException.ThrowIfNull(observationIds);
        var ids = observationIds.ToArray();
        if (ids.Length == 0 || ids.Any(id => !universe.Contains(id))
            || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("Invalid differential group.");
        groups.Add(ids.OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// Creates only a noncertifying diagnostic receipt. SHA values are required
    /// to be independently verified by the eventual native producer.
    /// </summary>
    internal string SealDiagnosticJson(
        string runtime, string runId, string runtimeArtifactSha256,
        string modelSha256, string universeSha256, string operationalEdgesSha256)
    {
        if (sealedEvidence)
            throw new InvalidOperationException("Differential evidence already sealed.");
        if (runtime is not ("scientific" or "production") || string.IsNullOrWhiteSpace(runId))
            throw new InvalidDataException("Invalid differential runtime or run ID.");
        foreach (var sha in new[] { runtimeArtifactSha256, modelSha256, universeSha256, operationalEdgesSha256 })
        {
            if (sha is null || sha.Length != 64 || sha.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new InvalidDataException("Expected lowercase SHA-256.");
        }
        if (decisions.Count != universe.Count || !universe.All(decisions.ContainsKey))
            throw new InvalidDataException("Incomplete differential decisions.");
        var members = groups.SelectMany(group => group).ToArray();
        if (members.Length != universe.Count || members.Distinct().Count() != universe.Count
            || !universe.SetEquals(members))
            throw new InvalidDataException("Groups must partition the frozen universe.");
        sealedEvidence = true;
        return JsonSerializer.Serialize(new
        {
            schema = "JORNADA_V9_DIFFERENTIAL_RECEIPT_V1",
            certified = false,
            testTruthUsed = false,
            runtime,
            provenance = new { runId, runtimeArtifactSha256 },
            modelSha256,
            universeSha256,
            operationalEdgesSha256,
            decisions = universe.Select(id => new { observationId = id, decision = decisions[id] }).ToArray(),
            groups = groups.OrderBy(g => g[0]).ToArray()
        });
    }
}

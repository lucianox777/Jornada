using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>A candidate is an unlabelled, directed source-to-UUID pair.</summary>
public sealed record CandidateSamplingCandidate(Guid CandidateId, BirthBlockingPass Membership);

/// <summary>
/// Finite-population, two-stage SRS. The caller supplies a complete, attested frame and
/// enumerates the full candidate union for each selected source. This component never
/// infers identity labels, estimates m/u, or writes an operational model.
/// </summary>
public static class CandidateSamplingEngine
{
    public static async Task<CandidateSamplingCapture> CaptureAsync(
        CandidateSamplingFrame frame, CandidateSamplingOptions options, byte[] seed,
        string snapshotToken, DateTimeOffset capturedAt,
        Func<CandidateUniverseSource, CancellationToken, Task<IReadOnlyList<CandidateSamplingCandidate>>> enumerate,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(enumerate);
        options.Validate();
        if (seed.Length != 32) throw new ArgumentException("A chave deve ter 256 bits.", nameof(seed));
        if (!frame.Complete || string.IsNullOrWhiteSpace(frame.Reference) ||
            frame.Sources is null || frame.Sources.Count == 0 || frame.Sources.Count > options.MaxSources ||
            options.SourceSampleSize > frame.Sources.Count || string.IsNullOrWhiteSpace(snapshotToken))
            throw new ArgumentException("Quadro completo, referência, snapshot e limites são obrigatórios.", nameof(frame));
        var sources = frame.Sources.ToArray();
        if (sources.Any(s => s is null || s.SourceId == Guid.Empty || s.Name is null ||
                s.KnownPessoaUuid == Guid.Empty) ||
            sources.Select(s => s.SourceId).Distinct().Count() != sources.Length)
            throw new ArgumentException("Fontes inválidas ou duplicadas.", nameof(frame));
        var ordered = sources.OrderBy(s => s.SourceId.ToString("D"), StringComparer.Ordinal).ToArray();
        var frameHash = Fingerprint(seed, "frame", ordered.Select(s => new
        {
            s.SourceId, s.BirthDate, s.Name, s.Mother, s.KnownPessoaUuid
        }));
        var configuration = Fingerprint(seed, "configuration", new
        {
            CandidateSamplingDesign.Version, BirthBlockingPlan.Version, frame.Reference,
            options.UseComponents, options.YearTolerance, options.SourceSampleSize,
            Quotas = options.PrimaryPassQuotas.ToArray(), options.MaxSources,
            options.MaxCandidatesPerSource, options.MaxEnumeratedPairs, options.MaxSelectedPairs,
            options.CommandTimeoutSeconds
        });
        var selectedSources = CandidateSamplingDesign.Draw(ordered, options.SourceSampleSize, seed,
            "sources", s => s.SourceId.ToString("D"));
        var members = new long[5];
        var primary = new long[5];
        var selected = new long[5];
        var estimatedPrimary = new decimal[5];
        var estimatedMembers = new decimal[5];
        var pairs = new List<CandidateSamplingPair>();
        var universe = new List<object>();
        long enumerated = 0, overlaps = 0;
        var emptySources = 0;
        decimal estimatedTotal = 0;
        foreach (var source in selectedSources)
        {
            ct.ThrowIfCancellationRequested();
            var plan = BirthBlockingPlan.Create(source.BirthDate, source.Name, source.Mother,
                options.UseComponents, options.YearTolerance);
            var candidates = await enumerate(source, ct) ??
                throw new InvalidOperationException("Enumeração de candidatos não retornada.");
            if (candidates.Count > options.MaxCandidatesPerSource ||
                candidates.Count > options.MaxEnumeratedPairs - enumerated)
                throw new InvalidOperationException("Limite de enumeração excedido; captura descartada.");
            var seen = new HashSet<Guid>();
            var groups = new List<CandidateSamplingCandidate>[5];
            for (var i = 0; i < groups.Length; i++) groups[i] = new List<CandidateSamplingCandidate>();
            foreach (var candidate in candidates.OrderBy(c => c.CandidateId.ToString("D"), StringComparer.Ordinal))
            {
                if (candidate is null || candidate.CandidateId == Guid.Empty ||
                    !seen.Add(candidate.CandidateId) || candidate.Membership == BirthBlockingPass.None ||
                    (((int)candidate.Membership) & ~31) != 0 ||
                    (!options.UseComponents && candidate.Membership != BirthBlockingPass.ExactDate))
                    throw new InvalidOperationException("Par inválido, duplicado ou fora do plano V2.");
                var pass = BirthBlockingPlan.PrimaryPass(candidate.Membership);
                var index = Index(pass);
                groups[index].Add(candidate);
                primary[index]++;
                if (System.Numerics.BitOperations.PopCount((uint)candidate.Membership) > 1) overlaps++;
                CountMembership(candidate.Membership, members);
            }
            enumerated += candidates.Count;
            if (candidates.Count == 0) emptySources++;
            universe.Add(new
            {
                source.SourceId, Plan = plan.ConfigurationFingerprint(),
                Pairs = candidates.OrderBy(c => c.CandidateId.ToString("D"), StringComparer.Ordinal)
                    .Select(c => new { c.CandidateId, Mask = (int)c.Membership }).ToArray()
            });
            for (var i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                if (group.Count == 0) continue;
                var n = Math.Min(options.PrimaryPassQuotas[i], group.Count);
                if (n == 0) throw new InvalidOperationException("Estrato não vazio sem probabilidade de inclusão.");
                if (n > options.MaxSelectedPairs - pairs.Count)
                    throw new InvalidOperationException("Limite de pares selecionados excedido; captura descartada.");
                var pass = BirthBlockingPlan.OrderedPasses[i];
                var draw = CandidateSamplingDesign.Draw(group, n, seed,
                    "pairs|" + source.SourceId.ToString("D") + "|" + ((int)pass).ToString(CultureInfo.InvariantCulture),
                    c => c.CandidateId.ToString("D"));
                var (probability, weight) = CandidateSamplingDesign.Inclusion(
                    sources.Length, selectedSources.Count, group.Count, n);
                selected[i] += n;
                foreach (var candidate in draw)
                {
                    pairs.Add(new CandidateSamplingPair(source.SourceId, candidate.CandidateId,
                        candidate.Membership, pass, group.Count, n, probability, weight));
                    estimatedPrimary[i] += weight;
                    estimatedTotal += weight;
                    AddMembership(candidate.Membership, weight, estimatedMembers);
                }
            }
        }
        var orderedPairs = pairs.OrderBy(p => p.SourceId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(p => p.CandidateId.ToString("D"), StringComparer.Ordinal).ToArray();
        var passCounts = BirthBlockingPlan.OrderedPasses.Select((pass, i) =>
            new CandidateSamplingPassCount(pass, members[i], primary[i], selected[i],
                estimatedPrimary[i], estimatedMembers[i])).ToArray();
        return new CandidateSamplingCapture(CandidateSamplingDesign.Version, BirthBlockingPlan.Version,
            frame.Reference, frameHash, CandidateSamplingDesign.Fingerprint(seed, "seed-commitment", "v1"),
            configuration, snapshotToken, capturedAt, Fingerprint(seed, "universe", universe),
            Fingerprint(seed, "selection", orderedPairs), sources.Length, selectedSources.Count,
            emptySources, enumerated, overlaps, estimatedTotal, passCounts, orderedPairs);
    }

    private static int Index(BirthBlockingPass pass)
    {
        for (var i = 0; i < BirthBlockingPlan.OrderedPasses.Count; i++)
            if (BirthBlockingPlan.OrderedPasses[i] == pass) return i;
        throw new InvalidOperationException("Pass primário desconhecido.");
    }

    private static void CountMembership(BirthBlockingPass mask, long[] counts)
    {
        for (var i = 0; i < counts.Length; i++)
            if ((mask & BirthBlockingPlan.OrderedPasses[i]) != 0) counts[i]++;
    }

    private static void AddMembership(BirthBlockingPass mask, decimal weight, decimal[] counts)
    {
        for (var i = 0; i < counts.Length; i++)
            if ((mask & BirthBlockingPlan.OrderedPasses[i]) != 0) counts[i] += weight;
    }

    private static string Fingerprint<T>(byte[] seed, string domain, T value) =>
        CandidateSamplingDesign.Fingerprint(seed, domain, JsonSerializer.Serialize(value));
}

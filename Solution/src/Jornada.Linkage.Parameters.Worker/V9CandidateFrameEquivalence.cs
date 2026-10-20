using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Exact label-blind quotient of the entire subset lattice. Two configurations
/// are equivalent only when they retrieve exactly the same observed SQL edges
/// in BOTH TRAIN and VALIDATION. No score, truth label or approximate hash decides membership.
/// </summary>
public static class V9CandidateFrameEquivalence
{
    public sealed record EquivalenceClass(IReadOnlyList<LinkageBlockingPass> Representative,
        long MemberCount, string MembershipSha256, string EdgeSelectionSha256);
    public sealed record Report(string SearchSpaceSha256, string CatalogueEdgeMasksSha256,
        string ConfigurationsEnumerated, int DistinctObservedEdgeMasks,
        IReadOnlyList<EquivalenceClass> Classes, bool FunctionalCandidateSpaceExhaustive = true,
        bool AllEquivalentPhysicalLatenciesMeasured = false);

    private sealed class Group(IReadOnlyList<LinkageBlockingPass> representative) : IDisposable
    {
        public IReadOnlyList<LinkageBlockingPass> Representative { get; } = representative;
        public long Count { get; set; }
        public IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public void Dispose() => Hash.Dispose();
    }

    public static Report Partition(V9DeclaredBlockingSearchSpace space,
        IEnumerable<BigInteger> physicalEdgeMasks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(physicalEdgeMasks);
        var catalogue = space.CanonicalCatalogue();
        var masks = physicalEdgeMasks.Distinct().OrderBy(x => x).ToArray();
        var completeMask = (BigInteger.One << catalogue.Count) - 1;
        if (masks.Any(x => x <= 0 || (x & completeMask) != x))
            throw new InvalidDataException("Incidência física fora do catálogo declarado.");
        var indices = catalogue.Select((p, i) => (p.PassId, i)).ToDictionary(x => x.PassId, x => x.i, StringComparer.Ordinal);
        var edgeSets = new BigInteger[catalogue.Count];
        for (var pass = 0; pass < catalogue.Count; pass++)
            for (var edge = 0; edge < masks.Length; edge++)
                if ((masks[edge] & (BigInteger.One << pass)) != 0)
                    edgeSets[pass] |= BigInteger.One << edge;
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        long total = 0;
        try
        {
            foreach (var passes in space.Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();
                BigInteger selected = 0;
                BigInteger includedEdges = 0;
                foreach (var pass in passes)
                {
                    var index = indices[pass.PassId];
                    selected |= BigInteger.One << index;
                    includedEdges |= edgeSets[index];
                }
                // Full bit-vector equality, NOT hash equality, establishes equivalence.
                var key = Convert.ToBase64String(includedEdges.ToByteArray(isUnsigned: true));
                if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new(passes));
                group.Count = checked(group.Count + 1);
                group.Hash.AppendData(Encoding.UTF8.GetBytes(selected.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"));
                total = checked(total + 1);
            }
            if (new BigInteger(total) != space.Count())
                throw new InvalidDataException("Enumeração integral não reconciliou com o espaço declarado.");
            var classes = groups.Select(x => new EquivalenceClass(x.Value.Representative, x.Value.Count,
                Convert.ToHexString(x.Value.Hash.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(x.Key))).ToLowerInvariant())).ToArray();
            var maskDigest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', masks.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
            return new(space.FingerprintSha256, Convert.ToHexString(maskDigest).ToLowerInvariant(),
                total.ToString(System.Globalization.CultureInfo.InvariantCulture), masks.Length, Array.AsReadOnly(classes));
        }
        finally { foreach (var group in groups.Values) group.Dispose(); }
    }
}

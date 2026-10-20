using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Explicit whole catalogue, without heuristic primitive retention. Null maximum
/// searches every nonempty subset. Enumeration is lazy; resource failure is not pruning.
/// </summary>
public sealed record V9DeclaredBlockingSearchSpace(IReadOnlyList<LinkageBlockingPass> Catalogue,
    int MinimumPasses = 1, int? MaximumPasses = null)
{
    public IReadOnlyList<LinkageBlockingPass> CanonicalCatalogue()
    {
        if (Catalogue is null || Catalogue.Count == 0 || MinimumPasses < 1
            || MaximumPasses is < 1 || MinimumPasses > (MaximumPasses ?? Catalogue.Count)
            || MinimumPasses > Catalogue.Count)
            throw new InvalidDataException("Catálogo e intervalo de passes inválidos.");
        var canonical = Catalogue.Select(p => p is null ? throw new InvalidDataException("Passe nulo.")
            : LinkageBlockingPass.Create(p.PassId, p.Fields)).OrderBy(p => p.PassId, StringComparer.Ordinal)
            .Select(p => new LinkageBlockingPass(p.PassId, Array.AsReadOnly(p.Fields.ToArray()))).ToArray();
        if (canonical.Select(p => p.PassId).Distinct(StringComparer.Ordinal).Count() != canonical.Length
            || canonical.Select(p => string.Join('|', p.Fields)).Distinct(StringComparer.Ordinal).Count() != canonical.Length)
            throw new InvalidDataException("Catálogo contém passes ou predicados duplicados.");
        return Array.AsReadOnly(canonical);
    }

    public BigInteger Count()
    {
        var n = CanonicalCatalogue().Count;
        BigInteger count = 0, choose = 1;
        for (var width = 1; width <= Math.Min(MaximumPasses ?? n, n); width++)
        {
            choose = choose * (n - width + 1) / width;
            if (width >= MinimumPasses) count += choose;
        }
        return count;
    }

    public string FingerprintSha256 => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { Catalogue = CanonicalCatalogue(), MinimumPasses, MaximumPasses = MaximumPasses ?? Catalogue.Count }))).ToLowerInvariant();

    public IEnumerable<IReadOnlyList<LinkageBlockingPass>> Enumerate()
    {
        // Snapshot before yielding so caller mutation cannot change an in-flight family.
        var rows = CanonicalCatalogue();
        var maximum = Math.Min(MaximumPasses ?? rows.Count, rows.Count);
        return EnumerateFrozen();
        IEnumerable<IReadOnlyList<LinkageBlockingPass>> EnumerateFrozen()
        {
            for (var width = MinimumPasses; width <= maximum; width++)
            {
                var indices = Enumerable.Range(0, width).ToArray();
                while (true)
                {
                    yield return Array.AsReadOnly(indices.Select(i => rows[i]).ToArray());
                    var position = width - 1;
                    while (position >= 0 && indices[position] == rows.Count - width + position) position--;
                    if (position < 0) break;
                    indices[position]++;
                    for (var next = position + 1; next < width; next++) indices[next] = indices[next - 1] + 1;
                }
            }
        }
    }
}

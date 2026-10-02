using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

public sealed record Dt05VerifiedCandidateState(
    Guid SourceRunId, string ManifestLogicalPath, string ManifestSha256,
    string PartitionSetSha256, long RowCount, IReadOnlyList<string> PartitionLogicalPaths);

/// <summary>
/// Verifica a publicação candidate-state histórica antes de qualquer consumo.
/// Esta classe não consulta Gold nem aceita fallback: binding, manifesto e objetos
/// content-addressed precisam concordar byte a byte.
/// </summary>
public sealed class Dt05HistoricalCandidateStateVerifier(string bronzeRoot)
{
    private const string ManifestPrefix = "linkage-snapshots/v1/candidate-state/manifests/";
    private const string ObjectPrefix = "objects/";

    public async Task<Dt05VerifiedCandidateState> VerifyAsync(
        Dt05HistoricalCandidateStateBinding binding, CancellationToken ct)
    {
        var manifestPath = ResolveUnderBronze(binding.ManifestLogicalPath, ManifestPrefix);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, ct);
        RequireSha(manifestBytes, binding.ManifestSha256, "manifesto candidate-state");

        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 2)
            throw new InvalidDataException("DT-05: schema_version do candidate-state histórico não suportada.");
        if (!string.Equals(root.GetProperty("snapshot_kind").GetString(), "candidate-state", StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: snapshot_kind histórico inválido.");
        if (!Guid.TryParse(root.GetProperty("run_id").GetString(), out var runId) || runId != binding.SourceRunId)
            throw new InvalidDataException("DT-05: run_id do candidate-state diverge do binding histórico.");
        if (!string.Equals(root.GetProperty("key_field").GetString(), "candidate_uuid", StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: key_field histórico inválido.");

        var rowCount = root.GetProperty("row_count").GetInt64();
        if (rowCount != binding.CandidateReferenceCount)
            throw new InvalidDataException("DT-05: row_count do candidate-state diverge da governança imutável.");

        var partitions = root.GetProperty("partitions");
        var canonicalPartitions = Dt05ReplayManifestPublisher.Canonicalize(partitions);
        var partitionSetSha = Sha256(canonicalPartitions);
        if (!string.Equals(partitionSetSha, binding.PartitionSetSha256, StringComparison.Ordinal)
            || !string.Equals(root.GetProperty("partition_set_sha256").GetString(), binding.PartitionSetSha256, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: partition_set_sha256 histórico diverge do binding.");

        long partitionRows = 0;
        var paths = new List<string>();
        foreach (var partition in partitions.EnumerateArray())
        {
            var relative = partition.GetProperty("path").GetString()
                ?? throw new InvalidDataException("DT-05: partição histórica sem path.");
            var expectedSha = partition.GetProperty("sha256").GetString()
                ?? throw new InvalidDataException("DT-05: partição histórica sem sha256.");
            var expectedBytes = partition.GetProperty("bytes").GetInt64();
            var rows = partition.GetProperty("rows").GetInt64();
            var expectedLogicalSha = partition.GetProperty("logical_sha256").GetString();
            if (!IsSha(expectedSha) || !IsSha(expectedLogicalSha ?? string.Empty)
                || expectedBytes < 0 || rows < 0)
                throw new InvalidDataException("DT-05: metadados de partição histórica inválidos.");

            var objectLogical = $"linkage-snapshots/v1/{relative}";
            var objectPath = ResolveUnderBronze(objectLogical, "linkage-snapshots/v1/" + ObjectPrefix);
            var bytes = await File.ReadAllBytesAsync(objectPath, ct);
            if (bytes.LongLength != expectedBytes)
                throw new InvalidDataException($"DT-05: tamanho físico diverge para {relative}.");
            RequireSha(bytes, expectedSha, $"partição {relative}");
            if (!relative.Equals($"objects/{expectedSha[..2]}/{expectedSha}.parquet", StringComparison.Ordinal))
                throw new InvalidDataException("DT-05: path content-addressed da partição diverge do SHA físico.");
            partitionRows = checked(partitionRows + rows);
            paths.Add(objectLogical);
        }
        if (partitionRows != rowCount)
            throw new InvalidDataException("DT-05: soma de rows das partições diverge do manifesto.");

        return new Dt05VerifiedCandidateState(
            binding.SourceRunId, binding.ManifestLogicalPath, binding.ManifestSha256,
            binding.PartitionSetSha256, rowCount, paths);
    }

    private string ResolveUnderBronze(string logical, string requiredPrefix)
    {
        if (!logical.StartsWith(requiredPrefix, StringComparison.Ordinal)
            || logical.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(logical))
            throw new InvalidDataException("DT-05: caminho lógico histórico inválido.");
        var root = Path.GetFullPath(bronzeRoot);
        var path = Path.GetFullPath(Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: caminho histórico escapou da raiz Bronze.");
        return path;
    }

    private static void RequireSha(byte[] bytes, string expected, string subject)
    {
        if (!IsSha(expected) || !string.Equals(Sha256(bytes), expected, StringComparison.Ordinal))
            throw new InvalidDataException($"DT-05: SHA físico diverge para {subject}.");
    }

    private static bool IsSha(string value) =>
        value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

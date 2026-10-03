using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Globalization;
using Parquet;
using Parquet.Schema;

namespace Jornada.Linkage.Runner;

public sealed record Dt05HistoricalCandidate(Guid PessoaUuid, string? NomeCompleto, DateOnly? DataNascimento, string? NomeMae);
public sealed record Dt05VerifiedCandidateState(
    Guid SourceRunId, string ManifestLogicalPath, string ManifestSha256,
    string PartitionSetSha256, long RowCount, IReadOnlyList<Dt05HistoricalCandidate> Candidates);

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
        var candidates = new List<Dt05HistoricalCandidate>();
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
            var partitionCandidates = await ReadPartitionAsync(objectPath, expectedLogicalSha!, rows, ct);
            partitionRows = checked(partitionRows + partitionCandidates.Count);
            candidates.AddRange(partitionCandidates);
        }
        if (partitionRows != rowCount)
            throw new InvalidDataException("DT-05: soma de rows das partições diverge do manifesto.");
        var canonicalCandidates = string.Join('\n', candidates.Select(x => x.PessoaUuid.ToString("D").ToLowerInvariant() + "|REFERENCIA"));
        var candidateSetSha = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(canonicalCandidates))).ToLowerInvariant();
        if (!string.Equals(candidateSetSha, binding.CandidateSetSha256, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: candidatos_sha256 histórico diverge das linhas Parquet.");

        return new Dt05VerifiedCandidateState(
            binding.SourceRunId, binding.ManifestLogicalPath, binding.ManifestSha256,
            binding.PartitionSetSha256, rowCount, candidates);
    }


    private static async Task<IReadOnlyList<Dt05HistoricalCandidate>> ReadPartitionAsync(
        string path, string expectedLogicalSha, long expectedRows, CancellationToken ct)
    {
        await using var reader = await ParquetReader.CreateAsync(path, cancellationToken: ct);
        var fields = reader.Schema.GetDataFields();
        var expected = new[] { "candidate_uuid", "nome_completo", "data_nascimento", "nome_mae", "estado_identidade" };
        if (fields.Length != expected.Length || !fields.Select(x => x.Name).SequenceEqual(expected, StringComparer.Ordinal)
            || fields.Any(x => x.ClrType != typeof(string))
            || fields[0].IsNullable || fields[4].IsNullable
            || !fields[1].IsNullable || !fields[2].IsNullable || !fields[3].IsNullable)
            throw new InvalidDataException(
                "DT-05: schema físico Parquet candidate-state inválido. Lido: " +
                string.Join(", ", fields.Select(x => $"{x.Name}:{x.ClrType.FullName}:nullable={x.IsNullable}")));

        var rows = new List<Dt05HistoricalCandidate>();
        using var logical = new MemoryStream();
        for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            using var group = reader.OpenRowGroupReader(groupIndex);
            var columns = new string?[fields.Length][];
            var count = checked((int)group.RowCount);
            for (var i = 0; i < fields.Length; i++)
            {
                columns[i] = new string?[count];
                await group.ReadAsync(fields[i], columns[i].AsMemory(), cancellationToken: ct);
            }
            if (columns.Any(x => x.Length != count))
                throw new InvalidDataException("DT-05: colunas Parquet históricas possuem cardinalidades divergentes.");
            for (var i = 0; i < count; i++)
            {
                if (!Guid.TryParseExact(columns[0][i], "D", out var uuid)
                    || !string.Equals(columns[0][i], uuid.ToString("D").ToLowerInvariant(), StringComparison.Ordinal)
                    || !string.Equals(columns[4][i], "REFERENCIA", StringComparison.Ordinal))
                    throw new InvalidDataException("DT-05: identidade de candidato histórica inválida.");
                DateOnly? birth = null;
                if (columns[2][i] is { } date)
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        throw new InvalidDataException("DT-05: data_nascimento histórica inválida.");
                    birth = parsed;
                }
                var canonical = Dt05ReplayManifestPublisher.Canonicalize(new {
                    candidate_uuid = columns[0][i], nome_completo = columns[1][i],
                    data_nascimento = columns[2][i], nome_mae = columns[3][i], estado_identidade = columns[4][i]
                });
                if (logical.Length > 0) logical.WriteByte((byte)'\n');
                logical.Write(canonical);
                rows.Add(new Dt05HistoricalCandidate(uuid, columns[1][i], birth, columns[3][i]));
            }
        }
        if (rows.Count != expectedRows)
            throw new InvalidDataException("DT-05: rows físicos da partição divergem do manifesto.");
        if (!string.Equals(Sha256(logical.ToArray()), expectedLogicalSha, StringComparison.Ordinal))
            throw new InvalidDataException("DT-05: logical_sha256 da partição histórica diverge das linhas.");
        return rows;
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

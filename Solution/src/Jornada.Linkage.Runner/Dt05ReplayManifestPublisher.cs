using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Bronze.Storage;

namespace Jornada.Linkage.Runner;

public sealed record Dt05BronzePin(string ObjectKey, string Sha256);
public sealed record Dt05ReplayManifestIdentity(
    string ScorerVersion, string RuleSetVersion, string ModelVersion, string InputSnapshotId);

/// <summary>
/// Publica o manifesto referencial DT-05 sem copiar payloads Bronze.
/// O destino é create-only e o conteúdo é JSON canônico; qualquer corrida perde fechada.
/// </summary>
public sealed class Dt05ReplayManifestPublisher(IBronzeObjectStore bronze, string bronzeRoot)
{
    public async Task<(string LogicalPath, string ManifestSha256, string BronzeSetSha256)> PublishAsync(
        Guid runId, IReadOnlyList<Dt05BronzePin> pins, Dt05ReplayManifestIdentity identity, CancellationToken ct)
    {
        if (pins.Count == 0) throw new InvalidOperationException("DT-05: manifesto exige ao menos um pin Bronze.");
        Require(identity.ScorerVersion, "scorer_version");
        Require(identity.RuleSetVersion, "ruleset_version");
        Require(identity.ModelVersion, "model_version");
        Require(identity.InputSnapshotId, "input_snapshot_id");

        var refs = new List<object>(pins.Count);
        foreach (var pin in pins.OrderBy(x => x.ObjectKey, StringComparer.Ordinal))
        {
            await using var stream = await bronze.OpenReadAsync(pin.ObjectKey, ct);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long length = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), ct);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                length += read;
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, pin.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"DT-05: SHA físico Bronze diverge para {pin.ObjectKey}.");
            refs.Add(new { objeto_chave = pin.ObjectKey, payload_sha256 = actual, bytes = length });
        }

        var refsBytes = Canonicalize(refs);
        var bronzeSetSha = Sha256(refsBytes);
        var document = new {
            schema_version = 1,
            run_id = runId.ToString(),
            versions = new {
                scorer_version = identity.ScorerVersion,
                ruleset_version = identity.RuleSetVersion,
                model_version = identity.ModelVersion,
                input_snapshot_id = identity.InputSnapshotId
            },
            bronze_objects = refs,
            bronze_set_sha256 = bronzeSetSha,
            pin_contract = "identidade.sp_fixar_bronze_para_linkage/v1",
            parent = (object?)null
        };
        var bytes = Canonicalize(document);
        var manifestSha = Sha256(bytes);
        var logical = $"linkage-snapshots/v1/manifests/{runId:D}-{manifestSha}.json";
        var root = Path.GetFullPath(bronzeRoot);
        var destination = Path.GetFullPath(Path.Combine(root, logical.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("DT-05: caminho do manifesto escapou da raiz Bronze.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            throw new InvalidOperationException("DT-05: manifesto imutável já existe; publicação recusada.");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
        return (logical, manifestSha, bronzeSetSha);
    }

    internal static string ComputeInputSnapshotId(long highWatermark, IEnumerable<long> orderedObservationIds)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var prefix = Encoding.UTF8.GetBytes($"dt05-input-v1\\nhigh_watermark={highWatermark}\\n");
        hash.AppendData(prefix);
        foreach (var id in orderedObservationIds)
            hash.AppendData(Encoding.UTF8.GetBytes(id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\\n"));
        return "dt05-input-v1:sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static byte[] Canonicalize<T>(T value)
    {
        using var source = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value));
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
            WriteCanonical(writer, source.RootElement);
        return output.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"DT-05: {name} exato é obrigatório.");
    }
}

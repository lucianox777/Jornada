using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Create-only portable calibration artifacts. This is not the operational SQL
/// reference publisher: storing an experimental fit cannot approve or activate it.
/// </summary>
public static class V9FittedSnapshotArtifact
{
    public static async Task WriteNewAsync(string path, V9FittedSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // CreateNew also protects concurrent writers. No replacement or cleanup
        // of an existing artifact is ever performed, including on cancellation.
        await using var output = new FileStream(Path.GetFullPath(path), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await output.WriteAsync(snapshot.SerializeUtf8(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public static async Task<V9FittedSnapshot> ReadPinnedAsync(string path, string contentSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var input = new FileStream(Path.GetFullPath(path), FileMode.Open,
            FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (input.Length <= 0 || input.Length > V9FittedSnapshot.MaximumContentBytes)
            throw new InvalidDataException("Artefato V9 vazio ou excede o limite permitido.");
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken);
        if (input.ReadByte() != -1)
            throw new InvalidDataException("Artefato V9 foi alterado durante a leitura.");
        return V9FittedSnapshot.Load(bytes, contentSha256);
    }

    /// <summary>Opt-in read-only CLI; no host, SQL connection or training is started.</summary>
    public static async Task InspectAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != "--v9-inspect-fitted-snapshot")
            throw new ArgumentException("Uso: --v9-inspect-fitted-snapshot <snapshot.json> <sha256-fixado>");
        var snapshot = await ReadPinnedAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            snapshot.Payload.SchemaVersion,
            snapshot.ContentSha256,
            snapshot.Payload.OptionId,
            snapshot.Payload.FitFingerprintSha256,
            snapshot.Payload.TrainCorpusSha256,
            snapshot.Payload.ValidationCorpusSha256,
            snapshot.Payload.Threshold,
            snapshot.Payload.ConflictMargin,
            snapshot.Payload.TfWeight,
            blockingPassCount = snapshot.Payload.BlockingPasses.Count,
            jointCellCount = snapshot.Payload.JointCells.Count,
            approved = false,
            operationalModelCreated = false,
            automaticallyActivated = false
        }));
    }
}

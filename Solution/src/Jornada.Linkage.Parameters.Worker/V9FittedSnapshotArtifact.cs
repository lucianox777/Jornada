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

    /// <summary>
    /// Read-only evidence for *exactly* the loaded portable fit. Do not infer
    /// that this snapshot is the currently selected SQL model or that its
    /// provenance grants permission for operation. This inspection never
    /// chooses passes, fits TF or changes thresholds.
    /// </summary>
    public static JsonElement DescribeFrozenConfiguration(V9FittedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = snapshot.Payload;
        return JsonSerializer.SerializeToElement(new
        {
            payload.SchemaVersion,
            snapshot.ContentSha256,
            payload.OptionId,
            payload.FitFingerprintSha256,
            payload.TrainCorpusSha256,
            payload.ValidationCorpusSha256,
            payload.Threshold,
            payload.ConflictMargin,
            payload.TfWeight,
            blockingPassCount = payload.BlockingPasses.Count,
            blockingPasses = payload.BlockingPasses.Select((pass, order) => new
            {
                ordinal = order + 1,
                pass.PassId,
                fields = pass.Fields.ToArray()
            }).ToArray(),
            jointCellCount = payload.JointCells.Count,
            hasJointSupport = payload.JointSupport is not null,
            grouping = payload.CandidateGrouping?.CandidateGrouping,
            goldParentSnapshotSha256 = payload.GoldParentSnapshotSha256,
            // Four independently calibrated TF slots are distinct from the
            // legacy TfWeight. A legacy zero does not imply no active unit TF.
            activeUnitTfCount = payload.UnitTfCalibrations?.Count(
                u => u.State == V9SnapshotTfFitState.Active) ?? 0,
            unitTf = payload.UnitTfCalibrations?.Select(u => new
            {
                side = u.Side.ToString(),
                unit = u.Unit.ToString(),
                state = u.State.ToString(),
                weight = u.Calibration?.Weight,
                mode = u.Calibration?.Mode.ToString(),
                populationFrame = u.Calibration?.Reference.PopulationFrameFingerprint
            }).ToArray(),
            hasLegacyPersonTf = payload.TfCalibration is not null,
            hasLegacyMotherTf = payload.MotherTfCalibration is not null,
            approved = false,
            operationalModelCreated = false,
            automaticallyActivated = false
        });
    }

    /// <summary>Opt-in read-only CLI; no host, SQL connection or training is started.</summary>
    public static async Task InspectAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != "--v9-inspect-fitted-snapshot")
            throw new ArgumentException("Uso: --v9-inspect-fitted-snapshot <snapshot.json> <sha256-fixado>");
        var snapshot = await ReadPinnedAsync(args[1], args[2]);
        Console.WriteLine(DescribeFrozenConfiguration(snapshot).GetRawText());
    }
}

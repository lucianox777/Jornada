using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// File-only V9 selection contract. This loader never reads or stages model parameters in SQL.
/// Production activation remains gated on complete V9 cohort/batch parity.
/// </summary>
public static class V9ConfiguredArtifactLoader
{
    public sealed record Selection(string ArtifactPath, string WinnerSha256);
    public sealed record Loaded(string ArtifactPath, string WinnerSha256, V9FittedSnapshot Snapshot);

    public static async Task<Loaded> LoadAsync(string selectionFile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(selectionFile))
            throw new InvalidDataException("RUNNER_V9_SELECTION_MISSING");
        var selectionPath = Path.GetFullPath(selectionFile);
        if (!File.Exists(selectionPath) || new FileInfo(selectionPath).LinkTarget is not null)
            throw new InvalidDataException("RUNNER_V9_SELECTION_INVALID");
        var bytes = await File.ReadAllBytesAsync(selectionPath, ct);
        if (bytes.Length is 0 or > 65536)
            throw new InvalidDataException("RUNNER_V9_SELECTION_SIZE_INVALID");
        var selection = JsonSerializer.Deserialize<Selection>(bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("RUNNER_V9_SELECTION_INVALID");
        if (string.IsNullOrWhiteSpace(selection.ArtifactPath) || string.IsNullOrWhiteSpace(selection.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_SELECTION_INVALID");
        var artifact = Path.GetFullPath(selection.ArtifactPath, Path.GetDirectoryName(selectionPath)!);
        var expectedName = V9ValidatedOperationalModelFile.FileName(selection.WinnerSha256);
        if (!string.Equals(Path.GetFileName(artifact), expectedName, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_ARTIFACT_SHA_MISMATCH");
        var snapshot = await V9ValidatedOperationalModelFile.ReadPinnedAsync(artifact, ct);
        if (!string.Equals(snapshot.ContentSha256, selection.WinnerSha256, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_ARTIFACT_SHA_MISMATCH");
        return new Loaded(artifact, selection.WinnerSha256, snapshot);
    }
}

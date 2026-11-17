using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// File-only V9 selection contract. This loader never reads or stages model parameters in SQL.
/// Production activation remains gated on complete V9 cohort/batch parity.
/// </summary>
public static class V9ConfiguredArtifactLoader
{
    private static readonly JsonSerializerOptions SelectionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public sealed record Selection(string ArtifactPath, string WinnerSha256);
    public sealed record Loaded(string ArtifactPath, string WinnerSha256, V9FittedSnapshot Snapshot);

    /// <summary>Nonpublishing in-memory V9 cohort runtime; no SQL model persistence.</summary>
    public static async Task<V9OperationalPinnedCohortRuntime> LoadCohortRuntimeAsync(
        string selectionFile, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(selectionFile, ct);
        return V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(loaded.Snapshot);
    }

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
        var selection = JsonSerializer.Deserialize<Selection>(bytes, SelectionJsonOptions)
            ?? throw new InvalidDataException("RUNNER_V9_SELECTION_INVALID");
        if (string.IsNullOrWhiteSpace(selection.ArtifactPath) || string.IsNullOrWhiteSpace(selection.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_SELECTION_INVALID");
        var artifact = Path.GetFullPath(selection.ArtifactPath, Path.GetDirectoryName(selectionPath)!);
        // Reject a link at the selected artifact path before opening it.
        // The pinned reader verifies bytes/SHA, but the selected path itself
        // must not be a mutable symlink controlled outside the package.
        if (!File.Exists(artifact) || new FileInfo(artifact).LinkTarget is not null)
            throw new InvalidDataException("RUNNER_V9_ARTIFACT_INVALID");
        var expectedName = V9ValidatedOperationalModelFile.FileName(selection.WinnerSha256);
        if (!string.Equals(Path.GetFileName(artifact), expectedName, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_ARTIFACT_SHA_MISMATCH");
        var snapshot = await V9ValidatedOperationalModelFile.ReadPinnedAsync(artifact, ct);
        if (!string.Equals(snapshot.ContentSha256, selection.WinnerSha256, StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_ARTIFACT_SHA_MISMATCH");
        return new Loaded(artifact, selection.WinnerSha256, snapshot);
    }
}

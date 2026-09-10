namespace Jornada.Linkage.Parameters.Worker;

public sealed record IbgeSourceMetadata(
    string? ETag,
    DateTimeOffset? LastModified,
    long? ContentLength);

public enum IbgeSnapshotChangeStatus
{
    Unchanged,
    Changed,
    Indeterminate
}

/// <summary>
/// Decide, apenas por metadados baratos da origem, se um novo download IBGE é necessário.
/// Content-Length isolado nunca prova identidade: tamanho igual pode representar conteúdo diferente.
/// A identidade do snapshot materializado continua sendo o fingerprint SHA-256 do catálogo.
/// </summary>
public static class IbgeSnapshotChangeDetector
{
    public const string MethodVersion = "IBGE_SNAPSHOT_CHANGE_DETECTOR_V1";

    public static IbgeSnapshotChangeStatus Compare(
        IbgeSourceMetadata previous,
        IbgeSourceMetadata current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (BothPresent(previous.ETag, current.ETag))
            return SameETag(previous.ETag!, current.ETag!)
                ? IbgeSnapshotChangeStatus.Unchanged
                : IbgeSnapshotChangeStatus.Changed;

        if (previous.LastModified is not null && current.LastModified is not null)
        {
            if (previous.LastModified != current.LastModified)
                return IbgeSnapshotChangeStatus.Changed;

            if (previous.ContentLength is not null && current.ContentLength is not null)
                return previous.ContentLength == current.ContentLength
                    ? IbgeSnapshotChangeStatus.Unchanged
                    : IbgeSnapshotChangeStatus.Changed;

            return IbgeSnapshotChangeStatus.Unchanged;
        }

        if (previous.ContentLength is not null && current.ContentLength is not null &&
            previous.ContentLength != current.ContentLength)
            return IbgeSnapshotChangeStatus.Changed;

        // Mesmo tamanho, sem ETag/Last-Modified confiável: baixar e comparar fingerprint.
        return IbgeSnapshotChangeStatus.Indeterminate;
    }

    public static bool RequiresDownload(
        IbgeSourceMetadata previous,
        IbgeSourceMetadata current) =>
        Compare(previous, current) != IbgeSnapshotChangeStatus.Unchanged;

    private static bool BothPresent(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right);

    private static bool SameETag(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}

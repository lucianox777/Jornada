using System.Security.Cryptography;

namespace Jornada.Linkage.Runner;

/// <summary>
/// The final *V9 operational JSON* produced after independent VALIDATION:
/// precisely the canonical V9_FITTED_SNAPSHOT_V3 bytes stored as
/// payload_fitted in the versioned production-side V9 SQL model contract.
/// No retraining, recalibration, threshold adjustment or model activation.
/// This file is NOT the legacy modelo_linkage JSON/SQL contract; full
/// ProbabilisticLinkageBatchRunner.RunAsync equivalence must be proven separately.
/// </summary>
public static class V9ValidatedOperationalModelFile
{
    public const string Suffix = ".operational.json";
    public const string ArtifactSchema = "JORNADA_V9_VALIDATED_OPERATIONAL_MODEL_FILE_V1";

    public sealed record Receipt(
        string Schema, string OperationalModelContract, string SnapshotSchemaVersion,
        string FileName, string WinnerSnapshotSha256, Guid OperationalModelId,
        string FitFingerprintSha256, string ProjectionFingerprintSha256,
        int JointCellCount, int TfSlotCount, int BlockingPassCount,
        bool ByteExactRoundTrip, bool Refitted, bool TestUsed,
        bool Certified, bool Activated, bool Published);

    public static string FileName(string winnerSha256)
    {
        // ModelIdFromWinnerSha rejects empty/mixed-case/non-hex SHA.
        _ = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winnerSha256);
        return winnerSha256 + Suffix;
    }

    public static async Task<Receipt> ExportAsync(
        V9FittedSnapshot validatedWinner, string newOutputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validatedWinner);
        var contract = V9OperationalSqlModelConverter.VerifyConversion(validatedWinner);
        if (!contract.ByteExactRoundTrip || contract.Active || contract.Published)
            throw new InvalidDataException("V9: modelo de VALIDATION sem contrato operacional preservado.");
        var name = FileName(contract.SourceSnapshotSha256);
        var bytes = validatedWinner.SerializeUtf8();
        if (bytes.Length <= 0 || bytes.Length > V9FittedSnapshot.MaximumContentBytes
            || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                != contract.SourceSnapshotSha256)
            throw new InvalidDataException("V9: bytes operacionais não correspondem ao vencedor.");

        // Create-only directory and file; never replace a previous winner or
        // export any other finalist. Caller may give a new validation-only path.
        Directory.CreateDirectory(newOutputDirectory);
        var file = Path.Combine(newOutputDirectory, name);
        await using (var stream = new FileStream(file, FileMode.CreateNew,
            FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }
        var recovered = await ReadPinnedAsync(file, cancellationToken);
        if (recovered.ContentSha256 != validatedWinner.ContentSha256
            || !recovered.SerializeUtf8().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("V9: serialização de produção divergiu após exportação.");
        return new(ArtifactSchema, V9OperationalSqlModelConverter.ContractVersion,
            V9FittedSnapshot.SupportSchemaVersion, name,
            contract.SourceSnapshotSha256, contract.ModelId,
            contract.FitFingerprintSha256, contract.ProjectionFingerprintSha256,
            contract.JointCellCount, contract.TfSlotCount, contract.BlockingPassCount,
            ByteExactRoundTrip: true, Refitted: false, TestUsed: false,
            Certified: false, Activated: false, Published: false);
    }

    /// <summary>
    /// TEST and the SQL conversion CLI consume the *same filename* emitted
    /// by VALIDATION. SHA and contract are checked before any TEST exposure.
    /// </summary>
    public static async Task<V9FittedSnapshot> ReadPinnedAsync(
        string filename, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new InvalidDataException("V9: arquivo operacional final não informado.");
        var file = new FileInfo(Path.GetFullPath(filename));
        var name = file.Name;
        if (!name.EndsWith(Suffix, StringComparison.Ordinal)
            || name.Length != 64 + Suffix.Length
            || !file.Exists || file.LinkTarget is not null)
            throw new InvalidDataException("V9: TEST requer um arquivo operacional da VALIDATION, SHA-addressed e sem symlink.");
        var sha = name[..64];
        _ = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(sha);
        if (file.Length <= 0 || file.Length > V9FittedSnapshot.MaximumContentBytes)
            throw new InvalidDataException("V9: arquivo de parâmetros operacionais excede limite.");
        var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        var winner = V9FittedSnapshot.Load(bytes, sha);
        var contract = V9OperationalSqlModelConverter.VerifyConversion(winner);
        if (contract.SourceSnapshotSha256 != sha || !contract.ByteExactRoundTrip
            || !winner.SerializeUtf8().AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("V9: arquivo final não é o contrato operacional byte-exato do vencedor.");
        return winner;
    }
}

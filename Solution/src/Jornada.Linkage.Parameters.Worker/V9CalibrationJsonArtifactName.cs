using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Content-addressed filename for new V9 calibration result JSON artifacts.
/// Hashes the final bytes, never a reserialized JSON representation.
/// Existing historical artifact filenames are not rewritten.
/// </summary>
public static class V9CalibrationJsonArtifactName
{
    private static readonly Regex Pattern = new(
        @"^calibracao_v9_(?<timestamp>\d{8}_\d{6})_(?<sha>[a-f0-9]{64})\.json$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Create(ReadOnlySpan<byte> originalJsonBytes, DateTimeOffset generatedAt)
    {
        if (originalJsonBytes.IsEmpty)
            throw new ArgumentException("JSON de calibração vazio.", nameof(originalJsonBytes));
        var digest = Convert.ToHexString(SHA256.HashData(originalJsonBytes)).ToLowerInvariant();
        return $"calibracao_v9_{generatedAt.UtcDateTime:yyyyMMdd_HHmmss}_{digest}.json";
    }

    /// <summary>
    /// Reads the exact stored bytes and rejects a missing or tampered artifact.
    /// Never infers a model association from a filename.
    /// </summary>
    public static async Task<byte[]> ReadVerifiedAsync(string directory, string fileName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || !Pattern.IsMatch(fileName))
            throw new InvalidDataException("Nome de artefato V9 inválido.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, fileName), ct);
        if (!Verify(fileName, bytes))
            throw new InvalidDataException("SHA-256 do artefato V9 diverge dos bytes originais.");
        return bytes;
    }

    /// <summary>
    /// Verifies an explicit provenance receipt against the original artifact.
    /// This is not a model association or certification.
    /// </summary>
    public static async Task<byte[]> ReadReceiptVerifiedAsync(
        string directory, string artifactFileName, string expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)
            || expectedSha256.Length != 64
            || !expectedSha256.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("SHA-256 declarado inválido.");
        var bytes = await ReadVerifiedAsync(directory, artifactFileName, ct);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedSha256), Convert.FromHexString(actual)))
            throw new InvalidDataException("SHA-256 do recibo diverge do artefato original.");
        return bytes;
    }

    public static bool Verify(string fileName, ReadOnlySpan<byte> originalJsonBytes)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var match = Pattern.Match(Path.GetFileName(fileName));
        if (!match.Success || fileName != Path.GetFileName(fileName)) return false;
        var actual = SHA256.HashData(originalJsonBytes);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(match.Groups["sha"].Value), actual);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Manifesto somente leitura para um corpus de avaliação independente.
/// Não executa Linkage, não altera scorer/modelo e não autoriza ativação.
/// </summary>
public sealed record IndependentEvaluationManifest(
    string MethodVersion,
    string ModelVersion,
    string CalibrationCorpusFingerprintSha256,
    string EvaluationCorpusFingerprintSha256,
    string ReferenceTruthFingerprintSha256,
    long CandidatePairs,
    long ReferenceLinks,
    DateTimeOffset CapturedAtUtc,
    string FingerprintSha256);

public static class IndependentEvaluationManifestCatalog
{
    public static IndependentEvaluationManifest Create(
        string methodVersion,
        string modelVersion,
        string calibrationCorpusFingerprintSha256,
        string evaluationCorpusFingerprintSha256,
        string referenceTruthFingerprintSha256,
        long candidatePairs,
        long referenceLinks,
        DateTimeOffset capturedAtUtc)
    {
        methodVersion = Required(methodVersion, nameof(methodVersion));
        modelVersion = Required(modelVersion, nameof(modelVersion));
        calibrationCorpusFingerprintSha256 = Sha256(calibrationCorpusFingerprintSha256, nameof(calibrationCorpusFingerprintSha256));
        evaluationCorpusFingerprintSha256 = Sha256(evaluationCorpusFingerprintSha256, nameof(evaluationCorpusFingerprintSha256));
        referenceTruthFingerprintSha256 = Sha256(referenceTruthFingerprintSha256, nameof(referenceTruthFingerprintSha256));

        if (string.Equals(calibrationCorpusFingerprintSha256, evaluationCorpusFingerprintSha256, StringComparison.Ordinal))
            throw new ArgumentException("Evaluation corpus must be independent from the calibration corpus.");
        if (candidatePairs <= 0)
            throw new ArgumentOutOfRangeException(nameof(candidatePairs), "CandidatePairs must be positive.");
        if (referenceLinks < 0 || referenceLinks > candidatePairs)
            throw new ArgumentOutOfRangeException(nameof(referenceLinks), "ReferenceLinks must be between zero and CandidatePairs.");
        if (capturedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("CapturedAtUtc must use UTC offset zero.", nameof(capturedAtUtc));

        var canonical = string.Join('\n', new[]
        {
            "LINKAGE_INDEPENDENT_EVALUATION_V1",
            methodVersion,
            modelVersion,
            calibrationCorpusFingerprintSha256,
            evaluationCorpusFingerprintSha256,
            referenceTruthFingerprintSha256,
            candidatePairs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            referenceLinks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            capturedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        }) + "\n";

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new IndependentEvaluationManifest(
            methodVersion,
            modelVersion,
            calibrationCorpusFingerprintSha256,
            evaluationCorpusFingerprintSha256,
            referenceTruthFingerprintSha256,
            candidatePairs,
            referenceLinks,
            capturedAtUtc,
            fingerprint);
    }

    private static string Required(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{paramName} is required.", paramName);
        return value.Trim();
    }

    private static string Sha256(string value, string paramName)
    {
        value = Required(value, paramName).ToLowerInvariant();
        if (value.Length != 64 || value.Any(static c => !Uri.IsHexDigit(c)))
            throw new ArgumentException($"{paramName} must be a 64-character SHA-256 hex fingerprint.", paramName);
        return value;
    }
}

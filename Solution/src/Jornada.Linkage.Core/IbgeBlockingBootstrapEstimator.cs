using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Bootstrap diagnóstico para dimensionar blocking a partir de marginais públicas.
/// Nunca promove passes, altera m/u, score, ruleset ou decisão de identidade.
/// </summary>
public static class IbgeBlockingBootstrapEstimator
{
    public const string MethodVersion = "IBGE_BLOCKING_BOOTSTRAP_DIAGNOSTIC_V1";
    public const string Marker = "NAO_PROMOCIONAL";
    public const string EstimationKind = "MARGINAL_INDEPENDENCE_DIAGNOSTIC";

    public static BlockingBootstrapProposal Estimate(
        ReferencePopulationEvidence referencePopulationEvidence,
        string snapshotReferenceCode,
        string snapshotHash,
        IReadOnlyList<BootstrapPassInput> passes,
        FrequentKeyRule frequentKeyRule,
        SurnameParticlePolicy particlePolicy)
    {
        ArgumentNullException.ThrowIfNull(referencePopulationEvidence);
        referencePopulationEvidence.Validate();
        var referencePopulation = referencePopulationEvidence.Population;
        if (string.IsNullOrWhiteSpace(snapshotReferenceCode)) throw new ArgumentException("Snapshot scope/reference code is required.", nameof(snapshotReferenceCode));
        if (!IsSha256(snapshotHash)) throw new ArgumentException("A valid SHA-256 snapshot hash is required.", nameof(snapshotHash));
        ArgumentNullException.ThrowIfNull(passes);
        ArgumentNullException.ThrowIfNull(frequentKeyRule);
        if (passes.Count == 0) throw new ArgumentException("At least one pass is required.", nameof(passes));

        var allExpected = passes.SelectMany(p => p.Keys)
            .Select(k => Expected(referencePopulation, k.Probability))
            .OrderBy(x => x).ToArray();
        if (allExpected.Length == 0) throw new ArgumentException("Passes must contain marginal keys.", nameof(passes));

        var threshold = frequentKeyRule.ResolveThreshold(allExpected);
        var estimated = passes.Select(pass =>
        {
            if (pass.Keys.Count == 0) throw new ArgumentException($"Pass {pass.PassId} has no keys.");
            if (pass.Keys.Any(k => k.Probability is <= 0 or > 1 || string.IsNullOrWhiteSpace(k.Scope)))
                throw new ArgumentException($"Pass {pass.PassId} contains invalid probability or missing scope.");
            var joint = pass.Keys.Aggregate(1d, (p, key) => p * key.Probability);
            var expected = Expected(referencePopulation, joint);
            var frequent = pass.Keys
                .Where(k => Expected(referencePopulation, k.Probability) >= threshold)
                .Select(k => k.KeyId).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return new BootstrapPassEstimate(
                pass.PassId, pass.Category, expected, joint, frequent,
                pass.Reason, pass.Keys.Select(k => k.Scope).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        })
        .OrderBy(p => CategoryOrder(p.Category))
        .ThenBy(p => p.ExpectedCandidates)
        .ThenBy(p => p.PassId, StringComparer.Ordinal)
        .ToArray();

        var withoutFingerprint = new BlockingBootstrapProposal(
            MethodVersion, Marker, EstimationKind, false,
            "Estimativa marginal sob independência; não mede a interseção real, não calibra o FS e não autoriza ativação de passes.",
            snapshotReferenceCode, snapshotHash.ToUpperInvariant(), referencePopulation,
            referencePopulationEvidence.SourceMethod, referencePopulationEvidence.SourceRunId,
            referencePopulationEvidence.CorpusFingerprintSha256,
            frequentKeyRule.Describe(), particlePolicy.ToString(), estimated, "");
        var canonical = JsonSerializer.Serialize(withoutFingerprint, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return withoutFingerprint with { ResultFingerprintSha256 = fingerprint };
    }

    public static string ToImmutableJson(BlockingBootstrapProposal proposal) =>
        JsonSerializer.Serialize(proposal, JsonOptions);

    private static double Expected(long n, double probability) => n * probability;
    private static bool IsSha256(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length == 64 && value.All(Uri.IsHexDigit);
    private static int CategoryOrder(BootstrapPassCategory category) => category switch
    {
        BootstrapPassCategory.Exact => 0,
        BootstrapPassCategory.DateVariant => 1,
        BootstrapPassCategory.NameVariant => 2,
        BootstrapPassCategory.IncompleteRecovery => 3,
        _ => 4
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed record MarginalKeyProbability(string KeyId, double Probability, string Scope);
public sealed record BootstrapPassInput(
    string PassId,
    BootstrapPassCategory Category,
    IReadOnlyList<MarginalKeyProbability> Keys,
    string Reason);
public enum BootstrapPassCategory { Exact, DateVariant, NameVariant, IncompleteRecovery }
public enum SurnameParticlePolicy { Preserve, ExcludePortugueseParticles }

public sealed record BootstrapPassEstimate(
    string PassId,
    BootstrapPassCategory Category,
    double ExpectedCandidates,
    double DiagnosticJointProbability,
    IReadOnlyList<string> ExcessivelyFrequentKeys,
    string Reason,
    IReadOnlyList<string> Scopes);

public sealed record BlockingBootstrapProposal(
    string MethodVersion,
    string Marker,
    string EstimationKind,
    bool JointDistributionObserved,
    string Warning,
    string SnapshotReferenceCode,
    string SnapshotHashSha256,
    long ReferencePopulation,
    string ReferencePopulationSourceMethod,
    string ReferencePopulationSourceRunId,
    string ReferencePopulationCorpusFingerprintSha256,
    string FrequentKeyRule,
    string SurnameParticlePolicy,
    IReadOnlyList<BootstrapPassEstimate> Passes,
    string ResultFingerprintSha256);

/// <summary>Regra estatística declarada antes da observação dos resultados.</summary>
public sealed record FrequentKeyRule(double Quantile)
{
    public double ResolveThreshold(IReadOnlyList<double> expectedCardinalities)
    {
        if (Quantile is <= 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(expectedCardinalities), "Configured quantile must be strictly between 0 and 1.");
        if (expectedCardinalities.Count == 0) throw new ArgumentException("Distribution is empty.", nameof(expectedCardinalities));
        var ordered = expectedCardinalities.OrderBy(x => x).ToArray();
        var rank = (int)Math.Ceiling(Quantile * ordered.Length) - 1;
        return ordered[Math.Clamp(rank, 0, ordered.Length - 1)];
    }

    public string Describe() => $"EXPECTED_CARDINALITY_QUANTILE_V1:q={Quantile:R}";
}

/// <summary>Valida manifestos e hashes locais. Falha fechada: sem fallback remoto/geográfico.</summary>
public static class IbgeBlockingSnapshotVerifier
{
    public const string RequiredPersonScope = "MUNICIPIO_SAO_PAULO_V2_CANDIDATA";
    public const string RequiredMotherScope = "BRASIL_V1";

    public static SnapshotVerification Verify(string snapshotDirectory)
    {
        if (string.IsNullOrWhiteSpace(snapshotDirectory) || !Directory.Exists(snapshotDirectory))
            throw new DirectoryNotFoundException("IBGE snapshot is required.");
        var manifestPath = Path.Combine(snapshotDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("IBGE manifest is required.", manifestPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        var referenceCode = root.GetProperty("referenceCode").GetString();
        if (string.IsNullOrWhiteSpace(referenceCode)) throw new InvalidDataException("referenceCode is required.");
        var files = root.GetProperty("snapshot").GetProperty("files").EnumerateArray().ToArray();
        if (files.Length == 0) throw new InvalidDataException("Snapshot files are required.");

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in files.OrderBy(e => e.GetProperty("path").GetString(), StringComparer.Ordinal))
        {
            var relative = entry.GetProperty("path").GetString();
            var expected = entry.GetProperty("sha256").GetString();
            if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
                throw new InvalidDataException("Every snapshot file requires path and SHA-256.");
            var path = Path.Combine(snapshotDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new FileNotFoundException("Required IBGE snapshot file is missing.", path);
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"IBGE snapshot hash mismatch: {relative}.");
            aggregate.AppendData(Encoding.UTF8.GetBytes(relative));
            aggregate.AppendData(Convert.FromHexString(actual));
        }
        return new SnapshotVerification(referenceCode!, Convert.ToHexString(aggregate.GetHashAndReset()), RequiredPersonScope, RequiredMotherScope);
    }
}

public sealed record SnapshotVerification(
    string ReferenceCode,
    string AggregateHashSha256,
    string PersonScope,
    string MotherScope);


/// <summary>
/// Evidência de N_ref derivada do corpus elegível observado pela execução do Calibrador.
/// É metadado de dimensionamento do blocking: não participa de m/u, LLR, posterior ou threshold FS.
/// </summary>
public sealed record ReferencePopulationEvidence(
    long Population,
    string SourceMethod,
    string SourceRunId,
    string CorpusFingerprintSha256)
{
    public const string CalibratorCorpusMethod = "CALIBRATOR_ELIGIBLE_REFERENCE_CORPUS_V1";

    public void Validate()
    {
        if (Population <= 0) throw new ArgumentOutOfRangeException(nameof(Population));
        if (!string.Equals(SourceMethod, CalibratorCorpusMethod, StringComparison.Ordinal))
            throw new ArgumentException("N_ref must be derived from the eligible Jornada corpus observed by the Calibrator.", nameof(SourceMethod));
        if (string.IsNullOrWhiteSpace(SourceRunId))
            throw new ArgumentException("Calibrator source run id is required.", nameof(SourceRunId));
        if (string.IsNullOrWhiteSpace(CorpusFingerprintSha256) || CorpusFingerprintSha256.Length != 64 || !CorpusFingerprintSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Calibrator corpus SHA-256 fingerprint is required.", nameof(CorpusFingerprintSha256));
    }
}

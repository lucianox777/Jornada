using System.Security.Cryptography;
using System.Text;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Fail-closed PRE-FLIGHT for an external synthetic Gold holdout. This contract
/// does not run SQL, certify statistical independence, expose truth labels,
/// select a threshold, refit a model or authorize operational activation.
/// A separate executor must verify the generator provenance and CSV bytes.
/// </summary>
public static class V9GoldIndependentHoldoutContract
{
    public const string Version = "V9_GOLD_INDEPENDENT_HOLDOUT_PREFLIGHT_V1";
    public const string Scope = "INDEPENDENT_HOLDOUT";
    public const string Profile = "demographic-primary/clean";
    public const int RequiredPeople = 30000;

    public sealed record CohortManifest(
        string EvidenceScope,
        string GeneratorProfile,
        ulong Seed,
        int DeclaredPeople,
        string CsvSha256,
        string CohortFingerprintSha256,
        string GoldFittedSnapshotSha256,
        bool TrainingSelectionUsed,
        bool HistoricalValidationUsed,
        bool TestUsed,
        bool Refitted,
        bool OperationallyActivated);

    public sealed record Plan(
        string ContractVersion,
        string EvidenceScope,
        ulong Seed,
        string CohortFingerprintSha256,
        string CsvSha256,
        string GoldFittedSnapshotSha256,
        string IdentityNamespaceSha256,
        decimal CandidateLowerThreshold,
        IReadOnlyList<string> Policies,
        IReadOnlyList<string> OrderIds,
        int RequiredPhysicalTrials,
        bool IndependentlyCertified,
        bool TestUsed,
        bool OperationallyActivated);

    /// <summary>
    /// Verifies declared cohort and pinned TRAIN candidate BEFORE any SQL write.
    /// SHA comparison proves file consistency only; the executor must independently
    /// audit that the generator produced this cohort from the declared fresh seed.
    /// </summary>
    public static Plan Prepare(
        GoldTrainThreeOrderProtocol.FrozenGoldValidationCandidate candidate,
        CohortManifest manifest,
        string actualCsvSha256,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(manifest);
        V9GoldHarnessCommand.ValidateDisposableConnection(connectionString);
        RequireSha(candidate.TrainingCorpusSha256);
        RequireSha(candidate.GoldFittedSnapshotSha256);
        RequireSha(manifest.CsvSha256);
        RequireSha(manifest.CohortFingerprintSha256);
        RequireSha(manifest.GoldFittedSnapshotSha256);
        RequireSha(actualCsvSha256);
        if (candidate.ContractVersion != "GOLD_V6_KNEE_VALIDATION_CANDIDATE_V1"
            || candidate.IndependentlyValidated || candidate.TestUsed
            || candidate.Certified || candidate.OperationallyActivated
            || candidate.CandidateLowerThreshold <= 0m || candidate.CandidateLowerThreshold >= 1m)
            throw new InvalidDataException("Candidato deve permanecer TRAIN, congelado e não certificado.");
        if (manifest.EvidenceScope != Scope || manifest.GeneratorProfile != Profile
            || manifest.Seed != candidate.ProposedHoldoutSeed
            || manifest.Seed is 0 or 42 or 43 or 44 or 20261019001
            || manifest.DeclaredPeople != RequiredPeople
            || manifest.TrainingSelectionUsed || manifest.HistoricalValidationUsed
            || manifest.TestUsed || manifest.Refitted || manifest.OperationallyActivated)
            throw new InvalidDataException("Coorte não é holdout externo sintético pré-declarado.");
        if (!Same(manifest.GoldFittedSnapshotSha256, candidate.GoldFittedSnapshotSha256)
            || !Same(manifest.CsvSha256, actualCsvSha256)
            || Same(manifest.CsvSha256, candidate.TrainingCorpusSha256))
            throw new InvalidDataException("CSV ou fit diverge do manifesto; TRAIN não pode ser reutilizado.");
        var namespaceSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "JORNADA_GOLD_HOLDOUT_ID_NAMESPACE_V1|" + manifest.Seed.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "|" +
            manifest.CohortFingerprintSha256.ToUpperInvariant())));
        return new Plan(Version, Scope, manifest.Seed, manifest.CohortFingerprintSha256,
            manifest.CsvSha256, candidate.GoldFittedSnapshotSha256, namespaceSha,
            candidate.CandidateLowerThreshold, Array.AsReadOnly(new[] { "CURRENT", "KNEE" }),
            GoldTrainThreeOrderProtocol.OrderIds, 6, false, false, false);
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void RequireSha(string sha)
    {
        if (sha is null || sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 obrigatório em hexadecimal com 64 dígitos.");
    }
}

using System.Security.Cryptography;
using System.Text.Json;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Checks the existing C# synthetic generator's materialized manifest, truth
/// provenance and all three output byte hashes before preparing a holdout plan.
/// Does not assert statistical independence or execute SQL.
/// </summary>
public static class V9GoldIndependentHoldoutProvenance
{
    public const string Flag = "--v9-gold-holdout-preflight";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string[] RequiredOutputs =
        ["observacoes.csv", "pessoas_verdade.csv", "gabarito.json"];

    public static async Task<V9GoldIndependentHoldoutContract.Plan> VerifyAsync(
        GoldTrainThreeOrderProtocol.FrozenGoldValidationCandidate candidate,
        string cohortDirectory,
        string disposableConnection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        V9GoldHarnessCommand.ValidateDisposableConnection(disposableConnection);
        var root = Path.GetFullPath(cohortDirectory);
        var manifestPath = Path.Combine(root, "generation-manifest.json");
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, cancellationToken));
        var m = manifest.RootElement;
        if (m.GetProperty("schema_version").GetInt32() != 1
            || m.GetProperty("seed").GetUInt64() != candidate.ProposedHoldoutSeed
            || m.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
            || m.GetProperty("error_profile").GetString() != "clean"
            || m.TryGetProperty("brazilian_name_errors", out _)
            || m.TryGetProperty("stratified_error_overlay", out _))
            throw new InvalidDataException("Manifesto do gerador não corresponde ao holdout clean pré-declarado.");

        var generator = RequiredText(m, "generator_version");
        var ruleset = RequiredText(m, "ruleset_version");
        var rng = RequiredText(m, "rng_version");
        var fingerprint = RequiredText(m, "input_fingerprint_sha256");
        RequireSha(fingerprint);
        var outputHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in m.GetProperty("outputs").EnumerateArray())
        {
            var name = RequiredText(entry, "path");
            var hash = RequiredText(entry, "sha256");
            RequireSha(hash);
            if (!RequiredOutputs.Contains(name, StringComparer.Ordinal)
                || !outputHashes.TryAdd(name, hash))
                throw new InvalidDataException("Manifesto contém saída desconhecida ou duplicada.");
        }
        if (outputHashes.Count != RequiredOutputs.Length)
            throw new InvalidDataException("Manifesto não contém as três saídas canônicas.");

        foreach (var name in RequiredOutputs)
        {
            await using var stream = File.OpenRead(Path.Combine(root, name));
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(actual, outputHashes[name], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Hash de saída do gerador divergente: " + name);
        }

        using var truth = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(root, "gabarito.json"), cancellationToken));
        var t = truth.RootElement;
        if (t.GetProperty("seed").GetUInt64() != candidate.ProposedHoldoutSeed
            || t.GetProperty("people").GetInt32() != V9GoldIndependentHoldoutContract.RequiredPeople
            || t.GetProperty("observations").GetInt32() < V9GoldIndependentHoldoutContract.RequiredPeople
            || t.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
            || t.GetProperty("error_profile").GetString() != "clean"
            || RequiredText(t, "generator_version") != generator
            || RequiredText(t, "ruleset_version") != ruleset
            || RequiredText(t, "rng_version") != rng
            || !string.Equals(RequiredText(t, "input_fingerprint_sha256"), fingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Gabarito não reconcilia com manifesto do gerador.");

        // Identity namespace derives from the generator's input fingerprint and seed.
        // This is integrity/provenance checking, NOT independent statistical certification.
        var declared = new V9GoldIndependentHoldoutContract.CohortManifest(
            V9GoldIndependentHoldoutContract.Scope,
            V9GoldIndependentHoldoutContract.Profile,
            candidate.ProposedHoldoutSeed,
            V9GoldIndependentHoldoutContract.RequiredPeople,
            outputHashes["observacoes.csv"], fingerprint, candidate.GoldFittedSnapshotSha256,
            false, false, false, false, false);
        return V9GoldIndependentHoldoutContract.Prepare(
            candidate, declared, outputHashes["observacoes.csv"], disposableConnection);
    }

    public static async Task RunAsync(string[] args, string disposableConnection)
    {
        if (args.Length != 4 || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-gold-holdout-preflight <candidato-V6.json> <pasta-coorte-nova> <plano-novo.json>.");
        var candidate = JsonSerializer.Deserialize<GoldTrainThreeOrderProtocol.FrozenGoldValidationCandidate>(
            await File.ReadAllBytesAsync(args[1]), Json)
            ?? throw new InvalidDataException("Candidato Gold congelado ausente.");
        var plan = await VerifyAsync(candidate, args[2], disposableConnection);
        var output = Path.GetFullPath(args[3]);
        await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, plan, Json);
    }

    private static string RequiredText(JsonElement element, string property)
    {
        var value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException("Proveniência ausente: " + property)
            : value;
    }

    private static void RequireSha(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 de proveniência inválido.");
    }
}

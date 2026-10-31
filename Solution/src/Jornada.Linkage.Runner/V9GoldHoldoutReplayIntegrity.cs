using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Integrity and deterministic-replay gate for a NEW synthetic Gold cohort.
/// This does not attest that two invocations were independent, establish
/// statistical independence, authorize TEST access, or run the Gold harness.
/// The orchestration must invoke the pinned generator twice with trusted inputs.
/// </summary>
public static class V9GoldHoldoutReplayIntegrity
{
    public const string Version = "V9_GOLD_HOLDOUT_REPLAY_INTEGRITY_V1";
    public const ulong PredeclaredSeed = 20261025001UL;
    private const int RequiredPeople = 30000;
    private static readonly string[] PeopleHeader =
        ["base_person_id", "particao", "nome", "nome_mae", "data_nascimento",
         "sexo", "cpf", "cns", "cns_scenario", "evaluation_weight"];
    private static readonly string[] ObservationsHeader =
        ["observacao_id", "base_person_id", "particao", "gestor", "nome",
         "nome_mae", "data_nascimento", "sexo", "cpf", "cns",
         "corrupcoes", "evaluation_weight"];

    public sealed record Evidence(
        string ContractVersion, ulong Seed, int People, int Observations,
        string GeneratorVersion, string InputFingerprintSha256,
        string ManifestSha256, string ObservationsSha256,
        string PeopleSha256, string TruthSha256, string CohortFingerprintSha256,
        bool ReplayBytesMatched, bool IndependentlyCertified,
        bool TestUsed, bool OperationallyActivated);

    public static Evidence Verify(
        ReadOnlyMemory<byte> manifest, ReadOnlyMemory<byte> observations,
        ReadOnlyMemory<byte> people, ReadOnlyMemory<byte> truth,
        ReadOnlyMemory<byte> replayManifest, ReadOnlyMemory<byte> replayObservations,
        ReadOnlyMemory<byte> replayPeople, ReadOnlyMemory<byte> replayTruth,
        ulong expectedSeed, string originalTrainObservationsSha256,
        string expectedGeneratorVersion)
    {
        RequireSha(originalTrainObservationsSha256);
        if (expectedSeed != PredeclaredSeed || string.IsNullOrWhiteSpace(expectedGeneratorVersion))
            throw new InvalidDataException("Seed e versão do gerador devem ser pré-declaradas.");
        var manifestSha = Hash(manifest);
        var observationsSha = Hash(observations);
        var peopleSha = Hash(people);
        var truthSha = Hash(truth);
        if (!Same(manifestSha, Hash(replayManifest))
            || !Same(observationsSha, Hash(replayObservations))
            || !Same(peopleSha, Hash(replayPeople))
            || !Same(truthSha, Hash(replayTruth)))
            throw new InvalidDataException("Replay determinístico não reproduziu todos os bytes.");
        if (Same(observationsSha, originalTrainObservationsSha256))
            throw new InvalidDataException("CSV de TRAIN não é holdout independente.");

        try
        {
            using var m = JsonDocument.Parse(manifest);
            using var t = JsonDocument.Parse(truth);
            var root = m.RootElement;
            var tr = t.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1
                || root.GetProperty("seed").GetUInt64() != expectedSeed
                || tr.GetProperty("seed").GetUInt64() != expectedSeed
                || root.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
                || tr.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
                || root.GetProperty("error_profile").GetString() != "clean"
                || tr.GetProperty("error_profile").GetString() != "clean"
                || root.TryGetProperty("brazilian_name_errors", out _)
                || root.TryGetProperty("stratified_error_overlay", out _))
                throw new InvalidDataException("Manifesto não corresponde à coorte clean pré-declarada.");

            var generator = root.GetProperty("generator_version").GetString();
            var input = root.GetProperty("input_fingerprint_sha256").GetString();
            RequireSha(input);
            if (generator != expectedGeneratorVersion
                || tr.GetProperty("generator_version").GetString() != generator
                || !Same(input!, tr.GetProperty("input_fingerprint_sha256").GetString())
                || root.GetProperty("ruleset_version").GetString() != tr.GetProperty("ruleset_version").GetString()
                || root.GetProperty("rng_version").GetString() != tr.GetProperty("rng_version").GetString())
                throw new InvalidDataException("Versão do gerador, RNG, regras ou referências divergem.");

            var declared = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in root.GetProperty("outputs").EnumerateArray())
            {
                var path = entry.GetProperty("path").GetString();
                var sha = entry.GetProperty("sha256").GetString();
                RequireSha(sha);
                if (path is null || !declared.TryAdd(path, sha!))
                    throw new InvalidDataException("Saída ausente ou duplicada no manifesto.");
            }
            if (declared.Count != 3
                || !declared.TryGetValue("observacoes.csv", out var declaredObservations)
                || !declared.TryGetValue("pessoas_verdade.csv", out var declaredPeople)
                || !declared.TryGetValue("gabarito.json", out var declaredTruth)
                || !Same(declaredObservations, observationsSha)
                || !Same(declaredPeople, peopleSha)
                || !Same(declaredTruth, truthSha))
                throw new InvalidDataException("SHA das três saídas diverge do manifesto.");

            var peopleIds = ReadCsvKeys(people, PeopleHeader, null);
            var observationIds = ReadCsvKeys(observations, ObservationsHeader, peopleIds);
            if (peopleIds.Count != RequiredPeople
                || tr.GetProperty("people").GetInt32() != peopleIds.Count
                || tr.GetProperty("observations").GetInt32() != observationIds.Count)
                throw new InvalidDataException("População ou observações divergem do gabarito.");

            var cohortFingerprint = Hash(Encoding.UTF8.GetBytes(
                Version + "|" + expectedSeed.ToString(CultureInfo.InvariantCulture) + "|"
                + input!.ToUpperInvariant() + "|" + manifestSha + "|" + observationsSha));
            return new Evidence(Version, expectedSeed, peopleIds.Count, observationIds.Count,
                generator!, input!, manifestSha, observationsSha, peopleSha, truthSha,
                cohortFingerprint, true, false, false, false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Manifesto ou gabarito JSON inválido.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidDataException("Campo obrigatório ausente na proveniência.", ex);
        }
    }

    private static HashSet<string> ReadCsvKeys(
        ReadOnlyMemory<byte> bytes, string[] header, HashSet<string>? people)
    {
        using var parser = new TextFieldParser(new StringReader(Encoding.UTF8.GetString(bytes.Span)))
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        if (parser.ReadFields() is not { } columns
            || !columns.SequenceEqual(header, StringComparer.Ordinal))
            throw new InvalidDataException("Cabeçalho CSV incompatível com gerador congelado.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || fields.Length != header.Length
                || string.IsNullOrWhiteSpace(fields[0])
                || !ids.Add(fields[0])
                || (people is not null && !people.Contains(fields[1])))
                throw new InvalidDataException("CSV contém fonte duplicada, órfã ou inválida.");
        }
        return ids;
    }

    private static string Hash(ReadOnlyMemory<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes.Span));

    private static bool Same(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void RequireSha(string? value)
    {
        if (value is null || value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("SHA-256 obrigatório em hexadecimal.");
    }
}

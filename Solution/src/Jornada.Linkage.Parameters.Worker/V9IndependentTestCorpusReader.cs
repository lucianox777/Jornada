using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// TEST-owned corpus reader. Never receives a TRAIN/VALIDATION file, plan,
/// selection envelope or other finalist. The independent generator's TEST partition (20% of its 100k population)
/// is the scientific holdout. Other internal partitions are integrity-checked
/// but never exposed to the evaluator. TEST has a corpus-scoped identity namespace.
/// </summary>
public static class V9IndependentTestCorpusReader
{
    public sealed record TestCorpus(string CorpusManifestSha256, string ObservationsSha256,
        ulong Seed, int BaseIdentities, int SelectedBaseIdentities, V9PhysicalCalibrationObservation[] Observations);

    public static async Task<TestCorpus> ReadAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Diretório do corpus TEST isolado ausente ou linkado.");
        var expected = new HashSet<string>(StringComparer.Ordinal)
        { "generation-manifest.json", "observacoes.csv", "pessoas_verdade.csv", "gabarito.json" };
        if (!Directory.EnumerateFiles(root).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal).SetEquals(expected)
            || Directory.EnumerateDirectories(root).Any())
            throw new InvalidDataException("Corpus TEST contém arquivos adicionais ou incompletos.");
        foreach (var name in expected)
            if (File.GetAttributes(Path.Combine(root, name)).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Arquivos TEST não admitem links.");
        var manifestFile = Path.Combine(root, "generation-manifest.json");
        var manifestBytes = await File.ReadAllBytesAsync(manifestFile, cancellationToken);
        var manifestSha = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        using var doc = JsonDocument.Parse(manifestBytes);
        var m = doc.RootElement;
        var expectedSeedText = Environment.GetEnvironmentVariable("JORNADA_V9_TEST_RESERVED_SEED");
        if (!ulong.TryParse(expectedSeedText, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedSeed)
            || expectedSeed == 0 || expectedSeed is 42UL or 20261112001UL or 20261113001UL)
            throw new InvalidDataException("TEST exige seed inédita vinculada à reserva oficial.");
        var seed = m.GetProperty("seed").GetUInt64();
        if (seed != expectedSeed
            || m.GetProperty("schema_version").GetInt32() != 1
            || m.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
            || m.GetProperty("error_profile").GetString() != "clean"
            || m.GetProperty("reference_code").GetString() != "CENSO2022_NOMES_BRASIL_V1")
            throw new InvalidDataException("TEST não corresponde ao protocolo da própria camada TEST.");
        var outputs = m.GetProperty("outputs").EnumerateArray()
            .ToDictionary(x => x.GetProperty("path").GetString()!,
                x => x.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        if (!outputs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Except(["generation-manifest.json"])))
            throw new InvalidDataException("Ledger de arquivos do TEST inválido.");
        foreach (var (name, expectedSha) in outputs)
        {
            await using var stream = File.OpenRead(Path.Combine(root, name));
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(expectedSha, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Dados de TEST alterados após lacre.");
        }
        using var csv = new TextFieldParser(Path.Combine(root, "observacoes.csv"));
        csv.SetDelimiters(",");
        csv.HasFieldsEnclosedInQuotes = true;
        var header = csv.ReadFields() ?? throw new InvalidDataException("CSV TEST sem cabeçalho.");
        var columns = header.Select((name, i) => (name, i))
            .ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        foreach (var required in new[] { "base_person_id", "particao", "gestor", "nome",
                    "nome_mae", "data_nascimento", "cpf" })
            if (!columns.ContainsKey(required))
                throw new InvalidDataException("Coluna TEST ausente: " + required);
        var byIdentity = new Dictionary<string, (string Part, int Count)>(StringComparer.Ordinal);
        var rows = new List<V9PhysicalCalibrationObservation>();
        var ns = "EXTERNAL_TEST_" + manifestSha + ":";
        while (!csv.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = csv.ReadFields() ?? throw new InvalidDataException("CSV TEST inválido.");
            if (fields.Length != header.Length) throw new InvalidDataException("CSV TEST malformado.");
            string Get(string column) => fields[columns[column]];
            var id = Get("base_person_id");
            var internalPartition = Get("particao");
            if (id.Length == 0 || internalPartition is not ("TRAIN" or "VALIDATION" or "TEST"))
                throw new InvalidDataException("Identidade ou partição interna do gerador inválida.");
            if (byIdentity.TryGetValue(id, out var prev) && prev.Part != internalPartition)
                throw new InvalidDataException("Gerador dividiu identidade-base dentro do TEST.");
            var count = prev.Count + 1;
            if (count > 4) throw new InvalidDataException("Cap de multiplicidade TEST excedido.");
            byIdentity[id] = (internalPartition, count);
            // Mirror VALIDATION: only the generator-owned 20% TEST partition is evaluated.
            // Check all identities for integrity first; never sample individual rows.
            if (internalPartition != "TEST" || !string.IsNullOrWhiteSpace(Get("cpf")))
                continue; // FS sem CPF, inclusive no conjunto TEST.
            var birth = Get("data_nascimento");
            var date = string.IsNullOrWhiteSpace(birth) ? (DateOnly?)null
                : DateOnly.ParseExact(birth, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            rows.Add(new V9PhysicalCalibrationObservation(checked(rows.Count + 1),
                ns + id, "TEST", Get("gestor"), Get("nome"), Get("nome_mae"), date));
        }
        if (byIdentity.Count != 100000 || rows.Count < 4)
            throw new InvalidDataException("População TEST de 100 mil identidades incompleta.");
        var testIdentities = byIdentity.Count(x => x.Value.Part == "TEST");
        // Same Bernoulli 60/20/20 partition rule as SyntheticCorpusGenerator.
        // With 100k people, a 19%-21% admission window is wider than six standard deviations.
        if (testIdentities < 19000 || testIdentities > 21000)
            throw new InvalidDataException("Partição TEST independente não corresponde ao cohort de aproximadamente 20%.");
        return new TestCorpus(manifestSha, outputs["observacoes.csv"], seed,
            byIdentity.Count, testIdentities, rows.ToArray());
    }
}

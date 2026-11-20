using Jornada.Linkage.SyntheticCorpus;
using Jornada.Contracts;

var root = Path.Combine(Path.GetTempPath(), "p0-partition-integrity-" + Guid.NewGuid().ToString("N"));
var person = new SyntheticPerson
{
    BasePersonId = "P1", Partition = "TRAIN", Name = "Teste", MotherName = "Mae",
    BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1
};
SyntheticObservation Observation(string id, string baseId = "P1", string partition = "TRAIN") => new()
{
    ObservationId = id, BasePersonId = baseId, Partition = partition,
    Gestor = "G1", Sex = "F", EvaluationWeight = 1, Corruptions = ""
};
async Task ExpectRejected(string label, IReadOnlyList<SyntheticPerson> people,
    IReadOnlyList<SyntheticObservation> observations)
{
    var output = Path.Combine(root, label);
    var generation = new SyntheticCorpusGeneration(people, observations,
        new Dictionary<string, SyntheticEmpiricalM>(), new SyntheticCorpusOptions());
    try
    {
        await SyntheticCorpusMaterializer.WriteAsync(output, generation,
            (SyntheticCorpusNominalSource)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(SyntheticCorpusNominalSource)), "TEST");
        throw new InvalidOperationException($"Accepted invalid fixture: {label}");
    }
    catch (InvalidDataException)
    {
        if (Directory.Exists(output))
            throw new InvalidOperationException($"Created output directory before rejection: {label}");
        Console.WriteLine($"PASS {label}");
    }
}
async Task ExpectInvalidOptions()
{
    var output = Path.Combine(root, "invalid-options");
    var invalid = new SyntheticCorpusOptions(TrainPercent: 80, ValidationPercent: 20, TestPercent: 20);
    var generation = new SyntheticCorpusGeneration([person], [Observation("O1")],
        new Dictionary<string, SyntheticEmpiricalM>(), invalid);
    try
    {
        await SyntheticCorpusMaterializer.WriteAsync(output, generation,
            (SyntheticCorpusNominalSource)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(SyntheticCorpusNominalSource)), "TEST");
        throw new InvalidOperationException("Accepted invalid partition percentages");
    }
    catch (ArgumentOutOfRangeException)
    {
        if (Directory.Exists(output))
            throw new InvalidOperationException("Created output directory before invalid options rejection");
        Console.WriteLine("PASS invalid-options");
    }
}
async Task ExpectValidMaterialization()
{
    var output = Path.Combine(root, "valid-materialization");
    var options = new SyntheticCorpusOptions(People: 3, Seed: 77,
        TrainPercent: 70, ValidationPercent: 20, TestPercent: 10);
    SyntheticPerson PartitionPerson(string id, string partition) => new()
    {
        BasePersonId = id, Partition = partition, Name = "Teste", MotherName = "Mae",
        BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1
    };
    var generation = new SyntheticCorpusGeneration(
        [person, PartitionPerson("P2", "VALIDATION"), PartitionPerson("P3", "TEST")],
        [Observation("O1"), Observation("O2", "P2", "VALIDATION"),
            Observation("O3", "P3", "TEST")],
        new Dictionary<string, SyntheticEmpiricalM>(), options);
    // This fixture deliberately avoids external IBGE inputs and scientific campaigns.
    var source = (SyntheticCorpusNominalSource)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(SyntheticCorpusNominalSource));
    var result = await SyntheticCorpusMaterializer.WriteAsync(
        output, generation, source, "abc123");
    var peopleCsv = await File.ReadAllTextAsync(result.PeoplePath);
    var observationsCsv = await File.ReadAllTextAsync(result.ObservationsPath);
    // These controlled fixtures contain no CSV delimiters or embedded newlines.
    // Compare complete rows rather than substring matches that could hide extra records.
    var peopleRows = peopleCsv.Split('\\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\\r')).ToArray();
    var observationRows = observationsCsv.Split('\\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\\r')).ToArray();
    if (peopleRows.Length != 4 || observationRows.Length != 4)
        throw new InvalidOperationException("Unexpected CSV record count");
    var peopleHeader = peopleRows[0].Split(',');
    var observationHeader = observationRows[0].Split(',');
    if (peopleHeader[0] != "base_person_id" || peopleHeader[1] != "particao" ||
        observationHeader[0] != "observacao_id" ||
        observationHeader[1] != "base_person_id" || observationHeader[2] != "particao")
        throw new InvalidOperationException("Unexpected CSV partition columns");
    var expectedPeople = new[] { ("P1", "TRAIN"), ("P2", "VALIDATION"), ("P3", "TEST") };
    var expectedObservations = new[]
    {
        ("O1", "P1", "TRAIN"), ("O2", "P2", "VALIDATION"), ("O3", "P3", "TEST")
    };
    for (var i = 0; i < expectedPeople.Length; i++)
    {
        var row = peopleRows[i + 1].Split(',');
        if (row.Length != peopleHeader.Length ||
            row[0] != expectedPeople[i].Item1 || row[1] != expectedPeople[i].Item2)
            throw new InvalidOperationException($"Unexpected person CSV row {i + 1}");
    }
    for (var i = 0; i < expectedObservations.Length; i++)
    {
        var row = observationRows[i + 1].Split(',');
        if (row.Length != observationHeader.Length ||
            row[0] != expectedObservations[i].Item1 ||
            row[1] != expectedObservations[i].Item2 ||
            row[2] != expectedObservations[i].Item3)
            throw new InvalidOperationException($"Unexpected observation CSV row {i + 1}");
    }
    using var manifest = System.Text.Json.JsonDocument.Parse(
        await File.ReadAllTextAsync(result.ManifestPath));
    var rootElement = manifest.RootElement;
    var percentages = rootElement.GetProperty("partition_percentages");
    if (rootElement.GetProperty("synthetic_population").GetInt32() != 3 ||
        percentages.GetProperty("train").GetDouble() != 70 ||
        percentages.GetProperty("validation").GetDouble() != 20 ||
        percentages.GetProperty("test").GetDouble() != 10 ||
        rootElement.GetProperty("input_fingerprint_sha256").GetString() != "ABC123")
        throw new InvalidOperationException("Valid materialization manifest metadata mismatch");
    using var truth = System.Text.Json.JsonDocument.Parse(
        await File.ReadAllTextAsync(result.TruthPath));
    if (truth.RootElement.GetProperty("people").GetInt32() != 3 ||
        truth.RootElement.GetProperty("observations").GetInt32() != 3 ||
        truth.RootElement.GetProperty("seed").GetUInt64() != 77 ||
        truth.RootElement.GetProperty("input_fingerprint_sha256").GetString() != "ABC123")
        throw new InvalidOperationException("Valid materialization truth metadata mismatch");
    var manifestHashes = rootElement.GetProperty("outputs").EnumerateArray()
        .ToDictionary(entry => entry.GetProperty("path").GetString()!,
            entry => entry.GetProperty("sha256").GetString()!,
            StringComparer.Ordinal);
    if (manifestHashes.Count != 3 ||
        !manifestHashes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
            new[] { "pessoas_verdade.csv", "observacoes.csv", "gabarito.json" }))
        throw new InvalidOperationException("Valid materialization manifest output set mismatch");
    if (result.OutputSha256.Count != 3)
        throw new InvalidOperationException("Valid materialization returned unexpected hash count");
    foreach (var outputHash in result.OutputSha256)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(output, outputHash.Key));
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (!manifestHashes.TryGetValue(outputHash.Key, out var declaredHash) ||
            !string.Equals(declaredHash, outputHash.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Manifest output hash mismatch: {outputHash.Key}");
        if (!string.Equals(actual, outputHash.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Materialization hash mismatch: {outputHash.Key}");
    }
    Console.WriteLine("PASS valid-materialization");
}

try
{
    await ExpectValidMaterialization();
    await ExpectInvalidOptions();
    await ExpectRejected("empty-people-with-observation", [], [Observation("O1")]);
    await ExpectRejected("duplicate-person-cross-partition",
        [person, new SyntheticPerson { BasePersonId = "P1", Partition = "TEST", Name = "Teste",
            MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F",
            EvaluationWeight = 1 }], [Observation("O1")]);
    await ExpectRejected("null-person-entry", [null!], []);
    await ExpectRejected("null-observation-entry", [person], [null!]);
    await ExpectRejected("orphan", [person], [Observation("O1", "UNKNOWN")]);
    await ExpectRejected("cross-partition", [person], [Observation("O1", partition: "TEST")]);
    await ExpectRejected("duplicate-person", [person, person], [Observation("O1")]);
    await ExpectRejected("null-person-base-id", [new SyntheticPerson { BasePersonId = null!, Partition = "TRAIN", Name = "Teste", MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1 }], []);
    await ExpectRejected("empty-base-id", [new SyntheticPerson { BasePersonId = " ", Partition = "TRAIN", Name = "Teste", MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1 }], []);
    await ExpectRejected("duplicate-observation", [person], [Observation("O1"), Observation("O1")]);
    await ExpectRejected("null-observation-id", [person], [Observation(null!)]);
    await ExpectRejected("empty-observation-id", [person], [Observation("")]);
    await ExpectRejected("partition-case-sensitive", [person], [Observation("O1", partition: "train")]);
    await ExpectRejected("whitespace-observation-id", [person], [Observation("  ")]);
    await ExpectRejected("null-base-id", [person], [Observation("O1", baseId: null!)]);
    await ExpectRejected("whitespace-base-id", [person], [Observation("O1", baseId: "  ")]);
    await ExpectRejected("excess-multiplicity", [person],
        Enumerable.Range(0, SyntheticObservationMultiplicityContract.MaximumObservationsPerBaseIdentity + 1)
            .Select(i => Observation($"O{i}")).ToArray());
    await ExpectRejected("invalid-partition", [new SyntheticPerson { BasePersonId = "P1", Partition = "OTHER", Name = "Teste", MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1 }], []); 
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

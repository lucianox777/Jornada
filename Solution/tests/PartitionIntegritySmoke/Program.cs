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
    var options = new SyntheticCorpusOptions(People: 1, Seed: 77,
        TrainPercent: 70, ValidationPercent: 20, TestPercent: 10);
    var generation = new SyntheticCorpusGeneration([person], [Observation("O1")],
        new Dictionary<string, SyntheticEmpiricalM>(), options);
    // This fixture deliberately avoids external IBGE inputs and scientific campaigns.
    var source = (SyntheticCorpusNominalSource)System.Runtime.CompilerServices.RuntimeHelpers
        .GetUninitializedObject(typeof(SyntheticCorpusNominalSource));
    var result = await SyntheticCorpusMaterializer.WriteAsync(
        output, generation, source, "abc123");
    var peopleCsv = await File.ReadAllTextAsync(result.PeoplePath);
    var observationsCsv = await File.ReadAllTextAsync(result.ObservationsPath);
    if (!peopleCsv.Contains("base_person_id,particao", StringComparison.Ordinal) ||
        !peopleCsv.Contains("P1,TRAIN", StringComparison.Ordinal) ||
        !observationsCsv.Contains("observacao_id,base_person_id,particao", StringComparison.Ordinal) ||
        !observationsCsv.Contains("O1,P1,TRAIN", StringComparison.Ordinal))
        throw new InvalidOperationException("Valid materialization lost partition CSV columns or values");
    using var manifest = System.Text.Json.JsonDocument.Parse(
        await File.ReadAllTextAsync(result.ManifestPath));
    var rootElement = manifest.RootElement;
    var percentages = rootElement.GetProperty("partition_percentages");
    if (rootElement.GetProperty("synthetic_population").GetInt32() != 1 ||
        percentages.GetProperty("train").GetDouble() != 70 ||
        percentages.GetProperty("validation").GetDouble() != 20 ||
        percentages.GetProperty("test").GetDouble() != 10 ||
        rootElement.GetProperty("input_fingerprint_sha256").GetString() != "ABC123")
        throw new InvalidOperationException("Valid materialization manifest metadata mismatch");
    foreach (var outputHash in result.OutputSha256)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(output, outputHash.Key));
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
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

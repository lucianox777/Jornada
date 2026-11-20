using Jornada.Linkage.SyntheticCorpus;

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
        await SyntheticCorpusMaterializer.WriteAsync(output, generation, null!, "TEST");
        throw new Exception($"Accepted invalid fixture: {label}");
    }
    catch (InvalidDataException)
    {
        if (Directory.Exists(output))
            throw new Exception($"Created output directory before rejection: {label}");
        Console.WriteLine($"PASS {label}");
    }
}
try
{
    await ExpectRejected("orphan", [person], [Observation("O1", "UNKNOWN")]);
    await ExpectRejected("cross-partition", [person], [Observation("O1", partition: "TEST")]);
    await ExpectRejected("duplicate-person", [person, person], [Observation("O1")]);
    await ExpectRejected("empty-base-id", [new SyntheticPerson { BasePersonId = " ", Partition = "TRAIN", Name = "Teste", MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1 }], []);
    await ExpectRejected("duplicate-observation", [person], [Observation("O1"), Observation("O1")]);
    await ExpectRejected("empty-observation-id", [person], [Observation("")]);
    await ExpectRejected("invalid-partition", [new SyntheticPerson { BasePersonId = "P1", Partition = "OTHER", Name = "Teste", MotherName = "Mae", BirthDate = new DateOnly(2000, 1, 1), Sex = "F", EvaluationWeight = 1 }], []); 
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

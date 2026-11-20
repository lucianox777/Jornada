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
try
{
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

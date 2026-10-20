using Jornada.Contracts;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9FrozenScoringParityTests
{
    [Test]
    public void Cached_fitted_context_matches_public_diagnostic_score_for_all_81_states_and_birth_cases()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Solution", "src"))) directory = directory.Parent;
        const string sha = "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d";
        var snapshot = V9FittedSnapshot.Load(File.ReadAllBytes(Path.Combine(directory!.FullName,
            "Solution/data/calibration/v9/bootstrap-v0/" + sha + ".fitted.json")), sha);
        (string? Left, string? Right)[] names = [(null, "Maria Silva"), ("Maria Silva", "Maria Silva"),
            ("Maria Aparecida Silva", "Maria Cecilia Silva"), ("Alice Costa", "Costa Alice"),
            ("Maria Silva", "Maria Silvaa"), ("Maria Aparecida Oliveira Silva", "Maria Cecilia Barbosa Silvaa"),
            ("Joao Silva", "Pedro Silva"), ("Maria Silva", "Maria Costa"), ("Alice Costa", "Pedro Lima")];
        DateOnly?[] births = [null, new(1980, 1, 1), new(1981, 2, 2)];
        var target = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var cases = 0;
        foreach (var person in names)
        foreach (var mother in names)
        foreach (var leftBirth in births)
        foreach (var rightBirth in births)
        {
            var observation = new IdentityObservation(null, "SEM_CPF", person.Left, leftBirth, mother.Left);
            var candidate = new V9SnapshotCandidate(target, person.Right, rightBirth, mother.Right);
            var cached = snapshot.RankPreview(observation, [candidate]).Single();
            var diagnostic = FellegiSunterScoring.CalculateCompositeNominalV9PreviewWithBreakdown(
                snapshot.Payload.Parameters, person.Left, person.Right, mother.Left, mother.Right,
                snapshot.Payload.JointCells, snapshot.Payload.NominalContractVersion, 1, leftBirth, rightBirth);
            Assert.That(cached.Score, Is.EqualTo(diagnostic.Score.Posterior));
            Assert.That(cached.LogOdds, Is.EqualTo(diagnostic.Score.LogOdds));
            cases++;
        }
        Assert.That(cases, Is.EqualTo(729));
    }
}

using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingProjectionKeyProjectorTests
{
    [Test]
    public void Project_UsesCanonicalNormalizationForPersonAndMotherNames()
    {
        var keys = BlockingProjectionKeyProjector.Project(
            "  María   da Silva ",
            "Ana de Souza",
            new DateOnly(1982, 4, 10));

        Assert.Multiple(() =>
        {
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FirstName, "MARIA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.Surnames, "DA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.Surnames, "SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.LastName, "SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherFirstName, "ANA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherSurnames, "DE")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherSurnames, "SOUZA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherLastName, "SOUZA")));
        });
    }

    [Test]
    public void Project_DecomposesBirthDateIntoStableKeys()
    {
        var keys = BlockingProjectionKeyProjector.Project(
            "Maria Silva",
            "Ana Souza",
            new DateOnly(1982, 4, 10));

        Assert.Multiple(() =>
        {
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.BirthDay, "10")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.BirthMonth, "04")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.BirthYear, "1982")));
        });
    }

    [Test]
    public void Project_IsDeterministicAndDoesNotEmitDuplicateKeys()
    {
        var first = BlockingProjectionKeyProjector.Project(
            "Joao Silva Silva",
            "Maria Souza Souza",
            new DateOnly(2000, 1, 2));
        var second = BlockingProjectionKeyProjector.Project(
            "João   Silva Silva",
            "Maria Souza Souza",
            new DateOnly(2000, 1, 2));

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Distinct().Count(), Is.EqualTo(first.Count));
    }
}

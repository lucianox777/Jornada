using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class BlockingProjectionKeyProjectorTests
{
    [Test]
    public void Project_UsesCanonicalAndBasicPtBrNameProjections()
    {
        var keys = BlockingProjectionKeyProjector.Project(
            "  María   da Silva ",
            "Ana de Souza",
            new DateOnly(1982, 4, 10));

        Assert.Multiple(() =>
        {
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpper, "MARÍA DA SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics, "MARIA DA SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameWithoutParticles, "MARIA SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullName, "MARIA DA SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FirstName, "MARIA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.Surnames, "DA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.Surnames, "SILVA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.LastName, "SILVA")));

            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherFullNameUpper, "ANA DE SOUZA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherFullNameUpperNoDiacritics, "ANA DE SOUZA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherFullNameWithoutParticles, "ANA SOUZA")));
            Assert.That(keys, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.MotherFullName, "ANA DE SOUZA")));
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
    public void Project_AccentSensitiveAndInsensitiveRepresentationsAreBothAvailable()
    {
        var withoutAccent = BlockingProjectionKeyProjector.Project(
            "Joao Silva",
            "Maria Souza",
            new DateOnly(2000, 1, 2));
        var withAccent = BlockingProjectionKeyProjector.Project(
            "João Silva",
            "Maria Souza",
            new DateOnly(2000, 1, 2));

        Assert.Multiple(() =>
        {
            Assert.That(withoutAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpper, "JOAO SILVA")));
            Assert.That(withAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpper, "JOÃO SILVA")));
            Assert.That(withoutAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics, "JOAO SILVA")));
            Assert.That(withAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullNameUpperNoDiacritics, "JOAO SILVA")));
            Assert.That(withoutAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullName, "JOAO SILVA")));
            Assert.That(withAccent, Does.Contain(new BlockingProjectionKey(
                BlockingCandidateFeatureCatalog.FullName, "JOAO SILVA")));
        });
    }

    [Test]
    public void Project_IsDeterministicAndDoesNotEmitDuplicateKeys()
    {
        var first = BlockingProjectionKeyProjector.Project(
            "João Silva Silva",
            "Maria Souza Souza",
            new DateOnly(2000, 1, 2));
        var second = BlockingProjectionKeyProjector.Project(
            "João Silva Silva",
            "Maria Souza Souza",
            new DateOnly(2000, 1, 2));

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.Distinct().Count(), Is.EqualTo(first.Count));
    }
}

using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class CompositeNominalComparisonV9Tests
{
    [Test]
    public void FullNameIncludesAllMiddleTokensAndAgnome()
    {
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Maria Aparecida de Oliveira Santos", "MARIA APARECIDA DE OLIVEIRA SANTOS"),
            Is.EqualTo(CompositeNominalLevelV9.ExactFull));

        // First and last are equal, but differences in the middle are NOT
        // silently promoted to whole-name exact agreement.
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Maria Aparecida de Oliveira Santos", "Maria Cecília de Oliveira Santos"),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirstLast));

        Assert.That(CompositeNominalComparisonV9.Classify(
            "João da Silva Filho", "João da Silva Neto"),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirstLast));
    }

    [Test]
    public void ReversedFirstLastIsOneExclusiveLevel()
    {
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Alice da Costa", "Costa Alice"),
            Is.EqualTo(CompositeNominalLevelV9.ReversedFirstLast));
    }

    [Test]
    public void HighWholeNameIsDistinctFromExact()
    {
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Maria Silva", "Maria Silvaa"),
            Is.EqualTo(CompositeNominalLevelV9.HighFull));
    }

    [Test]
    public void IncompleteAndOneTokenNamesDoNotInventSurnames()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.That(CompositeNominalComparisonV9.Classify(null, "Maria Silva"),
                Is.EqualTo(CompositeNominalLevelV9.Missing));
            Assert.That(CompositeNominalComparisonV9.Classify("  ", "Maria Silva"),
                Is.EqualTo(CompositeNominalLevelV9.Missing));
            Assert.That(CompositeNominalComparisonV9.Classify("Maria", "MARIA"),
                Is.EqualTo(CompositeNominalLevelV9.ExactFull));
            Assert.That(CompositeNominalComparisonV9.Classify("Maria", "Maria Silva"),
                Is.EqualTo(CompositeNominalLevelV9.ExactFirst));
        }));
    }

    [Test]
    public void PersonAndMotherUseIdenticalPureContract()
    {
        var person = CompositeNominalComparisonV9.Classify(
            "José da Silva", "José da Silva");
        var mother = CompositeNominalComparisonV9.Classify(
            "Ana Maria dos Santos", "Ana Cecília dos Santos");

        Assert.Multiple((Action)(() =>
        {
            Assert.That(person, Is.EqualTo(CompositeNominalLevelV9.ExactFull));
            Assert.That(mother, Is.EqualTo(CompositeNominalLevelV9.ExactFirstLast));
        }));
    }

    [Test]
    public void TitleRemovalIsAuxiliaryOnly()
    {
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Dra. Ana de Souza", "Ana de Souza"),
            Is.EqualTo(CompositeNominalLevelV9.ExactFirstLast));
        Assert.That(CompositeNominalComparisonV9.Classify(
            "Dra. Ana de Souza", "Dra. Ana de Souza"),
            Is.EqualTo(CompositeNominalLevelV9.ExactFull));
    }

    [Test]
    public void DraftContractContainsOnlyAClassificationNotUncalibratedWeight()
    {
        Assert.That(Enum.GetValues<CompositeNominalLevelV9>(), Has.Length.EqualTo(9));
        Assert.That(typeof(CompositeNominalComparisonV9).GetMethods()
            .Any(m => m.Name.Contains("Score", StringComparison.Ordinal)
                   || m.Name.Contains("Bayes", StringComparison.Ordinal)
                   || m.Name.Contains("Resolve", StringComparison.Ordinal)), Is.False);
    }
}

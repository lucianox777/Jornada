using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class V9MemoryCellExplorerTests
{
    [Test]
    public void Counts_M_U_and_abstains_without_person_name()
    {
        V9MemoryCellExplorer.Row[] rows =
        [
            new(1,"TRAIN","G1","MARIA SILVA","ANA SOUZA",new DateTime(1980,1,2),100),
            new(2,"TRAIN","G2","MARIA SILVA","ANA SOUZA",new DateTime(1980,1,2),100),
            new(3,"TRAIN","G3","MARIA SILVA","ANA SOUZA",new DateTime(1980,1,2),200),
            new(4,"TRAIN","G4",null,"ANA SOUZA",new DateTime(1980,1,2),300),
            new(5,"VALIDATION","G1","MARIA SILVA","ANA SOUZA",new DateTime(1980,1,2),100),
            new(6,"VALIDATION","G2","MARIA SILVA","ANA SOUZA",new DateTime(1980,1,2),100)
        ];
        var report = V9MemoryCellExplorer.Analyze(rows);
        var train = report.Single(x => x.Partition == "TRAIN" && x.Cell == "PERSON_EXACT_DOB");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(train.M, Is.EqualTo(1));
            Assert.That(train.U, Is.EqualTo(2));
            Assert.That(train.EligibleTruthM, Is.EqualTo(1));
            Assert.That(train.RecallEligible, Is.EqualTo(1m));
            Assert.That(train.HypotheticalFdr, Is.EqualTo(2m / 3m));
            Assert.That(train.P99U, Is.EqualTo(2));
            Assert.That(report.Single(x => x.Partition == "VALIDATION"
                && x.Cell == "PERSON_EXACT_DOB").M, Is.EqualTo(1));
        }));
    }

    [Test]
    public void NormalizedAndPhoneticCellsRecoverSpellingVariants()
    {
        V9MemoryCellExplorer.Row[] rows =
        [
            new(1,"TRAIN","G1","JOSÉ SILVA","ANA SOUZA",new DateTime(1980,1,2),100),
            new(2,"TRAIN","G2","JOSE SILVA","ANA SOUZA",new DateTime(1980,1,2),100),
            new(3,"VALIDATION","G1","MARIA DA CONCEIÇÃO","ANA SOUZA",new DateTime(1980,1,2),200),
            new(4,"VALIDATION","G2","MARIA CONCEICAO","ANA SOUZA",new DateTime(1980,1,2),200)
        ];
        var results = V9MemoryCellExplorer.Analyze(rows);
        Assert.That(results.Single(x => x.Partition == "TRAIN"
            && x.Cell == "PERSON_EXACT_DOB").M, Is.EqualTo(1));
        Assert.That(results.Single(x => x.Partition == "VALIDATION"
            && x.Cell == "PERSON_NO_PARTICLES_MOTHER").M, Is.EqualTo(1));
        Assert.That(results.Single(x => x.Partition == "TRAIN"
            && x.Cell == "PERSON_METAPHONE_DOB").M, Is.EqualTo(1));
    }

    [Test]
    public void Rejects_test_and_duplicate_ids()
    {
        V9MemoryCellExplorer.Row[] rows =
        [
            new(1,"TEST","G1","MARIA","ANA",new DateTime(1980,1,2),1)
        ];
        Assert.Throws<InvalidDataException>((Action)(() => V9MemoryCellExplorer.Analyze(rows)));
        rows[0] = rows[0] with { Partition = "TRAIN" };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MemoryCellExplorer.Analyze([rows[0],rows[0]])));
    }
}

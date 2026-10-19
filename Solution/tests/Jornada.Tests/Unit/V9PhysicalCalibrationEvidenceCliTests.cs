using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PhysicalCalibrationEvidenceCliTests
{
    private const string Header = "base_person_id,particao,gestor,nome,nome_mae,data_nascimento,cpf\n";

    [TestCase("TRAIN", "VALIDATION", "")]
    [TestCase("TRAIN", "TEST", "")]
    [TestCase("VALIDATION", "TEST", "12345678909")]
    public void Corpus_rejects_identity_reused_across_partitions_before_filtering_cpf_or_test(
        string firstPartition, string secondPartition, string cpf)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Header
                + $"SAME,{firstPartition},G1,MARIA,ANA,1980-01-01,{cpf}\n"
                + $"SAME,{secondPartition},G2,MARIA,ANA,1980-01-01,{cpf}\n");
            Assert.That((Action)(() => V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(path)),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("compartilhada"));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Corpus_does_not_parse_or_return_test_attributes()
    {
        var path = Path.GetTempFileName();
        try
        {
            var rows = Enumerable.Range(0, 1000).Select(i =>
                $"BASE{i},{(i < 500 ? "TRAIN" : "VALIDATION")},G1,MARIA,ANA,1980-01-01,");
            File.WriteAllText(path, Header + string.Join('\n', rows)
                + "\nTEST_ONLY,TEST,G2,UNREAD,UNREAD,NOT_A_DATE,\n");
            var parsed = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(path);
            Assert.That(parsed, Has.Length.EqualTo(1000));
            Assert.That(parsed.Any(x => x.Partition == "TEST" || x.BasePersonId == "TEST_ONLY"), Is.False);
        }
        finally { File.Delete(path); }
    }
}

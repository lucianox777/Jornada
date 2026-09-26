using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SplinkSyntheticConformanceExchangeTests
{
    [Test]
    public void Export_OnlyCompiledSyntheticFixture_HasStableHistoricalV1Shape()
    {
        var original = SplinkSyntheticConformanceExchange.CreateFixture();
        var json = SplinkSyntheticConformanceExchange.SerializeFixture(original);
        var roundTrip = SplinkSyntheticConformanceExchange.ReadFixture(json);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"schema_version\": \"JORNADA_SPLINK_EXCHANGE_V1\""));
            Assert.That(json, Does.Contain("\"source_dataset_l\""));
            Assert.That(json, Does.Contain("\"clerical_match_score\""));
            Assert.That(json, Does.Contain("\"ibge_source_version\": \"SYNTHETIC_FIXTURE_NO_IBGE\""));
            Assert.That(original.Records, Has.Count.EqualTo(18));
            Assert.That(original.Labels, Has.Count.EqualTo(9));
            Assert.That(roundTrip.Records, Is.EqualTo(original.Records));
            Assert.That(roundTrip.IbgeFingerprintSha256, Has.Length.EqualTo(64));
        });
    }

    [Test]
    public void Export_RejectsFalselyDeclaredOriginEvenWithSyntheticMarker()
    {
        var fixture = SplinkSyntheticConformanceExchange.CreateFixture();
        var altered = fixture with
        {
            Records = fixture.Records.Select((r, i) =>
                i == 0 ? r with { FirstName = "CIDADÃO REAL" } : r).ToArray()
        };
        var forged = fixture with { IbgeSourceVersion = "CENSO2022_NOMES_BRASIL_V1" };

        Assert.Multiple(() =>
        {
            Assert.That(() => SplinkSyntheticConformanceExchange.SerializeFixture(altered),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkSyntheticConformanceExchange.SerializeFixture(forged),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkSyntheticConformanceExchange.ReadFixture(
                SplinkSyntheticConformanceExchange.SerializeFixture(fixture)
                    .Replace("\"records\"", "\"unexpected_real_data\":true,\"records\"",
                        StringComparison.Ordinal)),
                Throws.TypeOf<JsonException>());
        });
    }

    [Test]
    public void ExternalResult_ValidatesOriginLevelsThresholdsAndUnknownFields()
    {
        var source = SplinkSyntheticConformanceExchange.CreateFixture();
        var valid = ResultJson(source);
        var result = SplinkSyntheticConformanceExchange.ReadExternal(valid, source);

        Assert.Multiple(() =>
        {
            Assert.That(result.Estimates.Select(x => x.Level),
                Is.EquivalentTo(new[] { "EXACT", "HIGH", "MEDIUM", "LOW" }));
            Assert.That(() => SplinkSyntheticConformanceExchange.ReadExternal(
                valid.Replace("\"seed\":20260926", "\"seed\":20260927",
                    StringComparison.Ordinal), source),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkSyntheticConformanceExchange.ReadExternal(
                valid.Replace("[0.92,0.8]", "[0.90,0.8]", StringComparison.Ordinal), source),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkSyntheticConformanceExchange.ReadExternal(
                valid.Replace("\"source_schema_version\":", "\"unrecognized_person\":1,\"source_schema_version\":",
                    StringComparison.Ordinal), source),
                Throws.TypeOf<JsonException>());
            Assert.That(() => SplinkSyntheticConformanceExchange.ReadExternal(
                valid.Replace("\"feature\":\"NOME\",\"level\":\"LOW\"",
                    "\"feature\":\"NOME\",\"level\":\"EXACT\"",
                    StringComparison.Ordinal), source),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void Diagnose_OnlyDiagnostic_NoPromotionOrClaimOfConditionalU()
    {
        var package = SplinkSyntheticConformanceExchange.CreateFixture();
        var report = SplinkSyntheticConformanceExchange.Diagnose(package, ResultJson(package));
        var json = SplinkSyntheticConformanceExchange.SerializeDiagnostic(report);

        Assert.Multiple(() =>
        {
            Assert.That(report.Status, Is.EqualTo("DIAGNOSTICO_NAO_GOVERNADO"));
            Assert.That(report.InputSchema, Is.EqualTo("JORNADA_SPLINK_EXCHANGE_V1"));
            Assert.That(report.PositiveLabels, Is.EqualTo(9));
            Assert.That(report.CanonicalPeople, Is.EqualTo(9));
            Assert.That(report.ExactUnconditionedUPairs, Is.EqualTo(36));
            Assert.That(report.Levels.Select(x => x.Level),
                Is.EquivalentTo(new[] { "EXACT", "HIGH", "MEDIUM", "LOW" }));
            Assert.That(report.MTotalVariation, Is.GreaterThanOrEqualTo(0m));
            Assert.That(report.UTotalVariation, Is.GreaterThanOrEqualTo(0m));
            Assert.That(report.MaxAbsoluteLlrDifference, Is.GreaterThanOrEqualTo(0d));
            Assert.That(json, Does.Contain("nao_g").IgnoreCase);
            Assert.That(json, Does.Not.Contain("promovido"));
        });
    }

    private static string ResultJson(SplinkSyntheticPackage source) =>
        JsonSerializer.Serialize(new
        {
            schema_version = "JORNADA_SPLINK_ESTIMATES_V1",
            source_schema_version = source.SchemaVersion,
            splink_version = "4.0.17",
            runner = "calibrador-splink/run_calibration.py",
            scope = "NOME",
            nominal_semantics_version = "IDENTITY_NAME_STATES_V1",
            generator_version = source.GeneratorVersion,
            ibge_source_version = source.IbgeSourceVersion,
            ibge_fingerprint_sha256 = source.IbgeFingerprintSha256,
            partition = source.Partition,
            seed = source.Seed,
            max_pairs = 1000,
            name_thresholds = new[] { 0.92m, 0.80m },
            u_population_records = 9,
            m_positive_pairs = 9,
            estimates = new object[]
            {
                new { feature="NOME",level="EXACT",m_probability=0.70m,u_probability=0.10m,sql_condition="exact" },
                new { feature="NOME",level="HIGH",m_probability=0.15m,u_probability=0.10m,sql_condition="high" },
                new { feature="NOME",level="MEDIUM",m_probability=0.10m,u_probability=0.20m,sql_condition="medium" },
                new { feature="NOME",level="LOW",m_probability=0.05m,u_probability=0.60m,sql_condition="low" }
            }
        });
}

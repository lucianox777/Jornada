using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class IbgeIndependentUConferenceTests
{
    // Entirely fictional counts. No IBGE export or actual Splink execution is implied.
    private const string Marginals = """
        {
          "schema_version": "JORNADA_IBGE_PUBLIC_MARGINALS_V1",
          "reference_code": "CENSO2022_NOMES_BRASIL_V1",
          "reference_content_sha256": "e3cc61bcc7fca353bb134ee32de4aa50a8da708827eac3750efc0e6ebca5e885",
          "first_name_sex": "TODOS",
          "surname_sex": "TODOS",
          "first_names": [{"name":"ANA","occurrences":3},{"name":"MARIA","occurrences":1}],
          "surnames": [{"name":"SANTOS","occurrences":2},{"name":"SILVA","occurrences":2}]
        }
        """;

    [Test]
    public void IdenticalSyntheticStateCountsProduceZeroTvdWithoutCertifyingParity()
    {
        var csharp = IbgeOfflineUReference.Estimate(Marginals, 128);
        var external = MakeFictionalExternal(csharp);
        var diagnostic = IbgeIndependentUConference.Compare(Marginals, csharp, external);
        using var doc = JsonDocument.Parse(diagnostic);
        var result = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("schema_version").GetString(),
                Is.EqualTo("JORNADA_IBGE_INDEPENDENT_U_CONFERENCE_V1"));
            Assert.That(result.GetProperty("status").GetString(),
                Is.EqualTo("DIAGNOSTIC_ONLY_NOT_GOVERNED"));
            Assert.That(result.GetProperty("pair_count_per_seed").GetInt32(), Is.EqualTo(128));
            Assert.That(result.GetProperty("states").GetArrayLength(), Is.EqualTo(4));
            Assert.That(result.GetProperty("total_variation_of_mean_state_probabilities")
                .GetDecimal(), Is.Zero);
            Assert.That(IbgeIndependentUConference.Compare(Marginals, csharp, external),
                Is.EqualTo(diagnostic));
        });
    }

    [Test]
    public void MismatchedSourceHashAndRecorteFailClosed()
    {
        var csharp = IbgeOfflineUReference.Estimate(Marginals, 128);
        var external = MakeFictionalExternal(csharp);
        var tamperedHash = JsonNode.Parse(external)!;
        tamperedHash["marginals_sha256"] = new string('0', 64);
        var wrongSex = JsonNode.Parse(external)!;
        wrongSex["first_name_sex"] = "FEMININO";
        Assert.Multiple(() =>
        {
            Assert.That(() => IbgeIndependentUConference.Compare(
                Marginals, csharp, tamperedHash.ToJsonString()),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeIndependentUConference.Compare(
                Marginals, csharp, wrongSex.ToJsonString()),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void ReusedSeedIncompleteStatesAndCorruptSupportFailClosed()
    {
        var csharp = IbgeOfflineUReference.Estimate(Marginals, 128);
        var external = MakeFictionalExternal(csharp);
        var duplicateSeed = JsonNode.Parse(external)!;
        duplicateSeed["runs"]![1]!["seed"] = 20261001;
        var missingState = JsonNode.Parse(external)!;
        ((JsonObject)missingState["runs"]![0]!["states"]!).Remove("LOW");
        var corruptSupport = JsonNode.Parse(external)!;
        var exact = corruptSupport["runs"]![0]!["states"]!["EXACT"]!;
        exact["support"] = exact["support"]!.GetValue<long>() + 1;
        Assert.Multiple(() =>
        {
            Assert.That(() => IbgeIndependentUConference.Compare(
                Marginals, csharp, duplicateSeed.ToJsonString()),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeIndependentUConference.Compare(
                Marginals, csharp, missingState.ToJsonString()),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => IbgeIndependentUConference.Compare(
                Marginals, csharp, corruptSupport.ToJsonString()),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    private static string MakeFictionalExternal(string csharpJson)
    {
        using var csharp = JsonDocument.Parse(csharpJson);
        var root = csharp.RootElement;
        var runs = root.GetProperty("runs").EnumerateArray().Select((run, index) =>
        {
            var analytic = run.GetProperty("analytic_exact_collision_probability").GetDecimal();
            var states = run.GetProperty("states").EnumerateArray().ToDictionary(
                state => state.GetProperty("state").GetString()!,
                state => new {
                    support = state.GetProperty("support").GetInt64(),
                    probability = state.GetProperty("probability").GetDecimal(),
                    wilson_95 = new[] { 0m, 1m }
                }, StringComparer.Ordinal);
            return new {
                seed = 20261001 + index,
                pair_count = run.GetProperty("pair_count").GetInt32(),
                replay_sha256 = new string('a', 64),
                splink_result_sha256 = new string('b', 64),
                states,
                empirical_exact_minus_analytic =
                    states["EXACT"].probability - analytic
            };
        }).ToArray();
        return JsonSerializer.Serialize(new {
            schema_version = "JORNADA_SPLINK_INDEPENDENT_U_V1",
            marginals_sha256 = root.GetProperty("marginals_sha256").GetString(),
            reference_code = root.GetProperty("reference_code").GetString(),
            reference_content_sha256 = root.GetProperty("reference_content_sha256").GetString(),
            first_name_sex = root.GetProperty("first_name_sex").GetString(),
            surname_sex = "TODOS",
            joint_construction = root.GetProperty("joint_construction").GetString(),
            observation_channel = root.GetProperty("observation_channel").GetString(),
            classifier = "SPLINK_4.0.17_JARO_WINKLER_0.92_0.80",
            analytic_exact_collision_probability =
                root.GetProperty("runs")[0].GetProperty("analytic_exact_collision_probability")
                    .GetDecimal(),
            runs
        });
    }
}

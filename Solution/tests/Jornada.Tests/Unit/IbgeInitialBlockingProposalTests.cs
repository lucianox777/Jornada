using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Evaluation;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class IbgeInitialBlockingProposalTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void Proposal_is_deterministic_public_only_and_never_promotable()
    {
        var source = """
        {
          "schema_version":"JORNADA_IBGE_PUBLIC_MARGINALS_V1",
          "reference_code":"CENSO2022_NOMES_BRASIL_V1",
          "reference_content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "first_name_sex":"TODOS",
          "surname_sex":"TODOS",
          "first_names":[
            {"name":"MARIA","occurrences":60},
            {"name":"ZULEICA","occurrences":10},
            {"name":"JOSE","occurrences":30}
          ],
          "surnames":[
            {"name":"SILVA","occurrences":80},
            {"name":"SOUZA","occurrences":20}
          ]
        }
        """;

        var first = IbgeInitialBlockingProposal.Build(source);
        var second = IbgeInitialBlockingProposal.Build(source);
        Assert.That(second, Is.EqualTo(first));

        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        var passes = root.GetProperty("pass_hypotheses").EnumerateArray().ToArray();
        var projections = root.GetProperty("projection_candidates").EnumerateArray().ToArray();
        var metrics = root.GetProperty("marginal_metrics").EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema_version").GetString(),
                Is.EqualTo(IbgeInitialBlockingProposal.SchemaVersion));
            Assert.That(root.GetProperty("method_version").GetString(),
                Is.EqualTo(IbgeInitialBlockingProposal.MethodVersion));
            Assert.That(root.GetProperty("reference_content_sha256").GetString(), Is.EqualTo(Hash));
            Assert.That(root.GetProperty("nature").GetString(),
                Is.EqualTo("DIAGNOSTIC_INITIAL_PROPOSAL_NOT_PROMOTABLE"));
            Assert.That(projections.Select(x => x.GetProperty("feature").GetString()),
                Is.EquivalentTo(new[] { BlockingFeatureNames.FirstName, BlockingFeatureNames.Surnames }));
            Assert.That(projections.All(x => !x.GetProperty("promotable").GetBoolean()), Is.True);
            Assert.That(passes.All(x => !x.GetProperty("promotable").GetBoolean()), Is.True);
            Assert.That(passes.All(x => x.GetProperty("requires_jornada_validation").GetBoolean()), Is.True);
            Assert.That(first, Does.Not.Contain(BlockingFeatureNames.LastName));
            Assert.That(first, Does.Contain("não presume independência"));
            Assert.That(metrics, Has.Length.EqualTo(2));
        });

        var nameMetric = metrics.Single(x => x.GetProperty("marginal").GetString() == "NOME/TODOS");
        Assert.Multiple(() =>
        {
            Assert.That(nameMetric.GetProperty("published_occurrences_sum").GetInt64(), Is.EqualTo(100));
            Assert.That(nameMetric.GetProperty("max_published_occurrences").GetInt64(), Is.EqualTo(60));
            Assert.That(nameMetric.GetProperty("max_relative_published_occurrence_share").GetString(),
                Is.EqualTo("0.6"));
        });
    }

    [Test]
    public void Proposal_refuses_mother_specific_or_noncanonical_marginal_as_person_bootstrap()
    {
        var source = """
        {
          "schema_version":"JORNADA_IBGE_PUBLIC_MARGINALS_V1",
          "reference_code":"CENSO2022_NOMES_BRASIL_V1",
          "reference_content_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "first_name_sex":"FEMININO",
          "surname_sex":"TODOS",
          "first_names":[{"name":"MARIA","occurrences":10}],
          "surnames":[{"name":"SILVA","occurrences":10}]
        }
        """;

        Assert.That(
            () => IbgeInitialBlockingProposal.Build(source),
            Throws.TypeOf<InvalidDataException>()
                .With.Message.Contains("NOME/TODOS"));
    }
}

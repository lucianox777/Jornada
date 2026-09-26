using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class SplinkIbgeBootstrapReplayTests
{
    private static readonly IbgeTypedNameFrequencyEntry[] Published =
    [
        new(IbgeNameStatisticKind.FirstName, "MARIA", 100),
        new(IbgeNameStatisticKind.FirstName, "ANA", 50),
        new(IbgeNameStatisticKind.FirstName, "JOSE", 40),
        new(IbgeNameStatisticKind.Surname, "SILVA", 100),
        new(IbgeNameStatisticKind.Surname, "SANTOS", 75),
        new(IbgeNameStatisticKind.Surname, "LIMA", 30)
    ];

    [Test]
    public void ReplayedPairs_AreExactlyThoseCountedByOperationalIbgeBootstrap()
    {
        var options = new IbgeNominalUBootstrapOptions(20260926, 1000);
        var estimate = IbgeNominalUBootstrapEstimator.Estimate(Published, options);
        var replay = IbgeNominalUBootstrapEstimator.ReplayPairs(Published, options);
        var repeated = IbgeNominalUBootstrapEstimator.ReplayPairs(Published, options);
        Assert.Multiple(() =>
        {
            Assert.That(replay, Is.EquivalentTo(repeated));
            Assert.That(replay, Has.Count.EqualTo(1000));
            Assert.That(replay.Select(x => x.Index), Is.EqualTo(Enumerable.Range(0, 1000)));
            foreach (var state in estimate.States)
                Assert.That(replay.Count(x => x.CSharpState == state.State),
                    Is.EqualTo(state.Support), state.State);
        });
    }

    [Test]
    public void SourceContract_RejectsUnknownMembersAndWrongComparisonState()
    {
        var document = CreateReplay(20);
        var json = SplinkIbgeReplayContract.SerializeInput(document);
        Assert.That(SplinkIbgeReplayContract.ParseInput(json).Pairs, Is.EqualTo(document.Pairs));
        var contaminated = document with
        {
            Pairs = document.Pairs.Select((p, i) =>
                i == 0 ? p with { CSharpState = "INVALID" } : p).ToArray()
        };
        Assert.Multiple(() =>
        {
            Assert.That(() => SplinkIbgeReplayContract.SerializeInput(contaminated),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkIbgeReplayContract.ParseInput(
                json.Replace("\"pairs\"", "\"cpf\":\"00000000000\",\"pairs\"",
                    StringComparison.Ordinal)), Throws.TypeOf<JsonException>());
            Assert.That(() => IbgeNominalUBootstrapEstimator.ReplayPairs(
                Published, new IbgeNominalUBootstrapOptions(42, 100_001)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Diagnose_ReportsExactPairwiseAgreementsAndDivergences()
    {
        var source = CreateReplay(32);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var result = ExternalJson(source, input);
        var identical = SplinkIbgeReplayContract.Diagnose(input, result);
        var forced = source.Pairs[0].CSharpState == "LOW" ? "EXACT" : "LOW";
        var different = ExternalJson(source, input, 0, forced);
        var divergent = SplinkIbgeReplayContract.Diagnose(input, different);
        Assert.Multiple(() =>
        {
            Assert.That(identical.Status, Is.EqualTo("ESTADOS_IDENTICOS_DIAGNOSTICO"));
            Assert.That(identical.PairwiseDisagreements, Is.Zero);
            Assert.That(identical.TotalVariation, Is.Zero);
            Assert.That(identical.PairCount, Is.EqualTo(32));
            Assert.That(divergent.Status, Is.EqualTo("ESTADOS_DIVERGENTES_DIAGNOSTICO"));
            Assert.That(divergent.PairwiseDisagreements, Is.EqualTo(1));
            Assert.That(divergent.TotalVariation, Is.GreaterThan(0m));
        });
    }

    [Test]
    public void Diagnose_RejectsWrongSourceHashMissingPairAndWrongComparator()
    {
        var source = CreateReplay(10);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var valid = ExternalJson(source, input);
        Assert.Multiple(() =>
        {
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(
                input, valid.Replace("\"input_sha256\"", "\"unexpected\":1,\"input_sha256\"",
                    StringComparison.Ordinal)), Throws.TypeOf<JsonException>());
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(
                input, valid.Replace(source.ReferenceContentSha256,
                    new string('b', 64), StringComparison.Ordinal)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(
                input, valid.Replace("WHOLE_NAME_JARO_WINKLER_V1",
                    "OTHER_COMPARATOR", StringComparison.Ordinal)),
                Throws.TypeOf<InvalidDataException>());
            var parsed = JsonSerializer.Deserialize<SplinkIbgeReplayExternalResult>(
                valid, SplinkIbgeReplayContract.JsonOptions)!;
            var dropped = parsed with { Pairs = parsed.Pairs.Take(9).ToArray() };
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(
                input, JsonSerializer.Serialize(dropped, SplinkIbgeReplayContract.JsonOptions)),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    private static SplinkIbgeReplayDocument CreateReplay(int count)
    {
        var options = new IbgeNominalUBootstrapOptions(20260926, count);
        var estimate = IbgeNominalUBootstrapEstimator.Estimate(Published, options);
        var pairs = IbgeNominalUBootstrapEstimator.ReplayPairs(Published, options)
            .Select(x => new SplinkIbgeReplayPair(x.Index, x.LeftName, x.RightName, x.CSharpState))
            .ToArray();
        return new(SplinkIbgeReplayContract.InputSchema, "CENSO2022_NOMES_BRASIL_V1",
            new string('a', 64), "TODOS", "TODOS",
            estimate.MethodVersion, estimate.JointConstructionVersion,
            estimate.ObservationChannelVersion, SplinkIbgeReplayContract.ComparisonV1,
            options.Seed, options.PairCount, estimate.FirstNamePublishedOccurrences,
            estimate.SurnamePublishedOccurrences,
            estimate.AnalyticExactSyntheticFullNameProbability, pairs);
    }

    private static string ExternalJson(SplinkIbgeReplayDocument doc, string input,
        int? overrideIndex = null, string? replacement = null)
    {
        var result = new SplinkIbgeReplayExternalResult(
            SplinkIbgeReplayContract.ExternalSchema,
            SplinkIbgeReplayContract.InputSchema,
            SplinkIbgeReplayContract.Sha(input),
            doc.ReferenceContentSha256,
            doc.ComparisonVersion, "4.0.17", doc.Seed, doc.PairCount,
            doc.Pairs.Select(p => new SplinkIbgeReplayExternalPair(
                p.PairIndex, overrideIndex == p.PairIndex ? replacement! : p.CSharpState)).ToArray());
        return JsonSerializer.Serialize(result, SplinkIbgeReplayContract.JsonOptions) + "\n";
    }
}

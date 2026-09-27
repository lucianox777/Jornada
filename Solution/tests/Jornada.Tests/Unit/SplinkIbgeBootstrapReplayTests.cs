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

    [Test]
    public void DiagnoseV2_TransitionMatrixIncludesEveryCellAndPreservesMarginals()
    {
        var source = CreateReplay(128);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var unchanged = SplinkIbgeReplayContract.Diagnose(input, ExternalJson(source, input));
        var states = new[] { "EXACT", "HIGH", "MEDIUM", "LOW" };
        var expectedOrder = states.SelectMany(from => states.Select(to => (from, to))).ToArray();
        var actualOrder = unchanged.Transitions
            .Select(cell => (cell.CSharpState, cell.SplinkState)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(SplinkIbgeReplayContract.LegacyReportSchema,
                Is.EqualTo("JORNADA_SPLINK_IBGE_U_REPLAY_DIAGNOSTIC_V1"));
            Assert.That(unchanged.SchemaVersion, Is.EqualTo(
                "JORNADA_SPLINK_IBGE_U_REPLAY_DIAGNOSTIC_V2"));
            Assert.That(unchanged.Transitions, Has.Count.EqualTo(16));
            Assert.That(actualOrder, Is.EqualTo(expectedOrder));
            Assert.That(unchanged.Transitions.Sum(cell => cell.Support), Is.EqualTo(128));
            Assert.That(unchanged.Transitions.Where(cell =>
                cell.CSharpState != cell.SplinkState).Sum(cell => cell.Support), Is.Zero);
            Assert.That(unchanged.Transitions.Count(cell => cell.Support == 0),
                Is.GreaterThanOrEqualTo(12));
            foreach (var state in unchanged.States)
            {
                Assert.That(unchanged.Transitions.Where(cell => cell.CSharpState == state.State)
                    .Sum(cell => cell.Support), Is.EqualTo(state.CSharpSupport));
                Assert.That(unchanged.Transitions.Where(cell => cell.SplinkState == state.State)
                    .Sum(cell => cell.Support), Is.EqualTo(state.SplinkSupport));
            }
        });

        using var document = JsonDocument.Parse(
            SplinkIbgeReplayContract.SerializeDiagnostic(unchanged));
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema_version").GetString(),
                Is.EqualTo(SplinkIbgeReplayContract.ReportSchema));
            Assert.That(root.GetProperty("transitions").GetArrayLength(), Is.EqualTo(16));
            Assert.That(root.GetProperty("transitions")[0]
                .GetProperty("c_sharp_state").GetString(), Is.EqualTo("EXACT"));
            Assert.That(root.GetProperty("transitions")[0]
                .GetProperty("splink_state").GetString(), Is.EqualTo("EXACT"));
        });
    }

    [Test]
    public void DiagnoseV2_OppositePairChangesRemainVisibleWhenAggregateTvdIsZero()
    {
        var source = CreateReplay(512);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var selected = source.Pairs.GroupBy(pair => pair.CSharpState)
            .Take(2).Select(group => group.First()).ToArray();
        Assert.That(selected, Has.Length.EqualTo(2),
            "Fixture precisa de dois estados C# distintos para testar cancelamento de TVD.");

        var original = JsonSerializer.Deserialize<SplinkIbgeReplayExternalResult>(
            ExternalJson(source, input), SplinkIbgeReplayContract.JsonOptions)!;
        var swapped = original with
        {
            // Ordem arbitrária na resposta do runner deve manter a mesma matriz.
            Pairs = original.Pairs.Select(pair => pair.PairIndex switch
            {
                var index when index == selected[0].PairIndex =>
                    pair with { SplinkState = selected[1].CSharpState },
                var index when index == selected[1].PairIndex =>
                    pair with { SplinkState = selected[0].CSharpState },
                _ => pair
            }).Reverse().ToArray()
        };
        var resultJson = JsonSerializer.Serialize(swapped,
            SplinkIbgeReplayContract.JsonOptions) + "\n";
        var report = SplinkIbgeReplayContract.Diagnose(input, resultJson);
        var forward = report.Transitions.Single(cell =>
            cell.CSharpState == selected[0].CSharpState &&
            cell.SplinkState == selected[1].CSharpState);
        var reverse = report.Transitions.Single(cell =>
            cell.CSharpState == selected[1].CSharpState &&
            cell.SplinkState == selected[0].CSharpState);
        Assert.Multiple(() =>
        {
            Assert.That(report.PairwiseDisagreements, Is.EqualTo(2));
            Assert.That(report.TotalVariation, Is.Zero,
                "Movimentos recíprocos cancelam marginais, mas não divergências por par.");
            Assert.That(report.Status, Is.EqualTo("ESTADOS_DIVERGENTES_DIAGNOSTICO"));
            Assert.That(forward.Support, Is.EqualTo(1));
            Assert.That(reverse.Support, Is.EqualTo(1));
            Assert.That(report.Transitions.Where(cell =>
                cell.CSharpState != cell.SplinkState).Sum(cell => cell.Support),
                Is.EqualTo(report.PairwiseDisagreements));
            Assert.That(report.Transitions.Sum(cell => cell.Support), Is.EqualTo(512));
        });

        // Rejeitar pares repetidos/estados inválidos antes de emitir matriz parcial.
        var repeated = swapped with
        {
            Pairs = swapped.Pairs.Select((pair, i) =>
                i == 0 ? pair with { PairIndex = swapped.Pairs[1].PairIndex } : pair).ToArray()
        };
        var unknown = swapped with
        {
            Pairs = swapped.Pairs.Select((pair, i) =>
                i == 0 ? pair with { SplinkState = "UNKNOWN" } : pair).ToArray()
        };
        Assert.Multiple(() =>
        {
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(input,
                JsonSerializer.Serialize(repeated, SplinkIbgeReplayContract.JsonOptions)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SplinkIbgeReplayContract.Diagnose(input,
                JsonSerializer.Serialize(unknown, SplinkIbgeReplayContract.JsonOptions)),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void ExportDisagreementsCsv_OnlyEmitsValidatedMismatchesInInputOrder()
    {
        var source = CreateReplay(32);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var changed = source.Pairs[3].CSharpState == "LOW" ? "EXACT" : "LOW";
        var external = ExternalJson(source, input, 3, changed);
        var csv = SplinkIbgeReplayContract.ExportDisagreementsCsv(input, external);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(
                "recorte,pair_index,left_name,right_name,c_sharp_state,splink_state"));
            Assert.That(lines[1], Does.StartWith("\"TODOS\",3,"));
            Assert.That(lines[1], Does.EndWith($",\"{changed}\""));
            Assert.That(SplinkIbgeReplayContract.ExportDisagreementsCsv(
                input, ExternalJson(source, input)).Split('\n',
                    StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void ExportDisagreementsCsv_RejectsInvalidExternalHashBeforeWritingRows()
    {
        var source = CreateReplay(10);
        var input = SplinkIbgeReplayContract.SerializeInput(source);
        var external = ExternalJson(source, input);
        Assert.That(() => SplinkIbgeReplayContract.ExportDisagreementsCsv(
            input, external.Replace(SplinkIbgeReplayContract.Sha(input),
                new string('0', 64), StringComparison.Ordinal)),
            Throws.TypeOf<InvalidDataException>());
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

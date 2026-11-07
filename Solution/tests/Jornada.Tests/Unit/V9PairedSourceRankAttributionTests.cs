using System.Text.Json;
using System.Globalization;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9PairedSourceRankAttributionTests
{
    private static V9SourceTopTwoDiagnostic Row(int source, string status, string reason,
        decimal? first, decimal? second, bool? correct,
        string? firstCell = "1/1", string? secondCell = "1/2") =>
        new(source, first is null ? null : source + 100,
            second is null ? null : source + 200,
            first is null ? null : (source + 100).ToString(CultureInfo.InvariantCulture),
            second is null ? null : (source + 200).ToString(CultureInfo.InvariantCulture),
            first is null ? null : firstCell, second is null ? null : secondCell,
            first, second, first is null ? null : correct,
            second is null ? null : false, status, reason);

    private static V9PhysicalSourceResolveMetric Metric(string option,
        V9SourceTopTwoDiagnostic[] rows, long tp, long conflicts, long unresolved) =>
        new(option, .95m, 0m, .5m, "BLIND_FS_COMPLETE_LINK_CANDIDATES",
            rows.Length, 3, 3, tp, 0, conflicts, unresolved, 1d, tp / 3d, 0d,
            null, 3, new Dictionary<string,long>(), false, false, 0,
            SourceRankInternal: Array.AsReadOnly(rows));

    [Test]
    public void Same_source_replay_reconciles_lost_and_gained_tps_and_margin_crossings()
    {
        var v2 = new[]
        {
            Row(1,"RESOLVIDO","RESOLVIDO",2m,1m,true),
            Row(2,"RESOLVIDO","RESOLVIDO",3m,1m,true),
            Row(3,"CONFLITO","MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE",2m,1.9m,true),
            Row(4,"NAO_RESOLVIDO","SEM_CANDIDATO_NO_RULESET_BLOCKING",null,null,null),
        };
        var v3 = new[]
        {
            Row(1,"CONFLITO","MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE",2m,1.9m,true),
            Row(2,"RESOLVIDO","RESOLVIDO",3m,1m,true),
            Row(3,"RESOLVIDO","RESOLVIDO",3m,1m,true),
            Row(4,"NAO_RESOLVIDO","SEM_CANDIDATO_NO_RULESET_BLOCKING",null,null,null),
        };
        var a=V9PairedSourceRankAttribution.Compare(
            Metric("v2",v2,2,1,1), Metric("v3",v3,2,1,1),
            new HashSet<string>(StringComparer.Ordinal) {"1/2"});
        Assert.Multiple((Action)(() =>
        {
            Assert.That(a.SourceCount, Is.EqualTo(4));
            Assert.That(a.V2CorrectLostToV3Conflict, Is.EqualTo(1));
            Assert.That(a.V3CorrectGained, Is.EqualTo(1));
            Assert.That(a.V2CorrectLostWithV3UFallback, Is.EqualTo(1));
            Assert.That(a.V2CorrectLostSameTopTwoGroups, Is.EqualTo(1));
            Assert.That(a.V2CorrectLostMarginHalfCrossedWithStableTopTwo, Is.EqualTo(1));
            Assert.That(a.V3ReasonsOnV2CorrectLoss["MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"], Is.EqualTo(1));
            Assert.That(a.UFallbackCausalityEstablished, Is.False);
            Assert.That(a.IndependentValidation, Is.False);
        }));
        var serialized = JsonSerializer.Serialize(a);
        Assert.That(serialized, Does.Not.Contain("SourceObservationId"));
        Assert.That(serialized, Does.Not.Contain("FirstObservationId"));
        Assert.That(JsonSerializer.Serialize(Metric("v2",v2,2,1,1)),
            Does.Not.Contain("SourceRankInternal"));
    }

    [Test]
    public void Mixed_U_is_separate_from_marginal_and_private_ids_remain_unpublished()
    {
        var left = new[] { Row(1,"RESOLVIDO","RESOLVIDO",2m,1m,true) };
        var right = new[] { Row(1,"CONFLITO","MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE",2m,1.9m,true) };
        var report = V9PairedSourceRankAttribution.Compare(
            Metric("v2",left,1,0,0), Metric("v3",right,0,1,0),
            new HashSet<string>(), new HashSet<string> {"1/2"});
        Assert.That(report.V2CorrectLostWithV3UMixed, Is.EqualTo(1));
        Assert.That(report.V2CorrectLostWithV3UMarginal, Is.Zero);
        Assert.That(JsonSerializer.Serialize(report), Does.Not.Contain("SourceObservationId"));
        Assert.That((Action)(() => V9PairedSourceRankAttribution.Compare(
            Metric("v2",left,1,0,0), Metric("v3",right,0,1,0),
            new HashSet<string> {"1/2"}, new HashSet<string> {"1/2"})),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Any_incomplete_source_or_changed_frame_fails_closed()
    {
        var baseRows = new[]
        {
            Row(1,"RESOLVIDO","RESOLVIDO",3m,1m,true),
            Row(2,"CONFLITO","MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE",3m,2.9m,true),
        };
        var m = Metric("v2",baseRows,1,1,0);
        var different = new[] {baseRows[0], baseRows[1] with {SourceObservationId=3}};
        Assert.That((Action)(() => V9PairedSourceRankAttribution.Compare(
            m, Metric("v3",different,1,1,0), new HashSet<string>())),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9PairedSourceRankAttribution.Compare(
            m, Metric("v3",baseRows,2,0,0), new HashSet<string>())),
            Throws.TypeOf<InvalidDataException>());
    }
}

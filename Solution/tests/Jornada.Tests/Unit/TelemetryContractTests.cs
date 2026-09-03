using System.Diagnostics.Metrics;
using System.Text.Json;
using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class TelemetryContractTests
{
    [Test]
    public void Telemetry_emits_only_catalogued_non_sensitive_dimensions_for_api_request()
    {
        var observed = new List<(string Name, string[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Jornada") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
            observed.Add((instrument.Name, tags.ToArray().Select(x => x.Key).OrderBy(x => x).ToArray())));
        listener.Start();

        JornadaTelemetry.RecordApiRequest(12.5, "/api/v1/pessoas/{pessoaUuid}", "GET", 200);
        Assert.That(observed.Any(x => x.Name == "jornada.api.request.duration"), Is.True);
        var api = observed.Single(x => x.Name == "jornada.api.request.duration");
        Assert.That(api.Tags, Is.EquivalentTo(new[] { "method", "route", "statusClass" }));
        Assert.That(api.Tags.Any(x => x.Contains("cpf", StringComparison.OrdinalIgnoreCase)
                                     || x.Contains("email", StringComparison.OrdinalIgnoreCase)
                                     || x.Contains("token", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public void Every_metric_declared_in_catalog_exists_in_Jornada_meter()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "config", "observability", "metrics-slo-catalog.json")));
        var expected = doc.RootElement.GetProperty("metrics").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToHashSet();
        var published = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "Jornada") published.Add(instrument.Name); };
        listener.Start();
        // Inicializa a classe e força publicação dos instrumentos estáticos.
        JornadaTelemetry.RecordApiAuditPersistence(1);
        Assert.That(published, Is.SupersetOf(expected));
    }
}

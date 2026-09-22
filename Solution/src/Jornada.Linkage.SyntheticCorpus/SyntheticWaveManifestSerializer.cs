using System.Text.Json;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// Serializes the aggregate ingestion-wave manifest with the exact camelCase contract
/// read by both Jornada.Ensaio and Jornada.Linkage.Evaluation (JsonElement.GetProperty
/// is case-sensitive). Applies naming to nested per-wave records as well as the root.
/// </summary>
public static class SyntheticWaveManifestSerializer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(
        ulong seed,
        string corpusInputFingerprintSha256,
        string pseudonymizationKeySha256,
        int baselineSourcesWithCpf,
        int baselineSourcesWithoutCpf,
        SyntheticWaveScenario scenario,
        IReadOnlyList<object> waves)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(waves);
        scenario.Validate();
        if (waves.Count != scenario.WaveCount)
            throw new ArgumentException("Quantidade de ondas diverge do cenário.", nameof(waves));

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            scenarioVersion = SyntheticIngestionWavePlanner.ScenarioVersion,
            bridgeVersion = SyntheticIngestionBridge.WaveBridgeVersion,
            seed,
            corpusInputFingerprintSha256,
            pseudonymizationKeySha256,
            baselineSourcesWithCpf,
            baselineSourcesWithoutCpf,
            scenario,
            waves
        }, Json) + "\n";
    }
}

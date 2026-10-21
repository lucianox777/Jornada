using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Offline aggregation of three PRIVATE evidence files from separate owned
/// disposable LocalDB runs. Never opens SQL; never exports per-person material.
/// </summary>
public static class GoldTrainThreeOrderCompareCommand
{
    public const string Flag = "--v9-gold-compare-orders";
    public const string PrivateEvidenceFileName = "gold-order-trial-private.json";
    private static readonly JsonSerializerOptions Strict = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static async Task<GoldTrainThreeOrderProtocol.Comparison> RunAsync(string[] args)
    {
        if (args.Length != 5 || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-gold-compare-orders <private-baseline.json> <private-reverse.json> <private-interleaved.json> <saida-agregada-nova.json>");
        var files = args.Skip(1).Select(Path.GetFullPath).ToArray();
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("Entradas e saída exigem caminhos distintos.");
        foreach (var file in files.Take(3))
            if (Path.GetFileName(file) != PrivateEvidenceFileName)
                throw new InvalidDataException("Comparador aceita somente evidência privada do harness, não JSON agregado.");
        if (files.Take(3).Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3)
            throw new InvalidDataException("Três execuções devem usar diretórios de saída distintos.");
        if (File.Exists(files[3]) || Directory.Exists(files[3]))
            throw new IOException("Relatório agregado precisa de destino novo e imutável.");
        var trials = new List<GoldTrainThreeOrderProtocol.Trial>();
        foreach (var file in files.Take(3))
        {
            var trial = JsonSerializer.Deserialize<GoldTrainThreeOrderProtocol.Trial>(
                await File.ReadAllBytesAsync(file), Strict)
                ?? throw new InvalidDataException("Evidência privada inválida.");
            trials.Add(trial);
        }
        var report = GoldTrainThreeOrderProtocol.Compare(trials);
        await using var stream = new FileStream(files[3], FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, report, Strict);
        return report;
    }
}

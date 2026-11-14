using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Converts ONE frozen winner to a SQL operational V9 model. No TEST data,
/// no validation metrics, no refitting, no activation or model selection.
/// The production Runner's V9 SQL reader consumes this same persisted layout.
/// </summary>
public static class V9OperationalModelConversionCli
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--v9-convert-winner-operational")
            throw new ArgumentException(
                "Uso: --v9-convert-winner-operational <winnerSha.operational.json> (emitido pela VALIDATION)");
        var modelPath = Path.GetFullPath(args[1]);
        // Explicit file name supplied by VALIDATION; never infer a model from
        // a training selection envelope or silently load a legacy model.
        var sha = Path.GetFileName(modelPath)[..Math.Min(64, Path.GetFileName(modelPath).Length)];
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("SQL próprio do TEST não configurado.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.InitialCatalog != "JornadaHarness")
            throw new InvalidDataException("Conversor de TEST exige SQL descartável JornadaHarness.");
        // Only the original selected-by-VALIDATION bytes are permitted here.
        var fitted = await V9ValidatedOperationalModelFile.ReadPinnedAsync(modelPath);
        if (fitted.ContentSha256 != sha)
            throw new InvalidDataException("Conversão SQL exige mesmo SHA do arquivo final VALIDATION.");
        var contract = V9OperationalSqlModelConverter.VerifyConversion(fitted);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var receipt = await V9OperationalSqlModelConverter.MaterializeOnceAsync(
            connection, fitted);
        if (!receipt.ByteExactRoundTrip || receipt.ModelId != contract.ModelId
            || receipt.SourceSnapshotSha256 != sha || receipt.Active || receipt.Published)
            throw new InvalidDataException("Conversão operacional não atestou modelo único imutável.");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            status = "V9_WINNER_OPERATIONAL_SQL_CONVERTED_NOT_ACTIVATED",
            contractVersion = receipt.ContractVersion,
            winnerSnapshotSha256 = receipt.SourceSnapshotSha256,
            operationalModelId = receipt.ModelId,
            jointCells = receipt.JointCellCount,
            tfSlots = receipt.TfSlotCount,
            blockingPasses = receipt.BlockingPassCount,
            byteExactRoundTrip = true,
            modelCount = 1,
            refitted = false,
            certified = false,
            activated = false
        }));
    }
}

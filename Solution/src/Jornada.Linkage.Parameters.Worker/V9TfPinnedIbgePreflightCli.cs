using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Exports provenance from a freshly loaded, published SQL reference. This is
/// NOT a TF fit, model certification or operational authorization.
/// </summary>
public static class V9TfPinnedIbgePreflightCli
{
    private const string ReferenceCode = "CENSO2022_NOMES_BRASIL_V1";
    public const string FrozenTransferTargetFlag = "--frozen-transfer-target-seed43";
    public const string FrozenTransferTargetSeedFlag = "--frozen-transfer-target-seed";
    public static readonly IReadOnlySet<ulong> FrozenTransferTargetSeeds = new HashSet<ulong> { 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 };
    private const string MetadataOnlyBlockingFrame = "V9_TF_PIN_PROVENANCE_ONLY_NO_BLOCKING_OR_CALIBRATION";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        // The optional flag is the ONLY route to a seed-43 bootstrap: it serves the
        // frozen seed42 -> seed43 transfer (no fit). The default contract stays seed 42.
        // Issue #1275: the same frozen transfer to seeds 44-47 uses an explicit seed argument.
        var legacySeed43 = args.Length == 4 && args[3] == FrozenTransferTargetFlag;
        var explicitTarget = args.Length == 5 && args[3] == FrozenTransferTargetSeedFlag;
        if ((args.Length != 3 && !legacySeed43 && !explicitTarget) || args[0] != "--v9-tf-export-ibge-pin")
            throw new ArgumentException("Uso: --v9-tf-export-ibge-pin <bootstrap.json> <novo-pin.json> ["
                + FrozenTransferTargetFlag + " | " + FrozenTransferTargetSeedFlag + " <43..57>]");
        ulong expectedSeed = 42UL;
        if (legacySeed43) expectedSeed = 43UL;
        if (explicitTarget && (!ulong.TryParse(args[4], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out expectedSeed)
            || !FrozenTransferTargetSeeds.Contains(expectedSeed)))
            throw new ArgumentException("Seed-alvo da transferência congelada fora de 43..57.");

        var bootstrapPath = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);
        if (new FileInfo(bootstrapPath).Length is <= 0 or > 65536)
            throw new InvalidDataException("Bootstrap ausente, vazio ou grande demais.");
        var bootstrap = JsonSerializer.Deserialize<NominalSyntheticIbgeBootstrapV9>(
            await File.ReadAllBytesAsync(bootstrapPath), Json)
            ?? throw new InvalidDataException("Bootstrap nulo.");
        if (bootstrap.Provenance != CompositeNominalTermFrequencyContractV9.SyntheticIbgeProvenance
            || bootstrap.People != CompositeNominalTermFrequencyContractV9.CanonicalBootstrapPeople
            || bootstrap.GeneratorSeed != expectedSeed
            || string.IsNullOrWhiteSpace(bootstrap.GeneratorVersion)
            || string.IsNullOrWhiteSpace(bootstrap.CorpusFingerprint)
            || string.IsNullOrWhiteSpace(bootstrap.GenerationHypothesesFingerprint))
            throw new InvalidDataException("Bootstrap TF 30k não atende ao contrato.");

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão SQL descartável não configurada.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException("Exportação TF aceita somente JornadaHarness descartável.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        long versionId;
        await using (var command = new SqlCommand(
            """
            SELECT frequencia_nome_versao_id
            FROM ref.frequencia_nome_versao
            WHERE codigo=@code AND status=N'ATIVA';
            """, connection))
        {
            command.Parameters.Add("@code", SqlDbType.NVarChar, 80).Value = ReferenceCode;
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidDataException("Referência IBGE canônica ATIVA não encontrada.");
            versionId = reader.GetInt64(0);
            if (await reader.ReadAsync())
                throw new InvalidDataException("Múltiplas versões IBGE canônicas ativas.");
        }

        // Use the production C# SQL materializer for the SHA, NEVER a Python
        // reimplementation or the SHA of a compressed reference file.
        var frozen = await FrozenIbgeNominalTfV9.LoadAsync(
            connection, versionId, MetadataOnlyBlockingFrame, bootstrap);
        if (frozen.ReferenceVersionId != versionId
            || frozen.ReferenceCode != ReferenceCode
            || frozen.PublishedReferenceSha256.Length != 64
            || frozen.MaterializedFrequencySha256.Length != 64)
            throw new InvalidDataException("A referência IBGE SQL materializada não corresponde à publicação.");

        var pin = new
        {
            PinnedIbgeReferenceVersionId = frozen.ReferenceVersionId,
            PinnedIbgePublishedSha256 = frozen.PublishedReferenceSha256,
            PinnedIbgeMaterializedFrequencySha256 = frozen.MaterializedFrequencySha256,
            PinnedIbgeBootstrap = frozen.Bootstrap
        };
        await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, pin, Json);
        Console.WriteLine("PASS: referência IBGE publicada e materializada fixada em JSON; sem TF fit, TEST ou ativação.");
    }
}

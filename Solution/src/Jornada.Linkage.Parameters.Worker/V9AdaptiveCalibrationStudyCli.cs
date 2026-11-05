using System.Text.Json;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Opt-in experimental entry point. Never starts the operational host.</summary>
public static class V9AdaptiveCalibrationStudyCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 4 || args[0] is not ("--v9-adaptive-study" or "--v9-adaptive-census"))
            throw new ArgumentException("Uso: --v9-adaptive-study|--v9-adaptive-census <plano.json> <observacoes.csv> <novo-diretorio>");
        var planPath = Path.GetFullPath(args[1]);
        var inputPath = Path.GetFullPath(args[2]);
        var outputPath = Path.GetFullPath(args[3]);
        if (Directory.Exists(outputPath) || File.Exists(outputPath))
            throw new InvalidOperationException("Estudo exige um diretório novo; resultados anteriores são imutáveis.");
        if (new FileInfo(planPath).Length is <= 0 or > 10_000_000)
            throw new InvalidDataException("Plano ausente, vazio ou grande demais.");
        var plan = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(await File.ReadAllBytesAsync(planPath), Json)
            ?? throw new InvalidDataException("Plano V9 ausente.");
        V9AdaptiveCalibrationCoordinator.ValidatePlan(plan);
        // Positive TF must have an explicit immutable published reference;
        // it is loaded by exact version per SQL blocking frame, never ATIVA.
        var positiveTf = plan.ModelOptions.Any(V9FellegiSunterAdaptiveCalibrationAdapter.HasPositiveTf);
        if (positiveTf
            && (args[0] == "--v9-adaptive-census"
                || plan.PinnedIbgeReferenceVersionId is null or <= 0
                || plan.PinnedIbgePublishedSha256 is not { Length: 64 }
                || plan.PinnedIbgeMaterializedFrequencySha256 is not { Length: 64 }
                || plan.PinnedIbgeBootstrap is null))
            throw new InvalidDataException(
                "CLI TF positiva exige versão, digests, bootstrap IBGE fixados e estudo físico (não censo).");
        int? expectedBaseIdentityCount = null;
        if (plan.ObservationCapDeclaration is { } declaration)
        {
            var manifestPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "generation-manifest.json");
            if (new FileInfo(manifestPath).Length is <= 0 or > 10_000_000)
                throw new InvalidDataException("Manifesto ausente, vazio ou grande demais.");
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            await using var input = File.OpenRead(inputPath);
            var inputSha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input));
            V9SyntheticObservationCapEvidence.ValidateManifest(manifestBytes,
                declaration, inputSha, plan.MaximumAcceptedPerBaseIdentity);
            // The plan binds the generator manifest, which authenticates the SHA of
            // gabarito.json. Only its aggregate "people" scalar is inspected, not
            // TEST identities or per-pair outcomes. The generated manifest itself
            // intentionally has no "people" field; do not modify frozen bytes.
            var truthPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "gabarito.json");
            if (new FileInfo(truthPath).Length is <= 0 or > 100_000_000)
                throw new InvalidDataException("Gabarito agregado ausente ou grande demais.");
            expectedBaseIdentityCount = V9SyntheticObservationCapEvidence.ReadPinnedPopulationCount(
                manifestBytes, await File.ReadAllBytesAsync(truthPath));
        }
        // Diagnostic arms must have an authenticated population census;
        // absence of evidence must not silently disable this verification.
        if (plan.ModelOptions.Any(x => !x.EligibleForSelection) && expectedBaseIdentityCount is null)
            throw new InvalidDataException("Diagnósticos exigem manifesto de população fixado por SHA.");
        // This shared entry point accepts the same >=4-row frame as the physical
        // coordinator. The historical seed42 CLI retains its 1000-row default.
        // Statistical eligibility is enforced downstream, never inferred from this floor.
        var observations = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(inputPath,
            minimumObservationCount: 4,
            maximumObservationsPerBaseIdentity: plan.ObservationCapDeclaration?.MaximumObservationsPerBaseIdentity,
            expectedBaseIdentityCount: expectedBaseIdentityCount);
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão de JornadaHarness descartável ausente.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, "JornadaHarness", StringComparison.Ordinal))
            throw new InvalidOperationException("Estudo físico aceita exclusivamente o banco descartável JornadaHarness.");

        Func<IReadOnlyList<LinkageBlockingPass>, CancellationToken,
            Task<FrozenIbgeNominalTfV9?>>? referenceFactory = null;
        if (positiveTf)
        {
            referenceFactory = async (passes, ct) =>
            {
                await using var referenceSql = new SqlConnection(connectionString);
                await referenceSql.OpenAsync(ct);
                var pinned = await FrozenIbgeNominalTfV9.LoadAsync(
                    referenceSql, plan.PinnedIbgeReferenceVersionId!.Value,
                    V9PhysicalSqlAdaptiveCalibrationProbe.ComputeBlockingFingerprint(passes),
                    plan.PinnedIbgeBootstrap!, ct);
                V9NestedCalibrationCoordinator.RequirePinnedTfReference(
                    plan.PinnedIbgeReferenceVersionId, plan.PinnedIbgePublishedSha256,
                    plan.PinnedIbgeMaterializedFrequencySha256,
                    plan.PinnedIbgeBootstrap, pinned);
                return pinned;
            };
        }

        Directory.CreateDirectory(outputPath);
        // CreateNew is also the concurrency guard for two invocations targeting the same directory.
        await using var ownership = new FileStream(Path.Combine(outputPath, "study.lock"),
            FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await WriteNewJsonAsync(Path.Combine(outputPath, "plan.json"), plan);
        try
        {
            if (args[0] == "--v9-adaptive-census")
            {
                if (plan.DeclaredSearchSpace is null)
                    throw new InvalidDataException("Censo exige espaço integral declarado.");
                var fullCount = checked((long)plan.DeclaredSearchSpace.Count() + (plan.BaselineRuleSets?.Count ?? 0));
                var compared = V9AdaptiveCalibrationCoordinator.CountComparedConfigurations(plan, fullCount);
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                var census = await V9PhysicalSqlAdaptiveCalibrationProbe.ReadCatalogueEdgeMasksAsync(connection,
                    observations, plan.DeclaredSearchSpace, plan.Execution, CancellationToken.None);
                await WriteNewJsonAsync(Path.Combine(outputPath, "catalogue-census.json"), census);
                var coverage = V9CandidateFrameEquivalence.Partition(plan.DeclaredSearchSpace, census.EdgeMasks, CancellationToken.None);
                await WriteNewJsonAsync(Path.Combine(outputPath, "candidate-frame-coverage.json"), coverage);
                Console.WriteLine(JsonSerializer.Serialize(new { census.DistinctPhysicalEdges,
                    coverage.ConfigurationsEnumerated, exactClasses = coverage.Classes.Count,
                    multiplicity = compared, outcomesUsed = false, testUsed = false, activated = false }));
                return;
            }
            var result = await V9AdaptiveCalibrationCoordinator.RunParallelAsync(async ct =>
            {
                var connection = new SqlConnection(connectionString);
                try { await connection.OpenAsync(ct); return connection; }
                catch { await connection.DisposeAsync(); throw; }
            },
                observations, plan, CancellationToken.None,
                frozenTfReferenceForRuleset: referenceFactory);
            // Reject missing or unpaired physical evidence before exporting fit artifacts.
            var paired = V9TfSharedSurnamePairedDiagnostics.Build(
                plan.ModelOptions, result.SourceMeasurements,
                result.SharedSurnameCandidatePairsByOption ?? new Dictionary<string, long>(),
                result.CandidatePairsSha256ByRuleset ?? new Dictionary<string, string>());
            // Diagnostic fit snapshots remain in the descriptive study evidence;
            // they are NOT emitted as standalone, potentially publishable .fitted.json files.
            var selectableIds = plan.ModelOptions.Where(x => x.EligibleForSelection)
                .Select(x => x.OptionId).ToHashSet(StringComparer.Ordinal);
            foreach (var configuration in result.FrozenConfigurations)
            {
                if (configuration.OptionId.Length <= 65 || configuration.OptionId[64] != ':')
                    throw new InvalidDataException("Fit TF sem identificador de ruleset físico.");
                if (!selectableIds.Contains(configuration.OptionId[65..]))
                    continue;
                var path = Path.Combine(outputPath, configuration.Snapshot.ContentSha256 + ".fitted.json");
                // Each configuration has a unique content hash; refuse any divergent duplicate.
                if (File.Exists(path))
                    _ = await V9FittedSnapshotArtifact.ReadPinnedAsync(path, configuration.Snapshot.ContentSha256);
                else await V9FittedSnapshotArtifact.WriteNewAsync(path, configuration.Snapshot);
            }
            await WriteNewJsonAsync(Path.Combine(outputPath, "study.json"), result);
            // Descriptive paired evidence only; never enters Pareto or publication.
            if (paired.Count > 0)
                await WriteNewJsonAsync(Path.Combine(outputPath, "tf-shared-paired-diagnostics.json"), paired);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                output = outputPath, result.PlanSha256,
                configurations = result.Pareto.Table.Count,
                result.Pareto.Status, result.Pareto.InitialSuggestedOptionId,
                testUsed = false, published = false, activated = false
            }));
        }
        catch (Exception exception)
        {
            // Retain the plan and an interrupted marker; never erase or reroll a failed study.
            // Do not write exception messages, which could contain connection credentials.
            await WriteNewJsonAsync(Path.Combine(outputPath, "interrupted.json"), new
            {
                exceptionType = exception.GetType().Name, completed = false,
                testUsed = false, published = false, activated = false
            });
            throw;
        }
    }

    private static async Task WriteNewJsonAsync<T>(string path, T value)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, Json);
    }
}

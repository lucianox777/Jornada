using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Linkage.Runner;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Manual default V9 flow. Search and one-time finalist validation are separate commands.</summary>
public static class V9NestedCalibrationCli
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static async Task RunAsync(string[] args)
    {
        if (args.Length < 1 || (args[0] == "--v9-calibrate" ? args.Length != 4
            : args[0] == "--v9-validate-finalists" ? args.Length != 5 : true))
            throw new ArgumentException("Uso: --v9-calibrate <plano> <CSV> <diretório novo>; --v9-validate-finalists <plano> <SHA.selection.json> <CSV novo> <diretório novo>");
        var plan = JsonSerializer.Deserialize<V9NestedCalibrationPlan>(await File.ReadAllBytesAsync(args[1]), Json)
            ?? throw new InvalidDataException("Plano V9 interno ausente.");
        V9NestedCalibrationCoordinator.ValidatePlan(plan);
        var output = Path.GetFullPath(args[^1]);
        if (Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("Resultados exigem diretório novo e imutável.");
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão do JornadaHarness descartável ausente.");
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("Calibração física aceita somente JornadaHarness descartável.");
        Directory.CreateDirectory(output);
        await using var ownership = new FileStream(Path.Combine(output, "execution.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await WriteNewAsync(Path.Combine(output, "plan.json"), plan);
        try
        {
            if (args[0] == "--v9-calibrate")
                await SearchAsync(args[2], output, plan, connectionString);
            else await ValidateAsync(args[2], args[3], output, plan, connectionString);
        }
        catch (Exception exception)
        {
            await WriteNewAsync(Path.Combine(output, "interrupted.json"), new { exceptionType = exception.GetType().Name,
                completed = false, testUsed = false, activated = false });
            throw;
        }
    }

    private static async Task SearchAsync(string source, string output, V9NestedCalibrationPlan plan, string connectionString)
    {
        await using var sourceStream = File.OpenRead(source);
        var sourceSha = Convert.ToHexString(await SHA256.HashDataAsync(sourceStream)).ToLowerInvariant();
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "generation-manifest.json"));
        var declaration = plan.Calibration.ObservationCapDeclaration
            ?? throw new InvalidDataException("Fluxo padrão exige manifesto e cap da massa-base sintética fixados.");
        V9SyntheticObservationCapEvidence.ValidateManifest(manifestBytes, declaration, sourceSha, plan.Calibration.MaximumAcceptedPerBaseIdentity);
        using var producer = JsonDocument.Parse(manifestBytes);
        if (producer.RootElement.GetProperty("error_profile").GetString() != "clean"
            || producer.RootElement.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1")
            throw new InvalidDataException("Bootstrap padrão exige clean conservador provisório e produtor fixado.");
        var train = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(source, 4,
            declaration.MaximumObservationsPerBaseIdentity, onlyPartition: "TRAIN");
        var result = await V9NestedCalibrationCoordinator.SearchAsync(async ct =>
        {
            var connection = new SqlConnection(connectionString);
            try { await connection.OpenAsync(ct); return connection; }
            catch { await connection.DisposeAsync(); throw; }
        }, train, plan, CancellationToken.None, progress =>
        {
            var line = JsonSerializer.Serialize(progress);
            File.AppendAllText(Path.Combine(output, "batch-progress.jsonl"), line + Environment.NewLine);
            Console.Error.WriteLine(line);
        });
        foreach (var fit in result.Evaluations.Where(x => x.Candidate is not null).Select(x => x.Candidate!.Snapshot)
            .DistinctBy(x => x.ContentSha256))
            await V9FittedSnapshotArtifact.WriteNewAsync(Path.Combine(output, fit.ContentSha256 + ".fitted.json"), fit);
        var finalists = result.Evaluations.Where(x => x.Candidate is not null
            && result.FinalistSnapshotShas.Contains(x.Candidate.Snapshot.ContentSha256)).Select(x => x.Candidate!).ToArray();
        var externalDesign = finalists.Length == 0 ? null : V9StatisticalSamplePlan.CalculateFinalistFdrOnly(finalists.Length,
            plan.Calibration.MaximumAcceptedPerBaseIdentity, finalists.Min(x =>
                (x.Metric.TrueAcceptedSourceObservations + x.Metric.FalseAcceptedSourceObservations) / (double)x.Metric.PreBlockingBaseIdentities),
            plan.Calibration.RiskCeilings!);
        await WriteNewAsync(Path.Combine(output, "fresh-holdout-sample-plan.json"), new { externalDesign,
            historicalKnownComparedConfigurations = 14159502, historicalScope = "CONSUMED_HISTORICAL_VALIDATION_ONLY_NOT_FRESH_FINALIST_MULTIPLICITY",
            searchConfigurations = result.Evaluations.Count, externalValidationMultiplicity = result.FinalistSnapshotShas.Count,
            frozenTestMultiplicity = 1, certified = false });
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            result, externalDesign, externalValidationMultiplicity = result.FinalistSnapshotShas.Count,
            originalObservationsSha256 = sourceSha, producerManifestSha256 = Sha(manifestBytes),
            producerSeed = producer.RootElement.GetProperty("seed").GetUInt64(),
            snapshotValidationSlotPurpose = "TRAIN_SELECTION_NOT_EXTERNAL_VALIDATION",
            validationRule = "UP_TO_3_PRECOMMITTED_FITS_MAX_RECALL_WITH_CP_RATIO_UCB_WITHIN_EPSILON_NO_REFIT",
            statisticalCertification = false, operationalActivation = false
        }, Json);
        var envelopeSha = Sha(envelope);
        await using (var file = new FileStream(Path.Combine(output, envelopeSha + ".selection.json"), FileMode.CreateNew))
            await file.WriteAsync(envelope);
        await WriteNewAsync(Path.Combine(output, "selection-reference.json"), new { envelopeSha256 = envelopeSha,
            result.FinalistSnapshotShas, result.StopReason, externalValidationUsed = false, testUsed = false, activated = false });
        await WriteNewAsync(Path.Combine(output, "training-split-manifest.json"), result.Split);
        await WriteNewAsync(Path.Combine(output, "gain-by-batch.json"), result.Progress);
        Console.WriteLine(JsonSerializer.Serialize(new { envelopeSha256 = envelopeSha, evaluations = result.Evaluations.Count,
            finalists = result.FinalistSnapshotShas.Count, result.StopReason, externalValidationUsed = false, testUsed = false, activated = false }));
    }

    private static async Task ValidateAsync(string selectionFile, string source, string output,
        V9NestedCalibrationPlan plan, string connectionString)
    {
        var bytes = await File.ReadAllBytesAsync(selectionFile);
        var selectionSha = Sha(bytes);
        if (Path.GetFileName(selectionFile) != selectionSha + ".selection.json")
            throw new InvalidDataException("Envelope diverge do SHA fixado no nome do arquivo.");
        using var envelope = JsonDocument.Parse(bytes);
        var root = envelope.RootElement;
        var result = root.GetProperty("result");
        if (result.GetProperty("Version").GetString() != V9NestedCalibrationCoordinator.Version
            || result.GetProperty("ExternalValidationUsed").GetBoolean() || result.GetProperty("TestUsed").GetBoolean()
            || result.GetProperty("PlanSha256").GetString() != V9InternalTrainingSplit.Hash(JsonSerializer.Serialize(plan)))
            throw new InvalidDataException("Envelope não corresponde à seleção interna e ao plano congelado.");
        var ids = result.GetProperty("FinalistSnapshotShas").EnumerateArray().Select(x => x.GetString()!).ToArray();
        if (ids.Length is < 1 or > 3 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new InvalidDataException("Sem um a três finalistas distintos: não abrir VALIDATION.");
        var original = root.GetProperty("originalObservationsSha256").GetString()!;
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "generation-manifest.json"));
        await using var csv = File.OpenRead(source);
        var sourceSha = Convert.ToHexString(await SHA256.HashDataAsync(csv)).ToLowerInvariant();
        var fresh = V9FreshHoldoutProtocol.Inspect(manifestBytes, sourceSha, original,
            root.GetProperty("producerSeed").GetUInt64(), plan.Calibration.MaximumAcceptedPerBaseIdentity,
            isPlumbingFixture: plan.Purpose == "PLUMBING_FIXTURE_NOT_CERTIFICATION");
        var snapshots = new List<V9FittedSnapshot>();
        foreach (var id in ids)
        {
            if (id is not { Length: 64 } || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("SHA de finalista inválido.");
            snapshots.Add(await V9FittedSnapshotArtifact.ReadPinnedAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(selectionFile))!, id + ".fitted.json"), id));
        }
        var registry = Environment.GetEnvironmentVariable("JORNADA_V9_HOLDOUT_REGISTRY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jornada", "V9", "holdout-exposures");
        await V9FreshHoldoutProtocol.ReserveOnceAsync(registry, fresh, selectionSha, ids, CancellationToken.None);
        // No reading of holdout attribute values before reserving this exact family.
        var validation = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(source, 4,
            plan.Calibration.MaximumAcceptedPerBaseIdentity, onlyPartition: "VALIDATION")
            .Select(x => x with { BasePersonId = fresh.Namespace + x.BasePersonId }).ToArray();
        var measured = await V9ParallelWork.MapAsync(snapshots, plan.Calibration.MaximumParallelRuleSets, async (snapshot, ct) =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(connection, validation,
                snapshot, plan.Calibration.Execution, plan.Calibration.MaximumAcceptedPerBaseIdentity, ids.Length, ct);
        }, CancellationToken.None);
        var best = SelectConditionalWinner(measured, plan.Calibration.SelectionPolicy,
            plan.Calibration.Budget.MaximumBlockingFalseDegreeP99);
        await WriteNewAsync(Path.Combine(output, "finalist-validation.json"), new { selectionEnvelopeSha256 = selectionSha,
            fresh, comparedConfigurations = ids.Length, measured,
            bestConditionalSnapshotSha256 = best?.SnapshotSha256, selectedApprovedSnapshotSha256 = (string?)null,
            status = best is null ? "NO_FINALIST_PASSES_DECLARED_CONDITIONAL_GATES" : "CONDITIONAL_ONLY_IID_NOT_INDEPENDENTLY_VALIDATED",
            identityClusterIidIndependentlyValidated = false, testUsed = false, refitted = false, certified = false, activated = false });
        Console.WriteLine(JsonSerializer.Serialize(new { finalists = ids.Length, conditionalWinner = best?.SnapshotSha256,
            refitted = false, certified = false, testUsed = false, activated = false }));
    }

    public static V9FrozenHoldoutEvaluation? SelectConditionalWinner(IReadOnlyList<V9FrozenHoldoutEvaluation> measured,
        V9BootstrapPareto2D.SelectionPolicy policy, double maximumFalseDegreeP99)
    {
        ArgumentNullException.ThrowIfNull(measured);
        ArgumentNullException.ThrowIfNull(policy);
        if (measured.Count is < 1 or > 3 || measured.Select(x => x.SnapshotSha256).Distinct(StringComparer.Ordinal).Count() != measured.Count
            || measured.Any(x => x.Refitted || x.Metric.SyntheticGoldOracleGrouping
                || x.Metric.BonferroniComparedConfigurations != measured.Count))
            throw new InvalidDataException("Escolha exige a mesma família de até três finalistas congelados, sem ORACLE/refit.");
        return measured.Where(x => x.Metric.ConditionalClusterCpFdrUpper.HasValue
            && x.Metric.ConditionalClusterCpFdrUpper <= policy.MaximumAcceptedFdrUpper
            && x.Metric.GlobalSourceRecall >= policy.MinimumGlobalRecall
            && x.Metric.PreBlockingBaseIdentities >= policy.MinimumIndependentIdentityGroups
            && x.P95Milliseconds.HasValue && x.P95Milliseconds <= policy.MaximumP95Milliseconds
            && x.FalseDegreeP99 <= maximumFalseDegreeP99)
            .OrderByDescending(x => x.Metric.GlobalSourceRecall).ThenBy(x => x.Metric.ConditionalClusterCpFdrUpper)
            .ThenBy(x => x.SnapshotSha256, StringComparer.Ordinal).FirstOrDefault();
    }

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task WriteNewAsync<T>(string path, T value)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, value, Json);
    }
}

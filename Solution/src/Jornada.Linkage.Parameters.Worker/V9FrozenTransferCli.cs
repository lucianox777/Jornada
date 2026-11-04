using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>One frozen arm: a seed-42 fitted snapshot replayed unchanged on the target corpus.</summary>
public sealed record V9FrozenTransferArm(string OptionId, decimal ConflictMargin, string SnapshotSha256, string Role);

/// <summary>
/// Pre-registered executor for the seed42 -> seed43 frozen transfer (V9-TF-002).
/// Committed to Git BEFORE the seed-43 corpus exists; every byte is pinned by SHA-256.
/// </summary>
public sealed record V9FrozenTransferExecutorPlan(
    string Version, long SourceRunId, string SourceCommitSha, string SourceArtifactName,
    string SourcePlanTfSha256, string SourceStudySha256, ulong SourceSeed, ulong TargetSeed,
    string SourceObservationsSha256, int EndToEndRepetitions, decimal PrimaryConflictMargin,
    IReadOnlyList<V9FrozenTransferArm> Arms);

public sealed record V9FrozenTransferArmResult(V9FrozenTransferArm Arm, int ComparedConfigurationsInFamily,
    string Family, V9FrozenHoldoutEvaluation Evaluation);

/// <summary>
/// Physical frozen replay (no m/u, TF, threshold or blocking refit) on a NEW
/// synthetic corpus. This is a descriptive transfer, not a fresh confirmatory holdout:
/// seed 43 was previously exposed (V9FreshHoldoutProtocol rejects it as fresh).
/// </summary>
public static class V9FrozenTransferCli
{
    public const string Version = "V9_TF_SEED43_FROZEN_TRANSFER_EXECUTOR_V1";
    public const string ResultVersion = "V9_TF_SEED43_FROZEN_TRANSFER_RESULT_V1";
    public const string ControlOption = "T0.95_TF0";
    public const string TreatmentOption = "TF_ONE_MF_W1_M0_A0.5_T0.95";
    private static readonly decimal[] Margins = [0.5m, 2m, 4m];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Static contract checks; never touches SQL or the target corpus.</summary>
    public static void ValidatePlan(V9FrozenTransferExecutorPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        static bool Hex(string? value, int length) => value is not null && value.Length == length
            && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
        if (plan.Version != Version || plan.SourceRunId < 1 || !Hex(plan.SourceCommitSha, 40)
            || !Hex(plan.SourcePlanTfSha256, 64) || !Hex(plan.SourceStudySha256, 64)
            || !Hex(plan.SourceObservationsSha256, 64)
            || plan.SourceArtifactName != "v9-tf-45-physical-" + plan.SourceRunId + "-" + plan.SourceCommitSha
            || plan.SourceSeed != 42 || plan.TargetSeed != 43
            || plan.EndToEndRepetitions is < 20 or > 200
            || plan.PrimaryConflictMargin != 0.5m || plan.Arms is not { Count: 6 })
            throw new InvalidDataException("Plano de transferência congelada fora do contrato pré-registrado.");
        var expected = Margins.SelectMany(m => new[] { (ControlOption, m), (TreatmentOption, m) }).ToHashSet();
        var actual = plan.Arms.Select(a => (a.OptionId, a.ConflictMargin)).ToHashSet();
        if (!expected.SetEquals(actual) || actual.Count != 6
            || plan.Arms.Any(a => !Hex(a.SnapshotSha256, 64) || a.Role != ExpectedRole(a))
            || plan.Arms.Select(a => a.SnapshotSha256).Distinct(StringComparer.Ordinal).Count() != 6)
            throw new InvalidDataException("Transferência exige exatamente TF0 e ONE_MF nas margens 0,5/2/4, snapshots distintos.");
    }

    private static string ExpectedRole(V9FrozenTransferArm arm) =>
        (arm.ConflictMargin == 0.5m ? "PRIMARY_" : "SECONDARY_")
        + (arm.OptionId == ControlOption ? "CONTROL" : "TREATMENT");

    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 6 || args[0] != "--v9-frozen-transfer")
            throw new ArgumentException("Uso: --v9-frozen-transfer <executor.json> <plan-tf.json seed42> <diretório snapshots> <observacoes.csv seed43> <diretório novo>");
        var executorBytes = await File.ReadAllBytesAsync(args[1]);
        var plan = JsonSerializer.Deserialize<V9FrozenTransferExecutorPlan>(executorBytes, Json)
            ?? throw new InvalidDataException("Plano de transferência ausente.");
        ValidatePlan(plan);

        var sourcePlanBytes = await File.ReadAllBytesAsync(args[2]);
        if (Sha(sourcePlanBytes) != plan.SourcePlanTfSha256)
            throw new InvalidDataException("plan-tf.json do seed 42 diverge do SHA pré-registrado.");
        var source = JsonSerializer.Deserialize<V9AdaptiveCalibrationPlan>(sourcePlanBytes, Json)
            ?? throw new InvalidDataException("Plano seed 42 ausente.");
        if (source.PinnedIbgeBootstrap is not { GeneratorSeed: 42 } sourceBootstrap
            || source.ObservationCapDeclaration is not { } sourceCap
            || !string.Equals(sourceCap.ObservationsSha256, plan.SourceObservationsSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plano seed 42 sem bootstrap/cap esperados.");

        // Snapshots: exact bytes, exact option/margin, and pinned IBGE publication of the FIT corpus.
        var snapshots = new Dictionary<string, V9FittedSnapshot>(StringComparer.Ordinal);
        foreach (var arm in plan.Arms)
        {
            var snapshot = await V9FittedSnapshotArtifact.ReadPinnedAsync(
                Path.Combine(Path.GetFullPath(args[3]), arm.SnapshotSha256 + ".fitted.json"), arm.SnapshotSha256);
            var optionId = snapshot.Payload.OptionId;
            // The study stores "<ruleset sha>:<option>"; accept only that form or the bare option.
            var bareOption = optionId.Length > 65 && optionId[64] == ':' ? optionId[65..] : optionId;
            if (bareOption != arm.OptionId
                || snapshot.Payload.ConflictMargin != arm.ConflictMargin || snapshot.Payload.Threshold != 0.95m)
                throw new InvalidDataException("Snapshot não corresponde ao braço pré-registrado.");
            V9NestedCalibrationCli.RequirePinnedSnapshotReference(source.PinnedIbgeReferenceVersionId,
                source.PinnedIbgePublishedSha256, sourceBootstrap, snapshot);
            snapshots.Add(arm.SnapshotSha256, snapshot);
        }

        // Target corpus: a NEW seed-43 generation with the same producer contract, never seed 42 bytes.
        var input = Path.GetFullPath(args[4]);
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(input)!, "generation-manifest.json"));
        string inputSha;
        await using (var stream = File.OpenRead(input))
            inputSha = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        using (var manifest = JsonDocument.Parse(manifestBytes))
        {
            var m = manifest.RootElement;
            if (m.GetProperty("seed").GetUInt64() != plan.TargetSeed
                || m.GetProperty("generator_version").GetString() != sourceCap.GeneratorVersion
                || m.GetProperty("error_profile").GetString() != "clean"
                || m.GetProperty("population_profile").GetString() != "DEMOGRAPHIC_PRIMARY_V1"
                || string.Equals(inputSha, plan.SourceObservationsSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Corpus-alvo não é a geração seed 43 limpa e independente.");
        }
        var targetCap = sourceCap with { ManifestSha256 = Sha(manifestBytes), ObservationsSha256 = inputSha };
        V9SyntheticObservationCapEvidence.ValidateManifest(manifestBytes, targetCap, inputSha,
            source.MaximumAcceptedPerBaseIdentity);

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jornada")
            ?? throw new InvalidOperationException("Conexão do JornadaHarness descartável ausente.");
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("Transferência física aceita somente JornadaHarness descartável.");
        var output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("Resultados exigem diretório novo e imutável.");
        Directory.CreateDirectory(output);
        await using var ownership = new FileStream(Path.Combine(output, "execution.lock"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        try
        {
            // Only VALIDATION rows are parsed; TEST names/dates are never read.
            var validation = V9PhysicalCalibrationEvidenceCli.ReadNoCpfTrainValidation(input, 4,
                source.MaximumAcceptedPerBaseIdentity, onlyPartition: "VALIDATION");
            // Frozen execution contract; repetitions only add timing samples for P95.
            var execution = source.Execution with { EndToEndRepetitions = plan.EndToEndRepetitions };
            var results = new List<V9FrozenTransferArmResult>();
            foreach (var arm in plan.Arms.OrderBy(a => a.ConflictMargin).ThenBy(a => a.OptionId, StringComparer.Ordinal))
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                // Each margin is its own pre-declared 2-model family. Margin 0.5 is primary.
                var evaluation = await V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenHoldoutAsync(connection,
                    validation, snapshots[arm.SnapshotSha256], execution, source.MaximumAcceptedPerBaseIdentity, 2,
                    CancellationToken.None);
                if (evaluation.Refitted || evaluation.SnapshotSha256 != arm.SnapshotSha256)
                    throw new InvalidDataException("Replay congelado não preservou o snapshot.");
                results.Add(new(arm, 2, arm.ConflictMargin == plan.PrimaryConflictMargin
                    ? "PRIMARY_PREDECLARED" : "SECONDARY_DESCRIPTIVE", evaluation));
                Console.Error.WriteLine(JsonSerializer.Serialize(new { arm.OptionId, arm.ConflictMargin,
                    evaluation.Metric.TrueAcceptedSourceObservations, evaluation.Metric.FalseAcceptedSourceObservations,
                    evaluation.P95Milliseconds }));
            }
            await WriteNewAsync(Path.Combine(output, "transfer-results.json"), new
            {
                Version = ResultVersion, ExecutorPlanSha256 = Sha(executorBytes), plan.SourcePlanTfSha256,
                plan.SourceStudySha256, plan.SourceRunId, plan.SourceCommitSha, TargetSeed = plan.TargetSeed,
                TargetObservationsSha256 = inputSha, TargetManifestSha256 = Sha(manifestBytes),
                ValidationObservations = validation.Length,
                ValidationBaseIdentities = validation.Select(x => x.BasePersonId).Distinct(StringComparer.Ordinal).Count(),
                Execution = execution, Arms = results,
                Status = "FROZEN_TRANSFER_MEASURED_NOT_CERTIFIED", Refitted = false, TestUsed = false,
                ConfirmatoryFreshHoldout = false, ClusterIidIndependentlyValidated = false,
                Certified = false, Activated = false
            });
        }
        catch (Exception exception)
        {
            // Never write messages: they may contain connection details.
            await WriteNewAsync(Path.Combine(output, "interrupted.json"), new { exceptionType = exception.GetType().Name,
                completed = false, testUsed = false, refitted = false, activated = false });
            throw;
        }
        Console.WriteLine("PASS: TF0 e ONE_MF congelados medidos no seed 43 (VALIDATION, SQL físico); sem refit, TEST, certificação ou ativação.");
    }

    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task WriteNewAsync<T>(string path, T value)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, value, Json);
    }
}

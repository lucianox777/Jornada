using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Runner;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Creates an entirely NEW, nonoperational V3 *trial candidate* by fitting
/// class-wise joint M/U on the 81 TRAIN cell counts already recorded in an
/// SHA-pinned physical study. TF, source blocking passes, threshold, conflict
/// margin and TF reference are not searched or fitted again. The score WILL
/// change, so independent validation and explicit model selection remain
/// mandatory. Do not mistake this for conversion of the old immutable V2.
/// </summary>
public static class V9FrozenJointTrainingDerivationCli
{
    public const string Flag = "--v9-derive-joint-v3-from-frozen-train";
    public const string Version = "V9_FROZEN_TRAIN_JOINT_V3_TRIAL_DERIVATION_V1";

    private static string Sha(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Returns new V3 bytes, never writes the V2 source.</summary>
    public static V9FittedSnapshot DeriveTrialCandidate(
        V9FittedSnapshot source,
        V9FittedJointSupportAudit.Report recordedTrain,
        V9JointSupportPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recordedTrain);
        ArgumentNullException.ThrowIfNull(policy);
        V9JointSupportEstimation.ValidatePolicy(policy);
        var p = source.Payload;
        if (p.SchemaVersion != V9FittedSnapshot.SchemaVersion
            || p.JointSupport is not null || p.GoldParentSnapshotSha256 is not null
            || p.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || p.JointCells.Count != 81
            || p.UnitTfCalibrations?.Any(u => u.State == V9SnapshotTfFitState.Active) != true)
            throw new InvalidDataException("Derivação TRAIN exige V2 BLIND, 81 células e TF de unidade ativa.");
        if (recordedTrain.SnapshotSha256 != source.ContentSha256
            || recordedTrain.Cells is not { Count: 81 }
            || recordedTrain.TrainMPairs < 2 || recordedTrain.TrainUPairs < 2)
            throw new InvalidDataException("Suporte físico TRAIN não pertence ao fit V2 original.");

        // No invented labels, probabilities or pair counts. The existing
        // inverse audit reconciles all 81 smoothed m/u and the TRAIN prior
        // to the independently recorded physical M/U pair totals.
        var reconstructed = V9FittedJointSupportAudit.Reconstruct(source,
            recordedTrain.SmoothingAlpha, recordedTrain.TrainMPairs,
            recordedTrain.TrainUPairs);
        if (recordedTrain.Cells.Count != reconstructed.Cells.Count
            || !recordedTrain.Cells.SequenceEqual(reconstructed.Cells)
            || recordedTrain.CellsWithM != reconstructed.CellsWithM
            || recordedTrain.CellsWithU != reconstructed.CellsWithU
            || recordedTrain.CellsWithBoth != reconstructed.CellsWithBoth
            || recordedTrain.CellsWithNeither != reconstructed.CellsWithNeither)
            throw new InvalidDataException("Contagens observadas não reconciliam com o fit congelado.");

        var fitted = V9JointSupportEstimation.Estimate(reconstructed.Cells.Select(c =>
                new V9JointObservedCell(c.Person, c.Mother, c.ObservedMPairs, c.ObservedUPairs)),
            policy, reconstructed.SmoothingAlpha, p.TrainCorpusSha256);

        // Derived FIT fingerprint is different from the V2 source; reusing the
        // source fingerprint after changing 81 joint probabilities is invalid.
        var derivationIdentity = Version + "|" + source.ContentSha256 + "|"
            + JsonSerializer.Serialize(policy) + "|"
            + JsonSerializer.Serialize(reconstructed.Cells);
        var newFitFingerprint = Sha(Encoding.UTF8.GetBytes(derivationIdentity));
        var trial = V9FittedSnapshot.Create(p with
        {
            SchemaVersion = V9FittedSnapshot.SupportSchemaVersion,
            JointCells = fitted.Probabilities,
            JointSupport = fitted.Provenance,
            FitFingerprintSha256 = newFitFingerprint
        });
        if (trial.ContentSha256 == source.ContentSha256)
            throw new InvalidDataException("Derivação V3 não recebeu identidade de conteúdo própria.");
        // Only the four explicitly changed fields may differ from the V2 fit.
        // No automatic transfer of a TRAIN winner/DT-15 approval is allowed.
        var restored = trial.Payload with
        {
            SchemaVersion = p.SchemaVersion,
            JointCells = p.JointCells,
            JointSupport = p.JointSupport,
            FitFingerprintSha256 = p.FitFingerprintSha256
        };
        if (JsonSerializer.Serialize(restored) != JsonSerializer.Serialize(p))
            throw new InvalidDataException("Derivação mudou peso, passe, limiar ou outra propriedade V2.");
        return V9FittedSnapshot.Load(trial.SerializeUtf8(), trial.ContentSha256);
    }

    public static async Task RunAsync(string[] args)
    {
        if (args is not { Length: 8 } || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-derive-joint-v3-from-frozen-train <fit-v2.json> <sha-fit> <study.json> <sha-study> <policy.json> <sha-policy> <diretorio-novo>.");
        var source = await V9FittedSnapshotArtifact.ReadPinnedAsync(args[1], args[2]);

        // Both study and policy must be independently SHA-pinned. Read only
        // the named physical TRAIN support receipt, NEVER TEST or VALIDATION
        // outcomes to fit the new joint distribution.
        var studyBytes = await File.ReadAllBytesAsync(args[3]);
        if (Sha(studyBytes) != args[4])
            throw new InvalidDataException("Study físico original não confere com SHA externo.");
        var policyBytes = await File.ReadAllBytesAsync(args[5]);
        if (Sha(policyBytes) != args[6])
            throw new InvalidDataException("Política de suporte não confere com SHA explícito.");
        var policy = JsonSerializer.Deserialize<V9JointSupportPolicy>(policyBytes,
            new JsonSerializerOptions { UnmappedMemberHandling =
                System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Política V3 ausente.");

        using var study = JsonDocument.Parse(studyBytes);
        var root = study.RootElement;
        if (root.GetProperty("Version").GetString()
            != "V9_ADAPTIVE_PHYSICAL_TRAIN_VALIDATION_PARETO_V1")
            throw new InvalidDataException("Study original fora do contrato físico V9.");
        var receipts = root.GetProperty("FrozenConfigurations").EnumerateArray()
            .Where(r => r.GetProperty("Snapshot").GetProperty("ContentSha256").GetString()
                == source.ContentSha256).ToArray();
        if (receipts.Length != 1
            || receipts[0].GetProperty("OptionId").GetString() != source.Payload.OptionId
            || receipts[0].GetProperty("ConflictMargin").GetDecimal() != source.Payload.ConflictMargin)
            throw new InvalidDataException("Study não contém um recibo único do fit V2 fixado.");
        var support = receipts[0].GetProperty("JointSupport")
            .Deserialize<V9FittedJointSupportAudit.Report>()
            ?? throw new InvalidDataException("Study sem contagens brutas TRAIN para este fit.");
        var candidate = DeriveTrialCandidate(source, support, policy);

        var dest = Path.GetFullPath(args[7]);
        if (Directory.Exists(dest) || File.Exists(dest))
            throw new IOException("Derivação exige diretório de saída inexistente.");
        Directory.CreateDirectory(dest);
        var fitPath = Path.Combine(dest, candidate.ContentSha256 + ".fitted.json");
        await File.WriteAllBytesAsync(fitPath, candidate.SerializeUtf8());
        var receipt = new
        {
            schema = Version,
            sourceSnapshotSha256 = source.ContentSha256,
            originalTrainStudySha256 = args[4],
            pinnedJointPolicySha256 = args[6],
            derivedTrialSnapshotSha256 = candidate.ContentSha256,
            originalFitFingerprintSha256 = source.Payload.FitFingerprintSha256,
            newFitFingerprintSha256 = candidate.Payload.FitFingerprintSha256,
            originalBlockingPassCount = source.Payload.BlockingPasses.Count,
            derivedBlockingPassCount = candidate.Payload.BlockingPasses.Count,
            activeUnitTfCount = source.Payload.UnitTfCalibrations!.Count(u => u.State == V9SnapshotTfFitState.Active),
            physicalTrainMPairs = support.TrainMPairs,
            physicalTrainUPairs = support.TrainUPairs,
            jointProbabilityParametersReestimated = true,
            tfRefitted = false,
            blockingReoptimized = false,
            upperRetuned = false,
            lowerRetuned = false,
            certified = false,
            selectedForUse = false,
            operationallyActivated = false,
            independentGoldValidated = false,
            dt15Authorized = false,
            status = "NEW_V3_TRAIN_ONLY_CANDIDATE_NOT_ORIGINAL_FIT"
        };
        await File.WriteAllTextAsync(Path.Combine(dest, "derivation-receipt.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(receipt));
    }
}

using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// The exact V3 SHA-pinned Gold TF scoring/SQL-query path can be inspected
/// without a database, TRAIN/VALIDATION labels, publishing, or activation.
/// A test observation and a candidate are synthetic and never an accuracy
/// measurement. This is not a physical Gold incremental experiment.
/// </summary>
public static class V9FrozenGoldRuntimeProbeCli
{
    public const string Flag = "--v9-frozen-gold-real-fit-runtime-smoke";

    public sealed record Diagnostic(
        string Schema, string ParentSnapshotSha256, string GoldVariantSha256,
        int FrozenBlockingPasses, int ExecutableBlockingPasses,
        int SqlCandidateParameters, int JointNominalCells, int ActiveTfUnits,
        bool SqlCandidateQueryParameterized, bool ExactScorerResolveMatches,
        bool RejectsForeignCandidateSnapshot, bool RejectsIncompleteCandidateUnion,
        bool SqlConnected = false, bool PhysicalGoldExecuted = false,
        bool Calibrated = false, bool Certified = false, bool Activated = false);

    /// <summary>
    /// Pure in-memory exercise of the *real model*, not a mock scorer.
    /// Prevents accidental fallback to a different frozen SHA/projection.
    /// </summary>
    public static Diagnostic Probe(V9FittedSnapshot pinnedV3)
    {
        ArgumentNullException.ThrowIfNull(pinnedV3);
        var p = pinnedV3.Payload;
        var active = p.UnitTfCalibrations?.Count(x => x.State == V9SnapshotTfFitState.Active) ?? 0;
        if (p.SchemaVersion != V9FittedSnapshot.SupportSchemaVersion
            || p.JointSupport is null || p.JointCells.Count != 81
            || p.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || p.GoldParentSnapshotSha256 is not null || active < 1
            || p.BlockingPasses.Count < 1)
            throw new InvalidDataException("Probe exige V3 BLIND íntegro com TF de unidade ativa.");
        var gold = pinnedV3.CreateGoldHarnessVariant();
        var runtime = V9FittedSnapshotExecutionRuntime.Load(
            gold.SerializeUtf8(), gold.ContentSha256);
        var dob = new DateOnly(1980, 4, 12);
        // These are fixed fabricated values; no cohort, identity or oracle
        // record is read or output as part of the diagnostic.
        var observation = new IdentityObservation(
            null, "SEM_CPF", "MARIA SILVA", dob, "ANA COSTA");
        var candidate = new V9SnapshotCandidate(
            Guid.Parse("5e881e4a-5ab7-4b19-9df3-42f92c1a5a09"),
            "MARIA SILVA", dob, "ANA COSTA");
        var lookup = runtime.Plan(observation);
        var knownOrder = p.BlockingPasses.Select(x => x.PassId).ToArray();
        if (lookup.Count < 1 || lookup.Count > p.BlockingPasses.Count
            || !lookup.Select(x => x.PassId).SequenceEqual(
                knownOrder.Where(id => lookup.Any(x => x.PassId == id))))
            throw new InvalidDataException("Runtime não respeitou a ordem dos passes congelados.");
        using var command = new SqlCommand();
        var query = runtime.BuildCandidateUuidQuery(command, observation,
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
        if (command.Parameters.Count < 1
            || query.Contains("MARIA", StringComparison.OrdinalIgnoreCase)
            || query.Contains("ANA COSTA", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Consulta Gold violou contrato SQL parametrizado.");
        var frame = new V9RetrievedSnapshotCandidateFrame(gold.ContentSha256,
            runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256,
            true, [candidate]);
        var modelId = Guid.Parse("481f2bcc-1bb4-42a7-a1ee-9fae12a865d2");
        var actual = runtime.ResolveRetrievedCandidatesPreview(modelId, observation, frame);
        var expected = gold.ResolvePreview(modelId, observation, [candidate]);
        if (!actual.Equals(expected))
            throw new InvalidDataException("FS/TF/Resolve real divergiu da mesma configuração Gold.");
        static bool Refuses(Action test)
        {
            try
            {
                test();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }
        var wrongShaRefused = Refuses(() => runtime.ResolveRetrievedCandidatesPreview(
            modelId, observation, frame with { SnapshotSha256 = new string('0', 64) }));
        var truncatedRefused = Refuses(() => runtime.ResolveRetrievedCandidatesPreview(
            modelId, observation, frame with { CompleteCandidateUniverse = false }));
        if (!wrongShaRefused || !truncatedRefused)
            throw new InvalidDataException("Runtime não rejeitou candidatos de SHA falso ou união truncada.");
        return new("V9_REAL_FROZEN_GOLD_TF_RUNTIME_NO_SQL_V1",
            pinnedV3.ContentSha256, gold.ContentSha256,
            p.BlockingPasses.Count, lookup.Count, command.Parameters.Count,
            p.JointCells.Count, active, true, true, true, true);
    }

    public static async Task RunAsync(string[] args)
    {
        if (args is not { Length: 3 } || args[0] != Flag)
            throw new ArgumentException(
                "Uso: --v9-frozen-gold-real-fit-runtime-smoke <SHA.fitted.json> <sha256>.");
        var frozen = await V9FittedSnapshotArtifact.ReadPinnedAsync(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(Probe(frozen)));
    }
}

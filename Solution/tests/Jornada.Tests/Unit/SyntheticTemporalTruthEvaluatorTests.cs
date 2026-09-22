using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Linkage.Evaluation;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class SyntheticTemporalTruthEvaluatorTests
{
    private const string Fingerprint =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Test]
    public async Task Distinguishes_incremental_truth_from_unmeasured_and_measured_runs()
    {
        var root = NewRoot();
        try
        {
            WriteFixture(root, completedRun: true);
            var report = await SyntheticTemporalTruthEvaluator.EvaluateAsync(root);
            Assert.Multiple(() =>
            {
                Assert.That(report.Waves.Count, Is.EqualTo(2));
                Assert.That(report.Waves[0].CurrentMaterializedSources, Is.EqualTo(2));
                Assert.That(report.Waves[0].TruePairs.NeitherCpf, Is.EqualTo(1));
                Assert.That(report.Waves[0].MeasurementStatus, Does.StartWith("NAO_MEDIDO"));
                Assert.That(report.Waves[0].Precision, Is.Null);
                Assert.That(report.Waves[0].Recall, Is.Null);
                Assert.That(report.Waves[1].CurrentMaterializedSources, Is.EqualTo(4));
                Assert.That(report.Waves[1].CurrentObservations, Is.EqualTo(5));
                Assert.That(report.Waves[1].TruePairs.MixedCpf, Is.EqualTo(1));
                Assert.That(report.Waves[1].TruePairs.Total, Is.EqualTo(1));
                Assert.That(report.Waves[1].MeasurementStatus, Is.EqualTo("MEDIDO_SHADOW_REAL_SEM_PUBLICACAO"));
                Assert.That(report.Waves[1].TruePositive!.Total, Is.EqualTo(1));
                Assert.That(report.Waves[1].FalsePositive!.Total, Is.EqualTo(2));
                Assert.That(report.Waves[1].FalseNegative!.Total, Is.Zero);
                Assert.That(report.Waves[1].Precision, Is.EqualTo(1m / 3m));
                Assert.That(report.Waves[1].Recall, Is.EqualTo(1m));
                Assert.That(report.ModelPromotionAttempted, Is.False);
                Assert.That(report.TruthConsumedByIngestionOrCalibrator, Is.False);
            });
            var json = JsonSerializer.Serialize(report);
            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Not.Contain("TRUTH-P1"));
                Assert.That(json, Does.Not.Contain("SYNTH-A"));
                Assert.That(json, Does.Not.Contain("SYNTH-B"));
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task No_completed_run_does_not_invent_precision_or_recall()
    {
        var root = NewRoot();
        try
        {
            WriteFixture(root, completedRun: false);
            var report = await SyntheticTemporalTruthEvaluator.EvaluateAsync(root);
            Assert.Multiple(() =>
            {
                Assert.That(report.Waves[1].TruePairs.Total, Is.EqualTo(1));
                Assert.That(report.Waves[1].Precision, Is.Null);
                Assert.That(report.Waves[1].Recall, Is.Null);
                Assert.That(report.Waves[1].TruePositive, Is.Null);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Tampered_truth_hash_fails_closed()
    {
        var root = NewRoot();
        try
        {
            WriteFixture(root, completedRun: false);
            File.AppendAllText(Path.Combine(root, "ingestion", "wave-02", "bridge-truth.jsonl"),
                "extra\n");
            Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await SyntheticTemporalTruthEvaluator.EvaluateAsync(root);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Source_cannot_switch_ground_truth_between_waves()
    {
        var root = NewRoot();
        try
        {
            WriteFixture(root, completedRun: false, swapTruth: true);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await SyntheticTemporalTruthEvaluator.EvaluateAsync(root);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-temporal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ingestion"));
        return root;
    }

    private static void WriteFixture(string root, bool completedRun, bool swapTruth = false)
    {
        var ingestion = Path.Combine(root, "ingestion");
        var a = new SyntheticTemporalTruthEvaluator.SourceTruth(
            "MATERIALIZADA", "TRUTH-P1", "G0", "SYNTH-A", "OBS-A-W1");
        var b = new SyntheticTemporalTruthEvaluator.SourceTruth(
            "MATERIALIZADA", "TRUTH-P1", "G1", "SYNTH-B", "OBS-B-W1");
        var c = new SyntheticTemporalTruthEvaluator.SourceTruth(
            "MATERIALIZADA", "TRUTH-P2", "G2", "SYNTH-C", "OBS-C-W2");
        var d = new SyntheticTemporalTruthEvaluator.SourceTruth(
            "MATERIALIZADA", "TRUTH-P3", "G3", "SYNTH-D", "OBS-D-W2");
        var a2 = a with { ObservationId = "OBS-A-W2",
            BasePersonId = swapTruth ? "TRUTH-CHANGED" : a.BasePersonId };
        var before = new[]
        {
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-A", "G0", 1, 1, false, null, null, null),
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-B", "G1", 2, 1, false, null, null, null)
        };
        var after = new[]
        {
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-A", "G0", 3, 2, true, "RESOLVIDO", "UUID-SAME", "LINKAGE_PROBABILISTICO"),
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-B", "G1", 2, 1, false, "RESOLVIDO", "UUID-SAME", "LINKAGE_PROBABILISTICO"),
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-C", "G2", 4, 1, true, "RESOLVIDO", "UUID-OTHER", "LINKAGE_PROBABILISTICO"),
            new SyntheticTemporalTruthEvaluator.SourceSnapshot(
                "SYNTH-D", "G3", 5, 1, false, "RESOLVIDO", "UUID-SAME", "LINKAGE_PROBABILISTICO")
        };
        var wave1 = WriteWave(ingestion, 1, [a, b], before,
            new SyntheticTemporalTruthEvaluator.OperationalSnapshot(
                "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V1", 1, 2, 2,
                "CURRENT_IDENTITY_NO_RUN", null, "NAO_EXECUTADO", before));
        var wave2 = WriteWave(ingestion, 2, [a2, c, d], after,
            new SyntheticTemporalTruthEvaluator.OperationalSnapshot(
                completedRun ? "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V2" : "SYNTHETIC_WAVE_OPERATIONAL_SNAPSHOT_V1", 2, 4, 5,
                completedRun ? "DETERMINISTIC_PLUS_MODEL_VALIDATION_SHADOW" : "CURRENT_IDENTITY_NO_RUN",
                completedRun ? Guid.NewGuid().ToString("D") : null,
                completedRun ? "CONCLUIDO_SEM_PUBLICACAO" : "NAO_EXECUTADO", after,
                completedRun ? Guid.NewGuid().ToString("D") : null,
                completedRun ? 1 : null));
        WriteJson(Path.Combine(ingestion, "waves-manifest.json"), new
        {
            schemaVersion = 1,
            scenarioVersion = "SYNTHETIC_INGESTION_WAVES_V1",
            seed = 42,
            corpusInputFingerprintSha256 = Fingerprint,
            waves = new[] { wave1, wave2 }
        });
    }

    private static object WriteWave(
        string ingestion, int wave,
        SyntheticTemporalTruthEvaluator.SourceTruth[] truth,
        SyntheticTemporalTruthEvaluator.SourceSnapshot[] sources,
        SyntheticTemporalTruthEvaluator.OperationalSnapshot snapshot)
    {
        var dir = Path.Combine(ingestion, "wave-" + wave.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var truthPath = Path.Combine(dir, "bridge-truth.jsonl");
        File.WriteAllLines(truthPath, truth.Select(row => JsonSerializer.Serialize(row)));
        var manifestPath = Path.Combine(dir, "bridge-manifest.json");
        WriteJson(manifestPath, new
        {
            bridgeVersion = "SYNTHETIC_INGESTION_BRIDGE_WAVES_V1",
            corpusInputFingerprintSha256 = Fingerprint,
            materializedObservationCount = truth.Length
        });
        var snapshotPath = Path.Combine(dir, "operational-snapshot.json");
        WriteJson(snapshotPath, snapshot);
        File.WriteAllText(snapshotPath + ".sha256",
            Sha(snapshotPath) + "  operational-snapshot.json\n");
        return new
        {
            wave,
            manifestSha256 = Sha(manifestPath),
            truthSha256 = Sha(truthPath),
            materializedObservationCount = truth.Length
        };
    }

    private static void WriteJson(string path, object item) =>
        File.WriteAllText(path, JsonSerializer.Serialize(item) + "\n", new UTF8Encoding(false));

    private static string Sha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}

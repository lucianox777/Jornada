using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldJointTrainMatrixTests
{
    private static readonly string Fit = new('a', 64);
    private static readonly string Gold = new('b', 64);
    private static readonly string Csv = new('c', 64);
    private static readonly string Selection = new('d', 64);
    private static readonly decimal[] Uppers = [.8m, .9m];
    private static readonly decimal[] Lowers = [.1m, .2m];

    private static byte[] PlanBytes()
    {
        var plan = new V9GoldJointTrainMatrixCommand.Plan(
            V9GoldJointTrainMatrixCommand.Version, Fit, Gold, Csv, Selection,
            Uppers, Lowers, 2, ManualCalibrationRequested: true, TestUsed: false,
            V9LowerNonMatchTrainCalibrator.JointGridFingerprint(Uppers, Lowers));
        return JsonSerializer.SerializeToUtf8Bytes(plan);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[][] PhysicalReceipts(byte[] plan)
    {
        var rows = new List<byte[]>();
        var n = 0;
        foreach (var upper in Uppers)
        foreach (var lower in Lowers)
        foreach (var order in V9LowerNonMatchTrainCalibrator.JointOrders)
        {
            var root = V9GoldJointPhysicalReceiptTests.Receipt();
            var trial = (JsonObject)root["jointTrainPhysicalTrial"]!;
            trial["preregisteredPlanSha256"] = V9GoldJointTrainMatrixCommand.Sha(plan);
            trial["disposableLocalDbInstance"] =
                "(localdb)\\JornadaV9Gold20261031_" + n.ToString("x8", CultureInfo.InvariantCulture);
            trial["upperThreshold"] = upper;
            trial["lowerThreshold"] = lower;
            trial["orderId"] = order;
            ((JsonObject)root["jointUpperTrain"]!)["upperThreshold"] = upper;
            ((JsonObject)root["lowerNonMatchExperiment"]!)["threshold"] = lower;
            var three = (JsonObject)root["goldThreeOrderTrial"]!;
            three["orderId"] = order;
            three["executedWaveOrder"] = new JsonArray(
                GoldTrainThreeOrderProtocol.ResolveOrder(order, ["G0", "G1", "G2", "G3"])
                    .Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            three["isolatedRunFingerprintSha256"] = Digest("physical-" + n);
            rows.Add(JsonSerializer.SerializeToUtf8Bytes(root));
            n++;
        }
        return rows.ToArray();
    }

    [Test]
    public void Aggregates_exact_preregistered_real_receipt_contract_without_certification()
    {
        var plan = PlanBytes();
        var result = V9GoldJointTrainMatrixCommand.Aggregate(plan, PhysicalReceipts(plan));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(result.PhysicalCellCount, Is.EqualTo(12));
            Assert.That(result.Study.Candidates, Has.Count.EqualTo(4));
            Assert.That(result.Study.SuggestedUpperThreshold, Is.Not.Null);
            Assert.That(result.TestUsed, Is.False);
            Assert.That(result.IndependentlyCertified, Is.False);
            Assert.That(result.OperationallyActivated, Is.False);
        }));
    }

    [Test]
    public void Physical_orchestrator_grid_cells_must_all_be_preregistered_before_SQL()
    {
        var plan = PlanBytes();
        var fingerprint = V9GoldJointTrainMatrixCommand.Sha(plan);
        var count = 0;
        foreach (var upper in Uppers)
        foreach (var lower in Lowers)
        foreach (var order in V9LowerNonMatchTrainCalibrator.JointOrders)
        {
            Assert.That(V9GoldJointTrainMatrixCommand.ValidateCellPlan(plan, upper, lower, order),
                Is.EqualTo(fingerprint));
            count++;
        }
        Assert.That(count, Is.EqualTo(12));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ValidateCellPlan(plan, .8m, .1m, "BASELINE")));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ValidateCellPlan(plan, .95m, .1m,
                V9LowerNonMatchTrainCalibrator.JointOrders[0])));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ValidateCellPlan(plan, .8m, .3m,
                V9LowerNonMatchTrainCalibrator.JointOrders[0])));
    }

    [Test]
    public void Rejects_missing_repeated_or_cross_plan_cells()
    {
        var plan = PlanBytes();
        var receipts = PhysicalReceipts(plan);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, receipts[..^1])));
        var duplicate = PhysicalReceipts(plan);
        duplicate[1] = duplicate[0];
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, duplicate)));
        var cross = PhysicalReceipts(plan);
        var node = JsonNode.Parse(cross[0])!.AsObject();
        ((JsonObject)node["jointTrainPhysicalTrial"]!)["preregisteredPlanSha256"] = new string('f', 64);
        cross[0] = JsonSerializer.SerializeToUtf8Bytes(node);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, cross)));
    }

    [Test]
    public void Rejects_reused_disposable_database_and_unregistered_threshold()
    {
        var plan = PlanBytes();
        var receipts = PhysicalReceipts(plan);
        var node = JsonNode.Parse(receipts[1])!.AsObject();
        ((JsonObject)node["jointTrainPhysicalTrial"]!)["disposableLocalDbInstance"] =
            "(localdb)\\JornadaV9Gold20261031_00000000";
        receipts[1] = JsonSerializer.SerializeToUtf8Bytes(node);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, receipts)));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ValidateCellPlan(plan, .7m, .1m,
                V9LowerNonMatchTrainCalibrator.JointOrders[0])));
    }

    [Test]
    public void Rejects_reused_physical_SQL_run_fingerprint_despite_distinct_receipt_bytes()
    {
        var plan = PlanBytes();
        var receipts = PhysicalReceipts(plan);
        var first = JsonNode.Parse(receipts[0])!.AsObject();
        var second = JsonNode.Parse(receipts[1])!.AsObject();
        var firstRun = ((JsonObject)first["goldThreeOrderTrial"]!)["isolatedRunFingerprintSha256"]!
            .GetValue<string>();
        ((JsonObject)second["goldThreeOrderTrial"]!)["isolatedRunFingerprintSha256"] = firstRun;
        receipts[1] = JsonSerializer.SerializeToUtf8Bytes(second);

        // Different LocalDB instance labels and JSON digests must not conceal
        // a duplicated physical SQL run in the preregistered TRAIN matrix.
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, receipts)));
    }

    [Test]
    public void Rejects_incomplete_or_unknown_preregistered_plan_fields()
    {
        var missing = JsonNode.Parse(PlanBytes())!.AsObject();
        missing.Remove("SelectionEnvelopeSha256");
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ReadPlan(JsonSerializer.SerializeToUtf8Bytes(missing))));

        var unknown = JsonNode.Parse(PlanBytes())!.AsObject();
        unknown["TestApproved"] = true;
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ReadPlan(JsonSerializer.SerializeToUtf8Bytes(unknown))));
    }

    [Test]
    public void Rejects_ambiguous_duplicate_json_properties_in_frozen_plan()
    {
        var plan = Encoding.UTF8.GetString(PlanBytes());
        var ambiguous = plan.Replace("\"TestUsed\":false", "\"TestUsed\":true,\"TestUsed\":false", StringComparison.Ordinal);
        Assert.That(ambiguous, Is.Not.EqualTo(plan));
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ReadPlan(Encoding.UTF8.GetBytes(ambiguous))));
    }

    [Test]
    public void Rejects_nested_duplicate_receipt_fields_before_matrix_aggregation()
    {
        var plan = PlanBytes();
        var receipts = PhysicalReceipts(plan);
        var json = Encoding.UTF8.GetString(receipts[0]);
        var ambiguous = json.Replace("\"testUsed\":false", "\"testUsed\":true,\"testUsed\":false", StringComparison.Ordinal);
        Assert.That(ambiguous, Is.Not.EqualTo(json));
        receipts[0] = Encoding.UTF8.GetBytes(ambiguous);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.Aggregate(plan, receipts)));
    }

    [Test]
    public async Task Plan_preflight_rejects_alias_and_invalid_grid_before_any_SQL()
    {
        var directory = Path.Combine(Path.GetTempPath(), "v9-joint-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var validBytes = PlanBytes();
            var validSha = V9GoldJointTrainMatrixCommand.Sha(validBytes);
            var validPath = Path.Combine(directory, validSha + V9GoldJointTrainMatrixCommand.PlanSuffix);
            await File.WriteAllBytesAsync(validPath, validBytes);
            Assert.That(await V9GoldJointTrainMatrixCommand.ValidatePlanFileAsync(validPath),
                Is.EqualTo(validSha));

            var aliasPath = Path.Combine(directory, "mutable.joint-train-plan.json");
            await File.WriteAllBytesAsync(aliasPath, validBytes);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await V9GoldJointTrainMatrixCommand.ValidatePlanFileAsync(aliasPath));

            var invalid = JsonNode.Parse(validBytes)!.AsObject();
            invalid["DeclaredGridSha256"] = new string('f', 64);
            var invalidBytes = JsonSerializer.SerializeToUtf8Bytes(invalid);
            var invalidPath = Path.Combine(directory,
                V9GoldJointTrainMatrixCommand.Sha(invalidBytes) + V9GoldJointTrainMatrixCommand.PlanSuffix);
            await File.WriteAllBytesAsync(invalidPath, invalidBytes);
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await V9GoldJointTrainMatrixCommand.ValidatePlanFileAsync(invalidPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Rejects_unfrozen_grid_or_test_in_preregistered_plan()
    {
        var node = JsonNode.Parse(PlanBytes())!.AsObject();
        node["TestUsed"] = true;
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ReadPlan(JsonSerializer.SerializeToUtf8Bytes(node))));
        node = JsonNode.Parse(PlanBytes())!.AsObject();
        node["DeclaredGridSha256"] = new string('f', 64);
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9GoldJointTrainMatrixCommand.ReadPlan(JsonSerializer.SerializeToUtf8Bytes(node))));
    }
}

using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9JointSupportEstimationTests
{
    private static readonly string Train = new('a', 64);
    private static V9JointObservedCell[] Counts() => Enum.GetValues<CompositeNominalLevelV9>()
        .SelectMany(p => Enum.GetValues<CompositeNominalLevelV9>().Select(m => new V9JointObservedCell(p, m,
            p == CompositeNominalLevelV9.ExactFull && m == CompositeNominalLevelV9.ExactFull ? 1000 : 1,
            p == CompositeNominalLevelV9.Other && m == CompositeNominalLevelV9.Other ? 100000 : 1))).ToArray();

    [Test]
    public void Universal_nominal_contract_has_witnesses_for_all_81_cells_so_unobserved_is_not_structurally_impossible()
    {
        (string? Left, string? Right)[] witnesses = [(null, "Maria Silva"), ("Maria Silva", "Maria Silva"),
            ("Maria Aparecida Silva", "Maria Cecilia Silva"), ("Alice Costa", "Costa Alice"),
            ("Maria Silva", "Maria Silvaa"), ("Maria Aparecida Oliveira Silva", "Maria Cecilia Barbosa Silvaa"),
            ("Joao Silva", "Pedro Silva"), ("Maria Silva", "Maria Costa"), ("Alice Costa", "Pedro Lima")];
        var levels = witnesses.Select(w => CompositeNominalComparisonV9.Classify(w.Left, w.Right)).ToArray();
        Assert.That(levels, Is.EqualTo(Enum.GetValues<CompositeNominalLevelV9>()));
        var cells = witnesses.SelectMany(person => witnesses.Select(mother => (
            CompositeNominalComparisonV9.Classify(person.Left, person.Right),
            CompositeNominalComparisonV9.Classify(mother.Left, mother.Right)))).ToHashSet();
        Assert.That(cells.Count, Is.EqualTo(81));
    }

    [Test]
    public void Classwise_core_preserves_match_mass_when_U_dominates_and_does_not_require_both_classes_in_every_cell()
    {
        var raw = Counts().Select(c => c with { M = c.M == 1 ? 0 : c.M, U = c.U == 1 ? 0 : c.U }).ToArray();
        var fit = V9JointSupportEstimation.Estimate(raw, new(.99m, 5, 5), .5m, Train);
        var match = fit.Provenance.Cells.Single(c => c.Person == CompositeNominalLevelV9.ExactFull && c.Mother == CompositeNominalLevelV9.ExactFull);
        Assert.That(match.CoreM, Is.True);
        Assert.That(match.CoreU, Is.False);
        Assert.That(match.ObservedU, Is.Zero);
        Assert.That(match.MethodU, Is.EqualTo(V9JointSupportEstimation.Marginal));
        Assert.That(V9JointSupportEstimation.HasRequiredCoreSupport(fit.Provenance), Is.True);
        Assert.That(fit.Probabilities.All(c => c.M > 0 && c.U > 0), Is.True);
    }

    [Test]
    public void Backoff_is_the_conditional_product_of_same_TRAIN_marginals_and_preserves_joint_core_mass()
    {
        var raw = Counts();
        var fit = V9JointSupportEstimation.Estimate(raw, new(.99m, 5, 5), .5m, Train);
        foreach (var isM in new[] { true, false })
        {
            long Count(V9JointObservedCell c) => isM ? c.M : c.U;
            var total = raw.Sum(Count); var denominator = total + 81 * .5m;
            var core = fit.Provenance.Cells.Where(c => isM ? c.CoreM : c.CoreU).Select(c => (c.Person, c.Mother)).ToHashSet();
            decimal Q(V9JointObservedCell c) => ((raw.Where(x => x.Person == c.Person).Sum(Count) + 9 * .5m) / denominator)
                * ((raw.Where(x => x.Mother == c.Mother).Sum(Count) + 9 * .5m) / denominator);
            var rare = raw.Where(c => !core.Contains((c.Person, c.Mother))).ToArray();
            var residual = rare.Sum(c => (Count(c) + .5m) / denominator);
            foreach (var c in raw)
            {
                var estimated = fit.Probabilities.Single(x => x.Person == c.Person && x.Mother == c.Mother);
                var expected = core.Contains((c.Person, c.Mother)) ? (Count(c) + .5m) / denominator : residual / rare.Sum(Q) * Q(c);
                Assert.That(Math.Abs((isM ? estimated.M : estimated.U) - expected), Is.LessThan(0.000000000000000000000001m));
            }
            Assert.That(Math.Abs(fit.Probabilities.Sum(c => isM ? c.M : c.U) - 1), Is.LessThan(0.000000000000000000000001m));
        }
        Assert.That(JsonSerializer.Serialize(V9JointSupportEstimation.Estimate(raw.Reverse(), new(.99m, 5, 5), .5m, Train)),
            Is.EqualTo(JsonSerializer.Serialize(fit)));
    }

    [Test]
    public void Dominant_cells_still_require_support_and_uniform_full_core_does_not_divide_by_zero()
    {
        var raw = Counts().Select(c => c with { M = 1, U = 1 }).ToArray();
        var fit = V9JointSupportEstimation.Estimate(raw, new(.99m, 5, 5), .5m, Train);
        Assert.That(V9JointSupportEstimation.HasRequiredCoreSupport(fit.Provenance), Is.False);
        Assert.That(fit.Provenance.Cells.All(c => c.CoreM && c.CoreU), Is.True);
        Assert.That(fit.Provenance.FallbackScaleM, Is.Zero);
        Assert.That(fit.Probabilities.Sum(c => c.M), Is.EqualTo(1m).Within(.000000000000000000000001m));
    }

    [Test]
    public void Provenance_verification_recomputes_probabilities_counts_methods_and_TRAIN_binding()
    {
        var fit = V9JointSupportEstimation.Estimate(Counts(), new(.99m, 5, 5), .5m, Train);
        var verified = V9JointSupportEstimation.VerifyAndFreeze(fit.Provenance, fit.Probabilities, Train);
        Assert.That(verified, Is.Not.SameAs(fit.Provenance));
        Assert.That((Action)(() => V9JointSupportEstimation.VerifyAndFreeze(fit.Provenance, fit.Probabilities, new string('b', 64))), Throws.TypeOf<InvalidDataException>());
        var altered = fit.Provenance with { Cells = fit.Provenance.Cells.Select((c, i) => i == 0 ? c with { MethodM = "CLAIMED_JOINT" } : c).ToArray() };
        Assert.That((Action)(() => V9JointSupportEstimation.VerifyAndFreeze(altered, fit.Probabilities, Train)), Throws.TypeOf<InvalidDataException>());
        var probabilities = fit.Probabilities.Select((c, i) => i == 0 ? c with { M = c.M * 2 } : c).ToArray();
        Assert.That((Action)(() => V9JointSupportEstimation.VerifyAndFreeze(fit.Provenance, probabilities, Train)), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9JointSupportEstimation.Estimate(Counts().Skip(1), new(.99m, 5, 5), .5m, Train)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void V3_snapshot_roundtrip_reconciles_raw_counts_and_preserves_legacy_V2_bytes()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Solution", "src"))) directory = directory.Parent;
        const string legacySha = "613f914114073d4a9100e848a01a56321354ae9a7885a77462941ae86936953d";
        var bytes = File.ReadAllBytes(Path.Combine(directory!.FullName, "Solution/data/calibration/v9/bootstrap-v0/" + legacySha + ".fitted.json"));
        var legacy = V9FittedSnapshot.Load(bytes, legacySha);
        var audit = V9FittedJointSupportAudit.Reconstruct(legacy, .5m, 19896, 26378);
        var fit = V9JointSupportEstimation.Estimate(audit.Cells.Select(c => new V9JointObservedCell(c.Person, c.Mother,
            c.ObservedMPairs, c.ObservedUPairs)), new(.99m, 5, 5), .5m, legacy.Payload.TrainCorpusSha256);
        var snapshot = V9FittedSnapshot.Create(legacy.Payload with { SchemaVersion = V9FittedSnapshot.SupportSchemaVersion,
            JointCells = fit.Probabilities, JointSupport = fit.Provenance });
        var loaded = V9FittedSnapshot.Load(snapshot.SerializeUtf8(), snapshot.ContentSha256);
        Assert.That(loaded.ContentSha256, Is.EqualTo(snapshot.ContentSha256));
        Assert.That(V9FittedJointSupportAudit.Reconstruct(loaded, .5m, 19896, 26378).Cells, Is.EqualTo(audit.Cells));
        Assert.That(legacy.SerializeUtf8(), Is.EqualTo(bytes));
        Assert.That((Action)(() => V9FittedSnapshot.Create(snapshot.Payload with { JointSupport = null })), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FittedSnapshot.Create(snapshot.Payload with { SchemaVersion = V9FittedSnapshot.SchemaVersion })), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9FittedJointSupportAudit.Reconstruct(loaded, .5m, 19897, 26378)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Fresh_sample_design_uses_only_finalists_and_one_frozen_TEST_model()
    {
        var one = V9StatisticalSamplePlan.CalculateFinalistFdrOnly(1, 4, 1.35, [.01, .0025]);
        var three = V9StatisticalSamplePlan.CalculateFinalistFdrOnly(3, 4, 1.35, [.01, .0025]);
        Assert.That(three.ExternalValidationMultiplicity, Is.EqualTo(3));
        Assert.That(three.FrozenTestMultiplicity, Is.EqualTo(1));
        foreach (var band in three.RiskBands)
        {
            Assert.That(band.ValidationIndependentUnitsZeroError, Is.GreaterThan(one.RiskBands.Single(b => b.MaximumFdr == band.MaximumFdr).ValidationIndependentUnitsZeroError!.Value));
            Assert.That(band.TestIndependentUnitsZeroError, Is.EqualTo(one.RiskBands.Single(b => b.MaximumFdr == band.MaximumFdr).TestIndependentUnitsZeroError));
            var n = band.ValidationIndependentUnitsZeroError!.Value;
            double Bound(long size) => 4 * (1 - Math.Exp(Math.Log(.05 / 6) / size))
                / (1.35 - 4 * Math.Sqrt(Math.Log(6 / .05) / (2 * size)));
            Assert.That(Bound(n), Is.LessThanOrEqualTo(band.MaximumFdr));
            Assert.That(Bound(n - 1), Is.GreaterThan(band.MaximumFdr));
        }
        Assert.That((Action)(() => V9StatisticalSamplePlan.CalculateFinalistFdrOnly(14159502, 4, 1.35, [.01])), Throws.TypeOf<InvalidDataException>());
    }
}

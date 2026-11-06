using NUnit.Framework;
using Jornada.Linkage.Runner;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
public class V9MuEstimationSearchSpaceTests
{
    private static V9MuEstimationMethod Default() => new(V9MuEstimationSearchSpace.Default);
    private static V9MuEstimationMethod HistoricalV3() => new(
        V9MuEstimationSearchSpace.Classwise,
        new V9MuEstimationParameterGrid([.99m], [5], [5], [true]));

    [Test]
    public void Historical_V2_and_V3_are_two_explicit_branches_with_exact_V3_values()
    {
        var methods = new[] { Default(), HistoricalV3() };
        var variants = V9MuEstimationSearchSpace.Enumerate(methods);
        Assert.That(variants, Has.Count.EqualTo(2));
        Assert.Multiple((Action)(() =>
        {
            Assert.That(variants[0].Method, Is.EqualTo(V9MuEstimationSearchSpace.Default));
            Assert.That(variants[0].Policy, Is.Null);
            Assert.That(variants[1].Method, Is.EqualTo(V9MuEstimationSearchSpace.Classwise));
            Assert.That(variants[1].Policy!.ClassMassCoverage, Is.EqualTo(.99m));
            Assert.That(variants[1].Policy!.MinimumM, Is.EqualTo(5));
            Assert.That(variants[1].Policy!.MinimumU, Is.EqualTo(5));
            Assert.That(variants[1].Policy!.SupportedTailRule, Is.EqualTo(V9JointSupportEstimation.SupportedTailRule));
        }));
        var original = new[] { new V9FsAdaptiveOptionConfiguration("ONE_MF", .5m, .95m) };
        var expanded = V9MuEstimationSearchSpace.Expand(original, methods, maximumOptionCount: 2);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(expanded, Has.Count.EqualTo(2));
            Assert.That(expanded[0].JointSupportPolicy, Is.Null);
            Assert.That(expanded[1].JointSupportPolicy, Is.EqualTo(variants[1].Policy));
            Assert.That(expanded[0].SmoothingAlpha, Is.EqualTo(expanded[1].SmoothingAlpha));
            Assert.That(expanded[0].DecisionThreshold, Is.EqualTo(expanded[1].DecisionThreshold));
            Assert.That(expanded[0].FingerprintSha256, Is.Not.EqualTo(expanded[1].FingerprintSha256));
            Assert.That(original[0].JointSupportPolicy, Is.Null);
        }));
    }

    [Test]
    public void Lists_generate_only_explicit_cartesian_values_without_implicit_ranges()
    {
        var variants = V9MuEstimationSearchSpace.Enumerate([
            Default(),
            new(V9MuEstimationSearchSpace.Classwise,
                new([.95m, .99m], [3, 5], [3, 5], [true, false]))
        ]);
        Assert.That(variants, Has.Count.EqualTo(17));
        Assert.That(variants.Count(x => x.Policy is not null), Is.EqualTo(16));
        Assert.That(variants.Count(x => x.Policy?.ClassMassCoverage == .95m), Is.EqualTo(8));
        Assert.That(variants.Count(x => x.Policy?.ClassMassCoverage == .99m), Is.EqualTo(8));
        Assert.That(variants.Count(x => x.Policy?.SupportedTailRule is null), Is.EqualTo(9));
    }

    [Test]
    public void Explicit_U_shrinkage_grid_is_finite_and_has_unique_fingerprints()
    {
        var variants = V9MuEstimationSearchSpace.Enumerate([
            new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))
        ]);
        Assert.That(variants, Has.Count.EqualTo(4));
        Assert.That(variants.Select(x => x.Key).Distinct().Count(), Is.EqualTo(4));
        Assert.That(variants.Select(x => x.Policy!.UBackoffJointMixtureWeight),
            Is.EqualTo(new decimal?[] {0m, .25m, .5m, 1m}));
        Assert.That(variants.All(x => x.Key.Contains("_UJW", StringComparison.Ordinal)), Is.True);
        var original = new[] {new V9FsAdaptiveOptionConfiguration("TF0", .5m, .95m)};
        var expanded = V9MuEstimationSearchSpace.Expand(original,
            [new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))], 4);
        Assert.That(expanded.Select(x => x.FingerprintSha256).Distinct().Count(), Is.EqualTo(4));
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Expand(original,
            [new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))], 3)),
            Throws.TypeOf<InvalidDataException>());
        foreach (var weights in new[] {Array.Empty<decimal>(), new[] {.25m, .25m}, new[] {-1m}, new[] {1.1m}})
            Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
                new(V9MuEstimationSearchSpace.Classwise, new([.99m], [5], [5], [true], weights))
            ])), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void V2_historical_V3_and_explicit_U_mixture_coexist_with_distinct_provenance()
    {
        var methods = new V9MuEstimationMethod[]
        {
            Default(),
            HistoricalV3(),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true], [0m, .25m, .5m, 1m]))
        };
        var variants = V9MuEstimationSearchSpace.Enumerate(methods);
        Assert.That(variants, Has.Count.EqualTo(6));
        Assert.That(variants.Select(x => x.Method), Is.EqualTo(new[]
        {
            V9MuEstimationSearchSpace.Default,
            V9MuEstimationSearchSpace.Classwise,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture,
            V9MuEstimationSearchSpace.ClasswiseMixture
        }));
        Assert.That(variants[1].Policy!.UBackoffJointMixtureWeight, Is.Null);
        Assert.That(variants[2].Policy!.UBackoffJointMixtureWeight, Is.Zero);
        Assert.That(variants[1].Key, Does.Not.Contain("_UJW"));
        Assert.That(variants[2].Key, Does.Contain("_UJW0"));
        Assert.That(variants.Select(x => x.Key).Distinct().Count(), Is.EqualTo(6));

        var original = new[] { new V9FsAdaptiveOptionConfiguration("TF0", .5m, .95m) };
        var expanded = V9MuEstimationSearchSpace.Expand(original, methods, 6);
        Assert.That(expanded, Has.Count.EqualTo(6));
        Assert.That(expanded.Select(x => x.FingerprintSha256).Distinct().Count(), Is.EqualTo(6));
        Assert.That(expanded[1].JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Null);
        Assert.That(expanded[2].JointSupportPolicy!.UBackoffJointMixtureWeight, Is.Zero);
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Expand(original, methods, 5)),
            Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default(), HistoricalV3(),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true]))
        ])), Throws.TypeOf<InvalidDataException>());
        Assert.That((Action)(() => V9MuEstimationSearchSpace.Enumerate([
            Default(),
            new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [5], [5], [true], [0m])),
            new(V9MuEstimationSearchSpace.ClasswiseMixture,
                new([.99m], [5], [5], [true], [0m]))
        ])), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Unknown_methods_duplicate_values_and_absent_parameters_fail_closed()
    {
        Assert.Multiple((Action)(() =>
        {
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([Default(), Default()])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new("UNKNOWN")])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise)])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Default,
                new([.99m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([.99m, .99m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([1m], [5], [5], [true]))])));
            Assert.Throws<InvalidDataException>((Action)(() => V9MuEstimationSearchSpace.Enumerate([new(V9MuEstimationSearchSpace.Classwise,
                new([.99m], [0], [5], [true]))])));
        }));
    }

    [Test]
    public void Cannot_ignore_declared_grid_because_computation_budget_is_too_small()
    {
        var original = new[] { new V9FsAdaptiveOptionConfiguration("A", .5m, .95m) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MuEstimationSearchSpace.Expand(original, [Default(), HistoricalV3()], maximumOptionCount: 1)));
    }

    [Test]
    public void A_preexisting_MU_policy_must_not_be_silently_overwritten_by_a_new_method()
    {
        var policy = new V9JointSupportPolicy(.99m, 5, 5, V9JointSupportEstimation.SupportedTailRule);
        var original = new[] { new V9FsAdaptiveOptionConfiguration("A", .5m, .95m, JointSupportPolicy: policy) };
        Assert.Throws<InvalidDataException>((Action)(() =>
            V9MuEstimationSearchSpace.Expand(original, [Default()], maximumOptionCount: 1)));
    }
}

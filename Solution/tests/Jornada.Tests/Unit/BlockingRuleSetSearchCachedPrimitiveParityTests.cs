using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class BlockingRuleSetSearchCachedPrimitiveParityTests
{
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    public void Cached_primitive_columns_preserve_reference_pool_unions_and_final_statistics(
        int maxFields, bool requireU)
    {
        var features = new[] { "f6", "f2", "f4", "f1", "f5", "f3" };
        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed);
            var observations = Enumerable.Range(0, 40).Select(i =>
            {
                var values = new Dictionary<string, bool?>(StringComparer.Ordinal);
                foreach (var field in features)
                {
                    var value = random.Next(4);
                    // Absent dictionary entries and present-null entries are distinct
                    // representations of the same unavailable comparison.
                    if (value != 0) values[field] = value == 1 ? null : value == 2;
                }
                return new BlockingFeatureObservation(i % 3 == 0, values,
                    new[] { .1m, .3m, 1m, 2.75m }[i % 4]);
            }).ToArray();
            var options = new BlockingRuleSetSearchOptions(maxFields, 3, 8,
                MinimumTrueMatchRecall: seed % 2 == 0 ? 0d : .5d,
                RequireObservedNonMatchSupport: requireU);
            var reference = ReferenceCandidates(observations, features, options);
            var actual = BlockingRuleSetSearch.EnumerateCandidateRuleSets(observations, features, options);
            Assert.That(JsonSerializer.Serialize(actual.Candidates), Is.EqualTo(JsonSerializer.Serialize(reference)),
                $"Primitive pool/ordered union mismatch seed={seed}, maxFields={maxFields}, requireU={requireU}");
            BlockingRuleSetOptimizationResult expected;
            try
            {
                expected = BlockingRuleSetOptimizer.SelectBest(observations, reference,
                    options.MinimumTrueMatchRecall, options.RequireObservedNonMatchSupport);
            }
            catch (InvalidOperationException)
            {
                Assert.Throws<InvalidOperationException>((Action)(() =>
                    BlockingRuleSetSearch.SearchBest(observations, features, options)));
                continue;
            }
            var best = BlockingRuleSetSearch.SearchBest(observations, features, options);
            Assert.That(JsonSerializer.Serialize(best), Is.EqualTo(JsonSerializer.Serialize(expected)),
                $"Final diagnostic/statistical mismatch seed={seed}");
        }
    }

    // Independent reference path: the original full optimizer/diagnostic evaluates
    // each primitive, without the new byte-column cache or indexed reducer.
    private static IReadOnlyList<IReadOnlyList<LinkageBlockingPass>> ReferenceCandidates(
        IReadOnlyCollection<BlockingFeatureObservation> observations,
        IReadOnlyCollection<string> features,
        BlockingRuleSetSearchOptions options)
    {
        var canonical = features.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var passes = new List<LinkageBlockingPass>();
        for (var size = 1; size <= options.MaxFieldsPerPass; size++)
        foreach (var fields in Choose(canonical, size))
            passes.Add(LinkageBlockingPass.Create($"P{passes.Count + 1:D3}", fields));
        var evaluated = passes.Select(pass => BlockingRuleSetOptimizer.SelectBest(observations,
            new IReadOnlyList<LinkageBlockingPass>[] { new[] { pass } })).ToArray();
        var retained = new List<BlockingRuleSetOptimizationResult>();
        void Add(BlockingRuleSetOptimizationResult result)
        {
            if (retained.Count < options.PrimitivePoolSize
                && retained.All(x => x.CanonicalSignature != result.CanonicalSignature)) retained.Add(result);
        }
        var bestEligible = evaluated.Where(x => x.Diagnostic.TrueMatchRecall >= options.MinimumTrueMatchRecall)
            .Where(x => !options.RequireObservedNonMatchSupport || x.Diagnostic.NonMatchRetention > 0d)
            .OrderByDescending(x => x.Diagnostic.ReductionRatio)
            .ThenByDescending(x => x.Diagnostic.TrueMatchRecall)
            .ThenByDescending(x => x.Diagnostic.CompleteMatchCoverage)
            .ThenBy(x => x.FieldClauseCount).ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal).FirstOrDefault();
        if (bestEligible is not null) Add(bestEligible);
        var recall = evaluated.OrderByDescending(x => x.Diagnostic.TrueMatchRecall)
            .ThenByDescending(x => x.Diagnostic.ReductionRatio)
            .ThenByDescending(x => x.Diagnostic.CompleteMatchCoverage)
            .ThenBy(x => x.FieldClauseCount).ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal).ToArray();
        var reduction = evaluated.OrderByDescending(x => x.Diagnostic.ReductionRatio)
            .ThenByDescending(x => x.Diagnostic.TrueMatchRecall)
            .ThenByDescending(x => x.Diagnostic.CompleteMatchCoverage)
            .ThenBy(x => x.FieldClauseCount).ThenBy(x => x.CanonicalSignature, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < evaluated.Length && retained.Count < options.PrimitivePoolSize; i++)
        {
            Add(recall[i]);
            Add(reduction[i]);
        }
        var pool = retained.Select(x => x.Passes[0]).ToArray();
        var result = new List<IReadOnlyList<LinkageBlockingPass>>();
        for (var size = 1; size <= Math.Min(options.MaxPasses, pool.Length); size++)
            result.AddRange(Choose(pool, size));
        return result;
    }

    private static IEnumerable<IReadOnlyList<T>> Choose<T>(IReadOnlyList<T> values, int size)
    {
        if (size == 0) { yield return Array.Empty<T>(); yield break; }
        for (var i = 0; i <= values.Count - size; i++)
        foreach (var rest in Choose(values.Skip(i + 1).ToArray(), size - 1))
            yield return new[] { values[i] }.Concat(rest).ToArray();
    }
}

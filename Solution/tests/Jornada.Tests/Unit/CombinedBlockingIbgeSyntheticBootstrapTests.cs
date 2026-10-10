using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Linkage.SyntheticCorpus;

namespace Jornada.Tests.Unit;

/// <summary>
/// Regression bootstrap on the repository's hash-verified IBGE 2022 public projection.
/// The synthetic joint distribution and injected errors are NOT observed census records.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class CombinedBlockingIbgeSyntheticBootstrapTests
{
    [Test]
    public async Task Ibge_based_synthetic_bootstrap_is_replayable_and_combined_union_contains_exact()
    {
        var root = FindRepositoryRoot();
        var reference = Path.Combine(root, "Solution", "data", "reference", "ibge-nomes-2022");
        var options = new SyntheticCorpusOptions(People: 120, Seed: 526,
            ErrorProfile: "correlated", CpfBasePrevalence: 0,
            CnsBasePrevalence: 0);
        var source = await SyntheticCorpusSourceLoader.LoadBrasilTotalAsync(reference, options);
        var generator = new SyntheticCorpusGenerator(source.FirstNames, source.Surnames);
        var first = generator.Generate(options);
        var second = generator.Generate(options);

        // No CPF route, no base-person ID, and no truth labels enter the blocking planner.
        var firstEvidence = Evaluate(first.Observations);
        var replayEvidence = Evaluate(second.Observations);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(source.PhysicalSha256, Has.Length.EqualTo(64));
            Assert.That(source.CanonicalContentSha256, Has.Length.EqualTo(64));
            Assert.That(firstEvidence, Is.EqualTo(replayEvidence));
            Assert.That(firstEvidence.Eligible, Is.GreaterThan(0));
            Assert.That(firstEvidence.UnionRetained, Is.GreaterThanOrEqualTo(firstEvidence.ExactRetained));
            Assert.That(firstEvidence.UnionRetained, Is.LessThanOrEqualTo(firstEvidence.Eligible));
        }));

        // This is a bootstrap identity, not a measured recall threshold.
        var canonical = string.Join("|", CombinedIdentityCandidatePlanner.MethodVersion,
            source.CanonicalContentSha256, options.Seed, options.ErrorProfile,
            firstEvidence.Eligible, firstEvidence.ExactRetained, firstEvidence.UnionRetained);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        Assert.That(sha, Has.Length.EqualTo(64));
        TestContext.Progress.WriteLine($"SYNTHETIC_IBGE_COMBINED_BOOTSTRAP reference={source.CanonicalContentSha256} " +
            $"seed={options.Seed} method={CombinedIdentityCandidatePlanner.MethodVersion} " +
            $"eligible={firstEvidence.Eligible} exact={firstEvidence.ExactRetained} " +
            $"union={firstEvidence.UnionRetained} evidence_sha256={sha}");
    }

    private static (long Eligible, long ExactRetained, long UnionRetained) Evaluate(
        IReadOnlyList<SyntheticObservation> observations)
    {
        long eligible = 0, exact = 0, union = 0;
        var rows = observations.Where(o => o.Cpf is null && o.Name is not null &&
            o.MotherName is not null && o.BirthDate is not null).Take(240).ToArray();
        foreach (var left in rows)
        foreach (var right in rows)
        {
            if (ReferenceEquals(left, right) || left.Gestor == right.Gestor ||
                left.BasePersonId != right.BasePersonId) continue;
            eligible++;
            var input = new IdentityObservation(null, "NAO_INFORMADO",
                left.Name, left.BirthDate, left.MotherName);
            var passes = CombinedIdentityCandidatePlanner.Plan(input);
            var keys = BlockingProjectionKeyProjector.Project(right.Name, right.MotherName, right.BirthDate)
                .ToHashSet();
            var matches = passes.Where(p => p.Clauses.All(c =>
                c.Values.Any(v => keys.Contains(new BlockingProjectionKey(c.Feature, v)))))
                .Select(p => p.PassId).ToHashSet(StringComparer.Ordinal);
            if (matches.Contains("combined-exact")) exact++;
            if (matches.Count > 0) union++;
        }
        return (eligible, exact, union);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Solution", "Jornada.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, "Solution", "data", "reference", "ibge-nomes-2022")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found for IBGE synthetic bootstrap.");
    }
}

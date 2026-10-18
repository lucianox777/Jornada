using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

/// <summary>Versioned experimental configuration is evidence, never an activation flag.</summary>
[TestFixture, Category("Unit")]
public sealed class V9BootstrapCandidateEvidenceConfigTests
{
    private static string ManifestPath()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var atRoot = Path.Combine(directory.FullName, "Solution", "config",
                "linkage", "v9-bootstrap-experimental-candidates.json");
            if (File.Exists(atRoot))
                return atRoot;
            var atSolution = Path.Combine(directory.FullName, "config", "linkage",
                "v9-bootstrap-experimental-candidates.json");
            if (File.Exists(atSolution))
                return atSolution;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Config V9 precisa ser rastreada no repo.");
    }

    [Test]
    public void Experimental_winners_are_frozen_as_evidence_but_not_operational_parameters()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var root = document.RootElement;
        Assert.That(root.GetProperty("schemaVersion").GetString(),
            Is.EqualTo("V9_BOOTSTRAP_EXPERIMENTAL_CANDIDATES_V1"));
        Assert.That(root.GetProperty("active").GetBoolean(), Is.False);
        Assert.That(root.GetProperty("status").GetString(),
            Is.EqualTo("HISTORICAL_DIAGNOSTIC_NOT_APPROVED_NOT_OPERATIONAL"));
        Assert.That(root.GetProperty("referenceStatus").GetString(),
            Is.EqualTo("NOT_MATERIALIZED_OR_APPROVED_BY_THIS_FILE"));
        Assert.That(root.GetProperty("population")
            .GetProperty("baseIdentitiesPerSeed").GetInt32(), Is.EqualTo(30_000));
        Assert.That(root.GetProperty("population")
            .GetProperty("testUsedForParameterSelection").GetBoolean(), Is.False);
        var candidates = root.GetProperty("observations").EnumerateArray().ToArray();
        Assert.That(candidates, Has.Length.EqualTo(2));
        var clean = candidates.Single(x => x.GetProperty("profile").GetString() == "clean");
        var correlated = candidates.Single(x => x.GetProperty("profile").GetString() == "correlated");
        var cleanPasses = clean.GetProperty("selectedPassIds")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.That(cleanPasses, Is.EqualTo(new[] { "P_PHON_M_LAST", "P_PREFIX3_DOB" }));
        Assert.That(clean.GetProperty("signatureComplete").GetBoolean(), Is.True);
        Assert.That(correlated.GetProperty("signatureComplete").GetBoolean(), Is.True);
        Assert.That(correlated.GetProperty("selectedPassCount").GetInt32(), Is.EqualTo(9));
        Assert.That(correlated.GetProperty("selectedPassIds")
            .EnumerateArray().Select(x => x.GetString()).ToArray(), Is.EqualTo(new[]
            {
                "P_PHON_BIRTH_YEAR", "P_LAST_M_PHON", "P_PREFIX3_M_PREFIX3",
                "P_PREFIX3_M_LAST", "P_FIRST_DOB_MONTHDAY",
                "P_ANY_BIRTH_YEAR", "P_ANY_DOB_MONTHDAY",
                "P_PREFIX3_BIRTH_YEAR", "P_LAST_M_FIRST"
            }));
        var source = root.GetProperty("sourceEvidence");
        Assert.That(source.GetProperty("runId").GetInt64(), Is.EqualTo(38628254862L));
        Assert.That(source.GetProperty("artifactId").GetInt64(), Is.EqualTo(11926175227L));
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.GetProperty("canonicalSha256").GetString(), Has.Length.EqualTo(64));
            Assert.That(candidate.GetProperty("manifestSha256").GetString(), Has.Length.EqualTo(64));
            Assert.That(candidate.GetProperty("sourceReportSha256").GetString(), Has.Length.EqualTo(64));
            Assert.That(candidate.GetProperty("selectedPassCount").GetInt32(),
                Is.EqualTo(candidate.GetProperty("selectedPassIds").GetArrayLength()));
            Assert.That(candidate.GetProperty("train").GetProperty("p99FalseCandidateDegree").GetInt32(),
                Is.GreaterThanOrEqualTo(candidate.GetProperty("train")
                    .GetProperty("p95FalseCandidateDegree").GetInt32()));
        }
        Assert.That(clean.GetProperty("train").GetProperty("recoveredM").GetInt32(),
            Is.EqualTo(19630));
        Assert.That(clean.GetProperty("train").GetProperty("returnedFalseCandidatesU").GetInt32(),
            Is.EqualTo(242));
        Assert.That(clean.GetProperty("validation").GetProperty("recoveredM").GetInt32(),
            Is.EqualTo(6509));
        Assert.That(correlated.GetProperty("train").GetProperty("recoveredM").GetInt32(),
            Is.EqualTo(19437));
        Assert.That(correlated.GetProperty("train")
            .GetProperty("identityClusterRecallLower95").GetDecimal(), Is.LessThan(.95m));
        foreach (var candidate in candidates)
        {
            var train = candidate.GetProperty("train");
            var validation = candidate.GetProperty("validation");
            Assert.That(train.GetProperty("recoveredM").GetInt32() * 20L,
                Is.GreaterThanOrEqualTo(19L * train.GetProperty("trueM").GetInt32()));
            Assert.That(validation.GetProperty("recoveredM").GetInt32() * 20L,
                Is.GreaterThanOrEqualTo(19L * validation.GetProperty("trueM").GetInt32()));
            Assert.That(candidate.GetProperty("selectedPassCount").GetInt32(),
                Is.GreaterThanOrEqualTo(candidate.GetProperty("selectedPassIds").GetArrayLength()));
            Assert.That(candidate.GetProperty("evidenceStatus").GetString(),
                Does.Not.Contain("CERTIFIED"));
        }
        var prerequisites = root.GetProperty("missingForApproval")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.That(prerequisites, Does.Contain("NEW_UNSEEN_SEALED_TEST"));
        Assert.That(prerequisites, Does.Not.Contain("COMPLETE_CORRELATED_PASS_ORDER_AND_SIGNATURE"));
        Assert.That(prerequisites, Does.Contain("CONDITIONED_SQL_TRAIN_M_U_PRIOR_AND_TF_SAME_UNIT"));
        var historical = root.GetProperty("historicalMeasurementWarning");
        Assert.That(historical.GetProperty("currentReplacementCertified").GetBoolean(), Is.False);
        Assert.That(historical.GetProperty("correctedCauseIndependentlyVerified").GetBoolean(),
            Is.False);
        foreach (var flag in root.GetProperty("safety").EnumerateObject())
            Assert.That(flag.Value.GetBoolean(), Is.False, flag.Name);
    }
}

using System.Text;
using Jornada.Linkage.Conference;
using NUnit.Framework;

namespace Jornada.Tests.Integration;

/// <summary>
/// Oráculos congelados, independentes do scorer operacional. Os testes negativos
/// mutam somente a referência, sem chamar o scorer para regenerar esperados.
/// </summary>
[TestFixture, Category("Integration"), NonParallelizable]
public sealed class GoldenReferenceVectorVerifierTests
{
    private static string SolutionRoot
    {
        get
        {
            var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "RELEASE_INFO.txt"))
                    && Directory.Exists(Path.Combine(current.FullName, "Solution")))
                    return Path.Combine(current.FullName, "Solution");
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Raiz Jornada não encontrada.");
        }
    }

    private static string Vectors => Path.Combine(
        SolutionRoot, "src", "Jornada.Linkage.Conference",
        "vectors", "golden-reference-v1.json");

    private static string Tolerance => Path.Combine(
        SolutionRoot, "config", "linkage",
        "implementation-conference-tolerance.json");

    [Test]
    public void Literal_goldens_cover_scores_comparators_birth_and_policy_without_sql()
    {
        var report = GoldenReferenceVectorVerifier.Verify(Vectors, Tolerance);
        Assert.Multiple(() =>
        {
            Assert.That(report.ReferenceVersion,
                Is.EqualTo("JORNADA_CONFERENCE_GOLDEN_V1_2026-09-29"));
            Assert.That(report.ToleranceVersion, Is.EqualTo("V1_2026-09-26"));
            Assert.That(report.ScoringVectors, Is.GreaterThanOrEqualTo(7));
            Assert.That(report.NameVectors, Is.GreaterThanOrEqualTo(9));
            Assert.That(report.BirthVectors, Is.EqualTo(7));
            Assert.That(report.PolicyVectors, Is.GreaterThanOrEqualTo(6));
            Assert.That(report.GuardVectors, Is.GreaterThanOrEqualTo(5));
            Assert.That(report.NumericDiagnostics, Has.Count.EqualTo(18));
            Assert.That(report.Runtime, Does.Contain(".NET"));
            Assert.That(report.NumericDiagnostics.All(
                item => item.AbsolutePairLlrDifference <= 0.000000000001m
                    && item.AbsoluteLogOddsDifference == 0m
                    && item.AbsolutePosteriorDifference == 0m), Is.True);
            Assert.That(report.VectorsSha256, Has.Length.EqualTo(64));
            Assert.That(report.Status,
                Is.EqualTo("TECHNICAL_VECTORS_PASSED_NOT_SQL_EVIDENCE"));
            // A verificação automatizada NÃO fabrica uma revisão humana.
            Assert.That(report.HumanReview, Is.EqualTo("PENDING_HUMAN_REVIEW"));
        });
        TestContext.Progress.WriteLine(
            $"Golden {report.ReferenceVersion}; sha256={report.VectorsSha256}; " +
            $"score={report.ScoringVectors} nome={report.NameVectors} " +
            $"nascimento={report.BirthVectors} políticas={report.PolicyVectors} " +
            $"guardInputs={report.GuardVectors}; " +
            $"humanReview={report.HumanReview}");
    }

    [Test]
    public void Mutating_one_literal_expected_llr_fails_closed()
    {
        var original = File.ReadAllText(Vectors, Encoding.UTF8);
        const string originalValue = "\"totalLlr\": 3.4657359027997265";
        Assert.That(original, Does.Contain(originalValue));
        var mutated = original.Replace(
            originalValue, "\"totalLlr\": 3.4000000000000000",
            StringComparison.Ordinal);
        WithTempFile(mutated, path =>
        {
            var ex = Assert.Throws<InvalidDataException>(
                () => GoldenReferenceVectorVerifier.Verify(path, Tolerance));
            Assert.That(ex!.Message, Does.Contain("GOLDEN_VECTOR_MISMATCH"));
        });
    }

    [Test]
    public void Human_review_cannot_be_marked_reviewed_without_identity_date_and_reference()
    {
        var original = File.ReadAllText(Vectors, Encoding.UTF8);
        var mutated = original.Replace(
            "\"status\": \"PENDING_HUMAN_REVIEW\"",
            "\"status\": \"REVIEWED\"",
            StringComparison.Ordinal);
        Assert.That(mutated, Is.Not.EqualTo(original));
        WithTempFile(mutated, path =>
        {
            var ex = Assert.Throws<InvalidDataException>(
                () => GoldenReferenceVectorVerifier.Verify(path, Tolerance));
            Assert.That(ex!.Message, Does.Contain("HUMAN_REVIEW_METADATA_INCOMPLETE"));
        });
    }

    [Test]
    public void Unfrozen_governance_tolerance_blocks_every_vector_before_execution()
    {
        var original = File.ReadAllText(Tolerance, Encoding.UTF8);
        var mutated = original.Replace(
            "\"status\": \"FROZEN\"", "\"status\": \"UNFROZEN\"",
            StringComparison.Ordinal);
        Assert.That(mutated, Is.Not.EqualTo(original));
        WithTempFile(mutated, path =>
        {
            var ex = Assert.Throws<InvalidDataException>(
                () => GoldenReferenceVectorVerifier.Verify(Vectors, path));
            Assert.That(ex!.Message, Does.Contain("GOLDEN_TOLERANCE_NOT_FROZEN"));
        });
    }

    [Test]
    public void Tampering_with_literal_guard_input_is_detected_without_regenerating_expected()
    {
        var original = File.ReadAllText(Vectors, Encoding.UTF8);
        const string target = "\"expectedCollisionRisk\": true";
        Assert.That(original, Does.Contain(target));
        var altered = original.Replace(
            target, "\"expectedCollisionRisk\": false",
            StringComparison.Ordinal);
        WithTempFile(altered, path =>
        {
            var ex = Assert.Throws<InvalidDataException>(
                () => GoldenReferenceVectorVerifier.Verify(path, Tolerance));
            Assert.That(ex!.Message, Does.Contain("ORACLE_GUARD_INPUT_INVALID"));
        });
    }

    [Test]
    public void Offline_mode_never_requires_model_id_or_database()
    {
        var options = ConferenceOptions.Parse(
            ["--verify-golden", "--golden-vectors", Vectors, "--tolerance-config", Tolerance]);
        Assert.Multiple(() =>
        {
            Assert.That(options.VerifyGolden, Is.True);
            Assert.That(options.ModelId, Is.Null);
            Assert.That(options.ConnectionString, Is.Null);
            Assert.That(options.GoldenVectorsPath, Is.EqualTo(Vectors));
            Assert.That(options.RequireHumanReview, Is.False);
        });
    }

    private static void WithTempFile(string content, Action<string> action)
    {
        var filename = Path.Combine(
            Path.GetTempPath(), "jornada-golden-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(filename, content, new UTF8Encoding(false));
            action(filename);
        }
        finally
        {
            if (File.Exists(filename))
                File.Delete(filename);
        }
    }
}

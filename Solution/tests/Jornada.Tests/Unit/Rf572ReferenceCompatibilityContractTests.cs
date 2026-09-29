using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class Rf572ReferenceCompatibilityContractTests
{
    private static string WorkerFile(string name)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "Solution", "src")))
            current = current.Parent;
        Assert.That(current, Is.Not.Null, "Raiz do repositório Jornada não encontrada.");
        return File.ReadAllText(Path.Combine(current!.FullName, "Solution", "src", "Jornada.Linkage.Parameters.Worker", name));
    }

    [Test]
    public void EnsureCanonical_RequiresCompleteSha256BeforeReuseOrReactivation()
    {
        var source = WorkerFile("NameFrequencyReferenceState.cs");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("hash is not { Length: 32 }"));
            Assert.That(source, Does.Contain("DATALENGTH(conteudo_sha256)=32"));
            Assert.That(source, Does.Contain("IsolationLevel.Serializable"));
            Assert.That(source, Does.Contain("normalization, IdentityComparison.NormalizationVersion"));
            Assert.That(source, Does.Contain("schema != 1"));
        });
    }

    [Test]
    public void GenerateDraft_RequiresCompatibleReferenceBeforeWorkerStarts()
    {
        var source = WorkerFile("Program.cs");
        var start = source.IndexOf("if (operation == \"GENERATE_DRAFT\"", StringComparison.Ordinal);
        var end = source.IndexOf("builder.Services.AddSingleton<IOperationalSqlAdapter>", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(end, Is.GreaterThan(start));
        var precondition = source[start..end];
        Assert.Multiple(() =>
        {
            Assert.That(precondition, Does.Contain("GenerateDraftIbgePrecondition.RequireActiveAsync"));
            Assert.That(precondition, Does.Contain("HasActiveNameFrequencyReferenceAsync"));
            Assert.That(precondition, Does.Not.Contain("NameFrequencySnapshotLoader"));
            Assert.That(precondition, Does.Not.Contain("EnsureCanonicalActiveAsync"));
            Assert.That(source, Does.Contain("v.normalizacao_versao=@normalizacao"));
            Assert.That(source, Does.Contain("v.manifest_schema_version=1"));
            Assert.That(source, Does.Contain("DATALENGTH(v.conteudo_sha256)=32"));
        });
    }
}

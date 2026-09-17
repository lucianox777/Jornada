namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class ProcessorV4ProviderBoundaryTests
{
    [Test]
    public void SqlServer_adapter_routes_v4_to_dedicated_cutover()
    {
        var source = File.ReadAllText(WorkerSource("SqlProcessorRepositoryAdapter.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("package.Manifest.PessoaSchemaVersao >= 4"));
            Assert.That(source, Does.Contain("inner.PersistValidatedV4Async(batch, package, ct)"));
            Assert.That(source, Does.Contain("inner.PersistValidatedAsync(batch, package, ct)"));
        });
    }

    [Test]
    public void PostgreSql_adapter_fails_closed_for_v4_before_delegating()
    {
        var source = File.ReadAllText(WorkerSource("PostgreSqlProcessorRepositoryAdapter.cs"));
        var guard = source.IndexOf("PessoaSchemaVersao >= 4", StringComparison.Ordinal);
        var delegation = source.IndexOf("inner.PersistValidatedAsync(batch, package, ct)", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(guard, Is.GreaterThanOrEqualTo(0));
            Assert.That(delegation, Is.GreaterThan(guard));
            Assert.That(source, Does.Contain("runtime operacional SQL Server"));
        });
    }

    [Test]
    public void Legacy_nullable_suppression_is_limited_to_two_files_and_one_warning()
    {
        var config = File.ReadAllText(WorkerSource(".editorconfig"));

        Assert.Multiple(() =>
        {
            Assert.That(config, Does.Contain("[SqlProcessorRepository.Persistence.cs]"));
            Assert.That(config, Does.Contain("[PostgreSqlProcessorRepository.cs]"));
            Assert.That(config.Split("dotnet_diagnostic.CS8604.severity = none", StringSplitOptions.None).Length - 1, Is.EqualTo(2));
            Assert.That(config, Does.Not.Contain("CS8602"));
            Assert.That(config, Does.Not.Contain("CS8618"));
        });
    }

    [Test]
    public void Parser_never_derives_v4_source_code_from_cpf()
    {
        var source = File.ReadAllText(WorkerSource("ProcessorModels.cs"));
        var guard = source.IndexOf("batch.PessoaSchemaVersao < 4", StringComparison.Ordinal);
        var fallback = source.IndexOf("sourceCode = cpf;", StringComparison.Ordinal);

        Assert.That(guard, Is.GreaterThanOrEqualTo(0));
        Assert.That(fallback, Is.GreaterThan(guard));
    }

    private static string WorkerSource(string file)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

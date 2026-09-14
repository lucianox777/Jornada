using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

public sealed class PersonV4PersistenceRoutingTests
{
    [Fact]
    public void Adapter_RoutesV4Only_ToDedicatedPersistence()
    {
        var source = File.ReadAllText(SourcePath("SqlProcessorRepositoryAdapter.cs"));

        Assert.Contains("batch.PessoaSchemaVersao >= 4", source, StringComparison.Ordinal);
        Assert.Contains("inner.PersistValidatedV4Async", source, StringComparison.Ordinal);
        Assert.Contains("inner.PersistValidatedAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void V4Persistence_DoesNotCreateOrigin_WhenSourceCodeIsAbsent()
    {
        var source = File.ReadAllText(SourcePath("SqlProcessorRepository.V4.cs"));

        Assert.Contains("PersonOriginSqlPersistence.ResolveOrCreateAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (origin is null)", source, StringComparison.Ordinal);
        Assert.Contains("pessoa_origem_id", source, StringComparison.Ordinal);
        Assert.Contains("DBNull.Value", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", source, StringComparison.Ordinal);
    }

    [Fact]
    public void V4Persistence_PersistsIdentifiersBeforeIdentityDecision()
    {
        var source = File.ReadAllText(SourcePath("SqlProcessorRepository.V4.cs"));
        var identifiers = source.IndexOf("PersistPersonIdentifiersAsync", StringComparison.Ordinal);
        var resolution = source.IndexOf("PersonIdentityResolutionSql.ResolveAsync", StringComparison.Ordinal);

        Assert.True(identifiers >= 0);
        Assert.True(resolution > identifiers);
    }

    [Fact]
    public void V4Persistence_RecordsFeedbackInconsistencyWithoutChangingCpfAnchor()
    {
        var source = File.ReadAllText(SourcePath("SqlProcessorRepository.V4.cs"));
        var resolver = File.ReadAllText(SourcePath("PersonIdentityResolutionSql.cs"));

        Assert.Contains("INCONSISTENCIA_RETROALIMENTACAO", source, StringComparison.Ordinal);
        Assert.Contains("Política de identificadores tentou substituir a âncora CPF", resolver, StringComparison.Ordinal);
        Assert.Contains("SqlIdentityMapRepository.ResolveOrCreateByCpfAsync", resolver, StringComparison.Ordinal);
        Assert.Contains("JornadaUuidLookup.ResolveAsync", resolver, StringComparison.Ordinal);
    }

    private static string SourcePath(string file)
    {
        var current = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(current, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

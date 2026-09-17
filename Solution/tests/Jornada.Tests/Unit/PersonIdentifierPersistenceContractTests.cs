using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

public sealed class PersonIdentifierPersistenceContractTests
{
    [Fact]
    public void SqlPersistence_UsesOnlyAuthorizedPersonBase()
    {
        var source = File.ReadAllText(SourcePath("PersonIdentifierSqlPersistence.cs"));
        Assert.Contains("ref.sistema_origem_base_pessoa", source, StringComparison.Ordinal);
        Assert.Contains("sb.sistema_origem_id=@sistema", source, StringComparison.Ordinal);
        Assert.Contains("sb.ativo=1", source, StringComparison.Ordinal);
        Assert.Contains("b.ativo=1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("codigo='JORNADA'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlPersistence_AllowsZeroIdentifiersWithoutSyntheticKey()
    {
        var source = File.ReadAllText(SourcePath("PersonIdentifierSqlPersistence.cs"));
        Assert.Contains("identifiers is null || identifiers.Count == 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_CutsCpfFallbackOnlyAtV4()
    {
        var source = File.ReadAllText(SourcePath("ProcessorModels.cs"));
        Assert.Contains("batch.PessoaSchemaVersao < 4", source, StringComparison.Ordinal);
        Assert.Contains("PersonIdentifierParsing.Parse", source, StringComparison.Ordinal);
    }

    private static string SourcePath(string file)
    {
        var current = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(current, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

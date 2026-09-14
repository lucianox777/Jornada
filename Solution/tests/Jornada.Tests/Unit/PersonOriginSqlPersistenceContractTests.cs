using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

public sealed class PersonOriginSqlPersistenceContractTests
{
    [Fact]
    public void Uses_Base_And_Code_As_Source_Identity_Namespace()
    {
        var source = File.ReadAllText(SourcePath("PersonOriginSqlPersistence.cs"));

        Assert.Contains("base_pessoa_origem_id=@base AND codigo_pessoa_origem=@codigo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE sistema_origem_id=@sistema AND codigo_pessoa_origem=@codigo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Requires_Base_Authorization_And_Registers_System_Use()
    {
        var source = File.ReadAllText(SourcePath("PersonOriginSqlPersistence.cs"));

        Assert.Contains("ref.sistema_origem_base_pessoa", source, StringComparison.Ordinal);
        Assert.Contains("sb.sistema_origem_id=@sistema", source, StringComparison.Ordinal);
        Assert.Contains("silver.pessoa_origem_sistema", source, StringComparison.Ordinal);
        Assert.Contains("return null", source, StringComparison.Ordinal);
    }

    private static string SourcePath(string file)
    {
        var current = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(current, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

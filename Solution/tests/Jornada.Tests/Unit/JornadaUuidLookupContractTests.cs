using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

public sealed class JornadaUuidLookupContractTests
{
    [Fact]
    public void Lookup_Is_ReadOnly_And_Uses_Authoritative_Progressive_State()
    {
        var source = File.ReadAllText(SourcePath("JornadaUuidLookup.cs"));

        Assert.Contains("identidade.pessoa_origem_progressiva", source, StringComparison.Ordinal);
        Assert.Contains("p.canonical_uuid=@uuid", source, StringComparison.Ordinal);
        Assert.Contains("p.initial_uuid=@uuid", source, StringComparison.Ordinal);
        Assert.Contains("p.estado='REFERENCIA'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResolveOrCreate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_Refuses_Ambiguous_Or_Unresolved_State()
    {
        var source = File.ReadAllText(SourcePath("JornadaUuidLookup.cs"));

        Assert.Contains("return null", source, StringComparison.Ordinal);
        Assert.Contains("resolução autoritativa ambígua", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PROVISORIA", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INDEFINIDA", source, StringComparison.Ordinal);
    }

    private static string SourcePath(string file)
    {
        var current = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(current, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

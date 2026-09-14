using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

public sealed class JornadaUuidLookupContractTests
{
    [Fact]
    public void Lookup_Is_ReadOnly_And_Uses_PersonIdentityAuthority()
    {
        var source = File.ReadAllText(SourcePath("JornadaUuidLookup.cs"));

        Assert.Contains("FROM identidade.pessoa", source, StringComparison.Ordinal);
        Assert.Contains("pessoa_uuid_sucessor", source, StringComparison.Ordinal);
        Assert.Contains("status", source, StringComparison.Ordinal);
        Assert.DoesNotContain("pessoa_origem_progressiva", source, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResolveOrCreate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_Follows_Only_Explicit_Merge_Successor()
    {
        var source = File.ReadAllText(SourcePath("JornadaUuidLookup.cs"));

        Assert.Contains("status, \"FUNDIDO\"", source, StringComparison.Ordinal);
        Assert.Contains("successor.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("status, \"ATIVO\"", source, StringComparison.Ordinal);
        Assert.Contains("SEPARADO", source, StringComparison.Ordinal);
        Assert.Contains("INATIVO", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Lookup_FailsClosed_On_Cycle_And_Excessive_Depth()
    {
        var source = File.ReadAllText(SourcePath("JornadaUuidLookup.cs"));

        Assert.Contains("HashSet<Guid>", source, StringComparison.Ordinal);
        Assert.Contains("Ciclo detectado", source, StringComparison.Ordinal);
        Assert.Contains("MaxRedirectDepth", source, StringComparison.Ordinal);
        Assert.Contains("excede o limite operacional", source, StringComparison.Ordinal);
    }

    private static string SourcePath(string file)
    {
        var current = AppContext.BaseDirectory;
        var root = Path.GetFullPath(Path.Combine(current, "../../../../../"));
        return Path.Combine(root, "src", "Jornada.Processor.Worker", file);
    }
}

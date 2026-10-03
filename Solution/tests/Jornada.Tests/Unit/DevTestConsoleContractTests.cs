namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevTestConsoleContractTests
{
    private static string Root()
    {
        var dir=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(dir is not null && !Directory.Exists(Path.Combine(dir.FullName,"Solution"))) dir=dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Test]
    public void Console_is_development_only_cumulative_and_one_shot()
    {
        var api=File.ReadAllText(Path.Combine(Root(),"Solution","src","Jornada.Api","DevTestConsoleApi.cs"));
        var runner=File.ReadAllText(Path.Combine(Root(),"Solution","src","Jornada.Api","DevTestConsole.cs"));
        Assert.Multiple(() => {
            Assert.That(api,Does.Contain("if(!env.IsDevelopment()) return app"));
            Assert.That(api,Does.Contain("/api/dev/test/sessions"));
            Assert.That(api,Does.Contain("actions/update-build"));
            Assert.That(api,Does.Contain("Parâmetros ativos"));
            Assert.That(api,Does.Contain("Histórico cumulativo"));
            Assert.That(runner,Does.Contain("session.Executions.Add(execution)"));
            Assert.That(runner,Does.Contain("git\",\"pull --ff-only"));
            Assert.That(runner,Does.Contain("dotnet\",\"restore Solution/Jornada.sln --locked-mode"));
            Assert.That(runner,Does.Contain("dotnet\",\"build Solution/Jornada.sln --no-restore --configuration Release"));
            Assert.That(runner,Does.Not.Contain("while (true)"));
            Assert.That(runner,Does.Not.Contain("PeriodicTimer"));
        });
    }

    [Test]
    public void Parameter_snapshot_masks_secret_material()
    {
        var api=File.ReadAllText(Path.Combine(Root(),"Solution","src","Jornada.Api","DevTestConsoleApi.cs"));
        Assert.Multiple(() => {
            Assert.That(api,Does.Contain("\"password\""));
            Assert.That(api,Does.Contain("\"secret\""));
            Assert.That(api,Does.Contain("\"token\""));
            Assert.That(api,Does.Contain("\"connectionstrings\""));
            Assert.That(api,Does.Contain("\"***\""));
        });
    }
}

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevTestConsoleContractTests
{
    private static string Root(){var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
    [Test]
    public void Console_is_separate_executable_with_independent_commands_and_navigable_runs()
    {
        var root=Root();var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Program.cs"));var script=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-command.ps1"));
        Assert.Multiple(()=>{
            Assert.That(program,Does.Contain("CommandCatalog.All"));
            Assert.That(program,Does.Contain("/api/commands/{command}/run"));
            Assert.That(program,Does.Contain("/api/runs/{id:guid}"));
            Assert.That(program,Does.Contain("gold-synthetic"));
            Assert.That(program,Does.Contain("Registros gerados"));\n            Assert.That(program,Does.Contain("CountByCommandAsync"));\n            Assert.That(program,Does.Contain("runCount"));\n            var launch=File.ReadAllText(Path.Combine(root,"Solution","Jornada.slnLaunch"));\n            Assert.That(launch,Does.Contain("Jornada.DevConsole.csproj"));\n            Assert.That(launch,Does.Contain("\\\"Action\\\": \\"Start\\\""));
            Assert.That(program,Does.Contain("nenhuma ação exige a anterior"));
            Assert.That(script,Does.Contain("Jornada_Dev_LinkageValidation.sql"));
            Assert.That(script,Does.Contain("FROM gold.pessoa"));
            Assert.That(script,Does.Contain("gold-synthetic-records.json"));
            Assert.That(program,Does.Not.Contain("Jornada.Api"));
        });
    }
}

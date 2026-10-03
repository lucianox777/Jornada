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
            Assert.That(program,Does.Contain("Registros gerados"));
            Assert.That(program,Does.Contain("CountByCommandAsync"));
            Assert.That(program,Does.Contain("runCount"));
            Assert.That(program,Does.Contain("ConsoleSession"));
            Assert.That(program,Does.Contain("x.StartedAt>=session.StartedAt"));
            Assert.That(program,Does.Contain("Subir infraestrutura"));
            Assert.That(program,Does.Contain("Executar blocking"));
            Assert.That(program,Does.Contain("Executar linkage"));
            Assert.That(program,Does.Contain("Executar replay"));
            Assert.That(program,Does.Contain("Destruir ambiente DEV"));
            Assert.That(program,Does.Contain("CommandLine"));
            Assert.That(program,Does.Contain("Comando real ainda não mapeado."));
            Assert.That(program,Does.Contain("SEM EXECUTOR"));
            Assert.That(program,Does.Not.Contain("disabled title=\\\"Ainda sem executor implementado\\\""));
            Assert.That(program,Does.Contain("displayCommand"));
            var launch=File.ReadAllText(Path.Combine(root,"Solution","Jornada.slnLaunch"));
            Assert.That(launch,Does.Contain("Jornada.DevConsole.csproj"));
            Assert.That(launch,Does.Contain("\"Action\": \"Start\""));
            Assert.That(program,Does.Contain("nenhuma ação exige a anterior"));
            Assert.That(script,Does.Contain("Jornada_Dev_LinkageValidation.sql"));
            Assert.That(script,Does.Contain("FROM gold.pessoa"));
            Assert.That(script,Does.Contain("gold-synthetic-records.json"));
            Assert.That(program,Does.Not.Contain("Jornada.Api"));
        });
    }
}

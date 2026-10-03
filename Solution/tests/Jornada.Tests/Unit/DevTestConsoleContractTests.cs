namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevTestConsoleContractTests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }

    [Test]
    public void Console_has_live_terminal_persistent_history_manual_zip_and_real_local_infrastructure()
    {
        var root=Root();
        var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Program.cs"));
        var runtime=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","DevConsoleRuntime.cs"));
        var page=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Page.cs"));
        var commandScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-command.ps1"));
        var goldScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-gold-synthetic.ps1"));
        var localDb=File.ReadAllText(Path.Combine(root,"Solution","scripts","local-db.ps1"));

        Assert.Multiple(()=>{
            Assert.That(program,Does.Contain("/api/commands/{command}/start"));
            Assert.That(program,Does.Contain("/api/runs/{id:guid}/stream"));
            Assert.That(program,Does.Contain("text/event-stream"));
            Assert.That(program,Does.Contain("/api/runs/{id:guid}/result"));
            Assert.That(program,Does.Contain("/api/zip/manual/start"));

            Assert.That(runtime,Does.Contain("LiveExecutionService"));
            Assert.That(runtime,Does.Contain("ReadLineAsync"));
            Assert.That(runtime,Does.Contain("live.Add("stdout""));
            Assert.That(runtime,Does.Contain("live.Add("stderr""));
            Assert.That(runtime,Does.Contain("local-db.ps1 -Action up"));
            Assert.That(runtime,Does.Contain("local-db.ps1 -Action down"));
            Assert.That(runtime,Does.Contain("local-db.ps1 -Action clean"));
            Assert.That(runtime,Does.Contain("dev-console-gold-synthetic.ps1"));
            Assert.That(runtime,Does.Contain("build-ingestion-fixture.py"));
            Assert.That(runtime,Does.Contain("manual-zip"));
            Assert.That(runtime,Does.Not.Contain("session.StartedAt"));

            Assert.That(page,Does.Contain("🕘 Execuções"));
            Assert.That(page,Does.Contain("console-shell"));
            Assert.That(page,Does.Contain("RODANDO..."));
            Assert.That(page,Does.Contain("new EventSource"));
            Assert.That(page,Does.Contain("Execuções anteriores"));
            Assert.That(page,Does.Contain("Entrada manual para o ZIP"));
            Assert.That(page,Does.Contain("manifest.json"));
            Assert.That(page,Does.Contain("pessoas.jsonl"));
            Assert.That(page,Does.Contain("registros.jsonl"));
            Assert.That(page,Does.Contain("Resultado salvo em:"));
            Assert.That(page,Does.Contain("Ver dados do resultado"));
            Assert.That(page,Does.Contain("Executar novamente"));

            Assert.That(localDb,Does.Contain("iniciando Docker Desktop"));
            Assert.That(localDb,Does.Contain("Start-Process -FilePath $dockerDesktopPath"));
            Assert.That(localDb,Does.Contain("Aguardando Docker Engine"));
            Assert.That(localDb,Does.Contain("Docker Engine pronto"));

            Assert.That(goldScript,Does.Contain("Jornada_Dev_GoldSynthetic.sql"));
            Assert.That(goldScript,Does.Contain("gold-synthetic-records.json"));
            Assert.That(goldScript,Does.Not.Contain("Jornada_Dev_LinkageValidation.sql"));
            Assert.That(commandScript,Does.Contain("SDK:"));

            var launch=File.ReadAllText(Path.Combine(root,"Solution","Jornada.slnLaunch"));
            Assert.That(launch,Does.Contain("Jornada.DevConsole.csproj"));
            Assert.That(launch,Does.Contain(""Action": "Start""));

            Assert.That(program,Does.Not.Contain("Jornada.Api"));
        });
    }
}

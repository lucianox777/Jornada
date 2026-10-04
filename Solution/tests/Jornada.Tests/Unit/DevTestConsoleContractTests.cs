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
        var infraScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-infrastructure.ps1"));
        var opsScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-operations.ps1"));
        var goldScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-gold-synthetic.ps1"));
        var localDb=File.ReadAllText(Path.Combine(root,"Solution","scripts","local-db.ps1"));
        var bootstrap=File.ReadAllText(Path.Combine(root,"Solution","teste.ps1"));
        var goldSql=File.ReadAllText(Path.Combine(root,"Solution","database","Jornada_Dev_GoldSynthetic.sql"));

        Assert.Multiple(()=>{
            Assert.That(program,Does.Contain("/api/commands/{command}/start"));
            Assert.That(program,Does.Contain("/api/runs/{id:guid}/stream"));
            Assert.That(program,Does.Contain("text/event-stream"));
            Assert.That(program,Does.Contain("/api/runs/{id:guid}/result"));
            Assert.That(program,Does.Contain("/api/zip/manual/start"));

            Assert.That(runtime,Does.Contain("LiveExecutionService"));
            Assert.That(runtime,Does.Contain("ReadLineAsync"));
            Assert.That(runtime,Does.Contain("live.Add(\"stdout\""));
            Assert.That(runtime,Does.Contain("live.Add(\"stderr\""));
            Assert.That(runtime,Does.Contain("dev-console-infrastructure.ps1 -Action up"));
            Assert.That(runtime,Does.Contain("dev-console-infrastructure.ps1 -Action clean"));
            Assert.That(runtime,Does.Not.Contain("update-build"));
            Assert.That(runtime,Does.Not.Contain("Comando real ainda não mapeado."));
            Assert.That(runtime,Does.Contain("dev-console-gold-synthetic.ps1"));
            Assert.That(runtime,Does.Contain("build-ingestion-fixture.py"));
            Assert.That(runtime,Does.Contain("manual-zip"));
            Assert.That(runtime,Does.Contain("sessionStartedAt=DateTimeOffset.UtcNow"));
            Assert.That(runtime,Does.Contain("Where(x=>x.StartedAt>=sessionStartedAt)"));
            Assert.That(runtime,Does.Contain("JsonSerializerDefaults.Web"));
            Assert.That(runtime,Does.Contain("JsonSerializer.Serialize(item,StreamJson)"));
            Assert.That(runtime,Does.Contain("StandardOutputEncoding=Encoding.UTF8"));
            Assert.That(runtime,Does.Contain("StandardErrorEncoding=Encoding.UTF8"));
            Assert.That(runtime,Does.Contain("result.ExitCode==0&&candidatePath is not null&&File.Exists(candidatePath)"));

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
            Assert.That(runtime,Does.Contain("SQL Server, schema, NAS, bootstrap IBGE e NODE1/NODE2"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action reference-check"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action ingest-latest"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action pipeline-status"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action blocking"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action calibrate"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action linkage"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action replay-latest"));
            Assert.That(runtime,Does.Contain("dev-console-operations.ps1 -Action report"));

            Assert.That(goldScript,Does.Contain("Jornada_Dev_GoldSynthetic.sql"));
            Assert.That(goldScript,Does.Contain("gold-synthetic-records.json"));
            Assert.That(goldScript,Does.Contain("[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)"));
            Assert.That(goldScript,Does.Contain("$PSStyle.OutputRendering='PlainText'"));
            Assert.That(goldScript,Does.Contain("-h -1 -y 0 -Q $q"));
            Assert.That(goldScript,Does.Not.Contain("-W -h -1 -y 0"));
            Assert.That(goldScript,Does.Not.Contain("Jornada_Dev_LinkageValidation.sql"));
            Assert.That(goldSql,Does.Contain("SET QUOTED_IDENTIFIER ON;"));
            Assert.That(goldSql,Does.Contain("SET ANSI_NULLS ON;"));
            Assert.That(goldSql,Does.Contain("SET ANSI_WARNINGS ON;"));
            Assert.That(goldSql,Does.Contain("SET ARITHABORT ON;"));
            Assert.That(goldSql,Does.Contain("SET CONCAT_NULL_YIELDS_NULL ON;"));
            Assert.That(goldSql,Does.Contain("SET NUMERIC_ROUNDABORT OFF;"));
            Assert.That(infraScript,Does.Contain("local-cluster.ps1"));
            Assert.That(infraScript,Does.Contain("-NoBuild"));
            Assert.That(infraScript,Does.Contain("jornada-reference-bootstrap"));
            Assert.That(opsScript,Does.Contain("local-check-ibge-reference.ps1"));
            Assert.That(opsScript,Does.Contain("Jornada.Bronze.Verify"));
            Assert.That(opsScript,Does.Contain("jornada.ingestao.write"));
            Assert.That(opsScript,Does.Contain("Ensure-ClusterRunning"));
            Assert.That(opsScript,Does.Contain("local-cluster.ps1"));
            Assert.That(opsScript,Does.Contain("--mode','REPLAY"));
            Assert.That(page,Does.Contain("A tela mostra somente operações reais"));
            Assert.That(bootstrap,Does.Contain("restore $Solution --locked-mode"));
            Assert.That(bootstrap,Does.Contain("build $Solution --no-restore"));

            var launch=File.ReadAllText(Path.Combine(root,"Solution","Jornada.slnLaunch"));
            Assert.That(launch,Does.Contain("Jornada.DevConsole.csproj"));
            Assert.That(launch,Does.Contain("\"Action\": \"Start\""));

            Assert.That(program,Does.Not.Contain("Jornada.Api"));
        });
    }
}

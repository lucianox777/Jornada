namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevTestConsoleContractTests
{
    private static string Root(){var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;return d?.FullName??throw new DirectoryNotFoundException();}
    [Test]
    public void Console_is_separate_executable_with_independent_commands_and_navigable_runs()
    {
        var root=Root();var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Program.cs"));var script=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-command.ps1"));var goldScript=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-gold-synthetic.ps1"));
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
            Assert.That(program,Does.Contain("RODANDO..."));
            Assert.That(program,Does.Contain("aria-busy=\"true\""));
            Assert.That(program,Does.Contain("running=new Set()"));
            Assert.That(program,Does.Contain("ação(ões) rodando agora"));
            Assert.That(program,Does.Contain("/api/zip/manual"));
            Assert.That(program,Does.Contain("Entrada manual para o ZIP"));
            Assert.That(program,Does.Contain("pessoas.jsonl"));
            Assert.That(program,Does.Contain("registros.jsonl"));
            Assert.That(program,Does.Contain("Resultado salvo em:"));
            Assert.That(program,Does.Contain("Diretório de trabalho:"));
            Assert.That(program,Does.Contain("Saída padrão (stdout)"));
            Assert.That(program,Does.Contain("Erros/diagnóstico (stderr)"));
            Assert.That(program,Does.Contain("Exit code:"));
            Assert.That(program,Does.Contain("Duração:"));
            var launch=File.ReadAllText(Path.Combine(root,"Solution","Jornada.slnLaunch"));
            Assert.That(launch,Does.Contain("Jornada.DevConsole.csproj"));
            Assert.That(launch,Does.Contain("\"Action\": \"Start\""));
            Assert.That(program,Does.Contain("nenhuma ação exige a anterior"));
            Assert.That(program,Does.Contain("dev-console-gold-synthetic.ps1"));
            Assert.That(goldScript,Does.Contain("Jornada_Dev_GoldSynthetic.sql"));
            Assert.That(goldScript,Does.Contain("FROM gold.pessoa"));
            Assert.That(goldScript,Does.Contain("gold-synthetic-records.json"));
            Assert.That(goldScript,Does.Not.Contain("Jornada_Dev_LinkageValidation.sql"));
            Assert.That(goldScript,Does.Contain("Etapa 1/4"));
            Assert.That(goldScript,Does.Contain("Etapa 4/4"));
            Assert.That(goldScript,Does.Contain("SQL Server container:"));
            Assert.That(goldScript,Does.Contain("Registros:"));
            Assert.That(script,Does.Contain("Etapa 1/4"));
            Assert.That(script,Does.Contain("Etapa 4/4"));
            Assert.That(script,Does.Contain("SDK:"));
            Assert.That(program,Does.Not.Contain("Jornada.Api"));
        });
    }
}

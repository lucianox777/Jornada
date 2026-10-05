namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class RuntimeModeContractTests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }

    [Test]
    public void Console_defaults_to_hml_and_dev_is_the_only_mode_with_extra_six_thousand()
    {
        var root=Root();
        var cmd=File.ReadAllText(Path.Combine(root,"Solution","console.cmd"));
        var launcher=File.ReadAllText(Path.Combine(root,"Solution","teste.ps1"));
        var env=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-env.ps1"));
        var ops=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-operations.ps1"));
        var localDb=File.ReadAllText(Path.Combine(root,"Solution","scripts","local-db.ps1"));
        var scaleSql=File.ReadAllText(Path.Combine(root,"Solution","database","Jornada_Dev_SyntheticScale.sql"));
        var pendingSql=File.ReadAllText(Path.Combine(root,"Solution","database","Jornada_Dev_SyntheticPending.sql"));

        Assert.Multiple(()=>{
            Assert.That(cmd,Does.Contain("JORNADA_MODE_ARG=-RuntimeMode HML"));
            Assert.That(File.Exists(Path.Combine(root,"Solution","teste.cmd")),Is.False);
            Assert.That(cmd,Does.Contain("--dev"));
            Assert.That(cmd,Does.Contain("--prod"));
            Assert.That(launcher,Does.Contain("[string]$RuntimeMode='HML'"));
            Assert.That(launcher,Does.Contain("$env:JORNADA_RUNTIME_MODE=$RuntimeMode.ToUpperInvariant()"));

            Assert.That(env,Does.Not.Contain("$map['JORNADA_SQL_DATABASE']='JornadaSyntheticDev'"));
            Assert.That(env,Does.Contain("$map['JORNADA_LOCAL_SYNTHETIC_PENDING']=if($RuntimeMode -eq 'DEV'){'6000'}else{'0'}"));
            Assert.That(env,Does.Contain("$script:DevConsoleRuntimeMode=$RuntimeMode"));

            Assert.That(ops,Does.Contain("DEV: avaliando o corpus adicional pela execução real do Linkage Runner"));
            Assert.That(ops,Does.Contain("Get-DevBootstrapLinkageReadiness"));
            Assert.That(ops,Does.Contain("ZERO_UNEVALUATED_AND_ZERO_FALSE_POSITIVE"));
            Assert.That(ops,Does.Contain("DEV preserva os 6.000 registros adicionais na Silver: resoluções seguras entram na Gold; resultados inconclusivos permanecem auditáveis sem forçar vínculo"));
            Assert.That(ops,Does.Contain("STANDARD_NO_EXTRA_PENDING"));

            Assert.That(localDb,Does.Contain("'DEV'{'Development'}'PROD'{'Production'}default{'Homologation'}"));
            Assert.That(localDb,Does.Contain("Operação destrutiva '$Action' bloqueada em PROD"));
            Assert.That(scaleSql,Does.Contain("SCALE_PENDING deve estar entre 0 e 2.000.000."));
            Assert.That(localDb,Does.Contain("Jornada_Dev_SyntheticPending.sql"));
            Assert.That(localDb,Does.Contain("startup nunca apaga dados implicitamente"));
            Assert.That(pendingSql,Does.Contain("Não cria vínculo: a publicação deve acontecer pelo"));
            Assert.That(pendingSql,Does.Contain("SCALE-PEND-"));
        });
    }

    [Test]
    public void Production_destructive_actions_are_disabled_in_ui_and_rejected_by_backend()
    {
        var root=Root();
        var runtime=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","DevConsoleRuntime.cs"));
        var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Program.cs"));
        var page=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Page.cs"));
        var infra=File.ReadAllText(Path.Combine(root,"Solution","scripts","dev-console-infrastructure.ps1"));
        var cluster=File.ReadAllText(Path.Combine(root,"Solution","scripts","local-cluster.ps1"));

        Assert.Multiple(()=>{
            Assert.That(runtime,Does.Contain("IsProduction=>Mode==\"PROD\""));
            Assert.That(runtime,Does.Contain("IsProduction&&command.Destructive"));
            Assert.That(runtime,Does.Contain("new(\"reset-environment\",\"Resetar ambiente\""));
            Assert.That(runtime,Does.Contain("Destructive=true"));

            Assert.That(program,Does.Contain("disabled=runtime.IsDisabled(x)"));
            Assert.That(program,Does.Contain("return Results.Conflict"));
            Assert.That(page,Does.Contain("command?.disabled"));
            Assert.That(page,Does.Contain("button:disabled"));
            Assert.That(page,Does.Contain("version.mode||'HML'"));

            Assert.That(infra,Does.Contain("Reset bloqueado em PROD"));
            Assert.That(infra,Does.Contain("-ConfirmProductionReset"));
            Assert.That(cluster,Does.Contain("Reset bloqueado em PROD"));
            Assert.That(cluster,Does.Contain("Clean destrutivo bloqueado em PROD"));
        });
    }
}

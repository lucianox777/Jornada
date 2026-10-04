namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevConsoleLayerBrowserContractTests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }

    [Test]
    public void Console_exposes_bronze_silver_identity_and_gold_as_read_only_layers()
    {
        var root=Root();
        var program=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Program.cs"));
        var runtime=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","DevConsoleRuntime.cs"));
        var page=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Page.cs"));
        var browser=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","LayerBrowserService.cs"));
        var cluster=File.ReadAllText(Path.Combine(root,"Solution","scripts","local-cluster.ps1"));

        Assert.Multiple(()=>{
            Assert.That(program,Does.Contain("AddSingleton<LayerBrowserService>"));
            Assert.That(program,Does.Contain("/api/layers/{layer}"));
            Assert.That(program,Does.Contain("BrowseAsync(layer,page,pageSize,search,ct)"));

            Assert.That(runtime,Does.Contain("new(\"bronze\",\"Bronze\""));
            Assert.That(runtime,Does.Contain("new(\"silver\",\"Silver · processar Bronze\""));
            Assert.That(runtime,Does.Contain("new(\"gold\",\"Gold / Serving\""));
            Assert.That(runtime.IndexOf("new(\"zip\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"bronze\"",StringComparison.Ordinal)));
            Assert.That(runtime.IndexOf("new(\"bronze\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"silver\"",StringComparison.Ordinal)));
            Assert.That(runtime.IndexOf("new(\"silver\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"linkage\"",StringComparison.Ordinal)));
            Assert.That(runtime.IndexOf("new(\"linkage\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"gold\"",StringComparison.Ordinal)));

            Assert.That(page,Does.Contain("Visualizar Bronze"));
            Assert.That(page,Does.Contain("Visualizar Silver"));
            Assert.That(page,Does.Contain("Visualizar identidade"));
            Assert.That(page,Does.Contain("Visualizar Gold"));
            Assert.That(page,Does.Contain("Buscar em qualquer coluna"));
            Assert.That(page,Does.Contain("const layerPageSize=50"));
            Assert.That(page,Does.Contain("new URLSearchParams"));
            Assert.That(page,Does.Contain("Página '+data.page+' de '+data.totalPages"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'gold\\')"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'bronze\\')"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'silver\\')"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'identity\\')"));

            Assert.That(browser,Does.Contain("\"gold.pessoa\""));
            Assert.That(browser,Does.Contain("\"bronze.entrega_arquivo\""));
            Assert.That(browser,Does.Contain("\"silver.pessoa_observacao po JOIN ingestao.lote l"));
            Assert.That(browser,Does.Contain("\"identidade.v_vinculo_corrente vc JOIN silver.pessoa_observacao po"));
            Assert.That(browser,Does.Contain("\"silver\"=>Silver"));
            Assert.That(browser,Does.Contain("\"identity\"=>Identity"));
            Assert.That(browser,Does.Contain("OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY"));
            Assert.That(browser,Does.Contain("CONCAT_WS(N'|'"));
            Assert.That(browser,Does.Contain("Latin1_General_100_CI_AI"));
            Assert.That(browser,Does.Contain("Math.Clamp(pageSize,10,MaxPageSize)"));

            Assert.That(cluster,Does.Contain("Ensure-CanonicalSeedBronzeObjects"));
            Assert.That(cluster,Does.Contain("Objetos Bronze canônicos do seed DEV presentes e íntegros."));
        });
    }
}

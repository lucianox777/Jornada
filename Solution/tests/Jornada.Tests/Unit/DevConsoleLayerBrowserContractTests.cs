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
    public void Console_restores_bronze_browser_adds_gold_paging_and_materializes_seed_objects()
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

            Assert.That(runtime,Does.Contain("new(\"bronze\",\"Visualizar camada Bronze\""));
            Assert.That(runtime,Does.Contain("bronze.entrega_arquivo · leitura paginada e busca DEV"));
            Assert.That(runtime.IndexOf("new(\"zip\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"bronze\"",StringComparison.Ordinal)));
            Assert.That(runtime.IndexOf("new(\"bronze\"",StringComparison.Ordinal),Is.LessThan(runtime.IndexOf("new(\"semiblind\"",StringComparison.Ordinal)));

            Assert.That(page,Does.Contain("Visualizar Bronze"));
            Assert.That(page,Does.Contain("Visualizar Gold"));
            Assert.That(page,Does.Contain("Buscar em qualquer coluna"));
            Assert.That(page,Does.Contain("const layerPageSize=50"));
            Assert.That(page,Does.Contain("new URLSearchParams"));
            Assert.That(page,Does.Contain("Página '+data.page+' de '+data.totalPages"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'gold\\')"));
            Assert.That(page,Does.Contain("openLayerDialog(\\'bronze\\')"));

            Assert.That(browser,Does.Contain("\"gold.pessoa\""));
            Assert.That(browser,Does.Contain("\"bronze.entrega_arquivo\""));
            Assert.That(browser,Does.Contain("OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY"));
            Assert.That(browser,Does.Contain("CONCAT_WS(N'|'"));
            Assert.That(browser,Does.Contain("Latin1_General_100_CI_AI"));
            Assert.That(browser,Does.Contain("Math.Clamp(pageSize,10,MaxPageSize)"));

            Assert.That(cluster,Does.Contain("Ensure-CanonicalSeedBronzeObjects"));
            Assert.That(cluster,Does.Contain("8dcc7e601606217f3b754766511182a916b17e9a26a94c9d887104eba92e9bb2"));
            Assert.That(cluster,Does.Contain("08befc1b72bbe89348739d0d994b031aa28db85ffc817ab2a2598a0af3583084"));
            Assert.That(cluster,Does.Contain("52efeb293d001f170549c0bdf4196cf94af375858b96a98a0d486a2ae2f81923"));
            Assert.That(cluster,Does.Contain("if [ ! -f \"$dest\" ]"));
            Assert.That(cluster,Does.Contain("Objetos Bronze canônicos do seed DEV presentes e íntegros."));
        });
    }
}

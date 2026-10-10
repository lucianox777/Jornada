using System.Text.Json;
using System.Text.RegularExpressions;
using Jornada.Ingestion;
using Jornada.Processor.Worker;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class DevConsoleManualZipV1Tests
{
    private static string Root()
    {
        var d=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while(d is not null&&!Directory.Exists(Path.Combine(d.FullName,"Solution")))d=d.Parent;
        return d?.FullName??throw new DirectoryNotFoundException();
    }

    [TestCase("CPF+nome","11144477735","Pessoa CPF",null,null)]
    [TestCase("Nome+data",null,"Pessoa Data","1990-02-03",null)]
    [TestCase("Nome+mae",null,"Pessoa Mae",null,"Mae da Pessoa")]
    [TestCase("Nome+mae+data",null,"Pessoa Completa","1990-02-03","Mae da Pessoa")]
    public void Pessoa_v1_accepts_manual_console_identity_combinations(
        string scenario,
        string? cpf,
        string nome,
        string? nascimento,
        string? mae)
    {
        var root=Root();
        var schema=Path.Combine(root,"Solution","config","contracts","gestores","SEHAB","pessoa","v1","pessoa.schema.json");
        var validator=JsonSchemaSubsetValidator.Load(schema);
        var reason=cpf is null?"NAO_INFORMADO_ORIGEM":null;

        var json=JsonSerializer.Serialize(new Dictionary<string,object?>
        {
            ["idPessoaEntrega"]="DEV-TEST-"+scenario.Replace("+","-",StringComparison.Ordinal),
            ["codigoPessoaOrigem"]="DEV-ORIGEM-"+scenario.Replace("+","-",StringComparison.Ordinal),
            ["cpf"]=cpf,
            ["cpfAusenteMotivo"]=reason,
            ["nomeCompleto"]=nome,
            ["dataNascimento"]=nascimento,
            ["nomeMae"]=mae,
            ["sourceTransactionId"]="DEV-TEST-TX",
            ["atributosTransversais"]=Array.Empty<object>()
        });

        Assert.Multiple((Action)(()=>
        {
            Assert.DoesNotThrow((Action)(()=>validator.ParseAndValidate(json,"pessoas.jsonl",1)),scenario);
            Assert.DoesNotThrow((Action)(()=>PersonContractRules.ValidateCpfAbsence(1,cpf,reason)),scenario);
        }));
    }

    [Test]
    public void Pessoa_v1_keeps_identity_core_optional_for_manual_console()
    {
        var root=Root();
        var schema=Path.Combine(root,"Solution","config","contracts","gestores","SEHAB","pessoa","v1","pessoa.schema.json");
        var validator=JsonSchemaSubsetValidator.Load(schema);
        const string json="""{"idPessoaEntrega":"DEV-ONLY-ID","cpf":null,"cpfAusenteMotivo":"NAO_INFORMADO_ORIGEM","nomeCompleto":null,"dataNascimento":null,"nomeMae":null}""";

        Assert.DoesNotThrow((Action)(()=>validator.ParseAndValidate(json,"pessoas.jsonl",1)));
    }

    [Test]
    public void Every_console_one_click_javascript_handler_is_declared()
    {
        var root=Root();
        var page=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","Page.cs"));
        var handlers=Regex.Matches(page,"on(?:click|change)=\\\"([A-Za-z_$][A-Za-z0-9_$]*)\\(")
            .Select(m=>m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(handlers,Is.Not.Empty);
        Assert.Multiple((Action)(()=>
        {
            foreach(var handler in handlers)
                Assert.That(
                    Regex.IsMatch(page,$@"(?:async\s+)?function\s+{Regex.Escape(handler)}\s*\("),
                    Is.True,
                    $"Handler de um clique ausente: {handler}");
        }));
    }

    [Test]
    public void Every_script_referenced_by_console_catalog_exists()
    {
        var root=Root();
        var runtime=File.ReadAllText(Path.Combine(root,"Solution","src","Jornada.DevConsole","DevConsoleRuntime.cs"));
        var scriptPaths=Regex.Matches(runtime,@"scripts/[A-Za-z0-9_.\-/]+\.(?:ps1|py)")
            .Select(m=>m.Value.Replace('/',Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(scriptPaths,Is.Not.Empty);
        Assert.Multiple((Action)(()=>
        {
            foreach(var relative in scriptPaths)
                Assert.That(File.Exists(Path.Combine(root,"Solution",relative)),Is.True,$"Executor da Console ausente: {relative}");
        }));
    }
}

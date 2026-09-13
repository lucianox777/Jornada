using System.Text.Json;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

[TestFixture]
public sealed class NoSemanticFallbackContractTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../"));

    [Test]
    public void Pessoa_v4_requires_source_code_and_does_not_offer_territorial_reference()
    {
        foreach (var gestor in new[] { "SEHAB", "SMADS", "SMDET", "SMS" })
        {
            var path = Path.Combine(Root, "config", "contracts", "gestores", gestor, "pessoa", "v4", "pessoa.schema.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var required = root.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.That(required, Does.Contain("codigoPessoaOrigem"));
            var attrs = root.GetProperty("properties").GetProperty("atributosTransversais").GetProperty("items").GetProperty("properties");
            var codes = attrs.GetProperty("atributoCodigo").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.That(codes, Does.Contain("ENDERECO_RESIDENCIAL"));
            Assert.That(codes, Does.Not.Contain("REFERENCIA_TERRITORIAL"));
            Assert.That(attrs.TryGetProperty("naturezaReferenciaTerritorial", out _), Is.False);
        }
    }

    [Test]
    public void Runtime_keeps_legacy_fallback_only_for_replay_before_v4()
    {
        var parser = File.ReadAllText(Path.Combine(Root, "src", "Jornada.Processor.Worker", "ProcessorModels.cs"));
        Assert.That(parser, Does.Contain("batch.PessoaSchemaVersao >= 4"));
        Assert.That(parser, Does.Contain("a Jornada não deriva chave de origem do CPF"));
        Assert.That(parser, Does.Contain("REFERENCIA_TERRITORIAL é legado e não é aceito no contrato Pessoa v4"));
    }

    [Test]
    public void Schema_371_persists_residential_geography_as_residential_data()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "20260913_Endereco_Residencial_Sem_Fallback.sql"));
        Assert.That(migration, Does.Contain("silver.endereco_residencial_geografia_observacao"));
        Assert.That(migration, Does.Contain("silver.v_pessoa_geografia_residencial"));
        Assert.That(migration, Does.Contain("fonte_semantica='ENDERECO_RESIDENCIAL'"));
    }
}

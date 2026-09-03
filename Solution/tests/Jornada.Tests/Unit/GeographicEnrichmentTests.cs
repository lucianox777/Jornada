using Jornada.Contracts;
using Jornada.Processor.Worker;
using NUnit.Framework;

namespace Jornada.Tests.Unit;

public sealed class GeographicEnrichmentTests
{
    [Test]
    public void Origin_geography_is_preserved_and_timestamped_from_manifest()
    {
        var sourceGeo = new ReferenceGeography("SE", "Sé", "SE", "Sé", "MALHA_OFICIAL_2026", ReferenceGeographyOrigin.ORIGEM, null);
        var attr = new ParsedTransversalAttribute(
            "A1", "ENDERECO_RESIDENCIAL", "CEP=01001000|NUMERO=1", "COMPROVADO", "DOCUMENTO", null,
            DateTimeOffset.Parse("2026-08-20T10:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture), null, null,
            GeographicResolutionStatus.RESOLVIDA, sourceGeo);
        var person = new ParsedPerson("P1", new string('a',64), null, "11144477735", null, "Pessoa", new DateOnly(1990,1,1), "Mae", [attr], []);
        var manifest = new IngestionPackageManifest(2, 1, "SAUDE", null, null, null, DateTimeOffset.Parse("2026-08-21T00:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));

        var result = OriginTerritorialGeography.ApplyResolutionTimestamp(new ParsedPackage(manifest, [person], []));

        Assert.Multiple(() =>
        {
            Assert.That(result.Pessoas[0].Atributos[0].SituacaoGeografia, Is.EqualTo(GeographicResolutionStatus.RESOLVIDA));
            Assert.That(result.Pessoas[0].Atributos[0].Geografia?.Origem, Is.EqualTo(ReferenceGeographyOrigin.ORIGEM));
            Assert.That(result.Pessoas[0].Atributos[0].Geografia?.ResolvidoEm, Is.EqualTo(manifest.DataReferencia));
        });
    }

    [Test]
    public void Explicit_non_resolution_does_not_create_geography()
    {
        var attr = new ParsedTransversalAttribute(
            "A2", "ENDERECO_RESIDENCIAL", "ENDERECO_INSUFICIENTE", "DECLARADO", null, null, null, null, null,
            GeographicResolutionStatus.NAO_RESOLVIDA_ORIGEM, null);
        var person = new ParsedPerson("P2", new string('b',64), null, null, "NAO_INFORMADO", "Pessoa 2", new DateOnly(1991,2,2), "Mae 2", [attr], []);
        var manifest = new IngestionPackageManifest(2, 1, "ASSISTENCIA", null, null, null, DateTimeOffset.Parse("2026-08-21T00:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));

        var result = OriginTerritorialGeography.ApplyResolutionTimestamp(new ParsedPackage(manifest, [person], []));
        Assert.Multiple(() =>
        {
            Assert.That(result.Pessoas[0].Atributos[0].SituacaoGeografia, Is.EqualTo(GeographicResolutionStatus.NAO_RESOLVIDA_ORIGEM));
            Assert.That(result.Pessoas[0].Atributos[0].Geografia, Is.Null);
        });
    }

    [Test]
    public void Phase1_code_contains_no_online_geography_adapter_contract()
    {
        Assert.That(typeof(ReferenceGeographyOrigin).GetEnumNames(), Is.EqualTo(new[] { "ORIGEM" }));
    }
}

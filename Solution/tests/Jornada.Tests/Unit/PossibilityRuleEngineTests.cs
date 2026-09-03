using Jornada.Contracts;

namespace Jornada.Tests.Unit;

[TestFixture]
[Category("Unit")]
public sealed class PossibilityRuleEngineTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    public void Rule_engine_distinguishes_compatible_incompatible_and_not_evaluable()
    {
        var rule = new PossibilityRuleSet("SERVICO", "EXMP", 1, "EXEMPLO.v1",
            [new("IDADE", PossibilityRuleOperator.NUMERO_MAIOR_IGUAL, "18"), new("MUNICIPIO", PossibilityRuleOperator.IGUAL, "SAO_PAULO")]);
        var compatible = PossibilityRuleEngine.Evaluate(rule, Snapshot(("IDADE","20"),("MUNICIPIO","SAO_PAULO")), At);
        var incompatible = PossibilityRuleEngine.Evaluate(rule, Snapshot(("IDADE","17"),("MUNICIPIO","SAO_PAULO")), At);
        var missing = PossibilityRuleEngine.Evaluate(rule, Snapshot(("MUNICIPIO","SAO_PAULO")), At);
        Assert.Multiple(() =>
        {
            Assert.That(compatible.Resultado, Is.EqualTo(PossibilityResult.COMPATIVEL));
            Assert.That(incompatible.Resultado, Is.EqualTo(PossibilityResult.NAO_COMPATIVEL));
            Assert.That(missing.Resultado, Is.EqualTo(PossibilityResult.NAO_AVALIAVEL));
            Assert.That(missing.Motivo, Is.EqualTo("DADO_AUSENTE:IDADE"));
        });
    }

    [Test]
    public void Dry_run_reports_entries_and_exits_without_publishing()
    {
        var current = new PossibilityRuleSet("BENEFICIO", "TEST", 1, "TEST.v1", [new("RENDA", PossibilityRuleOperator.NUMERO_MENOR_IGUAL, "100")]);
        var candidate = new PossibilityRuleSet("BENEFICIO", "TEST", 2, "TEST.v2", [new("RENDA", PossibilityRuleOperator.NUMERO_MENOR_IGUAL, "80")]);
        var population = new Dictionary<Guid, PossibilityFactSnapshot>
        {
            [Guid.NewGuid()] = Snapshot(("RENDA","70")),
            [Guid.NewGuid()] = Snapshot(("RENDA","90")),
            [Guid.NewGuid()] = Snapshot(("RENDA","110")),
            [Guid.NewGuid()] = Snapshot()
        };
        var impact = PossibilityImpactSimulator.Compare(current, candidate, population, At);
        Assert.Multiple(() =>
        {
            Assert.That(impact.Population, Is.EqualTo(4));
            Assert.That(impact.CurrentCompatible, Is.EqualTo(2));
            Assert.That(impact.CandidateCompatible, Is.EqualTo(1));
            Assert.That(impact.Entered, Is.Zero);
            Assert.That(impact.Exited, Is.EqualTo(1));
            Assert.That(impact.CurrentNotEvaluable, Is.EqualTo(1));
            Assert.That(impact.CandidateNotEvaluable, Is.EqualTo(1));
        });
    }

    [Test]
    public void Invalid_or_empty_published_rule_fails_closed()
    {
        var empty = new PossibilityRuleSet("SERVICO", "TEST", 1, "TEST.v1", []);
        Assert.Throws<InvalidDataException>(() => PossibilityRuleValidator.Validate(empty));
        var invalid = new PossibilityRuleSet("SERVICO", "TEST", 1, "TEST.v1", [new("DATA", PossibilityRuleOperator.DATA_MAIOR_IGUAL, null)]);
        Assert.Throws<InvalidDataException>(() => PossibilityRuleValidator.Validate(invalid));
    }

    [Test]
    public async Task Configured_evaluator_uses_versioned_rule_and_fact_provider()
    {
        var rule = new PossibilityRuleSet("SERVICO", "TEST", 3, "TEST.v3", [new("ATIVO", PossibilityRuleOperator.IGUAL, "SIM")]);
        var evaluator = new ConfiguredPossibilityEvaluator(rule, (_, _) => Task.FromResult(Snapshot(("ATIVO", "SIM"))));
        var result = await evaluator.EvaluateAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(evaluator.Natureza, Is.EqualTo("SERVICO"));
            Assert.That(evaluator.Codigo, Is.EqualTo("TEST"));
            Assert.That(evaluator.Versao, Is.EqualTo(3));
            Assert.That(result.Resultado, Is.EqualTo(PossibilityResult.COMPATIVEL));
        });
    }

    [Test]
    public void Pending_catalog_cannot_be_loaded_for_execution()
    {
        const string json = """{"schemaVersion":1,"status":"PENDENTE","catalogVersion":"v1","rules":[],"approval":null}""";
        Assert.Throws<InvalidDataException>(() => PossibilityRuleCatalogLoader.Load(json, requireApproved: true));
        var catalog = PossibilityRuleCatalogLoader.Load(json, requireApproved: false);
        Assert.That(catalog.Rules, Is.Empty);
    }

    [Test]
    public void Approved_catalog_requires_complete_approval_metadata()
    {
        const string missingApproval = """{"schemaVersion":1,"status":"APROVADO","catalogVersion":"v1","rules":[{"natureza":"SERVICO","codigo":"TEST","versao":1,"implementacaoVersao":"TEST.v1","allOf":[{"fact":"ATIVO","operator":"PRESENTE"}]}],"approval":null}""";
        Assert.Throws<InvalidDataException>(() => PossibilityRuleCatalogLoader.Load(missingApproval, requireApproved: true));
    }

    [Test]
    public void Approved_catalog_file_verifies_evidence_sha_and_rejects_tampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "jornada-poss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var evidence = Path.Combine(root, "approval.json");
            File.WriteAllText(evidence, "{\"decision\":\"approved\"}\n");
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(evidence))).ToLowerInvariant();
            var json = $$$"""{"schemaVersion":1,"status":"APROVADO","catalogVersion":"v1","rules":[{"natureza":"SERVICO","codigo":"TEST","versao":1,"implementacaoVersao":"TEST.v1","allOf":[{"fact":"ATIVO","operator":"PRESENTE"}]}],"approval":{"approvedAtUtc":"2026-09-01T00:00:00Z","approvedBy":"GESTOR_TESTE","evidencePath":"approval.json","evidenceSha256":"{{{sha}}}"}}""";
            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath, json);
            Assert.That(PossibilityRuleCatalogLoader.LoadFromFile(catalogPath, root).Status, Is.EqualTo("APROVADO"));
            File.AppendAllText(evidence, "tampered");
            Assert.Throws<InvalidDataException>(() => PossibilityRuleCatalogLoader.LoadFromFile(catalogPath, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PossibilityFactSnapshot Snapshot(params (string Key,string Value)[] values) =>
        new(values.ToDictionary(x => x.Key, x => (string?)x.Value, StringComparer.Ordinal), At);
}

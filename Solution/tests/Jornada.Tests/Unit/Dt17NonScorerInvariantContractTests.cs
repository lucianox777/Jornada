using System.Text.Json;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class Dt17NonScorerInvariantContractTests
{
    [Test]
    public void Observation_core_identity_attributes_are_nullable_while_secretariat_contract_may_require_them()
    {
        var root = RepositoryRoot();
        var ddl = File.ReadAllText(Path.Combine(root, "Solution", "database", "Jornada_Fase1.sql"));
        foreach (var fragment in new[]
        {
            "cpf CHAR(11) NULL",
            "cpf_ausente_motivo NVARCHAR(30) NULL",
            "nome_completo NVARCHAR(500) NULL",
            "data_nascimento DATE NULL",
            "nome_mae NVARCHAR(500) NULL"
        })
            Assert.That(ddl, Does.Contain(fragment), "A observação Silver deve aceitar ausência do atributo: " + fragment);

        var schemas = Directory.GetFiles(Path.Combine(root, "Solution", "config", "contracts"), "pessoa.schema.json", SearchOption.AllDirectories);
        Assert.That(schemas, Is.Not.Empty);
        Assert.That(schemas.Any(path =>
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("required", out var required)
                && required.EnumerateArray().Any(x => x.GetString() == "nomeCompleto");
        }), Is.True, "Obrigatoriedade deve permanecer uma propriedade do contrato da remessa, não da observação Silver.");
    }

    [Test]
    public void Initial_uuid_is_not_a_blocking_or_scoring_feature()
    {
        var root = RepositoryRoot();
        foreach (var relative in new[]
        {
            Path.Combine("Solution", "src", "Jornada.Linkage.Core", "FellegiSunterScoring.cs"),
            Path.Combine("Solution", "src", "Jornada.Linkage.Runner", "BlockingProjectionCandidateQueryBuilder.cs")
        })
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            Assert.That(source, Does.Not.Contain("InitialUuid").IgnoreCase,
                "initial_uuid é proveniência e não pode entrar no blocking/scorer: " + relative);
        }
    }

    [Test]
    public void Existing_identity_routes_keep_401_403_and_scope_regressions()
    {
        var root = RepositoryRoot();
        var origin = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Tests", "Unit", "ProgressiveOriginApiTests.cs"));
        var semiblind = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Tests", "Unit", "SemiblindIdentityHttpTests.cs"));
        var authorization = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Tests", "Unit", "AuthorizationMatrixContractTests.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(origin, Does.Contain("HttpStatusCode.Unauthorized"));
            Assert.That(origin, Does.Contain("HttpStatusCode.Forbidden"));
            Assert.That(origin, Does.Contain("wrong_scope"));
            Assert.That(semiblind, Does.Contain("Unauthorized"));
            Assert.That(semiblind, Does.Contain("Forbidden"));
            Assert.That(authorization, Does.Contain("Route_matrix_is_unique_and_type_credentials_are_limited_to_explicit_routes"));
        });
    }

    [Test]
    public void Cpf_regressions_cover_valid_invalid_and_conflict_evidence_without_claiming_route_execution()
    {
        var root = RepositoryRoot();
        var rules = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Tests", "Unit", "CpfRulesTests.cs"));
        var resolver = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Integration.Tests", "Integration", "CpfAnchorResolutionApiTests.cs"));
        var correction = File.ReadAllText(Path.Combine(root, "Solution", "tests", "Jornada.Integration.Tests", "Integration", "CpfAnchorGovernedCorrectionTests.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(rules, Does.Contain("Normalizes_valid_cpf"));
            Assert.That(rules, Does.Contain("Rejects_invalid_cpf"));
            Assert.That(resolver, Does.Contain("Resolver_classifies_structurally_invalid_cpf_without_uuid"));
            Assert.That(resolver, Does.Contain("Resolver_uses_permanent_anchor_when_current_map_is_closed_or_in_conflict"));
            Assert.That(correction, Does.Contain("Governed_correction_cannot_transfer_permanent_cpf_anchor"));
        });
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RELEASE_INFO.txt")))
            root = root.Parent;
        Assert.That(root, Is.Not.Null, "A raiz do repositório deve ser encontrada pelo teste.");
        return root!.FullName;
    }
}

using Jornada.Contracts;
using Jornada.Linkage.Runner;
using System.Text.Json;
using Jornada.Access.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class ProgressiveIdentityTests
{
    private static readonly Guid Initial = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid Existing = Guid.Parse("a1000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Created = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void InitialUuidIsAllocatedBeforeResolutionWithoutInventingAnAssociation()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        Assert.Multiple(() =>
        {
            Assert.That(state.InitialUuid, Is.EqualTo(Initial));
            Assert.That(state.CanonicalUuid, Is.Null);
            Assert.That(state.Status, Is.EqualTo(ProgressiveIdentityStatus.PROVISORIA));
            Assert.That(state.Version, Is.Zero);
            Assert.That(state.LastResolutionAt, Is.Null);
        });
    }

    [Test]
    public void CompletedSearchWithoutCandidatesEstablishesTheInitialReference()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var result = ProgressiveIdentityLifecycle.Conclude(state, Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(result.CanonicalUuid, Is.EqualTo(Initial));
        Assert.That(result.Version, Is.EqualTo(1));
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
    }

    [Test]
    public void ExistingIdentityIsSelectedOnlyByAnExplicitCompletedDecision()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var decision = Decision(state, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing);
        var result = ProgressiveIdentityLifecycle.Conclude(state, decision);
        Assert.That(result.CanonicalUuid, Is.EqualTo(Existing));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
        Assert.That(ProgressiveIdentityLifecycle.Conclude(result, decision), Is.SameAs(result));
    }

    [Test]
    public void AmbiguityKeepsTheReferenceWithoutChoosingAnyCandidate()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var result = ProgressiveIdentityLifecycle.Conclude(state, Decision(state, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(result.Status, Is.EqualTo(ProgressiveIdentityStatus.INDEFINIDA));
        Assert.That(result.CanonicalUuid, Is.Null);
        Assert.That(result.InitialUuid, Is.EqualTo(Initial));
        var later = ProgressiveIdentityLifecycle.Conclude(result,
            Decision(result, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing));
        Assert.That(later.Status, Is.EqualTo(ProgressiveIdentityStatus.REFERENCIA));
        Assert.That(later.CanonicalUuid, Is.EqualTo(Existing));
        Assert.That(later.Version, Is.EqualTo(2));
    }

    [Test]
    public void ReevaluationDoesNotCreateAnotherProvisionalState()
    {
        var initial = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var first = ProgressiveIdentityLifecycle.Conclude(initial, Decision(initial, ProgressiveResolutionOutcome.NOVA_IDENTIDADE));
        var second = ProgressiveIdentityLifecycle.Conclude(first, Decision(first, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(second.Status, Is.EqualTo(ProgressiveIdentityStatus.INDEFINIDA));
        Assert.That(second.Version, Is.EqualTo(2));
        Assert.That(second.InitialUuid, Is.EqualTo(Initial));
    }

    [Test]
    public void UncertaintySuspendsAnEarlierBindingAndNeverSilentlySeparatesIt()
    {
        var initial = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var associated = ProgressiveIdentityLifecycle.Conclude(initial,
            Decision(initial, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Existing));
        var uncertain = ProgressiveIdentityLifecycle.Conclude(associated,
            Decision(associated, ProgressiveResolutionOutcome.INDEFINIDA));
        Assert.That(uncertain.CanonicalUuid, Is.Null);
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(associated,
            Decision(associated, ProgressiveResolutionOutcome.NOVA_IDENTIDADE)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(uncertain,
            Decision(uncertain, ProgressiveResolutionOutcome.NOVA_IDENTIDADE)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(uncertain,
            Decision(uncertain, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE, Initial)));
        Assert.That(uncertain.LastExternalAssociationUuid, Is.EqualTo(Existing));
        Assert.That(associated.CanonicalUuid, Is.EqualTo(Existing));
    }

    [Test]
    public void InvalidOrIncompleteDecisionsFailClosed()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var valid = Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE);
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Create(Guid.Empty, Created));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Create(Initial, Created.ToOffset(TimeSpan.FromHours(-3))));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { Complete = false }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { InitialUuid = Existing }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { EvidenceReference = " " }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { UniverseReference = null }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { TargetUuid = Existing }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state, valid with { DecidedAt = Created.AddMinutes(-1) }));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            Decision(state, ProgressiveResolutionOutcome.ASSOCIACAO_EXISTENTE)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            Decision(state, ProgressiveResolutionOutcome.INDEFINIDA, Existing)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(state,
            valid with { Outcome = (ProgressiveResolutionOutcome)999 }));
    }

    [Test]
    public void OptimisticVersionAndDecisionIdPreventStaleOrConflictingReplay()
    {
        var state = ProgressiveIdentityLifecycle.Create(Initial, Created);
        var decision = Decision(state, ProgressiveResolutionOutcome.NOVA_IDENTIDADE);
        var referenced = ProgressiveIdentityLifecycle.Conclude(state, decision);
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            decision with { Outcome = ProgressiveResolutionOutcome.INDEFINIDA }));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            Decision(state, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced with { Status = ProgressiveIdentityStatus.PROVISORIA },
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<InvalidOperationException>(() => ProgressiveIdentityLifecycle.Conclude(referenced with { Version = -1 },
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA)));
        Assert.Throws<ArgumentException>(() => ProgressiveIdentityLifecycle.Conclude(referenced,
            Decision(referenced, ProgressiveResolutionOutcome.INDEFINIDA) with { DecidedAt = Created.AddMinutes(-1) }));
    }

    [Test]
    public void V1PublishesOnlyTheThreeReferenceStates()
    {
        Assert.That(Enum.GetNames<ProgressiveIdentityStatus>(), Is.EquivalentTo(new[] { "PROVISORIA", "REFERENCIA", "INDEFINIDA" }));
        Assert.That(Enum.GetNames<ProgressiveResolutionOutcome>(), Is.EquivalentTo(new[] { "NOVA_IDENTIDADE", "ASSOCIACAO_EXISTENTE", "INDEFINIDA" }));
        Assert.That(ProgressiveIdentityLifecycle.Version, Is.EqualTo("PROGRESSIVE_IDENTITY_V1"));
    }

    [Test]
    public void Progressive_reference_OpenApi_remains_typed_without_exposing_CPF()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "openapi", "jornada-v1.openapi.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(path));
        var response = contract.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/identidade/origens/consulta").GetProperty("post")
            .GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        Assert.That(response, Is.EqualTo("#/components/schemas/ProgressiveOriginQueryResponse"));
        var properties = contract.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ProgressiveOriginQueryResponse").GetProperty("properties");
        Assert.That(properties.TryGetProperty("cpf", out _), Is.False);
        Assert.That(properties.GetProperty("initialUuid").GetProperty("format").GetString(), Is.EqualTo("uuid"));
        Assert.That(properties.GetProperty("canonicalUuid").GetProperty("nullable").GetBoolean(), Is.True);
        Assert.That(properties.GetProperty("ultimaResolucaoEm").GetProperty("nullable").GetBoolean(), Is.True);
        Assert.That(properties.GetProperty("versao").GetProperty("format").GetString(), Is.EqualTo("int64"));
        Assert.That(properties.GetProperty("estado").GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString()), Is.EquivalentTo(new[] { "PROVISORIA", "REFERENCIA", "INDEFINIDA" }));
    }

    [Test]
    public void Progressive_publication_invokes_one_batch_contract_inside_the_run_transaction()
    {
        // O algoritmo de transições continua no SQL governado; o runner só o invoca.
        var sql = ProbabilisticLinkageBatchRunner.ProgressivePublicationSql();
        const string batchCall = "EXEC identidade.sp_publicar_resolucao_progressiva_linkage_lote";

        Assert.Multiple(() =>
        {
            Assert.That(sql.Split(batchCall, StringSplitOptions.None), Has.Length.EqualTo(2),
                "Uma execução do run deve acionar somente uma chamada de lote.");
            Assert.That(sql, Does.Contain("@linkage_run_id=@run_id"));
            Assert.That(sql, Does.Not.Contain("progressiva_linkage CURSOR"));
            Assert.That(sql, Does.Contain("progressiva_versao IS NULL"),
                "Origem não protegida sem versão nunca pode ser publicada.");
        });
    }

    private static ProgressiveIdentityDecision Decision(ProgressiveIdentitySnapshot state,
        ProgressiveResolutionOutcome outcome, Guid? target = null) =>
        new(Guid.NewGuid(), state.InitialUuid, state.Version, outcome, target, true,
            "evidence:synthetic", "POLICY_TEST_V1", Created.AddMinutes(state.Version + 1),
            "SYNTHETIC_MODEL_V1", "frame:synthetic");
}

[TestFixture, Category("Unit")]
public sealed class ProgressiveOriginAccessBoundaryTests
{
    [Test]
    public async Task Progressive_origin_requires_verified_gestor_context_not_just_a_scope_claim()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJornadaAccessSecurity();
        using var provider = services.BuildServiceProvider();
        var authorize = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("N"))],
            JornadaAccessSecurity.Scheme));
        var http = new DefaultHttpContext { RequestServices = provider };
        const string permission = "jornada.identidade.origem.read";

        // Um principal autenticado sem contexto verificado não pode consultar a origem.
        Assert.That((await authorize.AuthorizeAsync(principal, http, permission)).Succeeded, Is.False);

        // Nem mesmo um scope acidentalmente concedido pode habilitar credenciais de tipo.
        http.Items[JornadaAccessSecurity.AccessContextItem] = new AccessContext(
            Guid.NewGuid(), AccessCredentialType.BENEFICIO, "AR01", "SMADS", "AR01",
            [permission], ["AR01"]);
        Assert.That((await authorize.AuthorizeAsync(principal, http, permission)).Succeeded, Is.False);

        http.Items[JornadaAccessSecurity.AccessContextItem] = new AccessContext(
            Guid.NewGuid(), AccessCredentialType.GESTOR, "SMADS", "SMADS", null,
            [permission], []);
        Assert.That((await authorize.AuthorizeAsync(principal, http, permission)).Succeeded, Is.True);
    }
}

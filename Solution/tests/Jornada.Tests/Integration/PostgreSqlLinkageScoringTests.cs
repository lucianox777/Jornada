using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("PostgreSqlLinkage"), NonParallelizable]
public sealed class PostgreSqlLinkageScoringTests
{
    private static readonly Guid V1 = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private static readonly Guid V2 = Guid.Parse("81000000-0000-4000-8000-000000000002");
    private static readonly Guid Draft = Guid.Parse("81000000-0000-4000-8000-000000000003");
    private static readonly Guid A = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("82000000-0000-4000-8000-000000000002");
    private static readonly DateOnly Birth = new(1982, 4, 10);
    private const string Name = "Maria da Silva";
    private const string Mother = "Ana de Souza";
    private const string FixtureIds = "SELECT @a UNION ALL SELECT @b UNION ALL SELECT md5('linkage-cap-'||i)::uuid FROM generate_series(1,1001) AS i";
    private string connectionString = null!;
    private IOperationalDatabaseAdapter database = null!;
    private PostgreSqlProbabilisticIdentityLinkage linkage = null!;

    [OneTimeSetUp]
    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION é obrigatória.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_LINKAGE_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgLinkageTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgLinkageTest.");

        // Never truncate a pre-existing corpus. The CI must provide a fresh database.
        var existing = await ScalarAsync("""
            SELECT (SELECT COUNT(*) FROM identidade.modelo_linkage)
                 + (SELECT COUNT(*) FROM identidade.pessoa)
                 + (SELECT COUNT(*) FROM gold.pessoa)
                 + (SELECT COUNT(*) FROM silver.pessoa_observacao);
            """);
        if (existing != 0)
            throw new InvalidOperationException("A regressão Linkage exige banco descartável vazio; não serão apagados dados preexistentes.");

        database = new PostgreSqlOperationalAdapter(connectionString);
        linkage = CreateLinkage();
        await SeedModelAsync(V1, 1, "ATIVO", false);
        await SeedModelAsync(V2, 2, "VALIDADO", true);
        await SeedModelAsync(Draft, 3, "RASCUNHO", false);
    }

    [SetUp]
    public async Task ResetAsync()
    {
        // Only the UUIDs reserved for this fixture can be removed.
        await ExecuteAsync($"DELETE FROM gold.pessoa WHERE pessoa_uuid IN ({FixtureIds}); DELETE FROM identidade.pessoa WHERE pessoa_uuid IN ({FixtureIds});", ("a", A), ("b", B));
        await ExecuteAsync("""
            UPDATE identidade.modelo_linkage SET status='INATIVO'
            WHERE modelo_id IN (@v1,@v2) AND status='ATIVO';
            UPDATE identidade.modelo_linkage SET status='ATIVO' WHERE modelo_id=@v1;
            """, ("v1", V1), ("v2", V2));
        linkage = CreateLinkage();
    }

    [Test]
    public async Task Catalog_EnforcesVersionStatusAndImmutability()
    {
        Assert.That((await linkage.GetActiveModelAsync(CancellationToken.None)).ModelId, Is.EqualTo(V1));
        Assert.That((await linkage.GetModelByVersionAsync(2, CancellationToken.None)).ModelId, Is.EqualTo(V2));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await linkage.GetModelByVersionAsync(3, CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await linkage.GetModelByVersionAsync(99, CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await linkage.ResolveWithoutCpfAsync(Observation(), Draft, CancellationToken.None));
        await AssertDbRejectedAsync("UPDATE identidade.parametro_linkage SET valor=0.5 WHERE modelo_id=@id AND nome='T_LINKAGE';", ("id", V1));
        await AssertDbRejectedAsync("DELETE FROM identidade.parametro_linkage WHERE modelo_id=@id;", ("id", V1));
        await AssertDbRejectedAsync("UPDATE identidade.modelo_linkage SET algoritmo_versao='ALTERADO' WHERE modelo_id=@id;", ("id", V1));
        await AssertDbRejectedAsync("UPDATE identidade.modelo_linkage SET status='RASCUNHO' WHERE modelo_id=@id;", ("id", V1));
        await AssertDbRejectedAsync("UPDATE identidade.modelo_linkage SET status='ATIVO' WHERE modelo_id=@id;", ("id", V2));
    }

    [Test]
    public async Task V1_RequiresExactBirthAndRejectsCpf()
    {
        await SeedCandidateAsync(A, Birth.AddDays(1));
        var absent = await ResolveAsync(V1);
        Assert.That(absent.Motivo, Is.EqualTo("SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO"));
        await SeedCandidateAsync(A, Birth);
        Assert.That((await ResolveAsync(V1)).PessoaUuidResolvido, Is.EqualTo(A));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await linkage.ResolveWithoutCpfAsync(Observation("11144477735"), V1, CancellationToken.None));
    }

    [TestCase(1982, 4, 11)]
    [TestCase(1982, 5, 10)]
    [TestCase(1982, 10, 4)]
    [TestCase(1983, 4, 10)]
    public async Task V2_BirthPasses(int year, int month, int day)
    {
        await SeedCandidateAsync(A, new DateOnly(year, month, day));
        var result = await ResolveAsync(V2);
        Assert.Multiple(() => { Assert.That(result.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO)); Assert.That(result.PessoaUuidResolvido, Is.EqualTo(A)); Assert.That(result.ModeloId, Is.EqualTo(V2)); });
    }

    [Test]
    public async Task V2_NoCandidateAndNoSilentTruncation()
    {
        Assert.That((await ResolveAsync(V2)).Motivo, Is.EqualTo("SEM_CANDIDATO_NOS_BLOCOS_NASCIMENTO_COMPONENTE"));
        await ExecuteAsync("""
            INSERT INTO identidade.pessoa(pessoa_uuid,status)
            SELECT md5('linkage-cap-'||i)::uuid,'ATIVO' FROM generate_series(1,1001) AS i;
            INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
            SELECT md5('linkage-cap-'||i)::uuid,NULL,'AUSENTE','Maria da Silva',DATE '1982-04-10','Ana de Souza',1,'BASELINE_FONTE_UNICA'
            FROM generate_series(1,1001) AS i;
            """);
        linkage = CreateLinkage(1000);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await ResolveAsync(V2));
        Assert.That(ex!.Message, Does.Contain("excede MaxCandidatesPerBlock"));
    }

    [Test]
    public async Task DecisionPolicy_PreservesThresholdMarginAndOrdering()
    {
        await SeedCandidateAsync(A, Birth, "Nome sem relação", "Mãe diferente");
        var low = await ResolveAsync(V1);
        Assert.Multiple(() => { Assert.That(low.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO)); Assert.That(low.Motivo, Is.EqualTo("ABAIXO_T_LINKAGE")); Assert.That(low.PessoaUuidResolvido, Is.Null); });
        await SeedCandidateAsync(A, Birth);
        await SeedCandidateAsync(B, Birth);
        var conflict = await ResolveAsync(V1);
        Assert.Multiple(() => { Assert.That(conflict.Status, Is.EqualTo(ResolutionStatus.CONFLITO)); Assert.That(conflict.PessoaUuidResolvido, Is.Null); Assert.That(conflict.MelhorCandidatoUuid, Is.EqualTo(A)); Assert.That(conflict.SegundoCandidatoUuid, Is.EqualTo(B)); Assert.That(conflict.Margem, Is.EqualTo(0m)); });
        await SeedCandidateAsync(B, Birth, "Nome sem relação", "Mãe diferente");
        var resolved = await ResolveAsync(V1);
        Assert.That(resolved.PessoaUuidResolvido, Is.EqualTo(A));
        Assert.That(resolved.Margem, Is.GreaterThan(0.05m));
    }

    [Test]
    public async Task ModelSnapshot_RemainsPinnedAfterActivationChanges()
    {
        await SeedCandidateAsync(A, Birth.AddDays(1));
        var model = await linkage.GetActiveModelAsync(CancellationToken.None);
        await ExecuteAsync("UPDATE identidade.modelo_linkage SET status='INATIVO' WHERE modelo_id=@id;", ("id", V1));
        await ExecuteAsync("UPDATE identidade.modelo_linkage SET status='ATIVO',ativado_em=CURRENT_TIMESTAMP WHERE modelo_id=@id;", ("id", V2));
        Assert.That((await linkage.GetActiveModelAsync(CancellationToken.None)).ModelId, Is.EqualTo(V2));
        Assert.That((await ResolveAsync(model.ModelId)).Motivo, Is.EqualTo("SEM_CANDIDATO_NO_BLOCO_DATA_NASCIMENTO"));
        Assert.That((await ResolveAsync(V2)).PessoaUuidResolvido, Is.EqualTo(A));
    }

    [Test]
    public async Task ReadOnlyScoring_DoesNotPublishOrChangeIdentity()
    {
        await SeedCandidateAsync(A, Birth);
        var before = await ScalarAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte;");
        var people = await ScalarAsync("SELECT COUNT(*) FROM identidade.pessoa;");
        Assert.That((await ResolveAsync(V1)).Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte;"), Is.EqualTo(before));
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM identidade.pessoa;"), Is.EqualTo(people));
    }

    private PostgreSqlProbabilisticIdentityLinkage CreateLinkage(int maxCandidates = 100000)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProbabilisticLinkage:MaxCandidatesPerBlock"] = maxCandidates.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ProbabilisticLinkage:BirthYearTolerance"] = "1"
        }).Build();
        return new PostgreSqlProbabilisticIdentityLinkage(configuration, database, NullLogger<PostgreSqlProbabilisticIdentityLinkage>.Instance);
    }

    private static IdentityObservation Observation(string? cpf = null) => new(cpf, cpf is null ? "NAO_INFORMADO" : null, Name, Birth, Mother);
    private Task<ProbabilisticLinkageDecision> ResolveAsync(Guid model) => linkage.ResolveWithoutCpfAsync(Observation(), model, CancellationToken.None);

    private async Task SeedModelAsync(Guid id, int version, string status, bool v2)
    {
        await ExecuteAsync("""
            INSERT INTO identidade.modelo_linkage(modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,base_referencia,gerado_em)
            VALUES(@id,@version,'RASCUNHO','FELLEGI_SUNTER_V1',@normalization,'SYNTHETIC','CI_LINKAGE_SYNTHETIC',CURRENT_TIMESTAMP);
            """, ("id", id), ("version", version), ("normalization", IdentityComparison.NormalizationVersion));
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["PRIOR_MATCH_PROBABILITY"] = 0.001m, ["PRIOR_BLOCK_MIN"] = 0.000001m,
            ["PRIOR_BLOCK_MAX"] = 0.25m, ["T_LINKAGE"] = 0.90m, ["CONFLICT_MARGIN"] = 0.05m
        };
        foreach (var feature in new[] { "NOME", "NOME_MAE" })
            foreach (var (state, m, u) in new[] { ("EXACT", .90m, .01m), ("HIGH", .05m, .04m), ("MEDIUM", .03m, .10m), ("LOW", .02m, .85m) })
            {
                p[$"M_{feature}_{state}"] = m; p[$"U_{feature}_{state}"] = u;
            }
        if (v2)
        {
            p["BLOCKING_BIRTH_COMPONENTS_V2"] = 1m;
            foreach (var feature in new[] { "NASC_DIA", "NASC_MES", "NASC_ANO" })
            {
                p[$"M_{feature}_EXACT"] = .90m; p[$"M_{feature}_DIFF"] = .10m;
                p[$"U_{feature}_EXACT"] = .10m; p[$"U_{feature}_DIFF"] = .90m;
            }
        }
        foreach (var parameter in p)
            await ExecuteAsync("INSERT INTO identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@id,@name,@value);", ("id", id), ("name", parameter.Key), ("value", parameter.Value));
        if (status != "RASCUNHO")
            await ExecuteAsync("UPDATE identidade.modelo_linkage SET status=@status WHERE modelo_id=@id;", ("status", status), ("id", id));
    }

    private async Task SeedCandidateAsync(Guid id, DateOnly date, string name = Name, string mother = Mother)
    {
        await ExecuteAsync("INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@id,'ATIVO') ON CONFLICT(pessoa_uuid) DO NOTHING;", ("id", id));
        await ExecuteAsync("""
            INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
            VALUES(@id,NULL,'AUSENTE',@name,@birth,@mother,1,'BASELINE_FONTE_UNICA')
            ON CONFLICT(pessoa_uuid) DO UPDATE SET nome_completo=EXCLUDED.nome_completo,data_nascimento=EXCLUDED.data_nascimento,nome_mae=EXCLUDED.nome_mae;
            """, ("id", id), ("name", name), ("birth", date), ("mother", mother));
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Consulta sem valor."));
    }

    private async Task AssertDbRejectedAsync(string sql, params (string Name, object Value)[] parameters)
    {
        try { await ExecuteAsync(sql, parameters); Assert.Fail("A operação inválida deveria ter sido rejeitada pelo banco."); }
        catch (PostgresException) { /* Constraint/trigger expected. */ }
    }
}

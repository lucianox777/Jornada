using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlCandidateSampling"), NonParallelizable]
public sealed class PostgreSqlCandidateSamplingTests
{
    private static readonly byte[] Seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly Guid[] Ids = Enumerable.Range(1, 7)
        .Select(i => Guid.Parse($"96000000-0000-4000-8000-{i:000000000000}")).ToArray();
    private static readonly Guid SourceA = Guid.Parse("97000000-0000-4000-8000-000000000001");
    private static readonly Guid SourceB = Guid.Parse("97000000-0000-4000-8000-000000000002");
    private static readonly Guid SourceC = Guid.Parse("97000000-0000-4000-8000-000000000003");
    private string connectionString = null!;
    private PostgreSqlCandidateSampler sampler = null!;

    [OneTimeSetUp]
    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION é obrigatória.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_SAMPLING_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgSamplingTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgSamplingTest.");
        if (await ScalarAsync("""
            SELECT (SELECT COUNT(*) FROM gold.pessoa)+(SELECT COUNT(*) FROM identidade.pessoa)+
                   (SELECT COUNT(*) FROM identidade.modelo_linkage)+(SELECT COUNT(*) FROM silver.pessoa_observacao)+
                   (SELECT COUNT(*) FROM identidade.vinculo_fonte);
            """) != 0)
            throw new InvalidOperationException("O banco de regressão deve estar vazio; dados preexistentes não serão apagados.");
        sampler = new PostgreSqlCandidateSampler(new PostgreSqlOperationalAdapter(connectionString));
        var dates = new[]
        {
            new DateOnly(1982, 4, 10), new DateOnly(1982, 4, 11), new DateOnly(1982, 5, 10),
            new DateOnly(1982, 10, 4), new DateOnly(1983, 4, 10), new DateOnly(1982, 4, 10),
            new DateOnly(1982, 5, 11)
        };
        for (var i = 0; i < Ids.Length; i++)
        {
            var name = i >= 5 ? "Outra" : "Maria";
            var mother = i >= 5 ? "Outra" : "Ana";
            await ExecuteAsync("INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@id,'ATIVO');", ("id", Ids[i]));
            await ExecuteAsync("""
                INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
                VALUES(@id,NULL,'AUSENTE',@name,@birth,@mother,1,'BASELINE_FONTE_UNICA');
                """, ("id", Ids[i]), ("name", name), ("birth", dates[i]), ("mother", mother));
        }
    }

    [OneTimeTearDown]
    public async Task CleanupAsync()
    {
        if (connectionString is null || new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgSamplingTest" ||
            Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_SAMPLING_TESTS") != "1") return;
        await ExecuteAsync("DELETE FROM gold.pessoa WHERE pessoa_uuid=ANY(@ids); DELETE FROM identidade.pessoa WHERE pessoa_uuid=ANY(@ids);", ("ids", Ids));
    }

    [Test]
    public async Task CapturesFullFivePassUniverseWithActualWeightsAndNoIdentityWrites()
    {
        var before = await CountsAsync();
        var capture = await sampler.CaptureAsync(Frame(2), Options(2), Seed, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(capture.BlockingVersion, Is.EqualTo(BirthBlockingPlan.Version));
            Assert.That(capture.EnumeratedPairs, Is.EqualTo(12));
            Assert.That(capture.OverlapCount, Is.EqualTo(2));
            Assert.That(capture.Pairs.Count, Is.EqualTo(10));
            Assert.That(capture.EstimatedPairPopulation, Is.EqualTo(12m));
            Assert.That(capture.PassCounts.Select(x => x.Primary), Is.EqualTo(new long[] { 4, 2, 2, 2, 2 }));
            Assert.That(capture.PassCounts.Select(x => x.EstimatedMembership), Is.EqualTo(
                BirthBlockingPlan.OrderedPasses.Select(pass => capture.Pairs.Where(p => (p.Membership & pass) != 0).Sum(p => p.DesignWeight))));
            Assert.That(capture.Pairs.Select(x => (x.SourceId, x.CandidateId)).Distinct().Count(), Is.EqualTo(10));
            Assert.That(capture.Pairs.All(x => x.InclusionProbability > 0 && x.InclusionProbability <= 1), Is.True);
            Assert.That(capture.Pairs.All(x => x.PrimaryPass == BirthBlockingPlan.PrimaryPass(x.Membership)), Is.True);
            Assert.That(JsonSerializer.Serialize(capture), Does.Not.Contain("Maria"));
            Assert.That(JsonSerializer.Serialize(capture), Does.Not.Contain("Ana"));
        });
        var repeated = await sampler.CaptureAsync(Frame(2) with { Sources = Frame(2).Sources.Reverse().ToArray() },
            Options(2), Seed, CancellationToken.None);
        Assert.That(repeated.SelectionFingerprint, Is.EqualTo(capture.SelectionFingerprint));
        Assert.That(repeated.UniverseFingerprint, Is.EqualTo(capture.UniverseFingerprint));
        Assert.That(repeated.Pairs, Is.EqualTo(capture.Pairs));
        Assert.That(await CountsAsync(), Is.EqualTo(before));
        Assert.That(Seed, Is.EqualTo(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()));
    }

    [Test]
    public async Task EmptySourcesAndV1DoNotBroadenCandidates()
    {
        var capture = await sampler.CaptureAsync(Frame(3), Options(3), Seed, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(capture.EmptySources, Is.EqualTo(1));
            Assert.That(capture.EnumeratedPairs, Is.EqualTo(12));
            Assert.That(capture.EstimatedPairPopulation, Is.EqualTo(12m));
        });
        var v1 = await sampler.CaptureAsync(Frame(1), Options(1) with
        {
            UseComponents = false, PrimaryPassQuotas = new[] { 1, 0, 0, 0, 0 }
        }, Seed, CancellationToken.None);
        Assert.That(v1.EnumeratedPairs, Is.EqualTo(2));
        Assert.That(v1.Pairs.Count, Is.EqualTo(1));
        Assert.That(v1.Pairs[0].DesignWeight, Is.EqualTo(2m));
        var swapped = await sampler.CaptureAsync(Frame(1) with
        {
            Sources = new[] { Source(SourceA) with { Name = "Ana", Mother = "Maria" } }
        }, Options(1), Seed, CancellationToken.None);
        Assert.That(swapped.EnumeratedPairs, Is.EqualTo(4));
    }

    [Test]
    public async Task LimitsAbortWithoutPartialCaptureOrWrites()
    {
        var before = await CountsAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await sampler.CaptureAsync(Frame(1),
            Options(1) with { MaxCandidatesPerSource = 5 }, Seed, CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await sampler.CaptureAsync(Frame(2),
            Options(2) with { MaxEnumeratedPairs = 11 }, Seed, CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await sampler.CaptureAsync(Frame(1),
            Options(1) with { MaxSelectedPairs = 4 }, Seed, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(async () => await sampler.CaptureAsync(Frame(1) with { Complete = false },
            Options(1), Seed, CancellationToken.None));
        Assert.That(await CountsAsync(), Is.EqualTo(before));
    }

    private static CandidateUniverseSource Source(Guid id) =>
        new(id, new DateOnly(1982, 4, 10), "Maria", "Ana");

    private static CandidateSamplingFrame Frame(int count) => new("CI_FRAME_V1", true,
        new[] { Source(SourceA), Source(SourceB), Source(SourceC) with { BirthDate = new DateOnly(1970, 1, 1) } }.Take(count).ToArray());

    private static CandidateSamplingOptions Options(int n) =>
        new(true, 1, n, new[] { 1, 1, 1, 1, 1 }, 10, 100, 1000, 1000, 60);

    private async Task<string> CountsAsync() => string.Join("|", new[]
    {
        await ScalarAsync("SELECT COUNT(*) FROM gold.pessoa;"),
        await ScalarAsync("SELECT COUNT(*) FROM identidade.pessoa;"),
        await ScalarAsync("SELECT COUNT(*) FROM identidade.modelo_linkage;"),
        await ScalarAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte;")
    });

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Consulta sem valor."), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

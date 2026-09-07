using System.Globalization;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlCandidateUniverse"), NonParallelizable]
public sealed class PostgreSqlCandidateUniverseTests
{
    private static readonly Guid A = Guid.Parse("93000000-0000-4000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("93000000-0000-4000-8000-000000000002");
    private static readonly Guid C = Guid.Parse("93000000-0000-4000-8000-000000000003");
    private static readonly Guid D = Guid.Parse("93000000-0000-4000-8000-000000000004");
    private static readonly Guid E = Guid.Parse("93000000-0000-4000-8000-000000000005");
    private static readonly Guid F = Guid.Parse("93000000-0000-4000-8000-000000000006");
    private static readonly Guid G = Guid.Parse("93000000-0000-4000-8000-000000000007");
    private static readonly Guid SourceA = Guid.Parse("94000000-0000-4000-8000-000000000001");
    private static readonly Guid SourceB = Guid.Parse("94000000-0000-4000-8000-000000000002");
    private string connectionString = null!;
    private PostgreSqlCandidateUniverse universe = null!;
    private const string FixtureIds = "SELECT unnest(ARRAY[@a,@b,@c,@d,@e,@f,@g]::uuid[])";

    [OneTimeSetUp]
    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION é obrigatória.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_UNIVERSE_TESTS") != "1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database != "JornadaPgUniverseTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgUniverseTest.");
        if (await ScalarAsync("SELECT (SELECT COUNT(*) FROM gold.pessoa)+(SELECT COUNT(*) FROM identidade.pessoa)+(SELECT COUNT(*) FROM identidade.modelo_linkage);") != 0)
            throw new InvalidOperationException("O banco de regressão deve estar vazio; dados preexistentes não serão apagados.");
        universe = new PostgreSqlCandidateUniverse(new PostgreSqlOperationalAdapter(connectionString));
    }

    [SetUp]
    public async Task ResetAsync()
    {
        await ExecuteAsync($"DELETE FROM gold.pessoa WHERE pessoa_uuid IN ({FixtureIds}); DELETE FROM identidade.pessoa WHERE pessoa_uuid IN ({FixtureIds});",
            ("a", A), ("b", B), ("c", C), ("d", D), ("e", E), ("f", F), ("g", G));
    }

    [Test]
    public async Task CapturesFivePassesOverlapsAndDistinctUuidsWithoutInferringLabels()
    {
        await SeedAsync();
        var sources = new[] { Source(SourceB, B), Source(SourceA, A) };
        var capture = await universe.CaptureAsync(sources, Options(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(capture.BlockingVersion, Is.EqualTo(BirthBlockingPlan.Version));
            Assert.That(capture.SourceCount, Is.EqualTo(2));
            Assert.That(capture.PairCount, Is.EqualTo(12));
            Assert.That(capture.DistinctCandidateCount, Is.EqualTo(6));
            Assert.That(capture.SameKnownUuidCount, Is.EqualTo(2));
            Assert.That(capture.OverlapCount, Is.EqualTo(2));
            Assert.That(capture.PassCounts.Select(x => x.Members), Is.EqualTo(new long[] { 4, 4, 4, 2, 2 }));
            Assert.That(capture.PassCounts.Select(x => x.Primary), Is.EqualTo(new long[] { 4, 2, 2, 2, 2 }));
            Assert.That(capture.PairFingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(capture.ConfigurationFingerprint, Does.Match("^[0-9a-f]{64}$"));
        });
        var repeated = await universe.CaptureAsync(sources.Reverse().ToArray(), Options(), CancellationToken.None);
        Assert.That(repeated.PairFingerprint, Is.EqualTo(capture.PairFingerprint));
        Assert.That(JsonSerializer.Serialize(capture), Does.Not.Contain("Maria"));
        Assert.That(JsonSerializer.Serialize(capture), Does.Not.Contain(A.ToString("D")));
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM identidade.modelo_linkage;"), Is.Zero);
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM gold.pessoa;"), Is.EqualTo(7));
    }

    [Test]
    public async Task EmptyBlocksAndV1DoNotBroadenTheUniverse()
    {
        await SeedAsync();
        var empty = await universe.CaptureAsync(new[] { Source(SourceA, null) with { BirthDate = new DateOnly(1970, 1, 1) } },
            Options(), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(empty.PairCount, Is.Zero);
            Assert.That(empty.ZeroCandidateSources, Is.EqualTo(1));
        });
        var v1 = await universe.CaptureAsync(new[] { Source(SourceA, null) }, Options() with { UseComponents = false }, CancellationToken.None);
        Assert.That(v1.PairCount, Is.EqualTo(2));
        Assert.That(v1.PassCounts.Select(x => x.Members), Is.EqualTo(new long[] { 2, 0, 0, 0, 0 }));
        var noYear = await universe.CaptureAsync(new[] { Source(SourceA, null) }, Options() with { YearTolerance = 0 }, CancellationToken.None);
        Assert.That(noYear.PairCount, Is.EqualTo(5));
    }

    [Test]
    public async Task LimitsAndDuplicateSourcesFailWithoutTruncationOrWrites()
    {
        await SeedAsync();
        var sources = new[] { Source(SourceA, A) };
        Assert.ThrowsAsync<InvalidOperationException>(async () => await universe.CaptureAsync(sources,
            Options() with { MaxCandidatesPerSource = 5 }, CancellationToken.None));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await universe.CaptureAsync(sources,
            Options() with { MaxPairs = 5 }, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(async () => await universe.CaptureAsync(new[] { sources[0], sources[0] },
            Options(), CancellationToken.None));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await universe.CaptureAsync(sources,
            Options() with { MaxSources = 0 }, CancellationToken.None));
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM gold.pessoa;"), Is.EqualTo(7));
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM identidade.modelo_linkage;"), Is.Zero);
    }

    [Test]
    public async Task SourceFieldInitialsAndCalendarEdgesAreCheckedAgainstTheSharedPlan()
    {
        await SeedAsync();
        var swappedNames = await universe.CaptureAsync(new[] { Source(SourceA, null) with { Name = "Ana", Mother = "Maria" } },
            Options(), CancellationToken.None);
        // Only exact date and unfiltered transposition/year passes survive cross-field initials.
        Assert.That(swappedNames.PairCount, Is.EqualTo(4));
        var noInitials = await universe.CaptureAsync(new[] { Source(SourceA, null) with { Name = " ", Mother = " " } },
            Options(), CancellationToken.None);
        Assert.That(noInitials.PairCount, Is.EqualTo(4));
        var edge = await universe.CaptureAsync(new[] { Source(SourceA, null) with { BirthDate = DateOnly.MaxValue } },
            Options() with { YearTolerance = 2 }, CancellationToken.None);
        Assert.That(edge.PairCount, Is.Zero);
        var leap = await universe.CaptureAsync(new[] { Source(SourceA, null) with { BirthDate = new DateOnly(2000, 2, 29) } },
            Options(), CancellationToken.None);
        Assert.That(leap.PairCount, Is.Zero);
    }

    [Test]
    public void ScalarConversionAcceptsIntegerAndBigint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToCount(3), Is.EqualTo(3L));
            Assert.That(ToCount(3L), Is.EqualTo(3L));
        });
    }

    private static long ToCount(object value) =>
        Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static CandidateUniverseSource Source(Guid sourceId, Guid? known) =>
        new(sourceId, new DateOnly(1982, 4, 10), "Maria", "Ana", known);

    private static CandidateUniverseOptions Options() => new(true, 1, 10, 100, 1000, 60);

    private async Task SeedAsync()
    {
        await SeedPersonAsync(A, new DateOnly(1982, 4, 10));
        await SeedPersonAsync(B, new DateOnly(1982, 4, 11));
        await SeedPersonAsync(C, new DateOnly(1982, 5, 10));
        await SeedPersonAsync(D, new DateOnly(1982, 10, 4));
        await SeedPersonAsync(E, new DateOnly(1983, 4, 10));
        await SeedPersonAsync(F, new DateOnly(1982, 4, 10), "Outra", "Outra");
        await SeedPersonAsync(G, new DateOnly(1982, 5, 11), "Outra", "Outra");
    }

    private async Task SeedPersonAsync(Guid id, DateOnly date, string name = "Maria", string mother = "Ana")
    {
        await ExecuteAsync("INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@id,'ATIVO');", ("id", id));
        await ExecuteAsync("""
            INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia)
            VALUES(@id,NULL,'AUSENTE',@name,@birth,@mother,1,'BASELINE_FONTE_UNICA');
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
        return ToCount(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Consulta sem valor."));
    }
}

using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlCalibration"), NonParallelizable]
public sealed class PostgreSqlCalibrationTests
{
    private string connectionString = null!;
    private PostgreSqlLinkageCalibrator calibrator = null!;
    private static readonly PostgreSqlCalibrationOptions Options = new(8,10,2,0.5m,0.95m,0.03m,60,true);

    [OneTimeSetUp]
    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
            ?? throw new InvalidOperationException("Conexão de calibração não configurada.");
        if (Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS")!="1" ||
            new NpgsqlConnectionStringBuilder(connectionString).Database!="JornadaPgCalibrationTest")
            throw new InvalidOperationException("Exige opt-in e banco descartável JornadaPgCalibrationTest.");
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.modelo_linkage;"),Is.Zero);
        Assert.That(await ScalarAsync<int>("SELECT expected_people FROM controle.calibracao_ci_fixture WHERE fixture_id='93000000-0000-4000-8000-000000000001';"),Is.EqualTo(10));
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM gold.pessoa;"),Is.EqualTo(10));
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.vinculo_fonte WHERE ativo;"),Is.EqualTo(18));
        calibrator = new PostgreSqlLinkageCalibrator(new PostgreSqlOperationalAdapter(connectionString));
    }

    [Test]
    public async Task Draft_RequiresExplicitValidation_AndNeverPublishesIdentity()
    {
        var before = await SourceCountsAsync();
        var draft = await calibrator.GenerateDraftAsync(Options,CancellationToken.None);
        Assert.Multiple(() => {
            Assert.That(draft.Population,Is.EqualTo(10));
            Assert.That(draft.MatchedPairs,Is.EqualTo(8));
            Assert.That(draft.UnmatchedPairs,Is.EqualTo(5));
        });
        Assert.That(await ModelStatusAsync(draft.ModelId),Is.EqualTo("RASCUNHO"));
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.parametro_linkage WHERE modelo_id=@id;",("id",draft.ModelId)),Is.GreaterThan(40));
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.frequencia_linkage WHERE modelo_id=@id;",("id",draft.ModelId)),Is.GreaterThan(3));
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.calibracao_linkage WHERE modelo_id=@id AND validado_em IS NULL;",("id",draft.ModelId)),Is.EqualTo(1));
        await AssertFingerprintAsync(draft.ModelId);
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before));
        Assert.That(await ActiveCountAsync(),Is.Zero);

        Assert.That(await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None),Is.EqualTo(draft.ModelId));
        Assert.That(await ModelStatusAsync(draft.ModelId),Is.EqualTo("VALIDADO"));
        Assert.That(await ActiveCountAsync(),Is.Zero);
        await AssertFingerprintAsync(draft.ModelId);
        Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None);});
        await AssertDbRejectedAsync("UPDATE identidade.parametro_linkage SET valor=0.5 WHERE modelo_id=@id AND nome='T_LINKAGE';",("id",draft.ModelId));
        await AssertDbRejectedAsync("DELETE FROM identidade.estatistica_linkage WHERE modelo_id=@id;",("id",draft.ModelId));
        await AssertDbRejectedAsync("UPDATE identidade.frequencia_linkage SET ocorrencias=1 WHERE modelo_id=@id;",("id",draft.ModelId));
        await AssertDbRejectedAsync("UPDATE identidade.calibracao_linkage SET parametros_sha256=repeat('a',64) WHERE modelo_id=@id;",("id",draft.ModelId));
        await AssertDbRejectedAsync("UPDATE identidade.modelo_linkage SET status='RASCUNHO' WHERE modelo_id=@id;",("id",draft.ModelId));
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before));
    }

    [Test]
    public async Task Validation_RejectsMissingTamperedAndInvalidDistributions()
    {
        var draft = await calibrator.GenerateDraftAsync(Options,CancellationToken.None);
        var id = draft.ModelId;
        var original = await ScalarAsync<decimal>("SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@id AND nome='M_NOME_EXACT';",("id",id));
        await ExecuteAsync("UPDATE identidade.parametro_linkage SET valor=0.85 WHERE modelo_id=@id AND nome='T_LINKAGE';",("id",id));
        Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None);});
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("RASCUNHO"));
        await ExecuteAsync("UPDATE identidade.parametro_linkage SET valor=0.95 WHERE modelo_id=@id AND nome='T_LINKAGE';",("id",id));
        var missing = await ScalarAsync<decimal>("SELECT valor FROM identidade.parametro_linkage WHERE modelo_id=@id AND nome='U_NASC_MES_DIFF';",("id",id));
        await ExecuteAsync("DELETE FROM identidade.parametro_linkage WHERE modelo_id=@id AND nome='U_NASC_MES_DIFF';",("id",id));
        Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None);});
        await ExecuteAsync("INSERT INTO identidade.parametro_linkage(modelo_id,nome,valor) VALUES(@id,'U_NASC_MES_DIFF',@value);",("id",id),("value",missing));
        await AssertFingerprintAsync(id);

        await ExecuteAsync("UPDATE identidade.parametro_linkage SET valor=1.5 WHERE modelo_id=@id AND nome='M_NOME_EXACT';",("id",id));
        await RefreshFingerprintAsync(id);
        Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None);});
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("RASCUNHO"));
        await ExecuteAsync("UPDATE identidade.parametro_linkage SET valor=@value WHERE modelo_id=@id AND nome='M_NOME_EXACT';",("id",id),("value",original));
        await RefreshFingerprintAsync(id);
        Assert.That(await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None),Is.EqualTo(id));
    }

    [Test]
    public async Task InsufficientIndependentMatches_LeavesFailedModelWithoutPartialEvidence()
    {
        var before = await SourceCountsAsync();
        var previous = await ScalarAsync<int>("SELECT COALESCE(MAX(versao),0) FROM identidade.modelo_linkage;");
        var options = Options with {SampleSize=10,MinimumIndependentMatchedPairs=9};
        Assert.ThrowsAsync<InvalidOperationException>(async()=>{await calibrator.GenerateDraftAsync(options,CancellationToken.None);});
        var id = await ScalarAsync<Guid>("SELECT modelo_id FROM identidade.modelo_linkage WHERE versao=@version;",("version",previous+1));
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("FALHOU"));
        await AssertNoPartialEvidenceAsync(id);
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before));
        Assert.That(await ActiveCountAsync(),Is.Zero);
    }

    [Test]
    public async Task ForcedInsertFailure_RollsBackAllEvidenceAndMarksModelFailed()
    {
        await ExecuteAsync("""
            CREATE FUNCTION identidade.fn_ci_calibracao_falha() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.nome='T_LINKAGE' THEN RAISE EXCEPTION 'CI forced failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER tr_ci_calibracao_falha BEFORE INSERT ON identidade.parametro_linkage
            FOR EACH ROW EXECUTE FUNCTION identidade.fn_ci_calibracao_falha();
            """);
        var previous = await ScalarAsync<int>("SELECT COALESCE(MAX(versao),0) FROM identidade.modelo_linkage;");
        try
        {
            Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.GenerateDraftAsync(Options,CancellationToken.None);});
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER IF EXISTS tr_ci_calibracao_falha ON identidade.parametro_linkage; DROP FUNCTION IF EXISTS identidade.fn_ci_calibracao_falha();");
        }
        var id = await ScalarAsync<Guid>("SELECT modelo_id FROM identidade.modelo_linkage WHERE versao=@version;",("version",previous+1));
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("FALHOU"));
        await AssertNoPartialEvidenceAsync(id);
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before);
        Assert.That(await ActiveCountAsync(),Is.Zero);
    }

    [Test]
    public async Task ConcurrentGeneration_AllocatesDifferentVersionsAndReproducibleParameters()
    {
        var before = await SourceCountsAsync();
        var drafts = await Task.WhenAll(
            calibrator.GenerateDraftAsync(Options,CancellationToken.None),
            calibrator.GenerateDraftAsync(Options,CancellationToken.None));
        Assert.That(drafts.Select(x=>x.Version).Distinct().Count(),Is.EqualTo(2));
        Assert.That(drafts.Select(x=>x.ModelId).Distinct().Count(),Is.EqualTo(2));
        foreach(var draft in drafts)
        {
            Assert.That(await ModelStatusAsync(draft.ModelId),Is.EqualTo("RASCUNHO"));
            await AssertFingerprintAsync(draft.ModelId);
        }
        var hashes = new List<string>();
        foreach(var draft in drafts)
            hashes.Add(await ScalarAsync<string>("SELECT parametros_sha256 FROM identidade.calibracao_linkage WHERE modelo_id=@id;",("id",draft.ModelId)));
        Assert.That(hashes.Distinct().Count(),Is.EqualTo(1));
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before));
        Assert.That(await ActiveCountAsync(),Is.Zero);
    }

    [Test]
    public async Task SyntheticOptIn_IsRequiredBeforeAnyModelIsReserved()
    {
        var before = await ScalarAsync<long>("SELECT count(*) FROM identidade.modelo_linkage;");
        var old = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS");
        try
        {
            Environment.SetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS",null);
            Assert.ThrowsAsync<InvalidOperationException>(async()=>{await calibrator.GenerateDraftAsync(Options,CancellationToken.None);});
        }
        finally { Environment.SetEnvironmentVariable("JORNADA_POSTGRESQL_CALIBRATION_TESTS",old); }
        Assert.That(await ScalarAsync<long>("SELECT count(*) FROM identidade.modelo_linkage;"),Is.EqualTo(before));
    }

    private async Task AssertNoPartialEvidenceAsync(Guid id)
    {
        foreach(var table in new[]{"parametro_linkage","estatistica_linkage","frequencia_linkage","calibracao_linkage"})
        {
            var count = await ScalarAsync<long>($"SELECT count(*) FROM identidade.{table} WHERE modelo_id=@id;",("id",id));
            Assert.That(count,Is.Zero,table);
        }
    }
    private async Task AssertFingerprintAsync(Guid id)
    {
        var computed = await ScalarAsync<string>("""
            SELECT encode(sha256(convert_to(string_agg(nome||'='||valor::text,E'\n' ORDER BY nome COLLATE "C")||E'\n','UTF8')),'hex')
            FROM identidade.parametro_linkage WHERE modelo_id=@id;
            """,("id",id));
        var stored = await ScalarAsync<string>("SELECT parametros_sha256 FROM identidade.calibracao_linkage WHERE modelo_id=@id;",("id",id));
        Assert.That(stored,Is.EqualTo(computed));
        var metadata = await ScalarAsync<string>("SELECT row_to_json(c)::text FROM identidade.calibracao_linkage c WHERE modelo_id=@id;",("id",id));
        Assert.That(metadata,Does.Not.Contain("Pessoa Teste"));
        Assert.That(metadata,Does.Not.Contain("100000001"));
    }
    private Task<int> RefreshFingerprintAsync(Guid id) => ExecuteAsync("""
        UPDATE identidade.calibracao_linkage SET parametros_sha256=(
            SELECT encode(sha256(convert_to(string_agg(nome||'='||valor::text,E'\n' ORDER BY nome COLLATE "C")||E'\n','UTF8')),'hex')
            FROM identidade.parametro_linkage WHERE modelo_id=@id)
        WHERE modelo_id=@id;
        """,("id",id));
    private Task<string> ModelStatusAsync(Guid id) => ScalarAsync<string>("SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id;",("id",id));
    private Task<long> ActiveCountAsync() => ScalarAsync<long>("SELECT count(*) FROM identidade.modelo_linkage WHERE status='ATIVO';");
    private async Task<string> SourceCountsAsync() => string.Join("/",await ScalarAsync<long>("SELECT count(*) FROM identidade.pessoa;"),
        await ScalarAsync<long>("SELECT count(*) FROM gold.pessoa;"),await ScalarAsync<long>("SELECT count(*) FROM identidade.vinculo_fonte;"),
        await ScalarAsync<long>("SELECT count(*) FROM silver.pessoa_observacao;"));

    private async Task<T> ScalarAsync<T>(string sql,params (string Name,object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection,sql,parameters);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Consulta sem resultado."));
    }
    private async Task<int> ExecuteAsync(string sql,params (string Name,object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection,sql,parameters);
        return await command.ExecuteNonQueryAsync();
    }
    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection,string sql,(string Name,object Value)[] parameters)
    {
        var command=new NpgsqlCommand(sql,connection);
        foreach(var (name,value) in parameters) command.Parameters.AddWithValue(name,value);
        return command;
    }
    private Task AssertDbRejectedAsync(string sql,params (string Name,object Value)[] parameters)
    {
        Assert.ThrowsAsync<PostgresException>(async()=>{await ExecuteAsync(sql,parameters);});
        return Task.CompletedTask;
    }
}

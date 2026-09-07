using System.Globalization;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Npgsql;

namespace Jornada.Tests.Integration;

[TestFixture, Category("Integration"), Category("PostgreSqlCalibrationSafety"), NonParallelizable]
public sealed class PostgreSqlCalibrationSafetyTests
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
        Assert.That(Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM gold.pessoa;"),CultureInfo.InvariantCulture),Is.EqualTo(10));
        calibrator = new PostgreSqlLinkageCalibrator(new PostgreSqlOperationalAdapter(connectionString));
    }

    [Test]
    public async Task ExactBirthDraft_RejectsRealValidationAndDirectActivation()
    {
        var before = await SourceCountsAsync();
        var draft = await calibrator.GenerateDraftAsync(Options,CancellationToken.None);
        var id = draft.ModelId;
        await ExecuteAsync("UPDATE identidade.calibracao_linkage SET sintetico=FALSE WHERE modelo_id=@id;",("id",id));
        await ExecuteAsync("UPDATE identidade.modelo_linkage SET base_referencia='gold.pessoa' WHERE modelo_id=@id;",("id",id));
        Assert.ThrowsAsync<PostgresException>(async()=>{await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None);});
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("RASCUNHO"));
        Assert.ThrowsAsync<PostgresException>(async()=>{await ExecuteAsync("UPDATE identidade.modelo_linkage SET status='VALIDADO' WHERE modelo_id=@id;",("id",id));});
        Assert.ThrowsAsync<PostgresException>(async()=>{await ExecuteAsync("UPDATE identidade.modelo_linkage SET status='ATIVO' WHERE modelo_id=@id;",("id",id));});
        Assert.ThrowsAsync<PostgresException>(async()=>{await ExecuteAsync("UPDATE identidade.modelo_linkage SET amostra_metodo='OUTRO_METODO' WHERE modelo_id=@id;",("id",id));});
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("RASCUNHO"));

        // A validação estrutural sintética permanece possível, mas não a ativação.
        await ExecuteAsync("UPDATE identidade.calibracao_linkage SET sintetico=TRUE WHERE modelo_id=@id;",("id",id));
        await ExecuteAsync("UPDATE identidade.modelo_linkage SET base_referencia='CI_LINKAGE_SYNTHETIC' WHERE modelo_id=@id;",("id",id));
        Assert.That(await calibrator.ValidateDraftAsync(draft.Version,CancellationToken.None),Is.EqualTo(id));
        Assert.ThrowsAsync<PostgresException>(async()=>{await ExecuteAsync("UPDATE identidade.modelo_linkage SET status='ATIVO' WHERE modelo_id=@id;",("id",id));});
        Assert.That(await ModelStatusAsync(id),Is.EqualTo("VALIDADO"));
        Assert.That(await SourceCountsAsync(),Is.EqualTo(before));
        Assert.That(Convert.ToInt64(await ScalarAsync("SELECT count(*) FROM identidade.modelo_linkage WHERE status='ATIVO';"),CultureInfo.InvariantCulture),Is.Zero);
    }

    private async Task<string> SourceCountsAsync() => string.Join("/",
        await ScalarAsync("SELECT count(*) FROM identidade.pessoa;"),
        await ScalarAsync("SELECT count(*) FROM gold.pessoa;"),
        await ScalarAsync("SELECT count(*) FROM identidade.vinculo_fonte;"),
        await ScalarAsync("SELECT count(*) FROM silver.pessoa_observacao;"));
    private async Task<string> ModelStatusAsync(Guid id) => (string)(await ScalarAsync(
        "SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id;",("id",id)));
    private async Task<object> ScalarAsync(string sql,params (string Name,object Value)[] parameters)
    {
        await using var connection=new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command=CreateCommand(connection,sql,parameters);
        return await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Consulta sem resultado.");
    }
    private async Task<int> ExecuteAsync(string sql,params (string Name,object Value)[] parameters)
    {
        await using var connection=new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command=CreateCommand(connection,sql,parameters);
        return await command.ExecuteNonQueryAsync();
    }
    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection,string sql,(string Name,object Value)[] parameters)
    {
        var command=new NpgsqlCommand(sql,connection);
        foreach(var (name,value) in parameters) command.Parameters.AddWithValue(name,value);
        return command;
    }
}

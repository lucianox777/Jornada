using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_PROJECTION_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_PROJECTION_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var databaseName = pg ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (databaseName != "JornadaProjectionScopeTest")
    throw new InvalidOperationException("Smoke exige banco descartável JornadaProjectionScopeTest.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Add(DbCommand command, string name, DbType type, object? value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.DbType = type;
    parameter.Value = value ?? DBNull.Value;
    command.Parameters.Add(parameter);
}

async Task<object?> ScalarAsync(
    DbConnection connection,
    DbTransaction? transaction,
    string sql,
    params (string Name, DbType Type, object? Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach (var parameter in parameters) Add(command, parameter.Name, parameter.Type, parameter.Value);
    return await command.ExecuteScalarAsync();
}

var run = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
var code = "PROJ" + run;
await using var setup = await database.OpenAsync();
var gestorTail = pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var gestor = Convert.ToInt64(await ScalarAsync(
    setup, null,
    "INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)" + gestorTail,
    ("@code", DbType.String, code), ("@name", DbType.String, "Projection scope smoke")),
    System.Globalization.CultureInfo.InvariantCulture);
var systemTail = pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var system = Convert.ToInt64(await ScalarAsync(
    setup, null,
    "INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)" + systemTail,
    ("@gestor", DbType.Int64, gestor), ("@code", DbType.String, code),
    ("@name", DbType.String, "Projection scope source")),
    System.Globalization.CultureInfo.InvariantCulture);

async Task<long> AddSourceAsync(string suffix)
{
    await using var connection = await database.OpenAsync();
    var tail = pg ? " RETURNING pessoa_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    return Convert.ToInt64(await ScalarAsync(
        connection, null,
        "INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@system,@code)" + tail,
        ("@system", DbType.Int64, system), ("@code", DbType.String, suffix)),
        System.Globalization.CultureInfo.InvariantCulture);
}

async Task<Guid> EnsureAsync(long sourceId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var sql = pg
        ? "SELECT identidade.assegurar_origem_progressiva(@source);"
        : "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
    var value = await ScalarAsync(connection, transaction, sql, ("@source", DbType.Int64, sourceId));
    await transaction.CommitAsync();
    return value is Guid uuid && uuid != Guid.Empty
        ? uuid
        : throw new InvalidOperationException("Criação progressiva não devolveu UUID.");
}

async Task PublishAsync(long sourceId, Guid target)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var sql = pg
        ? "SELECT identidade.publicar_referencia_progressiva_deterministica(@source,@target,@evidence,@policy);"
        : "DECLARE @version bigint; EXEC identidade.sp_publicar_referencia_progressiva_deterministica @pessoa_origem_id=@source,@canonical_uuid=@target,@evidencia_referencia=@evidence,@politica_versao=@policy,@versao_resultado=@version OUTPUT; SELECT @version;";
    _ = await ScalarAsync(connection, transaction, sql,
        ("@source", DbType.Int64, sourceId), ("@target", DbType.Guid, target),
        ("@evidence", DbType.String, "evidence:projection-scope-smoke"),
        ("@policy", DbType.String, "PROJECTION_SCOPE_SMOKE_V1"));
    await transaction.CommitAsync();
}

var sourceA = await AddSourceAsync("A-" + run);
var sourceB = await AddSourceAsync("B-" + run);
var a = await EnsureAsync(sourceA);
var b = await EnsureAsync(sourceB);
await PublishAsync(sourceA, a);
await PublishAsync(sourceB, b);

var decision = new IdentityCompositionDecision(
    Guid.NewGuid(),
    IdentityCompositionOperation.FUSAO,
    ImmutableArray.Create(
        new IdentityCompositionAssignment(a, 1, a, ProgressiveIdentityStatus.REFERENCIA),
        new IdentityCompositionAssignment(b, 1, a, ProgressiveIdentityStatus.REFERENCIA)),
    "evidence:projection-scope-smoke",
    "PROJECTION_SCOPE_SMOKE_V1",
    DateTimeOffset.UtcNow);
var readSet = new IdentityCompositionReadSet(
    ImmutableArray.Create(
        new IdentityCompositionMember(a, a, ProgressiveIdentityStatus.REFERENCIA, 1, null),
        new IdentityCompositionMember(b, b, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
    ImmutableArray<Guid>.Empty,
    ImmutableArray<IdentityCompositionHistory>.Empty);
var plan = IdentityCompositionPlanner.Prepare(readSet, decision);
Check(plan.Changes.Length == 1 && plan.Changes[0].InitialUuid == b,
    "Fixture deveria alterar somente a segunda origem.");

var scopeReader = new IdentityCompositionProjectionScopeReader(database);
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var scopes = await scopeReader.LoadAsync(connection, transaction, plan);
    Check(scopes.Length == 1, "Fechamento factual retornou quantidade inesperada de origens.");
    Check(scopes[0].InitialUuid == b && scopes[0].PessoaOrigemId == sourceB && scopes[0].IsComplete,
        "Fechamento factual não preservou a origem autoritativa.");
    Check(scopes[0].RegistroObservacaoIds.IsEmpty,
        "Origem sintética sem fatos recebeu registros inexistentes.");
    var recomposition = IdentityCompositionRecompositionPlanner.Prepare(plan, scopes);
    Check(recomposition.AffectedInitialUuids.SequenceEqual(new[] { b }) &&
          recomposition.PessoaOrigemIds.SequenceEqual(new[] { sourceB }) &&
          recomposition.RegistroObservacaoIds.IsEmpty && !recomposition.RequiresFactualRevalidation,
        "Plano de recomposição não corresponde ao escopo autoritativo sem fatos.");
    await transaction.RollbackAsync();
}

Console.WriteLine("IDENTITY PROJECTION SCOPE: OK (authoritative source closure; no factual or Gold/Serving writes)");

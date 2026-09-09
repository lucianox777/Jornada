using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_ELIGIBILITY_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_ELIGIBILITY_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var expectedDatabase = "JornadaPublicationEligibilityTest";
var databaseName = pg
    ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (!string.Equals(databaseName, expectedDatabase, StringComparison.Ordinal))
    throw new InvalidOperationException($"Smoke exige banco descartável {expectedDatabase}.");

static void Add(DbCommand command, string name, DbType type, object? value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.DbType = type;
    parameter.Value = value ?? DBNull.Value;
    command.Parameters.Add(parameter);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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

async Task<(long SourceId, long ObservationId, long RecordId, Guid FactualUuid)> CandidateAsync()
{
    await using var connection = await database.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = pg
        ? """
          SELECT po.pessoa_origem_id,po.pessoa_observacao_id,ro.registro_observacao_id,vf.pessoa_uuid
          FROM silver.registro_observacao ro
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
          JOIN identidade.vinculo_fonte vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
          WHERE vf.ativo AND vf.status='RESOLVIDO' AND vf.pessoa_uuid IS NOT NULL
          ORDER BY ro.registro_observacao_id
          LIMIT 1;
          """
        : """
          SELECT TOP(1) po.pessoa_origem_id,po.pessoa_observacao_id,ro.registro_observacao_id,vf.pessoa_uuid
          FROM silver.registro_observacao ro
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
          JOIN identidade.vinculo_fonte vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
          WHERE vf.ativo=1 AND vf.status='RESOLVIDO' AND vf.pessoa_uuid IS NOT NULL
          ORDER BY ro.registro_observacao_id;
          """;
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        throw new InvalidOperationException("Fixture não contém fato com vínculo factual RESOLVIDO.");
    var candidate = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3));
    Check(candidate.Item1 > 0 && candidate.Item2 > 0 && candidate.Item3 > 0 && candidate.Item4 != Guid.Empty,
        "Fixture factual inválida.");
    return candidate;
}

async Task<Guid> EnsureProgressiveAsync(long sourceId)
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
        : throw new InvalidOperationException("Origem progressiva não devolveu initial_uuid.");
}

var candidate = await CandidateAsync();
var initialUuid = await EnsureProgressiveAsync(candidate.SourceId);
var structuralTarget = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
if (structuralTarget == candidate.FactualUuid)
    structuralTarget = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");

var recomposition = new IdentityCompositionRecompositionPlan(
    Guid.Parse("91000000-0000-4000-8000-000000000001"),
    new string('a', 64),
    ImmutableArray.Create(initialUuid),
    ImmutableArray.Create(structuralTarget),
    ImmutableArray.Create(candidate.SourceId),
    ImmutableArray.Create(candidate.RecordId),
    RequiresFactualRevalidation: true);

var authorityReader = new IdentityCompositionFactualAuthorityReader(database);
await using var readConnection = await database.OpenAsync();
await using var readTransaction = await readConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
var snapshots = await authorityReader.LoadAsync(readConnection, readTransaction, recomposition);
Check(snapshots.Length == 1, "Leitor factual não fechou exatamente um registro.");
var snapshot = snapshots[0];
Check(snapshot.InitialUuid == initialUuid && snapshot.PessoaOrigemId == candidate.SourceId &&
      snapshot.PessoaObservacaoId == candidate.ObservationId && snapshot.RegistroObservacaoId == candidate.RecordId,
    "Proveniência do snapshot factual divergiu do escopo autoritativo.");
Check(snapshot.AuthoritativePessoaUuid == candidate.FactualUuid && snapshot.LinkStatus == "RESOLVIDO",
    "Leitor não preservou a autoridade factual corrente.");

var validation = IdentityCompositionFactualRevalidationPlanner.Prepare(
    recomposition, new string('b', 64), snapshots);
Check(validation.IsPublishable && validation.Expectations.Length == 1,
    "Revalidação factual deveria ser elegível para o vínculo RESOLVIDO da fixture.");
var expectation = validation.Expectations[0];
Check(expectation.PessoaUuid == candidate.FactualUuid && expectation.AssignmentState == "ATRIBUIDA",
    "Expectativa factual não corresponde ao vínculo autoritativo.");
Check(expectation.PessoaUuid != structuralTarget,
    "Destino estrutural foi indevidamente usado como atribuição factual.");

var beforeGold = Convert.ToInt64(await ScalarAsync(
    readConnection, readTransaction,
    "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid;",
    ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralTarget)),
    System.Globalization.CultureInfo.InvariantCulture);
var beforeServing = Convert.ToInt64(await ScalarAsync(
    readConnection, readTransaction,
    "SELECT COUNT(*) FROM serving.registro_integrado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid;",
    ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralTarget)),
    System.Globalization.CultureInfo.InvariantCulture);
Check(beforeGold == 0 && beforeServing == 0,
    "Fixture contém publicação inesperada para o destino estrutural sintético.");
await readTransaction.RollbackAsync();

Console.WriteLine("IDENTITY PUBLICATION ELIGIBILITY RUNTIME: OK (active factual authority; structural target ignored; no publication)");

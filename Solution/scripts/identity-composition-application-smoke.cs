using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_APPLICATION_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_APPLICATION_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var databaseName = pg ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (databaseName != "JornadaCompositionApplicationTest")
    throw new InvalidOperationException("Smoke exige banco descartável JornadaCompositionApplicationTest.");

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

async Task<long> CountAsync(string sql, params (string Name, DbType Type, object? Value)[] parameters)
{
    await using var connection = await database.OpenAsync();
    return Convert.ToInt64(await ScalarAsync(connection, null, sql, parameters),
        System.Globalization.CultureInfo.InvariantCulture);
}

var run = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
var code = "APP" + run;
await using var setupConnection = await database.OpenAsync();
var gestorTail = pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var gestor = Convert.ToInt64(await ScalarAsync(
    setupConnection, null,
    "INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)" + gestorTail,
    ("@code", DbType.String, code), ("@name", DbType.String, "Synthetic application test")),
    System.Globalization.CultureInfo.InvariantCulture);
var systemTail = pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var system = Convert.ToInt64(await ScalarAsync(
    setupConnection, null,
    "INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)" + systemTail,
    ("@gestor", DbType.Int64, gestor), ("@code", DbType.String, code),
    ("@name", DbType.String, "Synthetic application source")),
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
    await ScalarAsync(connection, transaction, sql,
        ("@source", DbType.Int64, sourceId), ("@target", DbType.Guid, target),
        ("@evidence", DbType.String, "evidence:synthetic-application"),
        ("@policy", DbType.String, "APPLICATION_SMOKE_V1"));
    await transaction.CommitAsync();
}

static IdentityCompositionDecision MergeDecision(Guid decisionId, Guid first, Guid second) =>
    new(decisionId, IdentityCompositionOperation.FUSAO,
        ImmutableArray.Create(
            new IdentityCompositionAssignment(first, 1, first, ProgressiveIdentityStatus.REFERENCIA),
            new IdentityCompositionAssignment(second, 1, first, ProgressiveIdentityStatus.REFERENCIA)),
        "evidence:synthetic-application", "APPLICATION_SMOKE_V1", DateTimeOffset.UtcNow);

static IdentityCompositionReadSet MergeRead(Guid first, Guid second) =>
    new(ImmutableArray.Create(
            new IdentityCompositionMember(first, first, ProgressiveIdentityStatus.REFERENCIA, 1, null),
            new IdentityCompositionMember(second, second, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
        ImmutableArray<Guid>.Empty,
        ImmutableArray<IdentityCompositionHistory>.Empty);

var ledger = new IdentityCompositionLedgerStore(database);
var cpfAuthority = new IdentityCompositionCpfAuthorityReader(database);
var authoritative = new IdentityCompositionAuthoritativeReader(database, cpfAuthority);
var preApplication = new IdentityCompositionPreApplicationService(ledger, authoritative);
var applicationStore = new IdentityCompositionApplicationStore(database);
var application = new IdentityCompositionApplicationService(ledger, preApplication, applicationStore);

async Task<IdentityCompositionPlan> PrepareAsync(Guid decisionId, Guid first, Guid second)
{
    var decision = MergeDecision(decisionId, first, second);
    var plan = IdentityCompositionPlanner.Prepare(MergeRead(first, second), decision);
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ledger.RegisterPreparedAsync(connection, transaction, decision, plan, Array.Empty<Guid>(),
        "synthetic:application-smoke", Guid.NewGuid());
    await transaction.CommitAsync();
    return plan;
}

async Task<IdentityCompositionApplicationResult> ApplyCommitAsync(Guid decisionId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var result = await application.ApplyAsync(connection, transaction, decisionId, "synthetic:application-smoke");
    await transaction.CommitAsync();
    return result;
}

async Task<(Guid? Canonical, string State, long Version)> ReadOriginAsync(Guid initial)
{
    await using var connection = await database.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT canonical_uuid,estado,versao FROM identidade.pessoa_origem_progressiva WHERE initial_uuid=@initial;";
    Add(command, "@initial", DbType.Guid, initial);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("Origem progressiva não encontrada.");
    return (reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2));
}

async Task MakeIndefiniteAsync(long sourceId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var now = DateTimeOffset.UtcNow;
    var insert = pg
        ? "INSERT INTO identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,2,'RESOLUCAO','INDEFINIDA',NULL,1,'INDEFINIDA',NULL,@evidence,@policy,NULL,NULL,TRUE,@now);"
        : "INSERT identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,2,'RESOLUCAO','INDEFINIDA',NULL,1,'INDEFINIDA',NULL,@evidence,@policy,NULL,NULL,1,@now);";
    await ScalarAsync(connection, transaction, insert + " SELECT 1;",
        ("@event", DbType.Guid, Guid.NewGuid()), ("@source", DbType.Int64, sourceId),
        ("@evidence", DbType.String, "evidence:synthetic-stale-application"),
        ("@policy", DbType.String, "APPLICATION_SMOKE_V1"), ("@now", DbType.DateTimeOffset, now));
    var update = pg
        ? "UPDATE identidade.pessoa_origem_progressiva SET canonical_uuid=NULL,estado='INDEFINIDA',versao=2,ultima_resolucao_em=@now,ultimo_destino_externo_uuid=NULL,atualizado_em=@now WHERE pessoa_origem_id=@source RETURNING versao;"
        : "UPDATE identidade.pessoa_origem_progressiva SET canonical_uuid=NULL,estado='INDEFINIDA',versao=2,ultima_resolucao_em=@now,ultimo_destino_externo_uuid=NULL,atualizado_em=@now WHERE pessoa_origem_id=@source; SELECT 2;";
    await ScalarAsync(connection, transaction, update,
        ("@now", DbType.DateTimeOffset, now), ("@source", DbType.Int64, sourceId));
    await transaction.CommitAsync();
}

var linkBefore = await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte");
var anchorBefore = await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora");
var goldBefore = await CountAsync("SELECT COUNT(*) FROM gold.pessoa");

// 1. Aplicação efetiva e replay idempotente.
var sourceA = await AddSourceAsync("APP-A");
var sourceB = await AddSourceAsync("APP-B");
var a = await EnsureAsync(sourceA);
var b = await EnsureAsync(sourceB);
await PublishAsync(sourceA, a);
await PublishAsync(sourceB, b);
var decision = Guid.NewGuid();
var plan = await PrepareAsync(decision, a, b);
var eventsBeforeApply = await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
var first = await ApplyCommitAsync(decision);
Check(!first.Replay && first.Receipt.AppliedChanges == plan.Changes.Length &&
      first.Receipt.RegisteredHistories == plan.HistoryToAppend.Length,
    "Primeira aplicação não produziu recibo esperado.");
var stateB = await ReadOriginAsync(b);
Check(stateB.Canonical == a && stateB.State == "REFERENCIA" && stateB.Version == 2,
    "Fusão não atualizou a origem alterada para a referência esperada.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_aplicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, decision)) == 1, "Recibo APLICADA ausente.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_historico_aplicado WHERE decision_id=@id",
        ("@id", DbType.Guid, decision)) == plan.HistoryToAppend.Length, "Histórico aplicado incompleto.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento") == eventsBeforeApply + plan.Changes.Length,
    "Aplicação criou quantidade inesperada de eventos progressivos.");

var eventsBeforeReplay = await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
var historiesBeforeReplay = await CountAsync("SELECT COUNT(*) FROM identidade.composicao_historico_aplicado");
var replay = await ApplyCommitAsync(decision);
Check(replay.Replay && replay.Receipt.DecisionId == decision, "Replay não devolveu recibo existente.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento") == eventsBeforeReplay &&
      await CountAsync("SELECT COUNT(*) FROM identidade.composicao_historico_aplicado") == historiesBeforeReplay,
    "Replay repetiu efeitos persistentes.");

// 2. Rollback remove estado, histórico e recibo; retry posterior continua possível.
var sourceC = await AddSourceAsync("ROLL-C");
var sourceD = await AddSourceAsync("ROLL-D");
var c = await EnsureAsync(sourceC);
var d = await EnsureAsync(sourceD);
await PublishAsync(sourceC, c);
await PublishAsync(sourceD, d);
var rollbackDecision = Guid.NewGuid();
await PrepareAsync(rollbackDecision, c, d);
var eventsBeforeRollback = await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var tentative = await application.ApplyAsync(connection, transaction, rollbackDecision, "synthetic:rollback");
    Check(!tentative.Replay, "Primeira tentativa de rollback foi tratada como replay.");
    await transaction.RollbackAsync();
}
var stateD = await ReadOriginAsync(d);
Check(stateD.Canonical == d && stateD.State == "REFERENCIA" && stateD.Version == 1,
    "Rollback deixou alteração progressiva parcial.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_aplicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, rollbackDecision)) == 0 &&
      await CountAsync("SELECT COUNT(*) FROM identidade.composicao_historico_aplicado WHERE decision_id=@id",
        ("@id", DbType.Guid, rollbackDecision)) == 0 &&
      await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento") == eventsBeforeRollback,
    "Rollback deixou recibo, histórico ou evento parcial.");
var retry = await ApplyCommitAsync(rollbackDecision);
Check(!retry.Replay, "Retry após rollback deveria efetivar a decisão.");

// 3. Estado autoritativo obsoleto falha fechado antes de qualquer recibo APLICADA.
var sourceE = await AddSourceAsync("STALE-E");
var sourceF = await AddSourceAsync("STALE-F");
var e = await EnsureAsync(sourceE);
var f = await EnsureAsync(sourceF);
await PublishAsync(sourceE, e);
await PublishAsync(sourceF, f);
var staleDecision = Guid.NewGuid();
await PrepareAsync(staleDecision, e, f);
await MakeIndefiniteAsync(sourceF);
try
{
    await ApplyCommitAsync(staleDecision);
    throw new InvalidOperationException("Aplicação aceitou decisão obsoleta.");
}
catch (InvalidOperationException ex) when (ex.Message != "Aplicação aceitou decisão obsoleta.")
{
    // esperado: pre-application/replanning falha fechado
}
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_aplicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, staleDecision)) == 0,
    "Decisão obsoleta recebeu recibo APLICADA.");

// 4. Duas aplicações concorrentes do mesmo decision_id convergem para um único efeito.
var sourceG = await AddSourceAsync("CON-G");
var sourceH = await AddSourceAsync("CON-H");
var g = await EnsureAsync(sourceG);
var h = await EnsureAsync(sourceH);
await PublishAsync(sourceG, g);
await PublishAsync(sourceH, h);
var concurrentDecision = Guid.NewGuid();
await PrepareAsync(concurrentDecision, g, h);
async Task<IdentityCompositionApplicationResult> ConcurrentApplyAsync()
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var result = await application.ApplyAsync(connection, transaction, concurrentDecision, "synthetic:concurrent");
    await transaction.CommitAsync();
    return result;
}
var concurrent = await Task.WhenAll(ConcurrentApplyAsync(), ConcurrentApplyAsync());
Check(concurrent.Count(x => x.Replay) == 1 && concurrent.Count(x => !x.Replay) == 1,
    "Aplicação concorrente não convergiu para primeira aplicação + replay.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_aplicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, concurrentDecision)) == 1,
    "Concorrência criou mais de um recibo APLICADA.");

Check(await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte") == linkBefore,
    "Aplicação progressiva alterou vínculos factuais nesta fatia.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora") == anchorBefore,
    "Aplicação progressiva alterou âncora CPF.");
Check(await CountAsync("SELECT COUNT(*) FROM gold.pessoa") == goldBefore,
    "Aplicação progressiva publicou/recompôs Gold indevidamente.");

Console.WriteLine("IDENTITY COMPOSITION APPLICATION: OK (apply, replay, rollback, stale rejection, concurrency; no Gold publication)");

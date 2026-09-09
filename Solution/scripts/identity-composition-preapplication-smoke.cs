using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_PREAPPLICATION_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_PREAPPLICATION_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var databaseName = pg ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (databaseName != "JornadaCompositionPreApplicationTest")
    throw new InvalidOperationException("Smoke exige banco descartável JornadaCompositionPreApplicationTest.");

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

async Task<long> CountAsync(string sql)
{
    await using var connection = await database.OpenAsync();
    return Convert.ToInt64(
        await ScalarAsync(connection, null, sql),
        System.Globalization.CultureInfo.InvariantCulture);
}

var run = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
var code = "PAC" + run;
await using var setupConnection = await database.OpenAsync();
var gestorTail = pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var gestor = Convert.ToInt64(await ScalarAsync(
    setupConnection, null,
    "INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)" + gestorTail,
    ("@code", DbType.String, code), ("@name", DbType.String, "Synthetic pre-application test")),
    System.Globalization.CultureInfo.InvariantCulture);
var systemTail = pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var system = Convert.ToInt64(await ScalarAsync(
    setupConnection, null,
    "INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)" + systemTail,
    ("@gestor", DbType.Int64, gestor), ("@code", DbType.String, code),
    ("@name", DbType.String, "Synthetic pre-application source")),
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
        ("@evidence", DbType.String, "evidence:synthetic-preapplication"),
        ("@policy", DbType.String, "PREAPPLICATION_SMOKE_V1"));
    await transaction.CommitAsync();
}

static IdentityCompositionDecision MergeDecision(Guid decisionId, Guid first, Guid second) =>
    new(decisionId, IdentityCompositionOperation.FUSAO,
        ImmutableArray.Create(
            new IdentityCompositionAssignment(first, 1, first, ProgressiveIdentityStatus.REFERENCIA),
            new IdentityCompositionAssignment(second, 1, first, ProgressiveIdentityStatus.REFERENCIA)),
        "evidence:synthetic-preapplication", "PREAPPLICATION_SMOKE_V1", DateTimeOffset.UtcNow);

static IdentityCompositionReadSet MergeRead(Guid first, Guid second) =>
    new(ImmutableArray.Create(
            new IdentityCompositionMember(first, first, ProgressiveIdentityStatus.REFERENCIA, 1, null),
            new IdentityCompositionMember(second, second, ProgressiveIdentityStatus.REFERENCIA, 1, null)),
        ImmutableArray<Guid>.Empty,
        ImmutableArray<IdentityCompositionHistory>.Empty);

var ledger = new IdentityCompositionLedgerStore(database);
var cpfAuthority = new IdentityCompositionCpfAuthorityReader(database);
var authoritative = new IdentityCompositionAuthoritativeReader(database, cpfAuthority);
var service = new IdentityCompositionPreApplicationService(ledger, authoritative);

async Task<IdentityCompositionPlan> PrepareAndRegisterAsync(Guid decisionId, Guid first, Guid second)
{
    var decision = MergeDecision(decisionId, first, second);
    var plan = IdentityCompositionPlanner.Prepare(MergeRead(first, second), decision);
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await ledger.RegisterPreparedAsync(
        connection, transaction, decision, plan, Array.Empty<Guid>(),
        "synthetic:preapplication-smoke", Guid.NewGuid());
    await transaction.CommitAsync();
    return plan;
}

async Task ValidateAsync(Guid decisionId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await service.ValidateAsync(connection, transaction, decisionId);
    await transaction.RollbackAsync();
}

async Task ExpectValidationFailureAsync(Guid decisionId)
{
    try
    {
        await ValidateAsync(decisionId);
    }
    catch (InvalidOperationException)
    {
        return;
    }
    throw new InvalidOperationException("Pré-aplicação aceitou estado autoritativo incompatível.");
}

var eventCountBefore = await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
var linkCountBefore = await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte");
var anchorCountBefore = await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora");
var goldCountBefore = await CountAsync("SELECT COUNT(*) FROM gold.pessoa");

// Cenário 1: estado intacto reproduz exatamente o PREPARADA sem qualquer publicação.
var sourceA = await AddSourceAsync("PRE-A");
var sourceB = await AddSourceAsync("PRE-B");
var a = await EnsureAsync(sourceA);
var b = await EnsureAsync(sourceB);
await PublishAsync(sourceA, a);
await PublishAsync(sourceB, b);
var decisionValid = Guid.NewGuid();
var preparedValid = await PrepareAndRegisterAsync(decisionValid, a, b);
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var validation = await service.ValidateAsync(connection, transaction, decisionValid);
    Check(IdentityCompositionCanonical.SerializePlan(validation.Replanned) ==
          IdentityCompositionCanonical.SerializePlan(preparedValid),
        "Replanning autoritativo divergiu do PREPARADA intacto.");
    await transaction.RollbackAsync();
}

// Cenário 2: uma resolução válida posterior avança a versão e torna a decisão obsoleta.
var sourceC = await AddSourceAsync("STALE-C");
var sourceD = await AddSourceAsync("STALE-D");
var c = await EnsureAsync(sourceC);
var d = await EnsureAsync(sourceD);
await PublishAsync(sourceC, c);
await PublishAsync(sourceD, d);
var decisionStale = Guid.NewGuid();
await PrepareAndRegisterAsync(decisionStale, c, d);
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var now = DateTimeOffset.UtcNow;
    if (pg)
    {
        await ScalarAsync(connection, transaction,
            "INSERT INTO identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,2,'RESOLUCAO','INDEFINIDA',NULL,1,'INDEFINIDA',NULL,@evidence,@policy,NULL,NULL,TRUE,@now); SELECT 1;",
            ("@event", DbType.Guid, Guid.NewGuid()), ("@source", DbType.Int64, sourceD),
            ("@evidence", DbType.String, "evidence:synthetic-stale"),
            ("@policy", DbType.String, "PREAPPLICATION_SMOKE_V1"), ("@now", DbType.DateTimeOffset, now));
        await ScalarAsync(connection, transaction,
            "UPDATE identidade.pessoa_origem_progressiva SET canonical_uuid=NULL,estado='INDEFINIDA',versao=2,ultima_resolucao_em=@now,ultimo_destino_externo_uuid=NULL,atualizado_em=@now WHERE pessoa_origem_id=@source RETURNING versao;",
            ("@now", DbType.DateTimeOffset, now), ("@source", DbType.Int64, sourceD));
    }
    else
    {
        await ScalarAsync(connection, transaction,
            "INSERT identidade.pessoa_origem_progressiva_evento(evento_id,pessoa_origem_id,versao,tipo,estado,canonical_uuid,expected_version,resultado,target_uuid,evidencia_referencia,politica_versao,modelo_versao,universo_referencia,completo,ocorrido_em) VALUES(@event,@source,2,'RESOLUCAO','INDEFINIDA',NULL,1,'INDEFINIDA',NULL,@evidence,@policy,NULL,NULL,1,@now); SELECT 1;",
            ("@event", DbType.Guid, Guid.NewGuid()), ("@source", DbType.Int64, sourceD),
            ("@evidence", DbType.String, "evidence:synthetic-stale"),
            ("@policy", DbType.String, "PREAPPLICATION_SMOKE_V1"), ("@now", DbType.DateTimeOffset, now));
        await ScalarAsync(connection, transaction,
            "UPDATE identidade.pessoa_origem_progressiva SET canonical_uuid=NULL,estado='INDEFINIDA',versao=2,ultima_resolucao_em=@now,ultimo_destino_externo_uuid=NULL,atualizado_em=@now WHERE pessoa_origem_id=@source; SELECT 2;",
            ("@now", DbType.DateTimeOffset, now), ("@source", DbType.Int64, sourceD));
    }
    await transaction.CommitAsync();
}
await ExpectValidationFailureAsync(decisionStale);

// Cenário 3: um terceiro membro passa a pertencer ao destino após a preparação. A expansão real
// encontra o membro e o planejador recusa a partição incompleta.
var sourceE = await AddSourceAsync("CLOSE-E");
var sourceF = await AddSourceAsync("CLOSE-F");
var sourceG = await AddSourceAsync("CLOSE-G");
var e = await EnsureAsync(sourceE);
var f = await EnsureAsync(sourceF);
var g = await EnsureAsync(sourceG);
await PublishAsync(sourceE, e);
await PublishAsync(sourceF, f);
var decisionClosure = Guid.NewGuid();
await PrepareAndRegisterAsync(decisionClosure, e, f);
await PublishAsync(sourceG, e);
await ExpectValidationFailureAsync(decisionClosure);

var eventCountAfter = await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
var linkCountAfter = await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte");
var anchorCountAfter = await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora");
var goldCountAfter = await CountAsync("SELECT COUNT(*) FROM gold.pessoa");

// Os eventos adicionais são somente a montagem deliberada dos três cenários. Validar não cria
// eventos, vínculos, âncoras ou Gold. Os dois primeiros cenários publicam 4 referências; o terceiro
// publica 3, e o cenário stale acrescenta exatamente 1 evento sintético de mudança posterior.
Check(eventCountAfter - eventCountBefore == 15,
    "Quantidade inesperada de eventos: a pré-aplicação pode ter escrito estado progressivo.");
Check(linkCountAfter == linkCountBefore, "Pré-aplicação alterou vínculos factuais.");
Check(anchorCountAfter == anchorCountBefore, "Pré-aplicação alterou âncoras CPF.");
Check(goldCountAfter == goldCountBefore, "Pré-aplicação alterou Gold.");

Console.WriteLine("IDENTITY COMPOSITION PREAPPLICATION: OK (authoritative closure, stale rejection, no publication)");

using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_LEDGER_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_LEDGER_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var databaseName = pg ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (databaseName != "JornadaCompositionTest")
    throw new InvalidOperationException("Smoke exige banco descartável JornadaCompositionTest.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Add(DbCommand command, string name, DbType type, object? value)
{
    var p=command.CreateParameter();p.ParameterName=name;p.DbType=type;p.Value=value ?? DBNull.Value;command.Parameters.Add(p);
}
async Task<object?> ScalarInTransactionAsync(DbConnection connection, DbTransaction? tx, string sql,
    params (string Name, DbType Type, object? Value)[] parameters)
{
    await using var command=connection.CreateCommand();command.Transaction=tx;command.CommandText=sql;
    foreach(var p in parameters) Add(command,p.Name,p.Type,p.Value);
    return await command.ExecuteScalarAsync();
}
async Task<object?> ScalarAsync(string sql, params (string Name,DbType Type,object? Value)[] parameters)
{
    await using var connection=await database.OpenAsync();
    return await ScalarInTransactionAsync(connection,null,sql,parameters);
}
async Task<long> CountAsync(string sql, params (string Name,DbType Type,object? Value)[] parameters) =>
    Convert.ToInt64(await ScalarAsync(sql,parameters),System.Globalization.CultureInfo.InvariantCulture);
async Task ExpectActionFailureAsync(Func<Task> action)
{
    try { await action(); }
    catch(DbException) { return; }
    throw new InvalidOperationException("Operação inválida foi aceita pelo banco.");
}
async Task ExpectFailureAsync(string sql, params (string Name,DbType Type,object? Value)[] parameters) =>
    await ExpectActionFailureAsync(async () => { await ScalarAsync(sql,parameters); });

var run=Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
var code="CMP"+run;
var suffix=pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var gestor=Convert.ToInt64(await ScalarAsync("INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)"+suffix,
    ("@code",DbType.String,code),("@name",DbType.String,"Synthetic composition test")));
suffix=pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var system=Convert.ToInt64(await ScalarAsync("INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)"+suffix,
    ("@gestor",DbType.Int64,gestor),("@code",DbType.String,code),("@name",DbType.String,"Synthetic source")));
async Task<long> AddSourceAsync(string sourceCode)
{
    var tail=pg ? " RETURNING pessoa_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    return Convert.ToInt64(await ScalarAsync("INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@system,@code)"+tail,
        ("@system",DbType.Int64,system),("@code",DbType.String,sourceCode)));
}
async Task<Guid> EnsureAsync(long sourceId)
{
    var sql=pg ? "SELECT identidade.assegurar_origem_progressiva(@source);"
        : "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
    await using var c=await database.OpenAsync();
    await using var tx=await c.BeginTransactionAsync();
    var uuid=(Guid)(await ScalarInTransactionAsync(c,tx,sql,("@source",DbType.Int64,sourceId))
        ?? throw new InvalidOperationException("UUID inicial ausente."));
    await tx.CommitAsync();return uuid;
}
async Task PublishAsync(long sourceId,Guid uuid)
{
    var sql=pg ? "SELECT identidade.publicar_referencia_progressiva_deterministica(@source,@uuid,@evidence,@policy);"
        : "DECLARE @version bigint; EXEC identidade.sp_publicar_referencia_progressiva_deterministica @pessoa_origem_id=@source,@canonical_uuid=@uuid,@evidencia_referencia=@evidence,@politica_versao=@policy,@versao_resultado=@version OUTPUT; SELECT @version;";
    await using var c=await database.OpenAsync();await using var tx=await c.BeginTransactionAsync();
    await ScalarInTransactionAsync(c,tx,sql,("@source",DbType.Int64,sourceId),("@uuid",DbType.Guid,uuid),
        ("@evidence",DbType.String,"evidence:synthetic"),("@policy",DbType.String,"LEDGER_TEST_V1"));
    await tx.CommitAsync();
}
async Task<Guid> ReserveAsync(Guid decision,Guid reservation,DbConnection? connection=null,DbTransaction? tx=null)
{
    var sql=pg ? "SELECT identidade.reservar_uuid_composicao(@decision,@reservation);"
        : "DECLARE @uuid uniqueidentifier; EXEC identidade.sp_reservar_uuid_composicao @decision_id=@decision,@reserva_id=@reservation,@uuid_resultado=@uuid OUTPUT; SELECT @uuid;";
    var parameters=new[]{("@decision",DbType.Guid,(object?)decision),("@reservation",DbType.Guid,(object?)reservation)};
    var result=connection is null ? await ScalarAsync(sql,parameters) : await ScalarInTransactionAsync(connection,tx,sql,parameters);
    return result is Guid uuid && uuid!=Guid.Empty ? uuid : throw new InvalidOperationException("Reserva não devolveu UUID válido.");
}
async Task<string> RegisterAsync(Guid decision,string request,string plan,string reservations,
    DbConnection? connection=null,DbTransaction? tx=null)
{
    var sql=pg ? "SELECT identidade.registrar_plano_composicao(@decision,@request,@plan,@reservations,@actor,@correlation);"
        : "DECLARE @hash char(64); EXEC identidade.sp_registrar_plano_composicao @decision_id=@decision,@request_json=@request,@plan_json=@plan,@reservas_json=@reservations,@solicitante_referencia=@actor,@correlation_id=@correlation,@request_hash=@hash OUTPUT; SELECT @hash;";
    var parameters=new[]{("@decision",DbType.Guid,(object?)decision),("@request",DbType.String,(object?)request),
        ("@plan",DbType.String,(object?)plan),("@reservations",DbType.String,(object?)reservations),
        ("@actor",DbType.String,(object?)"synthetic:ledger-smoke"),("@correlation",DbType.Guid,(object?)Guid.NewGuid())};
    var result=connection is null ? await ScalarAsync(sql,parameters) : await ScalarInTransactionAsync(connection,tx,sql,parameters);
    return result as string ?? throw new InvalidOperationException("Hash de registro ausente.");
}
static string RequestJson(IdentityCompositionDecision d) => JsonSerializer.Serialize(new
{
    d.DecisionId,d.Operation,d.EvidenceReference,d.PolicyVersion,d.DecidedAt,
    Assignments=d.Assignments.OrderBy(a=>a.InitialUuid).ToArray()
});
static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

var sourceA=await AddSourceAsync("A");var sourceB=await AddSourceAsync("B");
var a=await EnsureAsync(sourceA);var b=await EnsureAsync(sourceB);
await PublishAsync(sourceA,a);await PublishAsync(sourceB,b);
var initialCount=await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento");
var goldCount=await CountAsync("SELECT COUNT(*) FROM gold.pessoa");
var linkCount=await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte");
var anchorCount=await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora");
var decisionId=Guid.NewGuid();var reservationId=Guid.NewGuid();
var reserved=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>ReserveAsync(decisionId,reservationId)));
var newUuid=reserved[0];
Check(reserved.All(id=>id==newUuid),"Reserva concorrente não foi idempotente.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_uuid_reserva WHERE reserva_id=@id",("@id",DbType.Guid,reservationId))==1,"Reserva duplicada.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@id",("@id",DbType.Guid,newUuid))==1,"Pessoa reservada ausente ou duplicada.");
await ExpectActionFailureAsync(async()=>{await ReserveAsync(Guid.NewGuid(),reservationId);});

var read=new IdentityCompositionReadSet(
    ImmutableArray.Create(new IdentityCompositionMember(a,a,ProgressiveIdentityStatus.REFERENCIA,1,null),
        new IdentityCompositionMember(b,b,ProgressiveIdentityStatus.REFERENCIA,1,null)),
    ImmutableArray.Create(newUuid),ImmutableArray<IdentityCompositionHistory>.Empty);
var decision=new IdentityCompositionDecision(decisionId,IdentityCompositionOperation.FUSAO,
    ImmutableArray.Create(new IdentityCompositionAssignment(a,1,a,ProgressiveIdentityStatus.REFERENCIA),
        new IdentityCompositionAssignment(b,1,a,ProgressiveIdentityStatus.REFERENCIA)),
    "evidência:sintética/sem-PII","LEDGER_TEST_V1",DateTimeOffset.UtcNow);
var plan=IdentityCompositionPlanner.Prepare(read,decision);
var requestJson=RequestJson(decision);var planJson=JsonSerializer.Serialize(plan);
var reservationsJson=JsonSerializer.Serialize(new[]{newUuid});
Check(Hash(requestJson)==plan.RequestHash,"Serialização canônica diverge do planejador.");
var hashes=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>RegisterAsync(decisionId,requestJson,planJson,reservationsJson)));
Check(hashes.All(h=>h==plan.RequestHash),"Registro concorrente retornou hash divergente.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_plano WHERE decision_id=@id",("@id",DbType.Guid,decisionId))==1,"Registro duplicado.");
Check((await ScalarAsync("SELECT estado FROM identidade.composicao_plano WHERE decision_id=@id",("@id",DbType.Guid,decisionId))) as string=="PREPARADA","Plano foi marcado como aplicado.");
var persisted=await ScalarAsync("SELECT plan_json FROM identidade.composicao_plano WHERE decision_id=@id",("@id",DbType.Guid,decisionId));
Check((persisted as string)==planJson,"Payload do plano não foi preservado exatamente.");
await ExpectActionFailureAsync(async()=>{await RegisterAsync(decisionId,requestJson,planJson+" ",reservationsJson);});
await ExpectActionFailureAsync(async()=>{await RegisterAsync(decisionId,requestJson.Replace("LEDGER_TEST_V1","LEDGER_TEST_V2",StringComparison.Ordinal),planJson,reservationsJson);});
await ExpectActionFailureAsync(async()=>{await RegisterAsync(decisionId,requestJson,planJson,"[]");});
await ExpectActionFailureAsync(async()=>{await ReserveAsync(decisionId,Guid.NewGuid());});
Check(await ReserveAsync(decisionId,reservationId)==newUuid,"Replay de reserva existente deixou de funcionar.");

var rollbackDecision=Guid.NewGuid();var rollbackReservation=Guid.NewGuid();Guid rolledBack;
await using(var c=await database.OpenAsync())
await using(var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    rolledBack=await ReserveAsync(rollbackDecision,rollbackReservation,c,tx);
    var rollbackRequest=decision with {DecisionId=rollbackDecision};
    var rollbackRead=read with {ReservedNewUuids=ImmutableArray.Create(rolledBack)};
    var rollbackPlan=IdentityCompositionPlanner.Prepare(rollbackRead,rollbackRequest);
    var rollbackJson=RequestJson(rollbackRequest);
    await RegisterAsync(rollbackDecision,rollbackJson,JsonSerializer.Serialize(rollbackPlan),JsonSerializer.Serialize(new[]{rolledBack}),c,tx);
    await tx.RollbackAsync();
}
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@id",("@id",DbType.Guid,rolledBack))==0,"Rollback deixou Pessoa órfã.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_uuid_reserva WHERE reserva_id=@id",("@id",DbType.Guid,rollbackReservation))==0,"Rollback deixou reserva.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_plano WHERE decision_id=@id",("@id",DbType.Guid,rollbackDecision))==0,"Rollback deixou plano.");
Check(await ReserveAsync(rollbackDecision,rollbackReservation)!=rolledBack,"Retry reciclou UUID de transação abortada.");

await ExpectFailureAsync("UPDATE identidade.composicao_uuid_reserva SET decision_id=@other WHERE reserva_id=@id",("@other",DbType.Guid,Guid.NewGuid()),("@id",DbType.Guid,reservationId));
await ExpectFailureAsync("DELETE FROM identidade.composicao_uuid_reserva WHERE reserva_id=@id",("@id",DbType.Guid,reservationId));
await ExpectFailureAsync("UPDATE identidade.composicao_plano SET estado='APLICADA' WHERE decision_id=@id",("@id",DbType.Guid,decisionId));
await ExpectFailureAsync("DELETE FROM identidade.composicao_plano WHERE decision_id=@id",("@id",DbType.Guid,decisionId));
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento")==initialCount,"Preparação alterou eventos progressivos.");
Check(await CountAsync("SELECT COUNT(*) FROM gold.pessoa")==goldCount,"Preparação alterou Gold.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte")==linkCount,"Preparação alterou vínculos.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora")==anchorCount,"Preparação alterou âncoras CPF.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva WHERE initial_uuid IN(@a,@b) AND canonical_uuid=initial_uuid AND estado='REFERENCIA' AND versao=1",("@a",DbType.Guid,a),("@b",DbType.Guid,b))==2,"Preparação publicou referência diferente.");
Console.WriteLine("IDENTITY COMPOSITION LEDGER: OK (concurrency, replay, rollback, immutability, conservation)");
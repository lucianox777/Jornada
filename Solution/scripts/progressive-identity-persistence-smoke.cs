using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;

var provider = Environment.GetEnvironmentVariable("JORNADA_PROGRESSIVE_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_PROGRESSIVE_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
var builder = pg ? new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database
    : new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (builder != "JornadaProgressiveTest")
    throw new InvalidOperationException("Smoke exige banco descartável JornadaProgressiveTest.");
var store = new ProgressiveIdentityOriginStore(database);
var run = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
var sourceCode = "PRG" + run;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

async Task<object?> ScalarAsync(string sql, params (string Name, DbType Type, object Value)[] parameters)
{
    await using var c = await database.OpenAsync();
    await using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    foreach (var p in parameters) Add(cmd,p.Name,p.Type,p.Value);
    return await cmd.ExecuteScalarAsync();
}

static void Add(DbCommand cmd, string name, DbType type, object value)
{
    var p = cmd.CreateParameter(); p.ParameterName=name; p.DbType=type; p.Value=value; cmd.Parameters.Add(p);
}

async Task<long> InsertAsync(string sql, params (string Name, DbType Type, object Value)[] parameters)
{
    var value=await ScalarAsync(sql,parameters);
    return Convert.ToInt64(value,System.Globalization.CultureInfo.InvariantCulture);
}

var suffix = pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var gestor = await InsertAsync("INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)"+suffix,
    ("@code",DbType.String,sourceCode),("@name",DbType.String,"Synthetic progressive test"));
suffix=pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
var system = await InsertAsync("INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)"+suffix,
    ("@gestor",DbType.Int64,gestor),("@code",DbType.String,sourceCode),("@name",DbType.String,"Synthetic progressive source"));
async Task<long> AddSourceAsync(string code)
{
    var tail=pg ? " RETURNING pessoa_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    return await InsertAsync("INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@system,@code)"+tail,
        ("@system",DbType.Int64,system),("@code",DbType.String,code));
}
async Task<long> CountAsync(string sql, long source)
{
    return Convert.ToInt64(await ScalarAsync(sql,("@source",DbType.Int64,source)),System.Globalization.CultureInfo.InvariantCulture);
}
async Task ExpectFailureAsync(string sql, params (string Name,DbType Type,object Value)[] parameters)
{
    try { await ScalarAsync(sql,parameters); }
    catch (DbException) { return; }
    throw new InvalidOperationException("Operação inválida foi aceita pelo banco.");
}

var a=await AddSourceAsync("A");
var b=await AddSourceAsync("B");
var rollbackSource=await AddSourceAsync("ROLLBACK");
var results=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>store.EnsureInitialAsync(a)));
var initial=results[0].InitialUuid;
Check(initial!=Guid.Empty && results.All(x=>x.InitialUuid==initial && x.SourceId==a && x.Status==ProgressiveIdentityStatus.PROVISORIA && x.Version==0),"Concorrência criou identidades divergentes.");
Check((await store.EnsureInitialAsync(a)).InitialUuid==initial,"Retransmissão alterou UUID inicial.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source",a)==1,"Origem duplicada.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source",a)==1,"Histórico duplicado.");
var second=await store.EnsureInitialAsync(b);
Check(second.InitialUuid!=initial,"Origens distintas compartilharam UUID inicial.");

Guid rolledBack;
await using(var c=await database.OpenAsync())
await using(var tx=await c.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    rolledBack=(await store.EnsureInitialAsync(c,tx,rollbackSource)).InitialUuid;
    await tx.RollbackAsync();
}
Check(await store.ReadAsync(rollbackSource) is null,"Rollback deixou identidade progressiva.");
Check(Convert.ToInt64(await ScalarAsync("SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@uuid",("@uuid",DbType.Guid,rolledBack)),System.Globalization.CultureInfo.InvariantCulture)==0,"Rollback deixou identidade órfã.");
var retry=await store.EnsureInitialAsync(rollbackSource);
Check(retry.InitialUuid!=rolledBack,"Retry reaproveitou UUID de transação abortada.");
try { await store.EnsureInitialAsync(long.MaxValue); throw new InvalidOperationException("Origem inexistente foi aceita."); }
catch (DbException) { }
Check(await store.ReadAsync(long.MaxValue) is null,"Origem inexistente foi materializada.");

// A referência legada é a atribuição da última observação, não uma resolução nova.
var legacySql="""
SELECT o.pessoa_origem_id,v.pessoa_uuid FROM
 (SELECT pessoa_origem_id,pessoa_observacao_id,ROW_NUMBER() OVER(PARTITION BY pessoa_origem_id ORDER BY versao_interna DESC,pessoa_observacao_id DESC) rn
  FROM silver.pessoa_observacao) o
 JOIN identidade.vinculo_fonte v ON v.pessoa_observacao_id=o.pessoa_observacao_id AND v.ativo=1 AND v.status='RESOLVIDO'
 WHERE o.rn=1
""";
legacySql=pg ? legacySql.Replace("v.ativo=1","v.ativo=TRUE",StringComparison.Ordinal)+" LIMIT 1;" : "SELECT TOP(1) * FROM ("+legacySql+") q;";
long legacySource; Guid legacyUuid;
await using(var c=await database.OpenAsync())
await using(var cmd=c.CreateCommand())
{
    cmd.CommandText=legacySql;
    await using var r=await cmd.ExecuteReaderAsync();
    Check(await r.ReadAsync(),"Fixture legada resolvida ausente.");
    legacySource=r.GetInt64(0);legacyUuid=r.GetGuid(1);
}
var legacy=await store.EnsureInitialAsync(legacySource);
Check(legacy.LegacyCanonicalUuid==legacyUuid && legacy.Status==ProgressiveIdentityStatus.PROVISORIA && legacy.Version==0,"Backfill inventou resolução ou perdeu vínculo legado.");
Check(legacy.InitialUuid!=legacyUuid,"UUID inicial deve ter namespace próprio, sem reciclar referência compartilhada.");
var noCanonical=await ScalarAsync("SELECT canonical_uuid FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source",("@source",DbType.Int64,legacySource));
Check(noCanonical is null or DBNull,"Backfill publicou identidade canônica sem Linkage.");

// Backfill em páginas: inclui fontes legadas e novas; deve terminar sem repetir eventos.
await AddSourceAsync("BACKFILL-1");await AddSourceAsync("BACKFILL-2");
var missingSql="SELECT COUNT(*) FROM silver.pessoa_origem o LEFT JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id WHERE p.pessoa_origem_id IS NULL";
var remaining=Convert.ToInt64(await ScalarAsync(missingSql),System.Globalization.CultureInfo.InvariantCulture);
for(var page=0;remaining>0 && page<100;page++)
{
    var count=await store.BackfillPageAsync(2);
    Check(count==Math.Min(2,remaining),"Backfill não respeitou o limite ou perdeu progresso.");
    var next=Convert.ToInt64(await ScalarAsync(missingSql),System.Globalization.CultureInfo.InvariantCulture);
    Check(next==remaining-count,"Backfill não convergiu.");remaining=next;
}
Check(remaining==0 && await store.BackfillPageAsync(2)==0,"Backfill não é reentrante.");
Check((await store.EnsureInitialAsync(a)).InitialUuid==initial,"Backfill alterou UUID existente.");

var forbiddenUuid=Guid.NewGuid();
await ExpectFailureAsync("UPDATE identidade.pessoa_origem_progressiva SET initial_uuid=@uuid WHERE pessoa_origem_id=@source",("@uuid",DbType.Guid,forbiddenUuid),("@source",DbType.Int64,a));
await ExpectFailureAsync("UPDATE identidade.pessoa_origem_progressiva SET estado='RESOLVIDA',canonical_uuid=@uuid,versao=1,ultima_resolucao_em=CURRENT_TIMESTAMP WHERE pessoa_origem_id=@source",("@uuid",DbType.Guid,initial),("@source",DbType.Int64,a));
await ExpectFailureAsync("DELETE FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source",("@source",DbType.Int64,a));
await ExpectFailureAsync("DELETE FROM identidade.pessoa_origem_progressiva WHERE pessoa_origem_id=@source",("@source",DbType.Int64,a));
Check((await store.EnsureInitialAsync(a)).InitialUuid==initial && await CountAsync("SELECT COUNT(*) FROM identidade.pessoa_origem_progressiva_evento WHERE pessoa_origem_id=@source",a)==1,"Guarda de imutabilidade alterou dados.");
Console.WriteLine("PROGRESSIVE IDENTITY PERSISTENCE: OK (concurrency, retry, rollback, legacy, backfill, immutability)");

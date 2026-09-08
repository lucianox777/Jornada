using System.Data;
using System.Data.Common;
using System.Globalization;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = args.Single();
var database = OperationalDatabaseAdapterFactory.Create(provider,
    Environment.GetEnvironmentVariable(provider == OperationalDatabaseProviders.SqlServer
        ? "JORNADA_TEST_SQL_CONNECTION" : "JORNADA_POSTGRESQL_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória."));
var sqlServer = provider == OperationalDatabaseProviders.SqlServer;
var sequence = 123456700;
string NewCpf()
{
    var digits = Interlocked.Increment(ref sequence).ToString("D9", CultureInfo.InvariantCulture);
    foreach (var length in new[] { 9, 10 })
    {
        var sum = digits.Select((c, i) => (c - '0') * (length + 1 - i)).Sum();
        var remainder = sum % 11;
        digits += (remainder < 2 ? 0 : 11 - remainder).ToString(CultureInfo.InvariantCulture);
    }
    if (CpfRules.NormalizeAndValidate(digits) != digits) throw new InvalidOperationException("Fixture CPF inválida.");
    return digits;
}
async Task<object?> Scalar(DbConnection c, DbTransaction? tx, string sql, params (string Name, object Value)[] values)
{
    await using var cmd = c.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    foreach (var (name, value) in values)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        p.DbType = value is Guid ? DbType.Guid : DbType.String;
        cmd.Parameters.Add(p);
    }
    return await cmd.ExecuteScalarAsync();
}
Task<object?> InsertPerson(DbConnection c, DbTransaction tx, Guid id) => Scalar(c, tx,
    "INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO'); SELECT 1;",
    ("@uuid", id));
async Task<Guid> Reserve(DbConnection c, DbTransaction tx, string cpf, Guid id)
{
    var sql = sqlServer
        ? "DECLARE @r UNIQUEIDENTIFIER; EXEC identidade.sp_reservar_cpf_ancora @cpf,@uuid,@r OUTPUT; SELECT @r;"
        : "SELECT identidade.fn_reservar_cpf_ancora(@cpf,@uuid);";
    return (Guid)(await Scalar(c, tx, sql, ("@cpf", cpf), ("@uuid", id))
        ?? throw new InvalidOperationException("Reserva sem UUID."));
}
async Task<Guid?> Lookup(DbConnection c, DbTransaction? tx, string cpf)
{
    var sql = sqlServer ? "EXEC identidade.sp_obter_cpf_ancora @cpf;"
        : "SELECT identidade.fn_obter_cpf_ancora(@cpf);";
    return await Scalar(c, tx, sql, ("@cpf", cpf)) is Guid id ? id : null;
}
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
bool Expected(DbException ex, int sqlNumber, string pgCode) =>
    sqlServer ? ex is SqlException sql && sql.Number == sqlNumber
        : ex is PostgresException pg && pg.SqlState == pgCode;
async Task Negative(string name, Func<DbConnection, DbTransaction, string, Guid, Guid, Task> action,
    int sqlNumber, string pgCode)
{
    await using var c = await database.OpenAsync();
    await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable);
    var a = Guid.NewGuid(); var b = Guid.NewGuid(); var cpf = NewCpf();
    await InsertPerson(c, tx, a);
    await InsertPerson(c, tx, b);
    Check(await Reserve(c, tx, cpf, a) == a, "Fixture não reservada.");
    var rejected = false;
    try { await action(c, tx, cpf, a, b); }
    catch (DbException ex) when (Expected(ex, sqlNumber, pgCode)) { rejected = true; }
    try { await tx.RollbackAsync(); }
    catch (InvalidOperationException) when (rejected) { }
    catch (DbException) when (rejected) { }
    Check(rejected, name + ": operação indevida foi aceita.");
}
await using (var c = await database.OpenAsync())
await using (var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var a = Guid.NewGuid(); var cpf = NewCpf();
    await InsertPerson(c, tx, a);
    Check(await Reserve(c, tx, cpf, a) == a, "Primeira reserva incorreta.");
    Check(await Reserve(c, tx, cpf, a) == a, "Retransmissão alterou o UUID.");
    Check(await Lookup(c, tx, cpf) == a, "Consulta não devolveu a âncora.");
    Check(Convert.ToInt64(await Scalar(c, tx, "SELECT COUNT(*) FROM identidade.cpf_ancora WHERE pessoa_uuid=@uuid;", ("@uuid", a)), CultureInfo.InvariantCulture) == 1,
        "Cardinalidade da âncora incorreta.");
    await tx.RollbackAsync();
}
await Negative("CPF não pode trocar UUID", async (c,t,cpf,a,b) => { await Reserve(c,t,cpf,b); }, 51353, "P0001");
await Negative("UUID não pode ter outro CPF", async (c,t,cpf,a,b) => { await Reserve(c,t,NewCpf(),a); }, 51354, "23505");
await Negative("Âncora não pode ser atualizada", async (c,t,cpf,a,b) => {
    await Scalar(c,t,"UPDATE identidade.cpf_ancora SET pessoa_uuid=@uuid WHERE cpf=@cpf; SELECT 1;", ("@uuid",b),("@cpf",cpf));
}, 51347, "P0001");
await Negative("Âncora não pode ser apagada", async (c,t,cpf,a,b) => {
    await Scalar(c,t,"DELETE FROM identidade.cpf_ancora WHERE cpf=@cpf; SELECT 1;", ("@cpf",cpf));
}, 51347, "P0001");
await Negative("CPF inválido não pode ser reservado", async (c,t,cpf,a,b) => {
    await Reserve(c,t,"00000000000",b);
}, 51348, "P0001");
await Negative("UUID inexistente não pode ser reservado", async (c,t,cpf,a,b) => {
    await Reserve(c,t,NewCpf(),Guid.NewGuid());
}, 547, "23503");

// Rastreabilidade permanente: a âncora pode existir sem qualquer mapa CPF legado ou fato ativo.
// O UUID não é apagado nem recriado quando a pessoa fica temporariamente "órfã" de observações.
var orphanCpf = NewCpf();
var orphanUuid = Guid.NewGuid();
await using (var c = await database.OpenAsync())
await using (var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable))
{
    await InsertPerson(c, tx, orphanUuid);
    Check(await Reserve(c, tx, orphanCpf, orphanUuid) == orphanUuid, "Âncora órfã não foi reservada.");
    await tx.CommitAsync();
}
await using (var c = await database.OpenAsync())
{
    Check(Convert.ToInt64(await Scalar(c, null,
        "SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf;",
        ("@cpf", orphanCpf)), CultureInfo.InvariantCulture) == 0,
        "Fixture órfã ganhou identity_map inesperado.");
    Check(await Lookup(c, null, orphanCpf) == orphanUuid,
        "CPF órfão deixou de resolver para o UUID permanente.");
    Check(Convert.ToInt64(await Scalar(c, null,
        "SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@uuid;", ("@uuid", orphanUuid)),
        CultureInfo.InvariantCulture) == 1,
        "Pessoa âncora foi removida ao ficar sem registros associados.");
}

var concurrentCpf = NewCpf();
async Task<Guid?> Compete()
{
    await using var c = await database.OpenAsync();
    await using var tx = await c.BeginTransactionAsync(IsolationLevel.Serializable);
    var id = Guid.NewGuid();
    try
    {
        await InsertPerson(c, tx, id);
        await Reserve(c, tx, concurrentCpf, id);
        await tx.CommitAsync();
        return id;
    }
    catch (DbException ex) when (Expected(ex, 51353, "P0001"))
    {
        try { await tx.RollbackAsync(); }
        catch (InvalidOperationException) { }
        catch (DbException) { }
        return null;
    }
}
var winners = await Task.WhenAll(Compete(), Compete());
Check(winners.Count(x => x.HasValue) == 1, "Concorrência não produziu exatamente uma âncora.");
await using (var c = await database.OpenAsync())
{
    Check(await Lookup(c, null, concurrentCpf) == winners.Single(x => x.HasValue), "A âncora mudou após a corrida.");
    Check(Convert.ToInt64(await Scalar(c,null,"SELECT COUNT(*) FROM identidade.cpf_ancora WHERE cpf=@cpf;",("@cpf",concurrentCpf)),CultureInfo.InvariantCulture)==1,
        "CPF duplicado após concorrência.");
}
Console.WriteLine("CPF ANCHOR SMOKE: OK (6 rejeições, idempotência, órfão permanente e concorrência)");

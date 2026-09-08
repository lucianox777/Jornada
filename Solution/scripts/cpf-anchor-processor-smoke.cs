using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;

var provider = Environment.GetEnvironmentVariable("JORNADA_CPF_PROCESSOR_PROVIDER")
    ?? throw new InvalidOperationException("JORNADA_CPF_PROCESSOR_PROVIDER não definido.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_CPF_PROCESSOR_CONNECTION")
    ?? throw new InvalidOperationException("JORNADA_CPF_PROCESSOR_CONNECTION não definido.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var sqlServer = database.Provider == OperationalDatabaseProviders.SqlServer;
var expectedDatabase = "JornadaCpfProcessorTest";
var actualDatabase = sqlServer
    ? new SqlConnectionStringBuilder(connectionString).InitialCatalog
    : new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database;
if (!string.Equals(actualDatabase, expectedDatabase, StringComparison.Ordinal))
    throw new InvalidOperationException($"Smoke exige banco descartável {expectedDatabase}.");

static string MakeCpf(int seed)
{
    var base9 = Math.Abs(seed).ToString("000000000", System.Globalization.CultureInfo.InvariantCulture)[^9..];
    if (base9.Distinct().Count()==1) base9 = "123456789";
    static int Digit(string digits, int startWeight)
    {
        var sum=0;
        for (var i=0;i<digits.Length;i++) sum+=(digits[i]-'0')*(startWeight-i);
        var mod=sum%11;
        return mod<2 ? 0 : 11-mod;
    }
    var d1=Digit(base9,10);
    var ten=base9+d1.ToString(System.Globalization.CultureInfo.InvariantCulture);
    var d2=Digit(ten,11);
    return ten+d2.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

static void Check(bool condition,string message)
{
    if(!condition) throw new InvalidOperationException(message);
}

var core = new IdentityCore("PESSOA TESTE ANCORA", new DateOnly(1988,4,12), "MAE TESTE ANCORA");

async Task<InternalIdentityResolution> Resolve(DbConnection connection, DbTransaction tx, string cpf)
{
    return sqlServer
        ? await SqlIdentityMapRepository.ResolveOrCreateByCpfAsync(
            (SqlConnection)connection,(SqlTransaction)tx,cpf,core,null,"CPF-PROCESSOR-SMOKE",CancellationToken.None)
        : await PostgreSqlIdentityPersistence.ResolveOrCreateByCpfAsync(
            connection,tx,cpf,core,null,"CPF-PROCESSOR-SMOKE",CancellationToken.None);
}

async Task Execute(DbConnection connection, DbTransaction? tx, string sql, params (string Name,DbType Type,object? Value)[] parameters)
{
    await using var command=connection.CreateCommand();
    command.Transaction=tx;
    command.CommandText=sql;
    foreach(var item in parameters)
    {
        var p=command.CreateParameter();
        p.ParameterName=item.Name;p.DbType=item.Type;p.Value=item.Value??DBNull.Value;
        command.Parameters.Add(p);
    }
    await command.ExecuteNonQueryAsync();
}

async Task<object?> Scalar(DbConnection connection, DbTransaction? tx, string sql, params (string Name,DbType Type,object? Value)[] parameters)
{
    await using var command=connection.CreateCommand();
    command.Transaction=tx;
    command.CommandText=sql;
    foreach(var item in parameters)
    {
        var p=command.CreateParameter();
        p.ParameterName=item.Name;p.DbType=item.Type;p.Value=item.Value??DBNull.Value;
        command.Parameters.Add(p);
    }
    return await command.ExecuteScalarAsync();
}

async Task SeedCore(DbConnection connection,DbTransaction tx,Guid uuid,string cpf)
{
    // O writer compara o núcleo já materializado antes de aceitar uma repetição/reaparição.
    // A fixture usa exatamente o domínio vigente de Gold: CPF presente e baseline de uma fonte.
    var sql=sqlServer ? """
        IF NOT EXISTS(SELECT 1 FROM gold.pessoa WHERE pessoa_uuid=@uuid)
        INSERT gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
        VALUES(@uuid,@cpf,'PRESENTE','PESSOA TESTE ANCORA','1988-04-12','MAE TESTE ANCORA',1,'BASELINE_FONTE_UNICA',SYSDATETIMEOFFSET());
        """ : """
        INSERT INTO gold.pessoa(pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,atualizado_em)
        VALUES(@uuid,@cpf,'PRESENTE','PESSOA TESTE ANCORA','1988-04-12','MAE TESTE ANCORA',1,'BASELINE_FONTE_UNICA',CURRENT_TIMESTAMP)
        ON CONFLICT(pessoa_uuid) DO NOTHING;
        """;
    await Execute(connection,tx,sql,("@uuid",DbType.Guid,uuid),("@cpf",DbType.AnsiStringFixedLength,cpf));
}

var cpf=MakeCpf(401237891);
Guid permanent;
await using(var connection=await database.OpenAsync())
await using(var tx=await connection.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var first=await Resolve(connection,tx,cpf);
    Check(first.Status==ResolutionStatus.RESOLVIDO && first.PessoaUuid is { } u && u!=Guid.Empty,"Primeira resolução não criou UUID determinístico.");
    permanent=first.PessoaUuid!.Value;
    var anchor=await Scalar(connection,tx,"SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf",("@cpf",DbType.AnsiStringFixedLength,cpf));
    Check(anchor is Guid a && a==permanent,"Primeira resolução não reservou a âncora CPF.");
    await SeedCore(connection,tx,permanent,cpf);
    await tx.CommitAsync();
}

await using(var connection=await database.OpenAsync())
await using(var tx=await connection.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var repeat=await Resolve(connection,tx,cpf);
    Check(repeat.Status==ResolutionStatus.RESOLVIDO && repeat.PessoaUuid==permanent,"Repetição alterou UUID permanente.");
    var count=Convert.ToInt64(await Scalar(connection,tx,"SELECT COUNT(*) FROM identidade.cpf_ancora WHERE cpf=@cpf",("@cpf",DbType.AnsiStringFixedLength,cpf)),System.Globalization.CultureInfo.InvariantCulture);
    Check(count==1,"Repetição duplicou âncora CPF.");
    await tx.CommitAsync();
}

// Fechar a projeção operacional não remove a âncora; reaparição deve recriar o mapa com o mesmo UUID.
await using(var connection=await database.OpenAsync())
await using(var tx=await connection.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var closeSql=sqlServer
        ? "UPDATE identidade.identity_map SET vigencia_fim=SYSDATETIMEOFFSET(),estado='INATIVO',estado_motivo='SMOKE_FECHAMENTO',estado_em=SYSDATETIMEOFFSET() WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;"
        : "UPDATE identidade.identity_map SET vigencia_fim=CURRENT_TIMESTAMP,estado='INATIVO',estado_motivo='SMOKE_FECHAMENTO',estado_em=CURRENT_TIMESTAMP WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL;";
    await Execute(connection,tx,closeSql,("@cpf",DbType.AnsiStringFixedLength,cpf));
    var recovered=await Resolve(connection,tx,cpf);
    Check(recovered.Status==ResolutionStatus.RESOLVIDO && recovered.PessoaUuid==permanent,"Reaparição não recuperou UUID da âncora.");
    var current=await Scalar(connection,tx,"SELECT pessoa_uuid FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL",("@cpf",DbType.AnsiStringFixedLength,cpf));
    Check(current is Guid currentUuid && currentUuid==permanent,"Mapa corrente não foi recomposto a partir da âncora.");
    await tx.CommitAsync();
}

// Divergência sintética mapa↔âncora precisa falhar fechada e ser integralmente revertida.
await using(var connection=await database.OpenAsync())
await using(var tx=await connection.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var other=Guid.NewGuid();
    await Execute(connection,tx,"INSERT INTO identidade.pessoa(pessoa_uuid,status) VALUES(@uuid,'ATIVO')",("@uuid",DbType.Guid,other));
    await Execute(connection,tx,"UPDATE identidade.identity_map SET pessoa_uuid=@other WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL",("@other",DbType.Guid,other),("@cpf",DbType.AnsiStringFixedLength,cpf));
    try
    {
        _=await Resolve(connection,tx,cpf);
        throw new InvalidOperationException("Divergência âncora/mapa foi aceita.");
    }
    catch(InvalidOperationException ex) when(ex.Message.StartsWith("CPF_ANCHOR_IDENTITY_MAP_DIVERGENCE",StringComparison.Ordinal))
    {
        await tx.RollbackAsync();
    }
}
await using(var connection=await database.OpenAsync())
{
    var anchor=await Scalar(connection,null,"SELECT pessoa_uuid FROM identidade.cpf_ancora WHERE cpf=@cpf",("@cpf",DbType.AnsiStringFixedLength,cpf));
    var current=await Scalar(connection,null,"SELECT pessoa_uuid FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf AND vigencia_fim IS NULL",("@cpf",DbType.AnsiStringFixedLength,cpf));
    Check(anchor is Guid a && a==permanent && current is Guid c && c==permanent,"Rollback da divergência alterou âncora ou mapa.");
}

// Nova constituição abortada não pode deixar Pessoa, mapa ou âncora.
var rollbackCpf=MakeCpf(527314609);
Guid rolledBackUuid;
await using(var connection=await database.OpenAsync())
await using(var tx=await connection.BeginTransactionAsync(IsolationLevel.Serializable))
{
    var result=await Resolve(connection,tx,rollbackCpf);
    rolledBackUuid=result.PessoaUuid ?? throw new InvalidOperationException("Fixture rollback sem UUID.");
    await tx.RollbackAsync();
}
await using(var connection=await database.OpenAsync())
{
    var anchorCount=Convert.ToInt64(await Scalar(connection,null,"SELECT COUNT(*) FROM identidade.cpf_ancora WHERE cpf=@cpf",("@cpf",DbType.AnsiStringFixedLength,rollbackCpf)),System.Globalization.CultureInfo.InvariantCulture);
    var personCount=Convert.ToInt64(await Scalar(connection,null,"SELECT COUNT(*) FROM identidade.pessoa WHERE pessoa_uuid=@uuid",("@uuid",DbType.Guid,rolledBackUuid)),System.Globalization.CultureInfo.InvariantCulture);
    var mapCount=Convert.ToInt64(await Scalar(connection,null,"SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador=@cpf",("@cpf",DbType.AnsiStringFixedLength,rollbackCpf)),System.Globalization.CultureInfo.InvariantCulture);
    Check(anchorCount==0 && personCount==0 && mapCount==0,"Rollback deixou resíduo de CPF permanente.");
}

Console.WriteLine($"CPF ANCHOR PROCESSOR: OK provider={database.Provider} permanent={permanent:D}");

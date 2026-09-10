using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Npgsql;

var provider = Environment.GetEnvironmentVariable("JORNADA_PUBLICATION_PROVIDER")
    ?? throw new InvalidOperationException("Provider de teste obrigatório.");
var connectionString = Environment.GetEnvironmentVariable("JORNADA_PUBLICATION_CONNECTION")
    ?? throw new InvalidOperationException("Conexão de teste obrigatória.");
var database = OperationalDatabaseAdapterFactory.Create(provider, connectionString);
var pg = database.Provider == OperationalDatabaseProviders.PostgreSql;
const string expectedDatabase = "JornadaAtomicPublicationTest";
var databaseName = pg ? new NpgsqlConnectionStringBuilder(connectionString).Database
    : new SqlConnectionStringBuilder(connectionString).InitialCatalog;
if (!string.Equals(databaseName, expectedDatabase, StringComparison.Ordinal))
    throw new InvalidOperationException($"Smoke exige banco descartável {expectedDatabase}.");

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

async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql,
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
            AND ((SELECT COUNT(*) FROM gold.beneficio_concedido b WHERE b.registro_observacao_id=ro.registro_observacao_id)
               + (SELECT COUNT(*) FROM gold.servico_prestado s WHERE s.registro_observacao_id=ro.registro_observacao_id))=1
            AND (SELECT COUNT(*) FROM serving.registro_integrado r WHERE r.registro_observacao_id=ro.registro_observacao_id)=1
          ORDER BY ro.registro_observacao_id LIMIT 1;
          """
        : """
          SELECT TOP(1) po.pessoa_origem_id,po.pessoa_observacao_id,ro.registro_observacao_id,vf.pessoa_uuid
          FROM silver.registro_observacao ro
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
          JOIN identidade.vinculo_fonte vf ON vf.pessoa_observacao_id=po.pessoa_observacao_id
          WHERE vf.ativo=1 AND vf.status='RESOLVIDO' AND vf.pessoa_uuid IS NOT NULL
            AND ((SELECT COUNT(*) FROM gold.beneficio_concedido b WHERE b.registro_observacao_id=ro.registro_observacao_id)
               + (SELECT COUNT(*) FROM gold.servico_prestado s WHERE s.registro_observacao_id=ro.registro_observacao_id))=1
            AND (SELECT COUNT(*) FROM serving.registro_integrado r WHERE r.registro_observacao_id=ro.registro_observacao_id)=1
          ORDER BY ro.registro_observacao_id;
          """;
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) throw new InvalidOperationException("Fixture sem fato publicável completo.");
    return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetGuid(3));
}

async Task<long> AddSourceAsync()
{
    var run = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    await using var connection = await database.OpenAsync();
    var gestorTail = pg ? " RETURNING gestor_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    var gestor = Convert.ToInt64(await ScalarAsync(connection, null,
        "INSERT INTO ref.gestor(codigo,nome) VALUES(@code,@name)" + gestorTail,
        ("@code", DbType.String, "PUB" + run), ("@name", DbType.String, "Atomic publication smoke")),
        System.Globalization.CultureInfo.InvariantCulture);
    var systemTail = pg ? " RETURNING sistema_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    var system = Convert.ToInt64(await ScalarAsync(connection, null,
        "INSERT INTO ref.sistema_origem(gestor_id,codigo,nome) VALUES(@gestor,@code,@name)" + systemTail,
        ("@gestor", DbType.Int64, gestor), ("@code", DbType.String, "PUB" + run),
        ("@name", DbType.String, "Atomic publication source")), System.Globalization.CultureInfo.InvariantCulture);
    var sourceTail = pg ? " RETURNING pessoa_origem_id;" : "; SELECT CAST(SCOPE_IDENTITY() AS bigint);";
    return Convert.ToInt64(await ScalarAsync(connection, null,
        "INSERT INTO silver.pessoa_origem(sistema_origem_id,codigo_pessoa_origem) VALUES(@system,@code)" + sourceTail,
        ("@system", DbType.Int64, system), ("@code", DbType.String, "STRUCT-" + run)),
        System.Globalization.CultureInfo.InvariantCulture);
}

async Task<Guid> EnsureAsync(long sourceId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var sql = pg ? "SELECT identidade.assegurar_origem_progressiva(@source);"
        : "EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;";
    var value = await ScalarAsync(connection, transaction, sql, ("@source", DbType.Int64, sourceId));
    await transaction.CommitAsync();
    return value is Guid uuid && uuid != Guid.Empty ? uuid
        : throw new InvalidOperationException("Origem progressiva não devolveu UUID.");
}

async Task PublishReferenceAsync(long sourceId, Guid target)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var sql = pg
        ? "SELECT identidade.publicar_referencia_progressiva_deterministica(@source,@target,@evidence,@policy);"
        : "DECLARE @version bigint; EXEC identidade.sp_publicar_referencia_progressiva_deterministica @pessoa_origem_id=@source,@canonical_uuid=@target,@evidencia_referencia=@evidence,@politica_versao=@policy,@versao_resultado=@version OUTPUT; SELECT @version;";
    _ = await ScalarAsync(connection, transaction, sql,
        ("@source", DbType.Int64, sourceId), ("@target", DbType.Guid, target),
        ("@evidence", DbType.String, "evidence:atomic-publication-smoke"),
        ("@policy", DbType.String, "ATOMIC_PUBLICATION_SMOKE_V1"));
    await transaction.CommitAsync();
}

async Task<(Guid Canonical, long Version)> ProgressiveAsync(Guid initial)
{
    await using var connection = await database.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT canonical_uuid,versao FROM identidade.pessoa_origem_progressiva WHERE initial_uuid=@initial;";
    Add(command, "@initial", DbType.Guid, initial);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || reader.IsDBNull(0)) throw new InvalidOperationException("Estado progressivo incompleto.");
    return (reader.GetGuid(0), reader.GetInt64(1));
}

async Task MakeCompositionCandidateNonAnchoringAsync(long observationId)
{
    await using var connection = await database.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = pg
        ? "UPDATE identidade.vinculo_fonte SET metodo_resolucao='CORRECAO_GOVERNADA',score=NULL,modelo_id=NULL,motivo='ATOMIC_PUBLICATION_SMOKE_NON_ANCHOR' WHERE pessoa_observacao_id=@obs AND ativo AND status='RESOLVIDO' AND pessoa_uuid IS NOT NULL;"
        : "UPDATE identidade.vinculo_fonte SET metodo_resolucao='CORRECAO_GOVERNADA',score=NULL,modelo_id=NULL,motivo='ATOMIC_PUBLICATION_SMOKE_NON_ANCHOR' WHERE pessoa_observacao_id=@obs AND ativo=1 AND status='RESOLVIDO' AND pessoa_uuid IS NOT NULL;";
    Add(command, "@obs", DbType.Int64, observationId);
    if (await command.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Fixture não conseguiu isolar uma autoridade factual não ancorante.");
    await transaction.CommitAsync();
}

var candidate = await CandidateAsync();
// A publicação precisa provar separação entre referência estrutural e autoridade factual sem
// fabricar uma fusão proibida de âncora CPF. CORRECAO_GOVERNADA mantém o UUID factual corrente,
// mas, por contrato, não transfere para esta origem a titularidade permanente da âncora observada.
await MakeCompositionCandidateNonAnchoringAsync(candidate.ObservationId);
var factualInitial = await EnsureAsync(candidate.SourceId);
await PublishReferenceAsync(candidate.SourceId, factualInitial);
var structuralSource = await AddSourceAsync();
var structuralInitial = await EnsureAsync(structuralSource);
await PublishReferenceAsync(structuralSource, structuralInitial);
Check(structuralInitial != candidate.FactualUuid, "Fixture não separou destino estrutural e factual.");
var factualState = await ProgressiveAsync(factualInitial);
var structuralState = await ProgressiveAsync(structuralInitial);

var decision = new IdentityCompositionDecision(Guid.NewGuid(), IdentityCompositionOperation.FUSAO,
    ImmutableArray.Create(
        new IdentityCompositionAssignment(factualInitial, factualState.Version, structuralInitial, ProgressiveIdentityStatus.REFERENCIA),
        new IdentityCompositionAssignment(structuralInitial, structuralState.Version, structuralInitial, ProgressiveIdentityStatus.REFERENCIA)),
    "evidence:atomic-publication-smoke", "ATOMIC_PUBLICATION_SMOKE_V1", DateTimeOffset.UtcNow);
var readSet = new IdentityCompositionReadSet(
    ImmutableArray.Create(
        new IdentityCompositionMember(factualInitial, factualState.Canonical, ProgressiveIdentityStatus.REFERENCIA, factualState.Version, null),
        new IdentityCompositionMember(structuralInitial, structuralState.Canonical, ProgressiveIdentityStatus.REFERENCIA, structuralState.Version, null)),
    ImmutableArray<Guid>.Empty, ImmutableArray<IdentityCompositionHistory>.Empty);
var plan = IdentityCompositionPlanner.Prepare(readSet, decision);
Check(plan.Changes.Length == 1 && plan.Changes[0].InitialUuid == factualInitial,
    "Composição deveria alterar apenas a origem factual escolhida.");

var ledger = new IdentityCompositionLedgerStore(database);
var cpfAuthority = new IdentityCompositionCpfAuthorityReader(database);
var authoritative = new IdentityCompositionAuthoritativeReader(database, cpfAuthority);
var preApplication = new IdentityCompositionPreApplicationService(ledger, authoritative);
var application = new IdentityCompositionApplicationService(ledger, preApplication, new IdentityCompositionApplicationStore(database));
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    await ledger.RegisterPreparedAsync(connection, transaction, decision, plan, Array.Empty<Guid>(),
        "synthetic:atomic-publication", Guid.NewGuid());
    await transaction.CommitAsync();
}
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var applied = await application.ApplyAsync(connection, transaction, decision.DecisionId, "synthetic:atomic-publication");
    Check(!applied.Replay, "Composição de fixture deveria ser aplicada uma vez.");
    await transaction.CommitAsync();
}

var recomposition = new IdentityCompositionRecompositionPlan(decision.DecisionId, plan.RequestHash,
    ImmutableArray.Create(factualInitial), ImmutableArray.Create(structuralInitial),
    ImmutableArray.Create(candidate.SourceId), ImmutableArray.Create(candidate.RecordId), RequiresFactualRevalidation: true);
var recompositionStore = new IdentityCompositionRecompositionPlanStore(database);
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var registered = await recompositionStore.RegisterAsync(connection, transaction, recomposition, DateTimeOffset.UtcNow);
    Check(!registered.Replay, "Plano de recomposição deveria ser registrado uma vez.");
    await transaction.CommitAsync();
}

var linkCountBefore = await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte");
var anchorCountBefore = await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora");
var activeFactualBefore = await CountAsync(pg
        ? "SELECT COUNT(*) FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@obs AND ativo AND pessoa_uuid=@uuid AND status='RESOLVIDO'"
        : "SELECT COUNT(*) FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@obs AND ativo=1 AND pessoa_uuid=@uuid AND status='RESOLVIDO'",
    ("@obs", DbType.Int64, candidate.ObservationId), ("@uuid", DbType.Guid, candidate.FactualUuid));
Check(activeFactualBefore == 1, "Vínculo factual autoritativo da fixture não é unívoco.");

// Coloca as projeções no destino estrutural para provar que a publicação as reconduz ao vínculo factual.
await using (var connection = await database.OpenAsync())
{
    foreach (var table in new[] { "gold.beneficio_concedido", "gold.servico_prestado", "serving.registro_integrado" })
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET pessoa_uuid=@uuid,estado_atribuicao_identidade='ATRIBUIDA' WHERE registro_observacao_id=@record;";
        Add(command, "@uuid", DbType.Guid, structuralInitial);
        Add(command, "@record", DbType.Int64, candidate.RecordId);
        await command.ExecuteNonQueryAsync();
    }
}
Check(await CountAsync("SELECT COUNT(*) FROM gold.beneficio_concedido WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralInitial))
    + await CountAsync("SELECT COUNT(*) FROM gold.servico_prestado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralInitial)) == 1,
    "Pré-condição estrutural Gold inválida.");
Check(await CountAsync("SELECT COUNT(*) FROM serving.registro_integrado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralInitial)) == 1,
    "Pré-condição estrutural Serving inválida.");

var publication = new IdentityCompositionAtomicPublication(database);
// 1. Publicação dentro da transação seguida de rollback: nenhuma projeção/recibo pode escapar.
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var tentative = await publication.PublishAsync(connection, transaction, recomposition,
        "synthetic:atomic-publication", DateTimeOffset.UtcNow);
    Check(!tentative.Replay && tentative.Receipt.MutationCount == 1, "Tentativa inicial não publicou exatamente um fato.");
    await transaction.RollbackAsync();
}
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_publicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, decision.DecisionId)) == 0, "Rollback deixou recibo PUBLICADA.");
Check(await CountAsync("SELECT COUNT(*) FROM serving.registro_integrado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, structuralInitial)) == 1,
    "Rollback não restaurou a projeção Serving anterior.");

// 2. Commit efetivo: destino factual vence o destino estrutural.
IdentityCompositionPublicationResult committed;
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    committed = await publication.PublishAsync(connection, transaction, recomposition,
        "synthetic:atomic-publication", DateTimeOffset.UtcNow);
    await transaction.CommitAsync();
}
Check(!committed.Replay && committed.Receipt.State == "PUBLICADA" && committed.Receipt.MutationCount == 1,
    "Publicação efetiva não gerou recibo esperado.");
var goldFactual = await CountAsync("SELECT COUNT(*) FROM gold.beneficio_concedido WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid AND estado_atribuicao_identidade='ATRIBUIDA'",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, candidate.FactualUuid))
    + await CountAsync("SELECT COUNT(*) FROM gold.servico_prestado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid AND estado_atribuicao_identidade='ATRIBUIDA'",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, candidate.FactualUuid));
Check(goldFactual == 1, "Gold não foi publicado para o UUID factual revalidado.");
Check(await CountAsync("SELECT COUNT(*) FROM serving.registro_integrado WHERE registro_observacao_id=@record AND pessoa_uuid=@uuid AND estado_atribuicao_identidade='ATRIBUIDA'",
        ("@record", DbType.Int64, candidate.RecordId), ("@uuid", DbType.Guid, candidate.FactualUuid)) == 1,
    "Serving não foi publicado para o UUID factual revalidado.");
Check(candidate.FactualUuid != structuralInitial, "Teste perdeu separação entre referência estrutural e destino factual.");

// 3. Replay idempotente verifica o estado publicado sem repetir efeitos.
await using (var connection = await database.OpenAsync())
await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
{
    var replay = await publication.PublishAsync(connection, transaction, recomposition,
        "synthetic:atomic-publication", DateTimeOffset.UtcNow);
    Check(replay.Replay && replay.Receipt.CommandHash == committed.Receipt.CommandHash,
        "Replay não reconheceu o recibo PUBLICADA canônico.");
    await transaction.CommitAsync();
}
Check(await CountAsync("SELECT COUNT(*) FROM identidade.composicao_publicacao WHERE decision_id=@id",
        ("@id", DbType.Guid, decision.DecisionId)) == 1, "Replay criou recibo adicional.");

// 4. Conservação: publicação não reescreve autoridade factual, âncora ou identidade progressiva.
Check(await CountAsync("SELECT COUNT(*) FROM identidade.vinculo_fonte") == linkCountBefore,
    "Publicação alterou quantidade de vínculos factuais.");
Check(await CountAsync("SELECT COUNT(*) FROM identidade.cpf_ancora") == anchorCountBefore,
    "Publicação alterou âncora CPF.");
Check(await CountAsync(pg
        ? "SELECT COUNT(*) FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@obs AND ativo AND pessoa_uuid=@uuid AND status='RESOLVIDO'"
        : "SELECT COUNT(*) FROM identidade.vinculo_fonte WHERE pessoa_observacao_id=@obs AND ativo=1 AND pessoa_uuid=@uuid AND status='RESOLVIDO'",
        ("@obs", DbType.Int64, candidate.ObservationId), ("@uuid", DbType.Guid, candidate.FactualUuid)) == 1,
    "Publicação alterou o vínculo factual autoritativo.");

Console.WriteLine("IDENTITY ATOMIC PUBLICATION: OK (real DB publish, replay, rollback, factual conservation; structural target never becomes factual implicitly)");
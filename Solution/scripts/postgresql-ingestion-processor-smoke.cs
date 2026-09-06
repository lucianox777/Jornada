using Jornada.Operational.Sql;
using System.Data.Common;

var connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
    ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION não configurada.");
var database = OperationalDatabaseAdapterFactory.Create("PostgreSql", connectionString);
var ingestion = new PostgreSqlIngestionMetadataStore(database);
var leases = new PostgreSqlProcessorLeaseStore(database);

static PostgreSqlIngestionMetadataRequest Request(
    string type,
    string key,
    char hashChar,
    long bytes) => new(
        "SEHAB",
        "SEHAB",
        1,
        "BENEFICIO",
        type,
        1,
        key,
        new string(hashChar, 64),
        bytes,
        new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero),
        $"ENTREGA_SEHAB_SEHAB_v2_{new string(hashChar, 64)}.zip",
        $"sha256/{hashChar}{hashChar}/{new string(hashChar, 64)}",
        new string(hashChar, 64),
        bytes);

var aa = await ingestion.RegisterAsync(Request("AA01", "pg-ingestion-aa01-001", 'b', 21001));
if (aa.Status != "RECEBIDA" || aa.RetransmissaoIdempotente)
    throw new InvalidOperationException("Primeira Entrega AA01 PostgreSQL não ficou RECEBIDA como nova.");

var aaRetry = await ingestion.RegisterAsync(Request("AA01", "pg-ingestion-aa01-001", 'b', 21001));
if (aaRetry.EntregaId != aa.EntregaId || !aaRetry.RetransmissaoIdempotente)
    throw new InvalidOperationException("Retransmissão idempotente AA01 não reutilizou a Entrega.");

try
{
    await ingestion.RegisterAsync(Request("AA01", "pg-ingestion-aa01-001", 'c', 21002));
    throw new InvalidOperationException("Conflito de Idempotency-Key deveria ter sido rejeitado.");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("conteúdo diferente", StringComparison.Ordinal))
{
}

var ae = await ingestion.RegisterAsync(Request("AE01", "pg-ingestion-ae01-001", 'd', 31001));
if (ae.Status != "RECEBIDA" || ae.EntregaId == aa.EntregaId)
    throw new InvalidOperationException("Entrega AE01 não foi registrada de forma independente.");

var statusAa = await ingestion.GetStatusAsync("SEHAB", aa.EntregaId)
    ?? throw new InvalidOperationException("Status AA01 não localizado.");
if (statusAa.Status != "RECEBIDA")
    throw new InvalidOperationException($"Status inicial AA01 inesperado: {statusAa.Status}.");

var reserveTasks = new[]
{
    leases.ReserveNextAsync("pg-worker-a", TimeSpan.FromMinutes(2)),
    leases.ReserveNextAsync("pg-worker-b", TimeSpan.FromMinutes(2))
};
await Task.WhenAll(reserveTasks);
var reserved = reserveTasks.Select(t => t.Result).ToArray();
if (reserved.Any(x => x is null))
    throw new InvalidOperationException("Dois lotes pendentes deveriam ter sido reservados concorrentemente.");
var r1 = reserved[0]!;
var r2 = reserved[1]!;
if (r1.LoteId == r2.LoteId)
    throw new InvalidOperationException("SKIP LOCKED entregou o mesmo lote para dois workers.");
var types = new HashSet<string?>(reserved.Select(x => x!.CodigoTipo), StringComparer.Ordinal);
if (!types.SetEquals(new string?[] { "AA01", "AE01" }))
    throw new InvalidOperationException("Reservas não preservaram AA01 e AE01 como Tipos distintos.");
if (reserved.Any(x => x!.CodigoSistemaOrigem != "SEHAB" || x.PessoaSchemaVersao != 1 || x.TipoVersao != 1))
    throw new InvalidOperationException("Reserva perdeu codigoSistemaOrigem=SEHAB/schema v1.");

if (await leases.ReserveNextAsync("pg-worker-c", TimeSpan.FromMinutes(2)) is not null)
    throw new InvalidOperationException("Não deveria existir terceiro lote pendente.");
if (!await leases.HeartbeatAsync(r1, TimeSpan.FromMinutes(2)) || !await leases.HeartbeatAsync(r2, TimeSpan.FromMinutes(2)))
    throw new InvalidOperationException("Heartbeat PostgreSQL não renovou leases válidos.");

// Expira artificialmente um lease para provar recuperação sem aguardar 30 s no CI.
await using (var connection = await database.OpenAsync())
await using (var command = connection.CreateCommand())
{
    command.CommandText = "UPDATE ingestao.lote SET lease_expira_em=CURRENT_TIMESTAMP-INTERVAL '1 second' WHERE lote_id=@lote_id;";
    Add(command, "@lote_id", r1.LoteId);
    if (await command.ExecuteNonQueryAsync() != 1)
        throw new InvalidOperationException("Não foi possível preparar lease expirado.");
}

var recovered = await leases.RecoverExpiredLeasesAsync(5);
if (recovered != 1)
    throw new InvalidOperationException($"Recuperação de lease esperava 1 lote, obteve {recovered}.");
var recoveredLease = await leases.ReserveNextAsync("pg-worker-recovered", TimeSpan.FromMinutes(2))
    ?? throw new InvalidOperationException("Lote recuperado não voltou a ficar reservável.");
if (recoveredLease.LoteId != r1.LoteId || recoveredLease.AttemptNumber != 2)
    throw new InvalidOperationException("Recuperação não preservou lote/tentativa esperados.");
await leases.MarkFailedAsync(recoveredLease, "REJEITADO", "SMOKE_REJEICAO");

var poison = await leases.ScheduleRetryOrPoisonAsync(
    r2,
    "SMOKE_POISON",
    maxAttempts: 1,
    retryBase: TimeSpan.FromSeconds(1),
    retryMax: TimeSpan.FromSeconds(2));
if (poison != PostgreSqlProcessingFailureOutcome.Poison)
    throw new InvalidOperationException("Lote deveria ter ido para POISON no limite de tentativas.");

var finalAa = await ingestion.GetStatusAsync("SEHAB", aa.EntregaId)
    ?? throw new InvalidOperationException("AA01 final não localizado.");
var finalAe = await ingestion.GetStatusAsync("SEHAB", ae.EntregaId)
    ?? throw new InvalidOperationException("AE01 final não localizado.");
var finalStatuses = new HashSet<string>(new[] { finalAa.Status, finalAe.Status }, StringComparer.Ordinal);
if (!finalStatuses.SetEquals(new[] { "REJEITADA", "QUARENTENA" }))
    throw new InvalidOperationException($"Estados finais inesperados: {finalAa.Status}/{finalAe.Status}.");

Console.WriteLine("POSTGRESQL INGESTION + PROCESSOR LEASE SMOKE: OK");

static void Add(DbCommand command, string name, object value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value;
    command.Parameters.Add(parameter);
}

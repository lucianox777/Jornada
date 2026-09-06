using System.Data;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;

var connectionString = Environment.GetEnvironmentVariable("JORNADA_POSTGRESQL_CONNECTION")
    ?? throw new InvalidOperationException("JORNADA_POSTGRESQL_CONNECTION não configurada.");

var adapter = OperationalDatabaseAdapterFactory.Create(OperationalDatabaseProviders.PostgreSql, connectionString);
if (!string.Equals(adapter.Provider, OperationalDatabaseProviders.PostgreSql, StringComparison.Ordinal))
    throw new InvalidOperationException($"Provider inesperado: {adapter.Provider}.");

await using (var connection = await adapter.OpenAsync())
{
    await using var command = connection.CreateCommand();
    command.CommandType = CommandType.Text;
    command.CommandText = "SELECT 1;";
    var scalar = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    if (scalar != 1) throw new InvalidOperationException("PostgreSQL SELECT 1 não retornou 1.");
}

await using (var dedicated = await adapter.OpenDedicatedSessionAsync())
{
    await using var command = dedicated.CreateCommand();
    command.CommandText = "SELECT pg_backend_pid();";
    var pid = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    if (pid <= 0) throw new InvalidOperationException("PID PostgreSQL inválido na sessão dedicada.");
}

var coordinatorA = new PostgreSqlPipelineCoordinator(
    adapter,
    heartbeatInterval: TimeSpan.FromSeconds(30),
    exclusiveIntentTimeout: TimeSpan.FromMilliseconds(300));
var coordinatorB = new PostgreSqlPipelineCoordinator(
    adapter,
    heartbeatInterval: TimeSpan.FromSeconds(30),
    exclusiveIntentTimeout: TimeSpan.FromMilliseconds(300));

await using (var exclusive = await coordinatorA.AcquireExclusiveJobAsync(
                 "PostgreSqlSmoke",
                 TimeSpan.FromMilliseconds(500),
                 CancellationToken.None))
{
    var processorWhileExclusive = await coordinatorB.TryAcquireProcessorBatchAsync(CancellationToken.None);
    if (processorWhileExclusive is not null)
    {
        await processorWhileExclusive.DisposeAsync();
        throw new InvalidOperationException("Processor adquiriu o corpus enquanto job exclusivo possuía advisory locks.");
    }
}

var processorAfterRelease = await coordinatorB.TryAcquireProcessorBatchAsync(CancellationToken.None)
    ?? throw new InvalidOperationException("Processor não adquiriu o corpus depois da liberação dos advisory locks.");
await processorAfterRelease.DisposeAsync();

Console.WriteLine("POSTGRESQL ADAPTER SMOKE: OK");

using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;

namespace Jornada.Tests.Integration;

[TestFixture]
[Category("Integration")]
public sealed class PipelineCoordinationTests
{
    [Test]
    public async Task Exclusive_job_intent_stops_new_processor_batches_and_waits_current_batch_to_drain()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(connectionString);

        await using var currentBatch = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(currentBatch, Is.Not.Null, "Primeiro lote do Processor deve obter o corpus livre.");

        var exclusiveTask = coordinator.AcquireExclusiveJobAsync(
            "TEST_EXCLUSIVE",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        await Task.Delay(200);

        await using var blockedProcessor = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(blockedProcessor, Is.Null,
            "Depois que existe intenção exclusiva, nenhum novo lote do Processor pode iniciar.");
        Assert.That(exclusiveTask.IsCompleted, Is.False,
            "O job exclusivo deve aguardar o lote corrente liberar o corpus.");

        await currentBatch!.DisposeAsync();
        await using var exclusive = await exclusiveTask;

        await using var processorDuringExclusive = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(processorDuringExclusive, Is.Null,
            "Enquanto Runner/Parameters seguram o corpus, o Processor não materializa novo lote.");
    }

    [Test]
    public async Task Only_one_processor_batch_can_hold_the_corpus_globally()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(connectionString);

        await using var first = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(first, Is.Not.Null);

        await using var second = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(second, Is.Null,
            "Mesmo com múltiplas instâncias do Worker, somente um lote pode materializar o corpus por vez.");

        await first!.DisposeAsync();

        await using var next = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(next, Is.Not.Null, "O próximo lote deve iniciar após a liberação do corpus.");
    }

    [Test]
    public async Task Processor_can_resume_after_exclusive_job_releases_session_locks()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(connectionString);

        await using (var exclusive = await coordinator.AcquireExclusiveJobAsync(
                         "TEST_EXCLUSIVE_RELEASE",
                         TimeSpan.FromSeconds(5),
                         CancellationToken.None))
        {
            await using var blocked = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
            Assert.That(blocked, Is.Null);
        }

        await using var resumed = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(resumed, Is.Not.Null,
            "Após sp_releaseapplock/fechamento da sessão dedicada, o Processor deve retomar.");
    }


    [Test]
    public async Task Second_exclusive_job_does_not_queue_indefinitely()
    {
        var connectionString = RequireIntegrationConnection();
        var holderCoordinator = new SqlPipelineCoordinator(connectionString);
        var contenderCoordinator = new SqlPipelineCoordinator(
            connectionString,
            heartbeatInterval: TimeSpan.FromSeconds(1),
            exclusiveIntentTimeout: TimeSpan.FromMilliseconds(250));

        await using var holder = await holderCoordinator.AcquireExclusiveJobAsync(
            "TEST_EXCLUSIVE_HOLDER", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.ThrowsAsync<PipelineCoordinationBusyException>(async () =>
        {
            await using var _ = await contenderCoordinator.AcquireExclusiveJobAsync(
                "TEST_EXCLUSIVE_CONTENDER", TimeSpan.FromSeconds(5), CancellationToken.None);
        });
    }

    [Test]
    public async Task Drain_timeout_releases_exclusive_intent_for_processor_resume()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(
            connectionString,
            heartbeatInterval: TimeSpan.FromSeconds(1),
            exclusiveIntentTimeout: TimeSpan.FromSeconds(1));

        await using var currentBatch = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(currentBatch, Is.Not.Null);

        Assert.ThrowsAsync<PipelineCoordinationBusyException>(async () =>
        {
            await using var _ = await coordinator.AcquireExclusiveJobAsync(
                "TEST_DRAIN_TIMEOUT", TimeSpan.FromMilliseconds(250), CancellationToken.None);
        });

        await currentBatch!.DisposeAsync();
        await using var resumed = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(resumed, Is.Not.Null, "Timeout do drain não pode deixar intenção exclusiva residual.");
    }

    [Test]
    [Category("FaultInjection")]
    public async Task Losing_coordination_session_cancels_lease_fail_closed()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(
            connectionString,
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            exclusiveIntentTimeout: TimeSpan.FromSeconds(1));

        await using var lease = await coordinator.AcquireExclusiveJobAsync(
            "TEST_LOST_SESSION", TimeSpan.FromSeconds(5), CancellationToken.None);

        await using (var admin = new SqlConnection(connectionString))
        {
            await admin.OpenAsync();
            await using var kill = admin.CreateCommand();
            kill.CommandText = $"KILL {lease.SessionId};";
            try { await kill.ExecuteNonQueryAsync(); }
            catch (SqlException ex) when (ex.Number == 6104)
            {
                Assert.Ignore("A credencial de integração não possui permissão para KILL da sessão coordenadora.");
            }
        }

        var lostSignal = Task.Delay(Timeout.InfiniteTimeSpan, lease.LostToken);
        var completed = await Task.WhenAny(lostSignal, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(completed, Is.SameAs(lostSignal), "Heartbeat deveria detectar a perda da sessão em até 5s.");

        Assert.That(lease.IsLost, Is.True, "Perda da sessão coordenadora deve invalidar o lease.");
        Assert.That(lease.LostToken.IsCancellationRequested, Is.True, "O trabalho deve receber cancelamento fail-closed.");
    }

    [Test]
    [Category("FaultInjection")]
    public async Task Killing_processor_coordination_session_releases_global_gate_and_invalidates_lease()
    {
        var connectionString = RequireIntegrationConnection();
        var coordinator = new SqlPipelineCoordinator(
            connectionString,
            heartbeatInterval: TimeSpan.FromMilliseconds(200),
            exclusiveIntentTimeout: TimeSpan.FromSeconds(1));

        await using var lease = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(lease, Is.Not.Null);

        await using (var admin = new SqlConnection(connectionString))
        {
            await admin.OpenAsync();
            await using var kill = admin.CreateCommand();
            kill.CommandText = $"KILL {lease!.SessionId};";
            try { await kill.ExecuteNonQueryAsync(); }
            catch (SqlException ex) when (ex.Number == 6104)
            {
                Assert.Ignore("A credencial de integração não possui permissão para KILL da sessão coordenadora.");
            }
        }

        var lostSignal = Task.Delay(Timeout.InfiniteTimeSpan, lease!.LostToken);
        var completed = await Task.WhenAny(lostSignal, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(completed, Is.SameAs(lostSignal));
        Assert.That(lease.IsLost, Is.True);

        var contender = new SqlPipelineCoordinator(connectionString);
        PipelineCoordinationLease? resumed = null;
        for (var i = 0; i < 20 && resumed is null; i++)
        {
            resumed = await contender.TryAcquireProcessorBatchAsync(CancellationToken.None);
            if (resumed is null) await Task.Delay(100);
        }
        Assert.That(resumed, Is.Not.Null, "KILL da sessão deve liberar o applock no SQL Server.");
        if (resumed is not null) await resumed.DisposeAsync();
    }

    [Test]
    [Category("Concurrency")]
    public async Task Eight_simultaneous_processor_contenders_have_exactly_one_winner_and_gate_recovers()
    {
        var connectionString = RequireIntegrationConnection();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWinner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = 0;
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            var coordinator = new SqlPipelineCoordinator(connectionString);
            await start.Task;
            await using var lease = await coordinator.TryAcquireProcessorBatchAsync(CancellationToken.None);
            if (lease is null) return 0;
            Interlocked.Increment(ref acquired);
            await releaseWinner.Task;
            return 1;
        }).ToArray();

        start.SetResult(true);
        try
        {
            for (var i = 0; i < 100 && Volatile.Read(ref acquired) == 0; i++) await Task.Delay(50);
            await Task.Delay(250);
            Assert.That(Volatile.Read(ref acquired), Is.EqualTo(1), "O gate global deve admitir exatamente um vencedor simultâneo.");
        }
        finally
        {
            // Nunca deixe o vencedor bloqueado caso a asserção falhe: evita teste órfão segurando applock.
            releaseWinner.TrySetResult(true);
        }
        var winners = (await Task.WhenAll(tasks)).Sum();
        Assert.That(winners, Is.EqualTo(1));

        var recovery = new SqlPipelineCoordinator(connectionString);
        await using var resumed = await recovery.TryAcquireProcessorBatchAsync(CancellationToken.None);
        Assert.That(resumed, Is.Not.Null, "Após liberar o vencedor, o corpus deve voltar a aceitar Processor.");
    }

    private static string RequireIntegrationConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("Defina JORNADA_TEST_SQL_CONNECTION para executar testes SQL Server.");

        var csb = new SqlConnectionStringBuilder(connectionString);
        var db = csb.InitialCatalog ?? string.Empty;
        if (!db.Contains("test", StringComparison.OrdinalIgnoreCase) && !db.Contains("dev", StringComparison.OrdinalIgnoreCase) && !db.Contains("local", StringComparison.OrdinalIgnoreCase))
            Assert.Fail("Por segurança, o banco de integração deve conter 'Test', 'Dev' ou 'Local' no nome.");

        return connectionString;
    }
}

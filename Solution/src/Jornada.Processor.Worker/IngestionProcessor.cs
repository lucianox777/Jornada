using Jornada.Bronze.Storage;
using Jornada.Contracts;
using System.Diagnostics;
using Jornada.Pipeline.Coordination;

namespace Jornada.Processor.Worker;

internal sealed class IngestionProcessor(
    SqlProcessorRepository repository,
    IngestionPackageParser parser,
    IBronzeObjectStore bronzeStore,
    SqlPipelineCoordinator pipelineCoordinator,
    ProcessorRuntimeIdentity runtime,
    ProcessorOptions options,
    ILogger<IngestionProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var pipelineLease = await pipelineCoordinator.TryAcquireProcessorBatchAsync(ct);
        if (pipelineLease is null)
        {
            logger.LogDebug("Processor aguardando janela exclusiva do pipeline; nenhum lote foi reservado.");
            return false;
        }

        var batch = await repository.ReserveNextAsync(
            runtime.WorkerId, TimeSpan.FromSeconds(Math.Max(30, options.LeaseDurationSeconds)), ct);
        if (batch is null) return false;
        var deliverySw = Stopwatch.StartNew();
        var telemetryResult = "UNKNOWN";
        var validationPhase = "RESERVA";

        using var workCts = CancellationTokenSource.CreateLinkedTokenSource(ct, pipelineLease.LostToken);
        using var heartbeatStop = new CancellationTokenSource();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeatStop.Token);
        var heartbeatTask = HeartbeatLoopAsync(batch, workCts, heartbeatCts.Token);

        try
        {
            validationPhase = "BRONZE";
            await using var payload = await bronzeStore.OpenReadAsync(batch.ObjetoChave, workCts.Token);
            validationPhase = "PARSE";
            var package = OriginTerritorialGeography.ApplyResolutionTimestamp(parser.Parse(batch, payload));
            validationPhase = "PERSISTENCIA";
            await repository.PersistValidatedAsync(batch, package, workCts.Token);
            logger.LogInformation(
                "Entrega {EntregaId} processada. Lote={LoteId} Tentativa={Attempt} Pessoas={Pessoas} Registros={Registros}",
                batch.EntregaId, batch.LoteId, batch.AttemptNumber, package.Pessoas.Count, package.Registros.Count);
            telemetryResult = "PROCESSED";
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O lease expira e outro worker poderá recuperar o lote.
            telemetryResult = "HOST_CANCELLED";
            throw;
        }
        catch (OperationCanceledException) when (pipelineLease.IsLost)
        {
            telemetryResult = "PIPELINE_LOST";
            logger.LogError(
                "Processamento cancelado fail-closed porque a sessão coordenadora do pipeline foi perdida. Lote={LoteId}; SPID={Spid}.",
                batch.LoteId, pipelineLease.SessionId);
            return true;
        }
        catch (OperationCanceledException) when (workCts.IsCancellationRequested)
        {
            telemetryResult = "LEASE_LOST";
            logger.LogWarning("Processamento cancelado porque o lease do lote {LoteId} foi perdido.", batch.LoteId);
            return true;
        }
        catch (InvalidDataException ex)
        {
            telemetryResult = "REJECTED";
            await repository.MarkRejectedAsync(batch, "PACOTE_OU_CONTRATO_INVALIDO", CancellationToken.None);
            // A mensagem pode incorporar identificadores fornecidos pela origem. Registre
            // somente uma classificação estável da etapa, nunca o texto da exceção.
            logger.LogWarning(
                "Entrega {EntregaId} rejeitada durante validação. Lote={LoteId}. Motivo=PACOTE_OU_CONTRATO_INVALIDO Etapa={ValidationStage}",
                batch.EntregaId, batch.LoteId, ClassifyValidationStage(ex, validationPhase));
            return true;
        }
        catch (BronzeObjectIntegrityException ex)
        {
            telemetryResult = "QUARANTINE_BRONZE_INTEGRITY";
            await repository.MarkQuarantineAsync(batch, "BRONZE_INTEGRIDADE_DIVERGENTE", CancellationToken.None);
            logger.LogError(ex, "Entrega {EntregaId} em quarentena: integridade do objeto Bronze diverge. Chave={ObjetoChave}.", batch.EntregaId, batch.ObjetoChave);
            return true;
        }
        catch (BronzeObjectNotFoundException ex)
        {
            telemetryResult = "QUARANTINE_BRONZE_MISSING";
            await repository.MarkQuarantineAsync(batch, "BRONZE_OBJETO_NAO_ENCONTRADO", CancellationToken.None);
            logger.LogError(ex, "Entrega {EntregaId} em quarentena: objeto Bronze não encontrado. Chave={ObjetoChave}.", batch.EntregaId, batch.ObjetoChave);
            return true;
        }
        catch (FileNotFoundException ex)
        {
            telemetryResult = "QUARANTINE_CONTRACT_MISSING";
            await repository.MarkQuarantineAsync(batch, "CONTRATO_NAO_ENCONTRADO", CancellationToken.None);
            logger.LogError(ex, "Entrega {EntregaId} em quarentena: contrato referenciado não encontrado.", batch.EntregaId);
            return true;
        }
        catch (BronzeStorageUnavailableException ex)
        {
            telemetryResult = "BRONZE_UNAVAILABLE";
            var outcome = await repository.ScheduleRetryOrPoisonAsync(
                batch, "BRONZE_STORAGE_INDISPONIVEL", options.MaxProcessingAttempts,
                TimeSpan.FromSeconds(Math.Max(1, options.RetryBaseSeconds)),
                TimeSpan.FromSeconds(Math.Max(options.RetryBaseSeconds, options.RetryMaxSeconds)),
                CancellationToken.None);
            if (outcome == ProcessingFailureOutcome.Poison)
                logger.LogError(ex, "Lote {LoteId} tornou-se POISON após indisponibilidade persistente da Bronze.", batch.LoteId);
            else
                logger.LogWarning(ex, "Lote {LoteId} reagendado: armazenamento Bronze indisponível.", batch.LoteId);
            return true;
        }
        catch (Exception ex)
        {
            telemetryResult = "PROCESSING_FAILURE";
            var outcome = await repository.ScheduleRetryOrPoisonAsync(
                batch, "FALHA_PROCESSAMENTO", options.MaxProcessingAttempts,
                TimeSpan.FromSeconds(Math.Max(1, options.RetryBaseSeconds)),
                TimeSpan.FromSeconds(Math.Max(options.RetryBaseSeconds, options.RetryMaxSeconds)),
                CancellationToken.None);
            if (outcome == ProcessingFailureOutcome.Poison)
                logger.LogError(ex, "Lote {LoteId} tornou-se POISON após {Attempt} tentativa(s).", batch.LoteId, batch.AttemptNumber);
            else
                logger.LogWarning(ex, "Lote {LoteId} reagendado após falha. Tentativa={Attempt}.", batch.LoteId, batch.AttemptNumber);
            return true;
        }
        finally
        {
            deliverySw.Stop();
            JornadaTelemetry.RecordProcessorDelivery(deliverySw.Elapsed.TotalMilliseconds, telemetryResult);
            heartbeatStop.Cancel();
            try { await heartbeatTask; }
            catch (OperationCanceledException) { }
        }
    }

    private static string ClassifyValidationStage(InvalidDataException exception, string validationPhase)
    {
        var message = exception.Message;
        var stack = exception.StackTrace ?? string.Empty;

        if (string.Equals(validationPhase, "PERSISTENCIA", StringComparison.Ordinal))
        {
            if (stack.Contains("ResolveAttributeIdentityRuleAsync", StringComparison.Ordinal)
                || stack.Contains("TransversalAttributeInstanceKey", StringComparison.Ordinal)) return "PERSISTENCIA_ATRIBUTO";
            if (stack.Contains("EnsureGeographyIdsAsync", StringComparison.Ordinal)
                || stack.Contains("InsertTerritorialReferenceAsync", StringComparison.Ordinal)
                || stack.Contains("SelectTerritorialReferenceAsync", StringComparison.Ordinal)) return "PERSISTENCIA_TERRITORIO";
            if (stack.Contains("PersistFactAsync", StringComparison.Ordinal)
                || stack.Contains("MaterializeBenefitGrantedAsync", StringComparison.Ordinal)
                || stack.Contains("MaterializeServiceProvidedAsync", StringComparison.Ordinal)) return "PERSISTENCIA_REGISTRO";
            if (stack.Contains("PersistPersonAsync", StringComparison.Ordinal)) return "PERSISTENCIA_PESSOA";
            return "PERSISTENCIA";
        }

        if (stack.Contains("ParsePeople", StringComparison.Ordinal)) return "PESSOAS";
        if (stack.Contains("ParseFacts", StringComparison.Ordinal)) return "REGISTROS";
        if (stack.Contains("ValidateEnvelopeAgainstDatabase", StringComparison.Ordinal)) return "ENVELOPE";
        if (message.Contains("manifest.json", StringComparison.OrdinalIgnoreCase)) return "MANIFEST";
        if (message.Contains("pessoas.jsonl", StringComparison.OrdinalIgnoreCase)) return "PESSOAS";
        if (message.Contains("registros.jsonl", StringComparison.OrdinalIgnoreCase)) return "REGISTROS";
        if (message.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)) return "CONTRATO";
        if (message.Contains("filename", StringComparison.OrdinalIgnoreCase)
            || message.Contains("metadados persistidos", StringComparison.OrdinalIgnoreCase)) return "ENVELOPE";
        return string.Equals(validationPhase, "PARSE", StringComparison.Ordinal) ? "PARSE" : "PACOTE";
    }

    private async Task HeartbeatLoopAsync(ReservedBatch batch, CancellationTokenSource workCts, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.HeartbeatSeconds)));
            while (await timer.WaitForNextTickAsync(ct))
            {
                var renewed = await repository.HeartbeatAsync(
                    batch, TimeSpan.FromSeconds(Math.Max(30, options.LeaseDurationSeconds)), ct);
                if (renewed) continue;

                logger.LogError("Lease perdido para o lote {LoteId}; cancelando processamento local.", batch.LoteId);
                workCts.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sem heartbeat confiável, o worker não pode assumir que ainda possui o lote.
            logger.LogError(ex, "Falha no heartbeat do lote {LoteId}; cancelando processamento por segurança.", batch.LoteId);
            workCts.Cancel();
        }
    }
}

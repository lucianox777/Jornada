using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualBasic.FileIO;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Manual, disposable-database experiment. The production service registration and
/// certified-model loader are never changed. Runs the real incremental runner and
/// canonical publication/Gold procedures with a privately injected pinned-fit preview.
/// It measures TRAIN_SELECTION only, after conditional finalist validation, not a
/// new holdout or an operationally approved execution.
/// </summary>
public static class V9GoldHarnessCommand
{
    public const string Flag = "--v9-gold-harness";
    private const string Marker = "PINNED_FIT_PREVIEW_NO_ACTIVATION";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Source(long Id, string Truth, string Manager, IdentityObservation Observation);
    private sealed record Outcome(bool Eligible, bool TrueAccepted, bool FalseAccepted, string Status,
        bool PublishedTrueAssociation, bool PublishedFalseAssociation, string PublicationResult, bool LowerBandProposal);
    private sealed record PendingReasonCount(
        string RawStatus, string RawReasonCategory, string PublicationReasonCategory, long Sources);

    public static void ValidateDisposableConnection(string connectionString)
    {
        var c = new SqlConnectionStringBuilder(connectionString);
        if (!System.Text.RegularExpressions.Regex.IsMatch(c.DataSource, @"^\(localdb\)\\JornadaV9Gold[0-9]{8}_[a-f0-9]{8}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || c.InitialCatalog != "JornadaHarness" || !c.IntegratedSecurity || c.AttachDBFilename.Length != 0
            || c.UserInstance || c.FailoverPartner.Length != 0)
            throw new InvalidOperationException("Gold preview exige instância LocalDB JornadaV9Gold exclusiva e banco JornadaHarness; ambiente operacional recusado.");
    }

    public static async Task RunAsync(string[] args, string connectionString)
    {
        _ = await RunInternalAsync(args, connectionString, captureTrainEvidence: false);
    }

    /// <summary>
    /// Physically execute the CURRENT baseline on a disposable LocalDB and return
    /// ONLY ephemeral, TRAIN_SELECTION evidence to the single C# calibrator.
    /// No per-source labels or candidate UUIDs are serialized to disk.
    /// </summary>
    public static Task<IReadOnlyList<V9GoldTrainPhysicalEvidence>> RunTrainStudyAsync(
        string[] args, string connectionString) =>
        RunInternalAsync(args, connectionString, captureTrainEvidence: true);

    private static async Task<IReadOnlyList<V9GoldTrainPhysicalEvidence>> RunInternalAsync(
        string[] args, string connectionString, bool captureTrainEvidence)
    {
        if (args.Length is < 6 or > 8 || args[0] != Flag)
            throw new ArgumentException("Uso: --v9-gold-harness <SHA.selection.json> <finalist-validation.json> <SHA.fitted.json Gold> <CSV TRAIN original> <saída nova> [BASELINE_G0_G1_G2_G3|REVERSE_G3_G2_G1_G0|INTERLEAVED_G1_G3_G0_G2] [LOWER_NONMATCH=<decimal>]; usar exclusivamente LocalDB descartável.");
        ValidateDisposableConnection(connectionString);
        var selectionBytes = await File.ReadAllBytesAsync(args[1]);
        var selectionSha = Sha(selectionBytes);
        if (Path.GetFileName(args[1]) != selectionSha + ".selection.json")
            throw new InvalidDataException("Envelope TRAIN não corresponde ao SHA do nome.");
        using var selection = JsonDocument.Parse(selectionBytes);
        using var validation = JsonDocument.Parse(await File.ReadAllBytesAsync(args[2]));
        var winner = validation.RootElement.GetProperty("bestConditionalSnapshotSha256").GetString();
        if (winner is null || validation.RootElement.GetProperty("selectionEnvelopeSha256").GetString() != selectionSha
            || validation.RootElement.GetProperty("testUsed").GetBoolean() || validation.RootElement.GetProperty("refitted").GetBoolean())
            throw new InvalidDataException("Gold preview exige vencedor condicional da mesma seleção, sem TEST/refit.");
        var fitBytes = await File.ReadAllBytesAsync(args[3]);
        var snapshot = V9FittedSnapshot.Load(fitBytes, Sha(fitBytes));
        if (snapshot.Payload.GoldParentSnapshotSha256 != winner || snapshot.Payload.CandidateGrouping?.CandidateGrouping != "GOLD")
            throw new InvalidDataException("Variante Gold não pertence ao vencedor condicional.");
        var result = selection.RootElement.GetProperty("result");
        if (result.GetProperty("ExternalValidationUsed").GetBoolean() || result.GetProperty("TestUsed").GetBoolean()
            || !result.GetProperty("FinalistSnapshotShas").EnumerateArray().Any(x => x.GetString() == winner))
            throw new InvalidDataException("Finalista não foi congelado exclusivamente no TRAIN.");
        var parent = V9FittedSnapshot.Load(await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, winner + ".fitted.json")), winner);
        if (!parent.CreateGoldHarnessVariant().SerializeUtf8().AsSpan().SequenceEqual(fitBytes))
            throw new InvalidDataException("Variante altera parâmetros além do contrato Gold permitido.");
        await using (var input = File.OpenRead(args[4]))
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input)), selection.RootElement.GetProperty("originalObservationsSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CSV não é o TRAIN original fixado.");
        var identities = result.GetProperty("Split").GetProperty("Assignments").EnumerateArray()
            .Where(x => x.GetProperty("Purpose").GetString() == "TRAIN_SELECTION")
            .Select(x => x.GetProperty("BaseIdentity").GetString()!).ToHashSet(StringComparer.Ordinal);
        var sources = ReadTrainSelection(args[4], identities);
        var wavesByManager = sources.GroupBy(x => x.Manager, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        // Validate the PRE-REGISTERED order against the complete same CSV
        // before any SQL connection, database write or output directory.
        var optional = args.Skip(6).ToArray();
        var lowerOptions = optional.Where(x => x.StartsWith(
            V9GoldLowerNonMatchExperiment.ArgumentPrefix, StringComparison.Ordinal)).ToArray();
        var orderOptions = optional.Where(x => !x.StartsWith(
            V9GoldLowerNonMatchExperiment.ArgumentPrefix, StringComparison.Ordinal)).ToArray();
        if (lowerOptions.Length > 1 || orderOptions.Length > 1)
            throw new ArgumentException("Ordem ou LOWER_NONMATCH duplicados.");
        decimal? lowerNonMatchThreshold = lowerOptions.Length == 1
            ? V9GoldLowerNonMatchExperiment.ParseThreshold(lowerOptions[0], snapshot.Payload.Threshold)
            : null;
        var trialOrderId = orderOptions.Length == 1 ? orderOptions[0] : GoldTrainThreeOrderProtocol.Baseline;
        var executedWaveOrder = GoldTrainThreeOrderProtocol.ResolveOrder(
            trialOrderId, wavesByManager.Keys);
        if (captureTrainEvidence && (lowerNonMatchThreshold is not null
            || trialOrderId != GoldTrainThreeOrderProtocol.Baseline))
            throw new InvalidOperationException(
                "Captura para o Pareto exige política CURRENT e ordem BASELINE pré-registrada.");
        var output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Gold preview exige saída nova.");
        var physicalTrialTimer = System.Diagnostics.Stopwatch.StartNew();
        var sql = new OperationalSqlAdapter(connectionString);
        await using var owner = await sql.OpenAsync(CancellationToken.None);
        await GuardEmptyOwnedDatabaseAsync(owner);
        // The canonical DT-10 procedure deliberately rejects NEW_IDENTITY with
        // recovered candidates. Extend that guard only inside THIS empty,
        // separately named, marked LocalDB. No migration or operational DB is altered.
        string? disposableProcedureSha256 = null;
        if (lowerNonMatchThreshold.HasValue)
        {
            await using var read = new SqlCommand(
                "SELECT OBJECT_DEFINITION(OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote'));",
                owner);
            var canonicalDefinition = await read.ExecuteScalarAsync() as string
                ?? throw new InvalidDataException("Procedure DT-10 não instalada no LocalDB descartável.");
            var patched = V9GoldDisposableLowerProcedurePatch.Apply(canonicalDefinition);
            await using var modify = new SqlCommand(patched, owner) { CommandTimeout = 900 };
            await modify.ExecuteNonQueryAsync();
            disposableProcedureSha256 = Sha(System.Text.Encoding.UTF8.GetBytes(patched));
        }
        Directory.CreateDirectory(output);
        try
        {
            var modelId = Guid.NewGuid();
            await InsertDraftAsync(owner, modelId, snapshot);
            var runtime = V9FittedSnapshotExecutionRuntime.Load(fitBytes, snapshot.ContentSha256);
            var preview = new IsolatedPreview(sql, runtime, new(modelId, 1, LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
                snapshot.Payload.Threshold, snapshot.Payload.ConflictMargin)
            {
                BlockingContract = new("PINNED_GOLD_HARNESS_" + snapshot.ContentSha256, snapshot.ContentSha256,
                    runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256)
            }, lowerNonMatchThreshold, captureTrainEvidence);
            var trainingEvidence = new List<V9GoldTrainPhysicalEvidence>();
            var bronzeRoot = Path.Combine(output, "bronze");
            var runner = new ProbabilisticLinkageBatchRunner(new ConfigurationBuilder().Build(), sql, preview,
                new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
                new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(bronzeRoot), bronzeRoot),
                new Dt05CandidateStateSnapshotPublisher(sql, bronzeRoot), new Dt05BlockingProjectionSnapshotPublisher(sql, bronzeRoot),
                new Dt05HistoricalCandidateStateVerifier(bronzeRoot), new Dt05HistoricalBlockingProjectionVerifier(bronzeRoot),
                NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
            var truthByObservation = new Dictionary<long, Source>();
            var outcomes = new Dictionary<long, Outcome>();
            var creationAudit = new V9GoldPhysicalCreationAudit();
            var waves = new List<object>();
            foreach (var manager in executedWaveOrder)
            {
                // The order is pinned by the TRAIN protocol before any outcome
                // is inspected; within-wave source order stays CSV-stable.
                var wave = wavesByManager[manager];
                var ownership = await OwnershipAsync(owner, truthByObservation);
                var added = await InsertWaveAsync(owner, wave);
                foreach (var item in added) truthByObservation.Add(item.Key, item.Value);
                if (captureTrainEvidence) preview.BeginWave();
                var summary = await runner.RunAsync(new(LinkageRunType.INCREMENTAL, 1, null, manager, null,
                    1000, 2, null, "V9_DISPOSABLE_HARNESS", "PINNED_FIT_TRAIN_SELECTION_DIAGNOSTIC", Guid.NewGuid(), true), CancellationToken.None);
                if (summary.Status != LinkageRunStatus.PUBLICADO || summary.Avaliados != added.Count)
                    throw new InvalidDataException("Runner incremental não publicou exatamente a onda congelada.");
                var measured = await ReadOutcomesAsync(owner, summary.RunId, truthByObservation, ownership);
                if (captureTrainEvidence)
                {
                    var captured = preview.DrainWave();
                    var represented = ownership.Values.SelectMany(x => x)
                        .ToHashSet(StringComparer.Ordinal);
                    var distinctWave = added.Values
                        .Select(source => ObservationKey.From(source.Observation))
                        .Distinct().Count();
                    if (captured.Count != distinctWave || measured.Count != added.Count)
                        throw new InvalidDataException(
                            "Captura pré-publicação não reconcilia todas as observações da onda.");
                    foreach (var source in added.Values)
                    {
                        if (!captured.TryGetValue(ObservationKey.From(source.Observation), out var diagnostic))
                            throw new InvalidDataException("Score FS pré-publicação ausente na captura física.");
                        trainingEvidence.Add(new(
                            Sha(System.Text.Encoding.UTF8.GetBytes("GOLD_TRAIN_SOURCE_V1|" + source.Id.ToString(CultureInfo.InvariantCulture))),
                            Sha(System.Text.Encoding.UTF8.GetBytes("GOLD_TRAIN_BASE_V1|" + source.Truth)),
                            represented.Contains(source.Truth), diagnostic.Decision, diagnostic.Retrieval));
                    }
                }
                foreach (var item in measured)
                {
                    outcomes[item.Key] = item.Value;
                    creationAudit.Observe(item.Key, item.Value.Eligible,
                        item.Value.LowerBandProposal, item.Value.PublicationResult);
                }
                await RefreshActualGoldProjectionAsync(owner, runtime);
                waves.Add(new { manager, summary, eligibleWithPriorGold = measured.Count(x => x.Value.Eligible),
                    trueAccepted = measured.Count(x => x.Value.TrueAccepted), falseAccepted = measured.Count(x => x.Value.FalseAccepted),
                    lowerBandProposals = measured.Count(x => x.Value.LowerBandProposal),
                    lowerBandCreated = measured.Count(x => x.Value.LowerBandProposal && x.Value.PublicationResult == "NOVA_IDENTIDADE"),
                    falseNewIdentityCreations = measured.Count(x => x.Value.Eligible && x.Value.PublicationResult == "NOVA_IDENTIDADE"),
                    falseLowerBandCreations = measured.Count(x => x.Value.LowerBandProposal && x.Value.Eligible
                        && x.Value.PublicationResult == "NOVA_IDENTIDADE") });
                Console.Error.WriteLine(JsonSerializer.Serialize(new { goldWave = manager, summary.Avaliados, summary.Status }));
            }
            if (captureTrainEvidence && (trainingEvidence.Count != sources.Count
                || trainingEvidence.Select(x => x.SourceFingerprintSha256)
                    .Distinct(StringComparer.Ordinal).Count() != sources.Count))
                throw new InvalidDataException("Captura TRAIN incompleta ou com fonte duplicada.");
            var beforeReevaluation = await OwnershipAsync(owner, truthByObservation);
            var reevaluation = await runner.RunAsync(new(LinkageRunType.INCREMENTAL, 1, null, null, null,
                1000, 2, null, "V9_DISPOSABLE_HARNESS", "REEVALUATE_PENDING_AFTER_ALL_MANAGER_WAVES", Guid.NewGuid(), true), CancellationToken.None);
            if (reevaluation.Status != LinkageRunStatus.PUBLICADO || reevaluation.FreshPending != 0)
                throw new InvalidDataException("Reavaliação final deve conter somente pendências do corpus já carregado.");
            foreach (var item in await ReadOutcomesAsync(owner, reevaluation.RunId, truthByObservation, beforeReevaluation))
            {
                outcomes[item.Key] = item.Value; // Final association metric: last result wins.
                creationAudit.Observe(item.Key, item.Value.Eligible,
                    item.Value.LowerBandProposal, item.Value.PublicationResult);
                // Physical creations are *events*, not final source outcomes:
                // never erase a creation when the final run associates that source.
            }
            await RefreshActualGoldProjectionAsync(owner, runtime);
            long eligible = outcomes.Count(x => x.Value.Eligible), trueAccepted = outcomes.Count(x => x.Value.TrueAccepted),
                falseAccepted = outcomes.Count(x => x.Value.FalseAccepted), conflicts = outcomes.Count(x => x.Value.Status == "CONFLITO"),
                unresolved = outcomes.Count(x => x.Value.Status == "NAO_RESOLVIDO");
            if (outcomes.Count != sources.Count || trueAccepted > eligible)
                throw new InvalidDataException("Métricas finais não reconciliam fontes distintas e elegibilidade anterior ao score.");
            var physicalCreations = creationAudit.Snapshot();
            if (physicalCreations.LowerBandCreated > physicalCreations.LowerBandProposals
                || physicalCreations.FalseLowerBandCreations > physicalCreations.LowerBandCreated
                || physicalCreations.FalseLowerBandCreations > physicalCreations.FalseNewIdentityCreations)
                throw new InvalidDataException("Ledger de criações físicas Gold não reconcilia.");
            var publishedTrue = outcomes.LongCount(x => x.Value.PublishedTrueAssociation);
            var publishedFalse = outcomes.LongCount(x => x.Value.PublishedFalseAssociation);
            var finalOwnership = await OwnershipAsync(owner, truthByObservation);
            var impure = finalOwnership.Count(x => x.Value.Count > 1);
            var finalGoldSources = await FinalGoldSourcesAsync(owner);
            var goldEndToEnd = V9GoldEndToEndAudit.Measure(
                truthByObservation.ToDictionary(x => x.Key, x => x.Value.Truth),
                finalGoldSources);
            var goldBcubed = GoldPartitionBcubedAudit.Measure(
                truthByObservation.ToDictionary(x => x.Key, x => x.Value.Truth),
                finalGoldSources);
            if (goldBcubed.Sources != goldEndToEnd.Observations
                || goldBcubed.PublishedSources != goldEndToEnd.PublishedSources
                || goldBcubed.PendingSources != goldEndToEnd.PendingSources
                || goldBcubed.GoldIdentityGroups != goldEndToEnd.GoldReferences)
                throw new InvalidDataException("B-cubed não reconcilia com a auditoria Gold final.");
            // SQL IDENTITY identifiers differ across order-specific LocalDB
            // instances. Convert them back to stable synthetic CSV source IDs
            // BEFORE fingerprinting the published Gold membership.
            var stableTruth = sources.ToDictionary(x => x.Id, x => x.Truth);
            var stableGold = finalGoldSources.ToDictionary(
                x => truthByObservation[x.Key].Id, x => x.Value);
            var stablePartitionSha256 = GoldTrainThreeOrderProtocol.FingerprintStablePartition(
                stableTruth, stableGold);
            var isolatedRunFingerprintSha256 = Sha(System.Text.Encoding.UTF8.GetBytes(
                string.Join("|", snapshot.ContentSha256, selectionSha,
                    new SqlConnectionStringBuilder(connectionString).DataSource,
                    modelId.ToString("D", CultureInfo.InvariantCulture))));
            var pendingReasonBreakdown = await PendingReasonsAsync(owner, outcomes);
            if (goldEndToEnd.GoldReferences != finalOwnership.Count
                || goldEndToEnd.ImpureGoldReferences != impure
                || goldEndToEnd.PendingSources != outcomes.LongCount(x => x.Value.PublicationResult == "INDEFINIDA")
                || pendingReasonBreakdown.Sum(x => x.Sources) != goldEndToEnd.PendingSources)
                throw new InvalidDataException("Indicadores Gold e razões de pendência não reconciliam a publicação.");
            await using (var guard = new SqlCommand("SELECT COUNT(*) FROM identidade.modelo_linkage WHERE status<>'RASCUNHO';", owner))
                if (Convert.ToInt32(await guard.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidDataException("Modelo experimental sofreu promoção indevida.");
            await WriteNewAsync(Path.Combine(output, "gold-runner-study.json"), new
            {
                version = "V9_REAL_INCREMENTAL_RUNNER_GOLD_TRAIN_SELECTION_V1", snapshotSha256 = snapshot.ContentSha256,
                parentSnapshotSha256 = winner, fitFingerprintSha256 = snapshot.Payload.FitFingerprintSha256,
                selectionEnvelopeSha256 = selectionSha, observations = sources.Count, identityGroups = identities.Count,
                eligibleSourcesWithPriorGold = eligible, trueAccepted, falseAccepted, conflicts, unresolved,
                recallWithPriorGold = eligible == 0 ? (double?)null : (double)trueAccepted / eligible,
                fdrAfterResolve = trueAccepted + falseAccepted == 0 ? (double?)null : (double)falseAccepted / (trueAccepted + falseAccepted),
                goldIdentities = finalOwnership.Count, impureGoldIdentities = impure, waves, reevaluation,
                goldEndToEnd, goldBcubed, pendingReasonBreakdown,
                lowerNonMatchExperiment = new
                {
                    policy = lowerNonMatchThreshold.HasValue ? "EXPERIMENTAL_LOWER_TRAIN_ONLY" : "CURRENT",
                    threshold = lowerNonMatchThreshold,
                    scorerReused = true, operationalActivation = false,
                    disposableProcedureSha256,
                    publicationReason = lowerNonMatchThreshold.HasValue
                        ? V9GoldLowerNonMatchExperiment.PublicationReason : null,
                    // Deduplicated historical physical events over all four waves
                    // AND the final reevaluation; names kept for matrix contract.
                    creationCountRule = "ANY_PHYSICAL_RUN_SOURCE_UNION_PREPUBLICATION_GOLD_TRUTH",
                    finalLowerBandProposals = physicalCreations.LowerBandProposals,
                    finalLowerBandCreated = physicalCreations.LowerBandCreated,
                    finalFalseNewIdentityCreations = physicalCreations.FalseNewIdentityCreations,
                    finalFalseLowerBandCreations = physicalCreations.FalseLowerBandCreations
                },
                goldThreeOrderTrial = new
                {
                    protocol = GoldTrainThreeOrderProtocol.Version,
                    orderId = trialOrderId,
                    executedWaveOrder,
                    sourceKeyContract = GoldTrainThreeOrderProtocol.StableSourceKeyContract,
                    originalCorpusSha256 = selection.RootElement.GetProperty("originalObservationsSha256").GetString(),
                    frozenSnapshotSha256 = snapshot.ContentSha256,
                    isolatedRunFingerprintSha256,
                    elapsedMilliseconds = physicalTrialTimer.ElapsedMilliseconds,
                    stablePartitionSha256,
                    allFourWavesPublished = true,
                    sourceCount = stableTruth.Count,
                    publishedSourceCount = stableGold.Count,
                    evidenceScope = "TRAIN_SELECTION",
                    truthExported = false, uuidExported = false, testUsed = false,
                    independentlyCertified = false, activated = false
                },
                publication = new
                {
                    trueExistingLinks = publishedTrue, falseExistingLinks = publishedFalse,
                    recallWithPriorGold = eligible == 0 ? (double?)null : (double)publishedTrue / eligible,
                    fdr = publishedTrue + publishedFalse > 0 ? (double?)publishedFalse / (publishedTrue + publishedFalse) : null,
                    newIdentitySources = outcomes.Count(x => x.Value.PublicationResult == "NOVA_IDENTIDADE"),
                    pendingSources = outcomes.Count(x => x.Value.PublicationResult == "INDEFINIDA")
                },
                sourceMetricRule = "LAST_ATTEMPT_PER_SOURCE_ELIGIBLE_WHEN_TRUTH_ALREADY_IN_PRIOR_GOLD_ACCEPTED_TARGET_MUST_BE_PURE",
                actualRunnerExecuted = true, canonicalGoldPublishedInDisposableHarness = true,
                ingestionMode = "CONTROLLED_SYNTHETIC_SILVER_INSERTION",
                productionDispatchUsed = false, truthUsedForGroupingOrRetrieval = false,
                scope = "TRAIN_SELECTION_POST_SELECTION_DIAGNOSTIC_NOT_EXTERNAL_CERTIFICATION",
                refitted = false, validationReopened = false, testUsed = false, certified = false, activated = false
            });
            return trainingEvidence.AsReadOnly();
        }
        catch (Exception error)
        {
            await WriteNewAsync(Path.Combine(output, "interrupted.json"), new { exceptionType = error.GetType().Name, error.Message, certified = false, activated = false });
            throw;
        }
    }

    private static async Task<Dictionary<long, Outcome>> ReadOutcomesAsync(SqlConnection connection, Guid run,
        Dictionary<long, Source> truth, Dictionary<Guid, HashSet<string>> ownership)
    {
        var priorTruth = ownership.Values.SelectMany(x => x).ToHashSet(StringComparer.Ordinal);
        var outcomes = new Dictionary<long, Outcome>();
        await using var command = new SqlCommand("SELECT pessoa_observacao_id,status,pessoa_uuid_resolvido,resultado_publicacao,pessoa_uuid_publicado,motivo FROM identidade.linkage_resultado WHERE linkage_run_id=@run;", connection);
        command.Parameters.AddWithValue("@run", run);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0); var source = truth[id]; var status = reader.GetString(1);
            bool tp = false, fp = false;
            if (status == "RESOLVIDO")
            {
                if (reader.IsDBNull(2) || !ownership.TryGetValue(reader.GetGuid(2), out var labels))
                    throw new InvalidDataException("Destino aceito não pertencia ao Gold anterior à execução.");
                tp = labels.Count == 1 && labels.Contains(source.Truth); fp = !tp;
            }
            if (reader.IsDBNull(3)) throw new InvalidDataException("Run publicado sem resultado de publicação por fonte.");
            var publication = reader.GetString(3);
            bool publishedTp = false, publishedFp = false;
            if (publication == "ASSOCIACAO_EXISTENTE")
            {
                if (reader.IsDBNull(4) || !ownership.TryGetValue(reader.GetGuid(4), out var labels))
                    throw new InvalidDataException("Associação publicada aponta para fora do Gold anterior ao run.");
                publishedTp = labels.Count == 1 && labels.Contains(source.Truth); publishedFp = !publishedTp;
            }
            else if (publication is not ("NOVA_IDENTIDADE" or "INDEFINIDA"))
                throw new InvalidDataException("Resultado de publicação desconhecido.");
            outcomes.Add(id, new(priorTruth.Contains(source.Truth), tp, fp, status, publishedTp, publishedFp, publication,
                !reader.IsDBNull(5) && reader.GetString(5) == V9GoldLowerNonMatchExperiment.PublicationReason));
        }
        return outcomes;
    }

    private static async Task GuardEmptyOwnedDatabaseAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand("""
            IF DB_NAME()<>N'JornadaHarness' THROW 51900,'Banco de ensaio incorreto.',1;
            IF COALESCE(TRY_CONVERT(int,SERVERPROPERTY(N'IsLocalDB')),0)<>1 THROW 51900,'Ensaio requer SQL LocalDB real.',1;
            IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.EnvironmentProfile' AND CONVERT(nvarchar(200),value)=N'Development')
                THROW 51900,'Ensaio exige perfil Development residente.',1;
            IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'Jornada.V9.ExperimentalHarness' AND CONVERT(nvarchar(200),value)=@marker)
                THROW 51900,'Marcador de propriedade do ensaio ausente.',1;
            IF EXISTS(SELECT 1 FROM sys.databases WHERE database_id>4 AND name<>DB_NAME())
                THROW 51900,'Instância contém outro banco de usuário.',1;
            IF EXISTS(SELECT 1 FROM silver.pessoa_origem) OR EXISTS(SELECT 1 FROM identidade.modelo_linkage)
                OR EXISTS(SELECT 1 FROM gold.pessoa) OR EXISTS(SELECT 1 FROM identidade.linkage_run)
                THROW 51900,'Ensaio exige schema vazio; limpeza automática proibida.',1;
            DECLARE @lock int;
            EXEC @lock=sys.sp_getapplock @Resource=N'Jornada.V9.OwnedGoldHarness',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
            IF @lock<0 THROW 51900,'Outro ensaio já possui esta instância.',1;
            """, connection);
        command.Parameters.AddWithValue("@marker", Marker);
        await command.ExecuteNonQueryAsync();
    }

    private static List<Source> ReadTrainSelection(string file, HashSet<string> identities)
    {
        using var csv = new TextFieldParser(file);
        csv.SetDelimiters(",");
        var header = csv.ReadFields() ?? throw new InvalidDataException("CSV sem cabeçalho.");
        var columns = header.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i, StringComparer.Ordinal);
        var rows = new List<Source>();
        var stableSourceKeys = new HashSet<long>();
        while (!csv.EndOfData)
        {
            var f = csv.ReadFields() ?? throw new InvalidDataException("CSV malformado.");
            string Get(string name) => f[columns[name]];
            // Do not interpret TEST/VALIDATION attribute values or use labels in any decision.
            if (Get("particao") != "TRAIN" || !identities.Contains(Get("base_person_id"))) continue;
            if (!string.IsNullOrWhiteSpace(Get("cpf"))) continue;
            var birth = Get("data_nascimento");
            var manager = Get("gestor");
            if (manager.Length is < 1 or > 40 || manager.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                throw new InvalidDataException("Código de gestor inválido.");
            // Use the actual frozen synthetic CSV observacao_id (hashed to a
            // private long), NOT filtered row ordinal or SQL IDENTITY values.
            // A duplicate/collision is a hard error, never silently rekeyed.
            var stableKey = GoldTrainThreeOrderProtocol.StableKeyFromCsvObservationId(Get("observacao_id"));
            if (!stableSourceKeys.Add(stableKey))
                throw new InvalidDataException("observacao_id TRAIN repetido ou colisão da chave estável.");
            rows.Add(new(stableKey, Get("base_person_id"), manager, new(null, "SEM_CPF", Get("nome"),
                string.IsNullOrWhiteSpace(birth) ? null : DateOnly.ParseExact(birth, "yyyy-MM-dd", CultureInfo.InvariantCulture), Get("nome_mae"))));
        }
        if (rows.Count == 0 || !rows.Select(x => x.Truth).ToHashSet(StringComparer.Ordinal).SetEquals(identities))
            throw new InvalidDataException("TRAIN_SELECTION não reproduz as identidades do manifesto.");
        if (rows.GroupBy(x => (x.Truth, x.Manager)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Ensaio por gestores exige uma observação por identidade/gestor; versões repetidas requerem o bridge temporal.");
        return rows;
    }

    private static async Task InsertDraftAsync(SqlConnection connection, Guid modelId, V9FittedSnapshot snapshot)
    {
        await using var command = new SqlCommand("""
            INSERT identidade.modelo_linkage(modelo_id,versao,status,algoritmo_versao,normalizacao_versao,deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
            VALUES(@id,1,N'RASCUNHO',@algorithm,@normalization,N'PINNED_FIT_HARNESS_PREVIEW',N'SYNTHETIC_TRAIN_SELECTION',@sha,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@id", modelId);
        command.Parameters.AddWithValue("@algorithm", LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion);
        command.Parameters.AddWithValue("@normalization", IdentityComparison.NormalizationVersion);
        command.Parameters.AddWithValue("@sha", snapshot.ContentSha256);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<long, Source>> InsertWaveAsync(SqlConnection connection, Source[] wave)
    {
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var setup = new SqlCommand("""
            INSERT ref.gestor(codigo,nome) VALUES(@manager,N'Synthetic disposable V9'); DECLARE @g bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem(gestor_id,codigo,nome) VALUES(@g,@manager,N'Synthetic disposable V9'); DECLARE @s bigint=SCOPE_IDENTITY();
            INSERT ref.base_pessoa_origem(codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                VALUES(@manager,N'Synthetic disposable V9',@g,N'PRIVADA',N'NAO_HOMOLOGADA'); DECLARE @b bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao) VALUES(@s,@b,1);
            INSERT ref.gestor_pessoa_versao(gestor_id,versao,vigencia_inicio,pessoa_schema_ref,status)
                VALUES(@g,1,'20260101',N'isolated-synthetic-preview',N'RASCUNHO'); DECLARE @v bigint=SCOPE_IDENTITY();
            INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em)
                VALUES(@delivery,@g,@s,@v,@manager,REPLICATE('0',64),0,N'PROCESSADA',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status) VALUES(@batch,@delivery,1,1,N'PROCESSADO');
            SELECT @g,@s,@b;
            """, connection, tx);
        var batch = Guid.NewGuid();
        setup.Parameters.AddWithValue("@manager", wave[0].Manager);
        setup.Parameters.AddWithValue("@delivery", Guid.NewGuid());
        setup.Parameters.AddWithValue("@batch", batch);
        long gestor, system, sourceBase;
        await using (var reader = await setup.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new InvalidDataException("Cadastro do gestor não retornou IDs.");
            gestor = reader.GetInt64(0); system = reader.GetInt64(1); sourceBase = reader.GetInt64(2);
        }
        var result = new Dictionary<long, Source>();
        foreach (var source in wave)
        {
            await using var insert = new SqlCommand("""
                INSERT silver.pessoa_origem(sistema_origem_id,base_pessoa_origem_id,codigo_pessoa_origem)
                    VALUES(@system,@base,@code); DECLARE @source bigint=SCOPE_IDENTITY();
                INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                    cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                    VALUES(@source,@batch,@gestor,@code,1,@hash,N'SEM_CPF',@name,@name_cmp,@birth,@mother,@mother_cmp,SYSUTCDATETIME());
                DECLARE @observation bigint=SCOPE_IDENTITY();
                EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
                SELECT @observation;
                """, connection, tx);
            insert.Parameters.AddWithValue("@system", system); insert.Parameters.AddWithValue("@base", sourceBase);
            insert.Parameters.AddWithValue("@code", "OBS_" + source.Id.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("@batch", batch); insert.Parameters.AddWithValue("@gestor", gestor);
            insert.Parameters.AddWithValue("@hash", Sha(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source.Observation))));
            insert.Parameters.AddWithValue("@name", (object?)source.Observation.NomeCompleto ?? DBNull.Value);
            insert.Parameters.AddWithValue("@name_cmp", (object?)IdentityComparison.NormalizeText(source.Observation.NomeCompleto) ?? DBNull.Value);
            insert.Parameters.Add("@birth", SqlDbType.Date).Value = source.Observation.DataNascimento is { } dob ? dob.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
            insert.Parameters.AddWithValue("@mother", (object?)source.Observation.NomeMae ?? DBNull.Value);
            insert.Parameters.AddWithValue("@mother_cmp", (object?)IdentityComparison.NormalizeText(source.Observation.NomeMae) ?? DBNull.Value);
            await using var reader = await insert.ExecuteReaderAsync();
            // The canonical origin procedure returns its own result set first.
            while (reader.FieldCount != 1 && await reader.NextResultAsync()) { }
            if (reader.FieldCount != 1 || !await reader.ReadAsync()) throw new InvalidDataException("Observação não retornou ID.");
            result.Add(reader.GetInt64(0), source);
        }
        await tx.CommitAsync();
        return result;
    }

    /// <summary>Reads the actually published FINAL Gold references, not just raw Resolve decisions.</summary>
    private static async Task<Dictionary<long, Guid>> FinalGoldSourcesAsync(SqlConnection connection)
    {
        var sources = new Dictionary<long, Guid>();
        await using var command = new SqlCommand("""
            SELECT o.pessoa_observacao_id,p.canonical_uuid
            FROM silver.pessoa_observacao o
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
            JOIN gold.pessoa g ON g.pessoa_uuid=p.canonical_uuid
            WHERE p.estado=N'REFERENCIA' AND g.estado_identidade=N'REFERENCIA';
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (!sources.TryAdd(reader.GetInt64(0), reader.GetGuid(1)))
                throw new InvalidDataException("Observação Gold duplicada no estado final.");
        return sources;
    }

    /// <summary>
    /// Aggregate the LAST published run result for each pending source.
    /// Reason labels are intentionally bucketed: free-text reasons and UUIDs
    /// never leave the disposable harness in the exported diagnostic.
    /// </summary>
    private static async Task<List<PendingReasonCount>> PendingReasonsAsync(
        SqlConnection connection, IReadOnlyDictionary<long, Outcome> outcomes)
    {
        var counts = new Dictionary<(string RawStatus, string RawCategory, string PublicationCategory), long>();
        var seen = new HashSet<long>();
        await using var command = new SqlCommand("""
            WITH latest AS (
                SELECT r.pessoa_observacao_id,r.status,r.motivo,r.resultado_publicacao,
                       r.motivo_publicacao,
                       ROW_NUMBER() OVER(
                           PARTITION BY r.pessoa_observacao_id
                           ORDER BY r.linkage_resultado_id DESC) AS rn
                FROM identidade.linkage_resultado r
            )
            SELECT pessoa_observacao_id,status,motivo,resultado_publicacao,motivo_publicacao
            FROM latest WHERE rn=1;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            var status = reader.GetString(1);
            var result = reader.IsDBNull(3) ? "" : reader.GetString(3);
            if (!seen.Add(id) || !outcomes.TryGetValue(id, out var expected)
                || expected.PublicationResult != result || expected.Status != status)
                throw new InvalidDataException("Último resultado SQL diverge do run publicado por fonte.");
            if (result != "INDEFINIDA") continue;
            var raw = reader.IsDBNull(2) ? null : reader.GetString(2);
            var publication = reader.IsDBNull(4) ? null : reader.GetString(4);
            var key = (status, V9GoldEndToEndAudit.ReasonCategory(raw),
                V9GoldEndToEndAudit.ReasonCategory(publication));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        if (seen.Count != outcomes.Count)
            throw new InvalidDataException("Motivos de publicação não cobrem todas as fontes.");
        return counts.OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.RawStatus, StringComparer.Ordinal)
            .ThenBy(x => x.Key.RawCategory, StringComparer.Ordinal)
            .ThenBy(x => x.Key.PublicationCategory, StringComparer.Ordinal)
            .Select(x => new PendingReasonCount(x.Key.RawStatus,
                x.Key.RawCategory, x.Key.PublicationCategory, x.Value)).ToList();
    }

    private static async Task<Dictionary<Guid, HashSet<string>>> OwnershipAsync(SqlConnection connection, Dictionary<long, Source> truth)
    {
        var result = new Dictionary<Guid, HashSet<string>>();
        await using var command = new SqlCommand("""
            SELECT o.pessoa_observacao_id,p.canonical_uuid
            FROM silver.pessoa_observacao o JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
            JOIN gold.pessoa g ON g.pessoa_uuid=p.canonical_uuid WHERE p.estado=N'REFERENCIA' AND g.estado_identidade=N'REFERENCIA';
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(1);
            if (!result.TryGetValue(id, out var labels)) result.Add(id, labels = new(StringComparer.Ordinal));
            labels.Add(truth[reader.GetInt64(0)].Truth);
        }
        return result;
    }

    private static async Task RefreshActualGoldProjectionAsync(SqlConnection connection, V9FittedSnapshotExecutionRuntime runtime)
    {
        var keys = new DataTable();
        keys.Columns.Add("person", typeof(Guid)); keys.Columns.Add("feature", typeof(string));
        keys.Columns.Add("value", typeof(string)); keys.Columns.Add("temporal", typeof(string));
        await using (var command = new SqlCommand("SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                foreach (var key in runtime.Project(new(null, "SEM_CPF", reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : reader.GetString(3))))
                    keys.Rows.Add(reader.GetGuid(0), key.Feature, key.Value,
                        runtime.TemporalSemantics(key.Feature) == BlockingFeatureTemporalSemantics.StableIdentityDatum
                            ? "STABLE_IDENTITY_DATUM" : "VERSIONED_ALIAS");
        // Match the operational temporal contract: names attached to a real
        // reference remain retrieval aliases even when Gold chooses another
        // canonical spelling. Birth keys above come only from current Gold.
        await using (var command = new SqlCommand("""
            SELECT p.canonical_uuid,o.nome_completo,o.data_nascimento,o.nome_mae
            FROM silver.pessoa_observacao o JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=o.pessoa_origem_id
            JOIN gold.pessoa g ON g.pessoa_uuid=p.canonical_uuid
            WHERE p.estado=N'REFERENCIA' AND g.estado_identidade=N'REFERENCIA';
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                foreach (var key in runtime.Project(new(null, "SEM_CPF", reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : reader.GetString(3)))
                    .Where(k => runtime.TemporalSemantics(k.Feature) == BlockingFeatureTemporalSemantics.VersionedAlias))
                    keys.Rows.Add(reader.GetGuid(0), key.Feature, key.Value, "VERSIONED_ALIAS");
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var create = new SqlCommand("CREATE TABLE #v9_gold_keys(person uniqueidentifier,feature nvarchar(80),value nvarchar(500),temporal nvarchar(80));", connection, tx))
            await create.ExecuteNonQueryAsync();
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, tx) { DestinationTableName = "#v9_gold_keys" })
            await bulk.WriteToServerAsync(keys);
        await using (var update = new SqlCommand("""
            UPDATE b SET vigencia_fim=SYSDATETIMEOFFSET()
            FROM identidade.blocking_chave b WHERE b.projection_schema_version=@schema AND b.projection_fingerprint_sha256=@fingerprint
              AND b.vigencia_fim IS NULL AND b.semantica_temporal=N'STABLE_IDENTITY_DATUM'
              AND NOT EXISTS(SELECT 1 FROM #v9_gold_keys k WHERE k.person=b.pessoa_uuid AND k.feature=b.atributo AND k.value=b.valor_normalizado);
            INSERT identidade.blocking_chave(pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,semantica_temporal,projection_schema_version,projection_fingerprint_sha256)
            SELECT DISTINCT k.person,@normalization,k.feature,k.value,k.temporal,@schema,@fingerprint FROM #v9_gold_keys k
            WHERE NOT EXISTS(SELECT 1 FROM identidade.blocking_chave b WHERE b.pessoa_uuid=k.person AND b.atributo=k.feature
                AND b.valor_normalizado=k.value AND b.normalizacao_versao=@normalization AND b.projection_schema_version=@schema
                AND b.projection_fingerprint_sha256=@fingerprint AND b.vigencia_fim IS NULL);
            DROP TABLE #v9_gold_keys;
            """, connection, tx))
        {
            update.Parameters.AddWithValue("@schema", runtime.ProjectionSchemaVersion);
            update.Parameters.AddWithValue("@fingerprint", runtime.ProjectionFingerprintSha256);
            update.Parameters.AddWithValue("@normalization", IdentityComparison.NormalizationVersion);
            await update.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();

        // Physically check the materialized keys used by this pinned V9 fit.
        // A declared fingerprint alone is not evidence that the SQL projection exists.
        var expected = keys.Rows.Cast<DataRow>().Select(r => (
            Person: (Guid)r["person"], Feature: (string)r["feature"],
            Value: (string)r["value"], Temporal: (string)r["temporal"])).ToHashSet();
        var actual = new HashSet<(Guid Person, string Feature, string Value, string Temporal)>();
        await using (var verify = new SqlCommand("""
            SELECT b.pessoa_uuid,b.atributo,b.valor_normalizado,b.semantica_temporal
            FROM identidade.blocking_chave b
            JOIN gold.pessoa g ON g.pessoa_uuid=b.pessoa_uuid
            WHERE g.estado_identidade=N'REFERENCIA'
              AND b.normalizacao_versao=@normalization
              AND b.projection_schema_version=@schema
              AND b.projection_fingerprint_sha256=@fingerprint
              AND b.vigencia_fim IS NULL;
            """, connection))
        {
            verify.Parameters.AddWithValue("@normalization", IdentityComparison.NormalizationVersion);
            verify.Parameters.AddWithValue("@schema", runtime.ProjectionSchemaVersion);
            verify.Parameters.AddWithValue("@fingerprint", runtime.ProjectionFingerprintSha256);
            await using var reader = await verify.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                actual.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        if (!expected.IsSubsetOf(actual))
            throw new InvalidDataException("Projeção SQL Gold incompleta: chaves de blocking do fit fixado ausentes.");
    }

    // Private to this guarded command: no production DI registration, active model
    // lookup or truth access. Complete candidates and comparison attributes come
    // exclusively from the real Gold state before the current runner wave.
    private sealed record ObservationKey(string? Name, DateOnly? Birth, string? Mother)
    {
        public static ObservationKey From(IdentityObservation value) =>
            new(value.NomeCompleto, value.DataNascimento, value.NomeMae);
    }

    private sealed record CapturedDecision(ProbabilisticLinkageDecision Decision,
        V9LowerNonMatchPreview.VerifiedRetrieval Retrieval);

    private sealed class IsolatedPreview(IOperationalSqlAdapter sql, V9FittedSnapshotExecutionRuntime runtime,
        ProbabilisticLinkageModelRef model, decimal? lowerNonMatchThreshold,
        bool captureTrainEvidence) : IProbabilisticIdentityLinkage
    {
        private ConcurrentDictionary<ObservationKey, CapturedDecision>? _wave;

        public void BeginWave()
        {
            if (!captureTrainEvidence || _wave is not null)
                throw new InvalidOperationException("Captura TRAIN inicializada fora de onda exclusiva.");
            _wave = new();
        }

        public IReadOnlyDictionary<ObservationKey, CapturedDecision> DrainWave()
        {
            var result = _wave ?? throw new InvalidOperationException("Onda TRAIN não iniciou captura.");
            _wave = null;
            return result;
        }
        public Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Preview não dispõe de modelo ativo.");
        public Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct) =>
            version == model.Version ? Task.FromResult(model) : throw new InvalidDataException("Versão não fixada.");
        public async Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(IdentityObservation observation, Guid modeloId, CancellationToken ct)
        {
            if (modeloId != model.ModelId) throw new InvalidDataException("Mistura de modelos recusada.");
            await using var connection = await sql.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 900;
            var query = runtime.BuildCandidateUuidQuery(command, observation, runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
            command.CommandText = $"SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae FROM ({query}) c JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;";
            var candidates = new List<V9SnapshotCandidate>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    candidates.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)), reader.IsDBNull(3) ? null : reader.GetString(3)));
            var decision = runtime.ResolveRetrievedCandidatesPreview(modeloId, observation, new(runtime.SnapshotSha256,
                runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256, true, candidates));
            var eligiblePasses = runtime.Plan(observation).Count != 0;
            if (!captureTrainEvidence && (lowerNonMatchThreshold is null || !eligiblePasses))
                return decision;
            // Query has no TOP/paging: read the full UNION of blocking passes
            // and the actual Gold columns before invoking the SAME frozen scorer.
            // The initial empty Gold is guarded; each later Gold projection is
            // physically checked after publication and before the next wave.
            var candidateFrameSha = Sha(System.Text.Encoding.UTF8.GetBytes(string.Join("|",
                runtime.SnapshotSha256, runtime.ProjectionFingerprintSha256,
                string.Join(",", candidates.Select(x => x.PersonUuid.ToString("D", CultureInfo.InvariantCulture))))));
            var retrieval = new V9LowerNonMatchPreview.VerifiedRetrieval(
                runtime.SnapshotSha256, candidateFrameSha, candidates.Count,
                IndexMaterializationVerified: true, CompleteCandidateUniverse: true,
                EligibleBlockingPasses: eligiblePasses);
            if (captureTrainEvidence)
            {
                var dictionary = _wave ?? throw new InvalidOperationException(
                    "Score do TRAIN executado fora de onda pré-registrada.");
                var key = ObservationKey.From(observation);
                var value = new CapturedDecision(decision, retrieval);
                if (!dictionary.TryAdd(key, value) && dictionary.TryGetValue(key, out var earlier)
                    && earlier != value)
                    throw new InvalidDataException(
                        "Observações homônimas idênticas receberam decisões FS divergentes.");
            }
            return lowerNonMatchThreshold is { } lower && eligiblePasses
                ? V9GoldLowerNonMatchExperiment.Apply(decision, retrieval,
                    runtime.SnapshotSha256, lower, model.Threshold)
                : decision;
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static async Task WriteNewAsync<T>(string file, T value)
    {
        await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, Json);
    }
}

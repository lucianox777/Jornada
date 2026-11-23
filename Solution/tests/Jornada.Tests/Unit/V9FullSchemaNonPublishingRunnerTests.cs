using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Bronze.Storage;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Jornada.Pipeline.Coordination;
using Jornada.Processor.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

/// <summary>
/// E2E físico e opt-in da função REAL ProbabilisticLinkageBatchRunner.RunAsync.
/// Não usa a semente TEST selada nem declara paridade de produção: a ligação
/// de modelo V9 é explicitamente injetada apenas no banco descartável.
/// </summary>
[TestFixture, Category("SQLSmoke")]
public sealed class V9FullSchemaNonPublishingRunnerTests
{
    [Test]
    public async Task Full_schema_RunAsync_200_two_batches_no_publication_and_second_batch_failure()
    {
        var connectionString = Environment.GetEnvironmentVariable("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("Smoke full-schema somente em JornadaHarness descartável explícita.");
            return;
        }
        if (new SqlConnectionStringBuilder(connectionString).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("E2E só aceita JornadaHarness, nunca banco de operação.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // Guard before ANY mutation; a clone of the small ledger is not a full operational schema.
        await using (var guard = new SqlCommand("""
            IF DB_NAME()<>N'JornadaHarness' THROW 52800,'Somente JornadaHarness.',1;
            IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
               OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
               OR OBJECT_ID(N'gold.pessoa',N'U') IS NULL
               OR OBJECT_ID(N'serving.registro_integrado',N'U') IS NULL
               OR OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
               OR OBJECT_ID(N'identidade.v_vinculo_corrente',N'V') IS NULL
               THROW 52801,'Esquema operacional integral exigido.',1;
            IF EXISTS (SELECT 1 FROM silver.pessoa_observacao)
               OR EXISTS (SELECT 1 FROM identidade.modelo_linkage)
               OR EXISTS (SELECT 1 FROM identidade.linkage_run)
               OR EXISTS (SELECT 1 FROM gold.pessoa)
               OR EXISTS (SELECT 1 FROM serving.registro_integrado)
               THROW 52802,'E2E exige SQL novo/vazio sem dados operacionais.',1;
            """, connection))
            await guard.ExecuteNonQueryAsync();

        // The full 4-TF, 81-cell snapshot is losslessly stored and reloaded
        // through the production-side V9 SQL loader. No winning parameters are refitted.
        var winner = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
        var stored = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        var pinned = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, stored.ModelId, stored.SourceSnapshotSha256);
        Assert.That(pinned.FrozenWinner.SerializeUtf8(), Is.EqualTo(winner.SerializeUtf8()));
        Assert.That(stored.Active, Is.False);
        Assert.That(stored.Published, Is.False);

        // The current legacy linkage_run FK requires a metadata anchor in
        // identidade.modelo_linkage. This is NOT a conversion of TF4 to legacy
        // parameters: the scorer below reads ONLY the SQL-pinned V9 snapshot.
        // This binding is restricted to disposable SQL and Publish=false.
        await using (var anchor = new SqlCommand("""
            INSERT identidade.modelo_linkage(
                modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
                deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
            VALUES(
                @id,1,N'RASCUNHO',@algorithm,@normalization,
                N'V9_PINNED_NONPUBLISH_E2E',N'SYNTHETIC_FULL_SCHEMA',
                @sha,SYSDATETIMEOFFSET());
            """, connection))
        {
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
            anchor.Parameters.Add("@algorithm", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = stored.SourceSnapshotSha256;
            Assert.That(await anchor.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        await InsertSyntheticUniverseAsync(connection, 200);
        var before = await ReadPublicationFingerprintAsync(connection);
        var model = new ProbabilisticLinkageModelRef(
            stored.ModelId, 1, LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
            winner.Payload.Threshold, winner.Payload.ConflictMargin)
        {
            BlockingContract = new(
                "V9_SYNTHETIC_NONPUBLISH_" + winner.ContentSha256, winner.ContentSha256,
                pinned.FrozenWinner.Payload.ProjectionSchemaVersion,
                pinned.FrozenWinner.Payload.ProjectionFingerprintSha256)
        };
        var sql = new OperationalSqlAdapter(connectionString);
        var root = Path.Combine(Path.GetTempPath(), "jornada-v9-nonpublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // FIRST PROVE the real current RunAsync cannot score a grouped V9
            // winner observation by observation; its rejection is intentional.
            // Passing this attempt off as TEST/production parity is forbidden.
            ProbabilisticLinkageBatchRunner BuildRunner(IProbabilisticIdentityLinkage scorer) =>
                new(new ConfigurationBuilder().Build(), sql, scorer,
                    new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
                    new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(root), root),
                    new Dt05CandidateStateSnapshotPublisher(sql, root),
                    new Dt05BlockingProjectionSnapshotPublisher(sql, root),
                    new Dt05HistoricalCandidateStateVerifier(root),
                    new Dt05HistoricalBlockingProjectionVerifier(root),
                    NullLogger<ProbabilisticLinkageBatchRunner>.Instance);

            static ProbabilisticLinkageRunRequest Request(int batchSize = 100) =>
                new(LinkageRunType.MODEL_VALIDATION, 1, null, null, null,
                    batchSize, 1, null, "V9_HARNESS", "NO_PUBLISH_FULL_SCHEMA_E2E",
                    Guid.NewGuid(), false);

            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BuildRunner(new PinnedV9DisposableLinkage(sql, model,
                    V9FittedSnapshotExecutionRuntime.Load(
                        pinned.FrozenWinner.SerializeUtf8(), winner.ContentSha256),
                    int.MaxValue)).RunAsync(Request(), CancellationToken.None)));
            await using (var negative = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                WHERE modelo_id=@id AND status=N'FALHOU' AND avaliados=0
                ORDER BY iniciado_em DESC;
                """, connection))
            {
                negative.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var negativeId = await negative.ExecuteScalarAsync();
                Assert.That(negativeId, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)negativeId!, "FALHOU", 0);
            }
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before));

            // Unlike the previous fixture, no caller-side replay/dictionary
            // precomputes decisions. The real Runner freezes all Silver rows,
            // invokes grouped PrepareAsync ONCE and only then writes batches.
            // Edges are explicitly empty: this tests lifecycle/absence of
            // candidates, not positive production SQL candidate parity.
            ProbabilisticLinkageBatchRunner MakeRunner(int failAfter) =>
                BuildRunner(new V9PinnedSyntheticGraphCohortRunScorer(
                    sql, stored.ModelId, winner.ContentSha256,
                    Array.Empty<(long A, long B)>(), failAfter));

            // Every guarded V9 grouped attempt must refuse Publish=true
            // BEFORE creating a run or invoking a publication procedure.
            var unsafeRequest = Request() with { Publish = true };
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await MakeRunner(int.MaxValue).RunAsync(unsafeRequest, CancellationToken.None)));
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before));

            // Filtering the frozen cohort can hide cross-manager or cross-page
            // candidates. Every filter must be rejected before creating a run.
            foreach (var partial in new[]
            {
                Request() with { PessoaObservacaoId = 1 },
                Request() with { GestorCodigo = "GESTOR_A" },
                Request() with { Since = DateTimeOffset.UnixEpoch },
                Request() with { MaxRecords = 100 }
            })
            {
                var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                    await MakeRunner(int.MaxValue).RunAsync(partial, CancellationToken.None)));
                Assert.That(error!.Message, Does.Contain("MODEL_VALIDATION integral"));
                await using var lookup = new SqlCommand(
                    "SELECT COUNT_BIG(*) FROM identidade.linkage_run WHERE correlation_id=@correlation;",
                    connection);
                lookup.Parameters.Add("@correlation", SqlDbType.UniqueIdentifier).Value = partial.CorrelationId;
                Assert.That((long)(await lookup.ExecuteScalarAsync())!, Is.Zero);
            }

            // Invalid intra-manager edge must fail before any SQL decision is
            // persisted; the scorer cannot silently drop or repair bad graph.
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BuildRunner(new V9PinnedSyntheticGraphCohortRunScorer(
                    sql, stored.ModelId, winner.ContentSha256,
                    new (long A, long B)[] { (1, 2) }))
                    .RunAsync(Request(), CancellationToken.None)));

            await using (var graphFailure = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                WHERE modelo_id=@id AND status=N'FALHOU' AND avaliados=0
                ORDER BY iniciado_em DESC;
                """, connection))
            {
                graphFailure.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var id = await graphFailure.ExecuteScalarAsync();
                Assert.That(id, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)id!, "FALHOU", 0);
            }

            var first = await MakeRunner(int.MaxValue).RunAsync(Request(), CancellationToken.None);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(first.Status, Is.EqualTo(LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO));
                Assert.That(first.ModeloId, Is.EqualTo(stored.ModelId));
                Assert.That(first.Elegiveis, Is.EqualTo(200));
                Assert.That(first.Avaliados, Is.EqualTo(200));
                Assert.That(first.Resolvidos + first.NaoResolvidos + first.Conflitos, Is.EqualTo(200));
                Assert.That(first.PublicadoEm, Is.Null);
            }));
            await AssertRunRowsAsync(connection, first.RunId, "CONCLUIDO_SEM_PUBLICACAO", 200);
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before),
                "O runner sem publicação não pode alterar Gold, Serving nem vínculo corrente.");

            // The real batch transaction commits batch #1 before a deliberately
            // injected exception in batch #2. It MUST NOT fake global rollback.
            // With MaxParallelism=1, the 101st score fails deterministically.
            var failedScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256,
                Array.Empty<(long A, long B)>(), 100);
            var failedRunner = BuildRunner(failedScorer);
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await failedRunner.RunAsync(Request(), CancellationToken.None)));
            await using (var terminal = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                WHERE status=N'FALHOU' AND modelo_id=@id AND avaliados=100
                ORDER BY iniciado_em DESC;
                """, connection))
            {
                terminal.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var id = await terminal.ExecuteScalarAsync();
                Assert.That(id, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)id!, "FALHOU", 100);
                Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                    await failedScorer.ReadCompletedGroupingDiagnosticAsync(
                        (Guid)id!, CancellationToken.None)));
                Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                    await failedScorer.ReadCompletedSourceGroupingDiagnosticAsync(
                        (Guid)id!, CancellationToken.None)));
            }
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(before),
                "Falha no segundo lote não pode expor run parcial em Gold/Serving/vínculo.");
            await using (var status = new SqlCommand(
                "SELECT status FROM identidade.modelo_linkage WHERE modelo_id=@id", connection))
            {
                status.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                Assert.That(await status.ExecuteScalarAsync(), Is.EqualTo("RASCUNHO"));
            }
            // First two physical runs exercise the legacy/synthetic-empty
            // path above. Now exercise POSITIVE SQL-retrieved cross-batch pairs
            // using the same snapshot. Identity stubs are explicitly limited
            // to disposable JornadaHarness to satisfy the real FK on
            // linkage_resultado, not to represent operational Gold publication.
            await SeedDisposableDiagnosticIdentitiesAsync(connection);
            // Actual Processor Gold recomposition + linked Silver name history
            // with an explicit V9 permit. The entire governed exercise rolls
            // back and leaves zero publication, Gold or historical changes.
            await ProveGovernedProcessorHistoryAndPublicationAsync(connection, winner);
            await SeedDisposableOperationalBlockingProjectionAsync(connection, winner);
            var positiveBefore = await ReadPublicationFingerprintAsync(connection);
            var sqlScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            var positive = await BuildRunner(sqlScorer).RunAsync(Request(), CancellationToken.None);
            Assert.That(positive.Status, Is.EqualTo(LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO));
            Assert.That(positive.Elegiveis, Is.EqualTo(200));
            Assert.That(positive.Avaliados, Is.EqualTo(200));
            Assert.That(positive.PublicadoEm, Is.Null);
            await AssertRunRowsAsync(connection, positive.RunId, "CONCLUIDO_SEM_PUBLICACAO", 200);
            // The grouping comes from the live V9 runtime and is verified against
            // the completed SQL ledger; it is not reconstructed from nullable
            // linkage_resultado.pessoa_uuid_resolvido on abstention.
            var grouping = await sqlScorer.ReadCompletedGroupingDiagnosticAsync(
                positive.RunId, CancellationToken.None);
            Assert.That(grouping.Count, Is.EqualTo(200));
            Assert.That(grouping.Keys, Is.EquivalentTo(
                Enumerable.Range(1, 200).Select(i => (long)i)));
            Assert.That(grouping.Values, Has.All.Not.EqualTo(Guid.Empty));
            // Both SQL candidate edges are below the frozen complete-link floor.
            Assert.That(grouping.Values.Distinct().Count(), Is.EqualTo(200));
            Assert.That(grouping, Is.InstanceOf<
                System.Collections.ObjectModel.ReadOnlyDictionary<long, Guid>>());
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await sqlScorer.ReadCompletedGroupingDiagnosticAsync(
                    Guid.NewGuid(), CancellationToken.None)));
            // Source names are read from SQL's actual frozen run_item/Silver
            // universe, never from the external synthetic identity truth.
            var sources = await sqlScorer.ReadCompletedSourceGroupingDiagnosticAsync(
                positive.RunId, CancellationToken.None);
            Assert.That(sources.Count, Is.EqualTo(200));
            Assert.That(sources, Is.InstanceOf<
                System.Collections.ObjectModel.ReadOnlyDictionary<string, Guid>>());
            for (var i = 1; i <= 200; i++)
            {
                var opaque = "V9TEST_" + i.ToString("D4",
                    System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(sources.TryGetValue(opaque, out var group), Is.True);
                Assert.That(group, Is.EqualTo(grouping[(long)i]));
            }
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await sqlScorer.ReadCompletedSourceGroupingDiagnosticAsync(
                    Guid.NewGuid(), CancellationToken.None)));
            // Non-publishing staging JSON deliberately excludes Gold states
            // and the separate bridge truth sidecar. Never emit as CI artifact.
            var stagingJson = await sqlScorer.BuildCompletedSourceGroupingStagingJsonAsync(
                positive.RunId, new string('c', 40), CancellationToken.None);
            using (var staging = JsonDocument.Parse(stagingJson))
            {
                var payload = staging.RootElement;
                Assert.That(payload.GetProperty("schema").GetString(),
                    Is.EqualTo("E2E_V9_GROUPING_LINKAGE_ONLY_DEV_V2"));
                Assert.That(payload.GetProperty("status").GetString(),
                    Is.EqualTo("STAGING_NOT_CERTIFIED"));
                Assert.That(payload.GetProperty("runId").GetString(),
                    Is.EqualTo(positive.RunId.ToString("D")));
                Assert.That(payload.GetProperty("modelWinnerSha256").GetString(),
                    Is.EqualTo(winner.ContentSha256.ToUpperInvariant()));
                Assert.That(payload.GetProperty("observationCount").GetInt32(),
                    Is.EqualTo(200));
                Assert.That(payload.GetProperty("sourceSetDigestMethod").GetString(),
                    Is.EqualTo("SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1"));
                var sourceIds = string.Concat(sources.Keys
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Select(x => x + "\n"));
                var expectedSourceDigest = Convert.ToHexString(
                    SHA256.HashData(Encoding.Unicode.GetBytes(sourceIds)));
                Assert.That(payload.GetProperty("sourceSetSha256Utf16le").GetString(),
                    Is.EqualTo(expectedSourceDigest));
                Assert.That(payload.GetProperty("identityStatesAvailable").GetBoolean(),
                    Is.False);
                Assert.That(payload.GetProperty("certified").GetBoolean(), Is.False);
                Assert.That(payload.GetProperty("E2E_NUCLEO_PASS").GetBoolean(), Is.False);
                Assert.That(payload.GetProperty("E2E_INTEGRAL_PASS").GetBoolean(), Is.False);
                Assert.That(payload.GetProperty("rows").GetArrayLength(), Is.EqualTo(200));
                Assert.That(payload.GetProperty("rows")[0].GetProperty("opaquePersonId").GetString(),
                    Is.EqualTo("V9TEST_0001"));
                Assert.That(payload.GetProperty("rows")[0].GetProperty("groupId").GetString(),
                    Is.EqualTo(grouping[1].ToString("D")));
            }
            Assert.That(stagingJson, Does.Not.Contain("identityState\":"));
            Assert.That(stagingJson, Does.Not.Contain("BasePersonId"));
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await sqlScorer.BuildCompletedSourceGroupingStagingJsonAsync(
                    positive.RunId, "invalid-sha", CancellationToken.None)));
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await sqlScorer.BuildCompletedSourceGroupingStagingJsonAsync(
                    Guid.NewGuid(), new string('c', 40), CancellationToken.None)));

            // TEST_ONLY ledger derives delivery membership from the same
            // frozen SQL run. This fixture inserts FAKE delivery metadata
            // (zero bytes, no Bronze, no HTTP), which MUST remain unverified.
            var ledgerJson = await sqlScorer.BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(
                positive.RunId, new string('c', 40), CancellationToken.None);
            using (var ledger = JsonDocument.Parse(ledgerJson))
            {
                var evidence = ledger.RootElement;
                Assert.That(evidence.GetProperty("schema").GetString(),
                    Is.EqualTo("E2E_V9_RUN_DELIVERY_SQL_LEDGER_TEST_ONLY_V1"));
                Assert.That(evidence.GetProperty("status").GetString(),
                    Is.EqualTo("SQL_RUN_LEDGER_DIAGNOSTIC_NOT_CERTIFIED"));
                Assert.That(evidence.GetProperty("runId").GetString(),
                    Is.EqualTo(positive.RunId.ToString("D")));
                Assert.That(evidence.GetProperty("modelWinnerSha256").GetString(),
                    Is.EqualTo(winner.ContentSha256.ToUpperInvariant()));
                Assert.That(evidence.GetProperty("observationCount").GetInt32(),
                    Is.EqualTo(200));
                Assert.That(evidence.GetProperty("deliveryCount").GetInt32(),
                    Is.EqualTo(2));
                using var staging = JsonDocument.Parse(stagingJson);
                Assert.That(evidence.GetProperty("sourceSetSha256Utf16le").GetString(),
                    Is.EqualTo(staging.RootElement.GetProperty(
                        "sourceSetSha256Utf16le").GetString()));
                Assert.That(evidence.GetProperty("sqlRunTerminalVerified").GetBoolean(),
                    Is.True);
                Assert.That(evidence.GetProperty("httpDeliveryProven").GetBoolean(),
                    Is.False);
                Assert.That(evidence.GetProperty("goldIdentityStatesAvailable").GetBoolean(),
                    Is.False);
                Assert.That(evidence.GetProperty("certified").GetBoolean(),
                    Is.False);
                Assert.That(evidence.GetProperty("E2E_NUCLEO_PASS").GetBoolean(),
                    Is.False);
                Assert.That(evidence.GetProperty("E2E_INTEGRAL_PASS").GetBoolean(),
                    Is.False);
                var deliveries = evidence.GetProperty("deliveryProofs");
                Assert.That(deliveries.GetArrayLength(), Is.EqualTo(2));
                Assert.That(deliveries[0].GetProperty("gestor").GetString(),
                    Is.EqualTo("V9TEST"));
                Assert.That(deliveries[1].GetProperty("gestor").GetString(),
                    Is.EqualTo("V9TEST2"));
                Assert.That(deliveries.EnumerateArray().Sum(
                    d => d.GetProperty("silverPersons").GetInt32()), Is.EqualTo(200));
                foreach (var delivery in deliveries.EnumerateArray())
                {
                    Assert.That(delivery.GetProperty("silverPersons").GetInt32(),
                        Is.EqualTo(100));
                    Assert.That(delivery.GetProperty("deliveryIdSha256").GetString(),
                        Has.Length.EqualTo(64));
                    Assert.That(delivery.GetProperty("sqlPayloadMetadataVerified").GetBoolean(),
                        Is.False, "Synthetic fixture has no HTTP or Bronze data.");
                    Assert.That(delivery.GetProperty("sourceDeliveryIdMatches").GetBoolean(),
                        Is.False, "Synthetic SQL fixture has no delivery person IDs.");
                }
            }
            Assert.That(ledgerJson, Does.Not.Contain("BasePersonId"));
            Assert.That(ledgerJson, Does.Not.Contain("identityState"));
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await sqlScorer.BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(
                    Guid.NewGuid(), new string('c', 40), CancellationToken.None)));
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await sqlScorer.BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(
                    positive.RunId, "not-a-commit", CancellationToken.None)));

            // Physical SQL ledger mutation: positive SQL METADATA is NOT an
            // authenticated HTTP ZIP or an existing Bronze object in storage.
            // Force content and source drift and require per-delivery fail-closed.
            await ProveDisposableRunDeliverySqlMetadataGuardsAsync(
                connection, sqlScorer, positive.RunId);

            // Counts alone do not bind the stored result rows to the frozen
            // run_item universe. Exercise real SQL membership/model/status
            // tampering without publishing, then restore exact SQL state.
            await ProveCompletedRunResultMembershipGuardsAsync(
                connection, sqlScorer, positive.RunId);

            // The SQL raw results are immutable. To exercise the additional
            // read-only row-level decision reconciliation without bypassing
            // the canonical SQL trigger, corrupt only the ephemeral in-memory
            // prepared decision and restore it immediately.
            await ProveCompletedRunPreparedDecisionParityGuardsAsync(
                sqlScorer, positive.RunId);

            // SQL candidate signatures, not caller-provided edges. Both links
            // must be recovered across the 100/101 page boundary. Two different
            // comparison levels are used: FULL (FirstName TF) and exact LAST
            // (LastContentSurname TF), on person and mother sides.
            var physical = sqlScorer.SqlGraphEvidence;
            Assert.That(physical, Is.Not.Null);
            Assert.That(physical!.ProductionCandidateParityCertified, Is.False);
            Assert.That(physical.Edges, Is.EqualTo(new (int A, int B)[]
            {
                (1, 101), (2, 102)
            }));
            Assert.That(physical.CanonicalEdgesSha256, Has.Length.EqualTo(64));
            Assert.That(physical.SignatureRows, Is.GreaterThan(0));
            var operational = sqlScorer.BlockingChaveParityEvidence;
            Assert.That(operational, Is.Not.Null);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(operational!.GoldReferenceRows, Is.EqualTo(200));
                Assert.That(operational.DirectedSqlEdges, Is.EqualTo(4));
                Assert.That(operational.UndirectedSqlEdges, Is.EqualTo(2));
                Assert.That(operational.CanonicalEdgesSha256, Is.EqualTo(physical.CanonicalEdgesSha256));
                Assert.That(operational.ProductionGoldCertified, Is.False);
            }));

            var cohort = Enumerable.Range(1, 200).Select(i =>
                new V9PhysicalCalibrationObservation(
                    i, "TRUTH_UNUSED_" + i, "TEST", i <= 100 ? "V9TEST" : "V9TEST2",
                    SyntheticPersonName(i), SyntheticMotherName(i), new DateOnly(1980, 5, 6)))
                .ToArray();
            var reference = pinned.ReplayTest(cohort, physical.Edges);
            Assert.That(reference.Decisions, Has.Count.EqualTo(200));
            await AssertFrozenRunDecisionsAsync(connection, positive.RunId, reference);
            await AssertNonCircularSqlCandidateOracleAsync(connection, positive.RunId, winner);
            // Different actual RunAsync SQL pagination (200x1 instead of 100x2).
            // Compare both the shared ReplayTest baseline and the genuinely
            // independent, fixed-input oracle below; ReplayTest is NOT independent.
            var alternateScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            var alternate = await BuildRunner(alternateScorer)
                .RunAsync(Request(batchSize: 200), CancellationToken.None);
            Assert.That(alternate.Status, Is.EqualTo(LinkageRunStatus.CONCLUIDO_SEM_PUBLICACAO));
            Assert.That(alternate.Elegiveis, Is.EqualTo(200));
            Assert.That(alternate.Avaliados, Is.EqualTo(200));
            Assert.That(alternate.PublicadoEm, Is.Null);
            Assert.That(alternateScorer.SqlGraphEvidence?.CanonicalEdgesSha256,
                Is.EqualTo(physical.CanonicalEdgesSha256));
            Assert.That(alternateScorer.BlockingChaveParityEvidence?.CanonicalEdgesSha256,
                Is.EqualTo(operational!.CanonicalEdgesSha256));
            await AssertRunRowsAsync(connection, alternate.RunId, "CONCLUIDO_SEM_PUBLICACAO", 200);
            await AssertFrozenRunDecisionsAsync(connection, alternate.RunId, reference);
            await AssertNonCircularSqlCandidateOracleAsync(connection, alternate.RunId, winner);
            // The real Runner enforces a minimum BatchSize of 100. Reject
            // an undersized request BEFORE creating a SQL run, without
            // weakening production validation to accommodate the fixture.
            var undersizedScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>((Func<Task>)(async () =>
                await BuildRunner(undersizedScorer)
                    .RunAsync(Request(batchSize: 50), CancellationToken.None)));
            Assert.That(undersizedScorer.SqlGraphEvidence, Is.Null);
            Assert.That(undersizedScorer.BlockingChaveParityEvidence, Is.Null);
            Assert.That(await ReadPublicationFingerprintAsync(connection),
                Is.EqualTo(positiveBefore));
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(positiveBefore),
                "Gold/Serving não podem ser alterados nem com candidatos TF positivos.");

            // The independently implemented Worker physical SQL signature
            // join is already bounded at 128 diagnostic observations. This
            // subset includes both CROSS-BATCH pairs while keeping that cap.
            var workerEdges = await V9PhysicalSqlAdaptiveCalibrationProbe
                .ReadFrozenTestCandidateEdgesForParityAsync(
                    connection, cohort.Take(128).ToArray(), winner,
                    new V9PhysicalAdaptiveExecution(MaximumPrefixLength: 4),
                    CancellationToken.None);
            Assert.That(workerEdges, Is.EquivalentTo(physical.Edges));

            // Exactly the same SQL-positive graph must be prepared even when
            // the second SQL batch fails at its first observation. No partial
            // publication and no mixed output from an incomplete run.
            var failingSqlScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256, failAfter: 100);
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await BuildRunner(failingSqlScorer).RunAsync(Request(), CancellationToken.None)));
            Assert.That(failingSqlScorer.SqlGraphEvidence?.Edges,
                Is.EqualTo(new (int A, int B)[] { (1, 101), (2, 102) }));
            Assert.That(failingSqlScorer.BlockingChaveParityEvidence?.DirectedSqlEdges, Is.EqualTo(4));
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(positiveBefore));
            await using (var sqlFailed = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                WHERE status=N'FALHOU' AND modelo_id=@id AND avaliados=100
                ORDER BY iniciado_em DESC;
                """, connection))
            {
                sqlFailed.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var failedId = await sqlFailed.ExecuteScalarAsync();
                Assert.That(failedId, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)failedId!, "FALHOU", 100);
            }

            // Regression: blocking_chave can be valid while the backing Gold
            // identity changes. The actual Runner MUST reject this stale
            // reference before persisting any decisions, not score old keys.
            var staleGoldUuid = V9OperationalBlockingChaveCohortParity.DiagnosticUuid(101);
            await using (var stale = new SqlCommand("""
                UPDATE gold.pessoa SET nome_mae=N'V9_STALE_GOLD_MOTHER'
                 WHERE pessoa_uuid=@id;
                """, connection))
            {
                stale.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = staleGoldUuid;
                Assert.That(await stale.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            var staleGoldScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BuildRunner(staleGoldScorer).RunAsync(Request(), CancellationToken.None)));
            Assert.That(staleGoldScorer.BlockingChaveParityEvidence, Is.Null);
            await using (var noStaleResults = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM identidade.linkage_resultado lr
                JOIN identidade.linkage_run r ON r.linkage_run_id=lr.linkage_run_id
                WHERE r.modelo_id=@id AND r.status=N'FALHOU'
                  AND r.iniciado_em=(SELECT MAX(iniciado_em)
                    FROM identidade.linkage_run
                    WHERE modelo_id=@id AND status=N'FALHOU');
                """, connection))
            {
                noStaleResults.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                Assert.That(await noStaleResults.ExecuteScalarAsync(), Is.EqualTo(0L));
            }
            await using (var restoreGold = new SqlCommand("""
                UPDATE gold.pessoa SET nome_mae=@original
                 WHERE pessoa_uuid=@id;
                """, connection))
            {
                restoreGold.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = staleGoldUuid;
                restoreGold.Parameters.Add("@original", SqlDbType.NVarChar, 500).Value = SyntheticMotherName(101);
                Assert.That(await restoreGold.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(positiveBefore));

            // Negative coverage: one Gold reference disappears from the
            // eligible reference set while its blocking keys remain intact.
            // Even if the two positive candidate pairs still exist, a partial
            // Gold universe MUST NOT be accepted or silently truncated.
            var missingGoldUuid = V9OperationalBlockingChaveCohortParity.DiagnosticUuid(200);
            await using (var removeReference = new SqlCommand("""
                UPDATE gold.pessoa SET estado_identidade=N'INDEFINIDA'
                 WHERE pessoa_uuid=@id;
                """, connection))
            {
                removeReference.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = missingGoldUuid;
                Assert.That(await removeReference.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            var missingReferenceScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BuildRunner(missingReferenceScorer).RunAsync(Request(), CancellationToken.None)));
            Assert.That(missingReferenceScorer.BlockingChaveParityEvidence, Is.Null);
            // Fail-closed must be visible in the actual SQL run ledger, with
            // zero persisted decisions. Exception alone is insufficient.
            await using (var failedMissingReference = new SqlCommand("""
                SELECT TOP (1) linkage_run_id FROM identidade.linkage_run
                 WHERE modelo_id=@id AND status=N'FALHOU' AND avaliados=0
                 ORDER BY iniciado_em DESC;
                """, connection))
            {
                failedMissingReference.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
                var failedId = await failedMissingReference.ExecuteScalarAsync();
                Assert.That(failedId, Is.InstanceOf<Guid>());
                await AssertRunRowsAsync(connection, (Guid)failedId!, "FALHOU", 0);
            }
            await using (var restoreReference = new SqlCommand("""
                UPDATE gold.pessoa SET estado_identidade=N'REFERENCIA'
                 WHERE pessoa_uuid=@id;
                """, connection))
            {
                restoreReference.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = missingGoldUuid;
                Assert.That(await restoreReference.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(positiveBefore));

            // Corrupt one physically persisted projection fingerprint. The next
            // grouped RunAsync must refuse the stale/tampered manifest BEFORE
            // persisting its first SQL result. Never silently fall back to
            // an empty graph or accept a partial materialization.
            await using (var corrupt = new SqlCommand("""
                UPDATE TOP (1) identidade.blocking_chave
                   SET projection_fingerprint_sha256=REPLICATE('a',64)
                 WHERE pessoa_uuid=@id;
                """, connection))
            {
                corrupt.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value =
                    V9OperationalBlockingChaveCohortParity.DiagnosticUuid(1);
                Assert.That(await corrupt.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            var tamperedScorer = new V9PinnedSyntheticGraphCohortRunScorer(
                sql, stored.ModelId, winner.ContentSha256);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BuildRunner(tamperedScorer).RunAsync(Request(), CancellationToken.None)));
            Assert.That(tamperedScorer.BlockingChaveParityEvidence, Is.Null);
            Assert.That(await ReadPublicationFingerprintAsync(connection), Is.EqualTo(positiveBefore));
            TestContext.WriteLine("V9_FULL_SCHEMA_OPERATIONAL_BLOCKING_CHAVE_PARITY_PASS: two positive bidirectional pairs across batches; 200 full Gold/blocking manifests; stale Gold + missing reference + projection tamper fail-closed; TF4 frozen; SQL Gold/Serving unchanged; scientific TEST 100k blocked");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    /// <summary>
    /// Exercise real disposable SQL metadata joins, never HTTP or storage bytes.
    /// The fake Bronze keys do NOT reference an actual object. Even a perfect
    /// ledger metadata match remains HTTP-unproven and noncertified.
    /// </summary>
    private static async Task ProveDisposableRunDeliverySqlMetadataGuardsAsync(
        SqlConnection connection, V9PinnedSyntheticGraphCohortRunScorer scorer, Guid runId)
    {
        async Task ExecuteAsync(string commandText)
        {
            await using var command = new SqlCommand(commandText, connection);
            command.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = BatchId;
            command.Parameters.Add("@batch2", SqlDbType.UniqueIdentifier).Value = BatchId2;
            await command.ExecuteNonQueryAsync();
        }

        async Task<Dictionary<string, (bool Metadata, bool Source)>> ReadFlagsAsync()
        {
            var text = await scorer.BuildCompletedRunDeliveryLedgerDiagnosticJsonAsync(
                runId, new string('c', 40), CancellationToken.None);
            using var ledger = JsonDocument.Parse(text);
            var document = ledger.RootElement;
            Assert.That(document.GetProperty("sqlRunTerminalVerified").GetBoolean(), Is.True);
            Assert.That(document.GetProperty("observationCount").GetInt32(), Is.EqualTo(200));
            Assert.That(document.GetProperty("deliveryCount").GetInt32(), Is.EqualTo(2));
            Assert.That(document.GetProperty("httpDeliveryProven").GetBoolean(), Is.False,
                "SQL-only metadata never authenticates actual HTTP request bytes.");
            Assert.That(document.GetProperty("goldIdentityStatesAvailable").GetBoolean(),
                Is.False);
            Assert.That(document.GetProperty("certified").GetBoolean(), Is.False);
            Assert.That(document.GetProperty("E2E_NUCLEO_PASS").GetBoolean(), Is.False);
            Assert.That(document.GetProperty("E2E_INTEGRAL_PASS").GetBoolean(), Is.False);
            return document.GetProperty("deliveryProofs").EnumerateArray()
                .ToDictionary(
                    p => p.GetProperty("gestor").GetString()!,
                    p => (
                        Metadata: p.GetProperty("sqlPayloadMetadataVerified").GetBoolean(),
                        Source: p.GetProperty("sourceDeliveryIdMatches").GetBoolean()),
                    StringComparer.Ordinal);
        }

        // All modifications are restricted to the empty, disposable
        // JornadaHarness initialized in this same physical SQL smoke.
        // These VALUES are invented metadata and NEVER a downloaded HTTP ZIP.
        try
        {
            await ExecuteAsync("""
                IF DB_NAME()<>N'JornadaHarness'
                    THROW 52820,'Somente JornadaHarness descartável.',1;
                DECLARE @one uniqueidentifier =
                    (SELECT entrega_id FROM ingestao.lote WHERE lote_id=@batch);
                DECLARE @two uniqueidentifier =
                    (SELECT entrega_id FROM ingestao.lote WHERE lote_id=@batch2);
                IF @one IS NULL OR @two IS NULL OR @one=@two
                    THROW 52821,'Entregas sintéticas ausentes ou repetidas.',1;
                UPDATE ingestao.entrega
                   SET payload_sha256=REPLICATE('a',64),bytes_recebidos=256
                 WHERE entrega_id=@one;
                UPDATE ingestao.entrega
                   SET payload_sha256=REPLICATE('b',64),bytes_recebidos=512
                 WHERE entrega_id=@two;
                INSERT bronze.entrega_arquivo(
                    entrega_id,nome_arquivo,content_type,objeto_chave,
                    payload_sha256,tamanho_bytes,recebido_em)
                VALUES
                    (@one,N'NO_HTTP_FIXTURE_1.zip',N'application/zip',
                     N'fixture/NO_BYTES_EXIST/one',REPLICATE('a',64),256,
                     TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00')),
                    (@two,N'NO_HTTP_FIXTURE_2.zip',N'application/zip',
                     N'fixture/NO_BYTES_EXIST/two',REPLICATE('b',64),512,
                     TODATETIMEOFFSET(SYSUTCDATETIME(),'+00:00'));
                UPDATE silver.pessoa_observacao
                   SET id_pessoa_entrega=codigo_pessoa_origem
                 WHERE lote_id IN (@batch,@batch2);
                """);
            var valid = await ReadFlagsAsync();
            Assert.That(valid.Keys, Is.EquivalentTo(new[] { "V9TEST", "V9TEST2" }));
            Assert.That(valid.Values.All(p => p.Metadata && p.Source), Is.True,
                "Only the ledger metadata are internally consistent, NOT real HTTP.");

            // Deliberate Bronze payload SHA substitution in just one delivery.
            await ExecuteAsync("""
                UPDATE b SET payload_sha256=REPLICATE('c',64)
                FROM bronze.entrega_arquivo b
                JOIN ingestao.lote l ON l.entrega_id=b.entrega_id
                WHERE l.lote_id=@batch;
                """);
            var changedBronze = await ReadFlagsAsync();
            Assert.That(changedBronze["V9TEST"].Metadata, Is.False);
            Assert.That(changedBronze["V9TEST2"].Metadata, Is.True);
            Assert.That(changedBronze.Values.All(p => p.Source), Is.True);

            await ExecuteAsync("""
                UPDATE b SET payload_sha256=REPLICATE('a',64)
                FROM bronze.entrega_arquivo b
                JOIN ingestao.lote l ON l.entrega_id=b.entrega_id
                WHERE l.lote_id=@batch;
                UPDATE silver.pessoa_observacao SET id_pessoa_entrega=N'ALTERADA'
                WHERE pessoa_observacao_id=(
                    SELECT MIN(pessoa_observacao_id)
                    FROM silver.pessoa_observacao WHERE lote_id=@batch2);
                """);
            var changedSource = await ReadFlagsAsync();
            Assert.That(changedSource["V9TEST"].Source, Is.True);
            Assert.That(changedSource["V9TEST2"].Source, Is.False);
            Assert.That(changedSource.Values.All(p => p.Metadata), Is.True);

            await ExecuteAsync("""
                UPDATE silver.pessoa_observacao
                   SET id_pessoa_entrega=codigo_pessoa_origem WHERE lote_id=@batch2;
                UPDATE ingestao.lote SET status=N'PENDENTE' WHERE lote_id=@batch2;
                """);
            var changedLote = await ReadFlagsAsync();
            Assert.That(changedLote["V9TEST"].Metadata, Is.True);
            Assert.That(changedLote["V9TEST2"].Metadata, Is.False);
            Assert.That(changedLote.Values.All(p => p.Source), Is.True);

            await ExecuteAsync("""
                UPDATE ingestao.lote SET status=N'PROCESSADO' WHERE lote_id=@batch2;
                UPDATE e SET bytes_recebidos=513
                FROM ingestao.entrega e
                JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
                WHERE l.lote_id=@batch2;
                """);
            var changedBytes = await ReadFlagsAsync();
            Assert.That(changedBytes["V9TEST"].Metadata, Is.True);
            Assert.That(changedBytes["V9TEST2"].Metadata, Is.False);
            Assert.That(changedBytes.Values.All(p => p.Source), Is.True);
        }
        finally
        {
            // Restore all synthetic fixture state even on an assertion failure.
            // No run, identity, serving or real Gold publication is changed.
            await ExecuteAsync("""
                IF DB_NAME()<>N'JornadaHarness'
                    THROW 52822,'Somente JornadaHarness descartável.',1;
                DELETE b FROM bronze.entrega_arquivo b
                JOIN ingestao.lote l ON l.entrega_id=b.entrega_id
                WHERE l.lote_id IN (@batch,@batch2);
                UPDATE silver.pessoa_observacao SET id_pessoa_entrega=NULL
                WHERE lote_id IN (@batch,@batch2);
                UPDATE ingestao.lote SET status=N'PROCESSADO'
                WHERE lote_id IN (@batch,@batch2);
                UPDATE e SET payload_sha256=REPLICATE('0',64),bytes_recebidos=0
                FROM ingestao.entrega e
                JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
                WHERE l.lote_id IN (@batch,@batch2);
                """);
        }
        var restored = await ReadFlagsAsync();
        Assert.That(restored.Values.All(p => !p.Metadata && !p.Source), Is.True,
            "Disposable fixture must return to its original non-HTTP state.");
    }


    /// <summary>
    /// Demonstrate that equal SQL row counts are insufficient to prove the
    /// completed run's frozen decisions. All mutations are disposable only
    /// and are restored before any subsequent scoring or candidate tests.
    /// </summary>
    private static async Task ProveCompletedRunResultMembershipGuardsAsync(
        SqlConnection connection, V9PinnedSyntheticGraphCohortRunScorer scorer, Guid runId)
    {
        async Task ExecuteAsync(string command, long shadow = 0)
        {
            await using var query = new SqlCommand(command, connection);
            query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            query.Parameters.Add("@shadow", SqlDbType.BigInt).Value = shadow;
            await query.ExecuteNonQueryAsync();
        }

        async Task AssertRawMutationBlockedAsync(string command, string reason, long shadow = 0)
        {
            await using var query = new SqlCommand(command, connection);
            query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            query.Parameters.Add("@shadow", SqlDbType.BigInt).Value = shadow;
            // The canonically installed 20260919 migration makes raw result
            // evidence IMMUTABLE. It rejects UPDATE irrespective of row count.
            // Never disable this trigger to simulate corrupt terminal results.
            var error = Assert.ThrowsAsync<SqlException>((Func<Task>)(async () =>
                await query.ExecuteNonQueryAsync()));
            Assert.That(error, Is.Not.Null, reason);
            Assert.That(error!.Number, Is.AnyOf(51822, 51823), reason);
            Assert.That((await scorer.ReadCompletedGroupingDiagnosticAsync(
                runId, CancellationToken.None)).Count, Is.EqualTo(200));
        }

        await ExecuteAsync("""
            IF DB_NAME()<>N'JornadaHarness'
                THROW 52830,'Somente JornadaHarness descartável.',1;
            IF (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                WHERE linkage_run_id=@run)<>200
                THROW 52831,'Coorte sintética incompleta.',1;
            """);
        Assert.That((await scorer.ReadCompletedGroupingDiagnosticAsync(
            runId, CancellationToken.None)).Count, Is.EqualTo(200));

        // The actual operational schema has an immutable raw scorer ledger.
        // Fail CLOSED at the SQL trigger; don't try to bypass it or rewrite
        // a terminal run just to test the fallback read-only consistency.
        await AssertRawMutationBlockedAsync("""
            UPDATE identidade.linkage_resultado SET modelo_versao=2
            WHERE linkage_run_id=@run AND pessoa_observacao_id=1;
            """, "A alteração de versão do modelo bruto precisa ser bloqueada.");
        await AssertRawMutationBlockedAsync("""
            UPDATE identidade.linkage_resultado
               SET status=N'CONFLITO',motivo=N'TAMPER_ONLY'
            WHERE linkage_run_id=@run AND pessoa_observacao_id=1;
            """, "A decisão bruta imutável não pode ser reescrita.");

        long shadowId = 0;
        try
        {
            // Only a temporary Silver row can be inserted safely; do NOT
            // tamper with immutable linkage_resultado. The shadow is outside
            // the frozen universe but has a valid foreign key and identity.
            await using (var create = new SqlCommand("""
                IF DB_NAME()<>N'JornadaHarness'
                    THROW 52832,'Somente JornadaHarness descartável.',1;
                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                    versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
                    nome_completo,nome_cmp,data_nascimento,nome_mae,
                    nome_mae_cmp,source_as_of)
                OUTPUT INSERTED.pessoa_observacao_id
                SELECT pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                       2,REPLICATE('f',64),NULL,cpf_ausente_motivo,
                       nome_completo,nome_cmp,data_nascimento,nome_mae,
                       nome_mae_cmp,SYSUTCDATETIME()
                FROM silver.pessoa_observacao
                WHERE pessoa_observacao_id=1;
                """, connection))
            {
                shadowId = (long)(await create.ExecuteScalarAsync()
                    ?? throw new InvalidDataException("Shadow Silver DEV ausente."));
            }
            Assert.That(shadowId, Is.GreaterThan(200));

            await AssertRawMutationBlockedAsync("""
                UPDATE identidade.linkage_resultado SET pessoa_observacao_id=@shadow
                WHERE linkage_run_id=@run AND pessoa_observacao_id=1;
                """, "A membership de um resultado imutável não pode mudar.", shadowId);

            // Run items don't have the same SQL immutable-result trigger.
            // Replace ONE frozen membership with a valid shadow ID while
            // preserving exactly 200 items and 200 immutable results.
            // ReadCompletedGroupingDiagnosticAsync MUST detect that the sets
            // diverge, even though both COUNT_BIG(*) remain 200.
            await ExecuteAsync("""
                DELETE FROM identidade.linkage_run_item
                WHERE linkage_run_id=@run AND pessoa_observacao_id=1;
                INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                VALUES(@run,@shadow);
                """, shadowId);
            Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                await scorer.ReadCompletedGroupingDiagnosticAsync(
                    runId, CancellationToken.None)),
                "Frozen SQL membership substitution must fail despite 200/200 row counts.");
        }
        finally
        {
            // Restore only mutable frozen membership. Do NOT update or delete
            // the append-only raw result rows. This clean-up is a disposable
            // JornadaHarness fixture, not a general recovery procedure.
            await ExecuteAsync("""
                IF DB_NAME()<>N'JornadaHarness'
                    THROW 52833,'Somente JornadaHarness descartável.',1;
                DELETE FROM identidade.linkage_run_item
                WHERE linkage_run_id=@run AND pessoa_observacao_id=@shadow;
                IF NOT EXISTS (SELECT 1 FROM identidade.linkage_run_item
                               WHERE linkage_run_id=@run AND pessoa_observacao_id=1)
                    INSERT identidade.linkage_run_item(linkage_run_id,pessoa_observacao_id)
                    VALUES(@run,1);
                IF @shadow>0
                    DELETE FROM silver.pessoa_observacao
                    WHERE pessoa_observacao_id=@shadow;
                """, shadowId);
        }
        var restored = await scorer.ReadCompletedGroupingDiagnosticAsync(
            runId, CancellationToken.None);
        Assert.That(restored.Count, Is.EqualTo(200));
        Assert.That(restored.Keys, Is.EquivalentTo(
            Enumerable.Range(1, 200).Select(i => (long)i)));
    }


    /// <summary>
    /// A failed row-by-row comparison is tested by changing ONLY the TEST
    /// scorer's ephemeral in-memory decision. Immutable SQL evidence is never
    /// updated, triggers remain active, and the field is restored in finally.
    /// This is not independent scientific scoring or productive provenance.
    /// </summary>
    private static async Task ProveCompletedRunPreparedDecisionParityGuardsAsync(
        V9PinnedSyntheticGraphCohortRunScorer scorer, Guid runId)
    {
        var field = typeof(V9PinnedSyntheticGraphCohortRunScorer).GetField(
            "prepared", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Prepared V9 TEST scorer state missing.");
        var original = field!.GetValue(scorer);
        Assert.That(original, Is.Not.Null);
        var frozen = (IReadOnlyDictionary<long,
            (IdentityObservation Observation, ProbabilisticLinkageDecision Decision)>)original!;

        void RejectPreparedTamper(
            Func<ProbabilisticLinkageDecision, ProbabilisticLinkageDecision> alter,
            string reason)
        {
            var mutated = frozen.ToDictionary(pair => pair.Key, pair => pair.Value);
            var row = mutated[1L];
            mutated[1L] = (row.Observation, alter(row.Decision));
            try
            {
                field.SetValue(scorer, mutated);
                Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(async () =>
                    await scorer.ReadCompletedGroupingDiagnosticAsync(
                        runId, CancellationToken.None)), reason);
            }
            finally
            {
                field.SetValue(scorer, original);
            }
        }

        RejectPreparedTamper(
            d => d with { MelhorScore = d.MelhorScore + 0.001m },
            "Persisted best posterior must equal TEST scorer's prepared decision.");
        RejectPreparedTamper(
            d => d with { MelhorCandidatoUuid = null },
            "Persisted best candidate must equal TEST scorer's prepared decision.");
        RejectPreparedTamper(
            d => d with { Status = ResolutionStatus.CONFLITO },
            "Persisted status must equal TEST scorer's prepared decision.");
        RejectPreparedTamper(
            d => d with { Motivo = "TAMPER_ONLY" },
            "Persisted abstention reason must equal TEST scorer's prepared decision.");
        RejectPreparedTamper(
            d => d with { SegundoScore = 0.5m },
            "Persisted second posterior must equal TEST scorer's prepared decision.");

        Assert.That((await scorer.ReadCompletedGroupingDiagnosticAsync(
            runId, CancellationToken.None)).Count,
            Is.EqualTo(200), "Prepared V9 TEST source must be restored exactly.");
    }

    private static async Task InsertSyntheticUniverseAsync(SqlConnection connection, int count)
    {
        // Canonical data contracts are inserted, not fake copies of linkage tables.
        // This fixture deliberately has NO candidate in Gold. It tests the
        // full RunAsync materialization/batches/transactions/nonpublication path;
        // positive TF4 candidate parity remains a separate prerequisite.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using (var setup = new SqlCommand("""
            INSERT ref.gestor(codigo,nome) VALUES(N'V9TEST',N'Disposable V9 TEST harness');
            DECLARE @g bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem(gestor_id,codigo,nome)
                VALUES(@g,N'V9TEST',N'Disposable V9 TEST harness');
            DECLARE @s bigint=SCOPE_IDENTITY();
            INSERT ref.base_pessoa_origem(
                codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                VALUES(N'V9TEST',N'Disposable V9 TEST harness',@g,N'PRIVADA',N'NAO_HOMOLOGADA');
            DECLARE @b bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao)
                VALUES(@s,@b,1);
            INSERT ref.gestor_pessoa_versao(
                gestor_id,versao,vigencia_inicio,pessoa_schema_ref,status)
                VALUES(@g,1,'20260101',N'isolated-v9-synthetic',N'RASCUNHO');
            DECLARE @v bigint=SCOPE_IDENTITY();
            INSERT ingestao.entrega(
                entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,
                idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em)
                VALUES(@delivery,@g,@s,@v,N'v9-test-fresh',REPLICATE('0',64),0,
                N'PROCESSADA',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status)
                VALUES(@batch,@delivery,1,1,N'PROCESSADO');
            INSERT ref.gestor(codigo,nome) VALUES(N'V9TEST2',N'Disposable V9 TEST partner');
            DECLARE @g2 bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem(gestor_id,codigo,nome)
                VALUES(@g2,N'V9TEST2',N'Disposable V9 TEST partner');
            DECLARE @s2 bigint=SCOPE_IDENTITY();
            INSERT ref.base_pessoa_origem(
                codigo,nome,gestor_custodiante_id,escopo,confianca_identidade)
                VALUES(N'V9TEST2',N'Disposable V9 TEST partner',@g2,N'PRIVADA',N'NAO_HOMOLOGADA');
            DECLARE @b2 bigint=SCOPE_IDENTITY();
            INSERT ref.sistema_origem_base_pessoa(sistema_origem_id,base_pessoa_origem_id,padrao)
                VALUES(@s2,@b2,1);
            INSERT ref.gestor_pessoa_versao(
                gestor_id,versao,vigencia_inicio,pessoa_schema_ref,status)
                VALUES(@g2,1,'20260101',N'isolated-v9-synthetic',N'RASCUNHO');
            DECLARE @v2 bigint=SCOPE_IDENTITY();
            INSERT ingestao.entrega(
                entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,
                idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em)
                VALUES(@delivery2,@g2,@s2,@v2,N'v9-test-partner-fresh',REPLICATE('0',64),0,
                N'PROCESSADA',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status)
                VALUES(@batch2,@delivery2,1,1,N'PROCESSADO');
            SELECT @g,@s,@b,@g2,@s2,@b2;
            """, connection, transaction))
        {
            setup.Parameters.Add("@delivery", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            setup.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = BatchId;
            setup.Parameters.Add("@delivery2", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            setup.Parameters.Add("@batch2", SqlDbType.UniqueIdentifier).Value = BatchId2;
            await using var reader = await setup.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            GestorId = reader.GetInt64(0);
            SistemaId = reader.GetInt64(1);
            BaseId = reader.GetInt64(2);
            GestorId2 = reader.GetInt64(3);
            SistemaId2 = reader.GetInt64(4);
            BaseId2 = reader.GetInt64(5);
        }

        for (var i = 1; i <= count; i++)
        {
            var code = "V9TEST_" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            var person = SyntheticPersonName(i);
            var mother = SyntheticMotherName(i);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
            await using var add = new SqlCommand("""
                INSERT silver.pessoa_origem(sistema_origem_id,base_pessoa_origem_id,codigo_pessoa_origem)
                    VALUES(@system,@base,@code);
                DECLARE @source bigint=SCOPE_IDENTITY();
                INSERT silver.pessoa_observacao(
                    pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,versao_interna,conteudo_hash,
                    cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
                    VALUES(@source,@batch,@gestor,@code,1,@hash,N'SEM_CPF',
                        @name,@name_cmp,@birth,@mother,@mother_cmp,SYSUTCDATETIME());
                DECLARE @observation bigint=SCOPE_IDENTITY();
                EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@source;
                SELECT @observation;
                """, connection, transaction);
            var partner = i > count / 2;
            add.Parameters.Add("@system", SqlDbType.BigInt).Value = partner ? SistemaId2 : SistemaId;
            add.Parameters.Add("@base", SqlDbType.BigInt).Value = partner ? BaseId2 : BaseId;
            add.Parameters.Add("@gestor", SqlDbType.BigInt).Value = partner ? GestorId2 : GestorId;
            add.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = partner ? BatchId2 : BatchId;
            add.Parameters.Add("@code", SqlDbType.NVarChar, 120).Value = code;
            add.Parameters.Add("@hash", SqlDbType.Char, 64).Value = hash;
            add.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = person;
            add.Parameters.Add("@name_cmp", SqlDbType.NVarChar, 400).Value = IdentityComparison.NormalizeText(person);
            add.Parameters.Add("@mother", SqlDbType.NVarChar, 400).Value = mother;
            add.Parameters.Add("@mother_cmp", SqlDbType.NVarChar, 400).Value = IdentityComparison.NormalizeText(mother);
            add.Parameters.Add("@birth", SqlDbType.Date).Value = new DateTime(1980, 5, 6);
            await using var reader = await add.ExecuteReaderAsync();
            while (reader.FieldCount != 1 && await reader.NextResultAsync()) { }
            Assert.That(reader.FieldCount, Is.EqualTo(1));
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt64(0), Is.GreaterThan(0));
        }
        await transaction.CommitAsync();
    }

    private static readonly Guid BatchId = Guid.NewGuid();
    private static readonly Guid BatchId2 = Guid.NewGuid();
    private static long GestorId;
    private static long SistemaId;
    private static long BaseId;
    private static long GestorId2;
    private static long SistemaId2;
    private static long BaseId2;

    // Two positive, cross-manager, CROSS-BATCH links. Pair (1,101) chooses
    // person/mother FirstName TF; pair (2,102) chooses surname TF.
    // Every other name has disjoint leading prefixes across managers.
    private static string SyntheticPersonName(int id) => id switch
    {
        1 or 101 => "MARIA SILVA",
        2 => "SILVANA SILVA",
        102 => "SILVIA SILVA",
        _ => (id <= 100 ? "ALFA" : "BETA") +
            id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + " SILVA"
    };
    private static string SyntheticMotherName(int id) => id switch
    {
        1 or 101 => "ANA COSTA",
        2 => "COSTELA COSTA",
        102 => "COSTUMO COSTA",
        _ => (id <= 100 ? "CARLA" : "DORA") +
            id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + " COSTA"
    };

    private static async Task SeedDisposableDiagnosticIdentitiesAsync(SqlConnection connection)
    {
        // The full schema has FKs from linkage_resultado.*_uuid to identidade.pessoa.
        // Diagnostic group GUIDs are NEVER inserted into Gold/Serving by RunAsync;
        // these stubs exist only in disposable JornadaHarness.
        await using var gate = new SqlCommand("SELECT DB_NAME();", connection);
        if (!string.Equals((string?)await gate.ExecuteScalarAsync(), "JornadaHarness",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Referências sintéticas fora de JornadaHarness.");
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        for (var i = 1; i <= 200; i++)
        {
            await using var insert = new SqlCommand("""
                INSERT identidade.pessoa(pessoa_uuid,status) VALUES(@id,N'ATIVO');
                """, connection, tx);
            insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value =
                new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4);
            Assert.That(await insert.ExecuteNonQueryAsync(), Is.EqualTo(1));
            // Gold reference is physically persisted BEFORE materialization.
            // A separate production-like component must later read these
            // values back; the fixture itself cannot supply any blocking key.
            await using var gold = new SqlCommand("""
                INSERT gold.pessoa(
                    pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
                    fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em)
                VALUES(
                    @id,NULL,N'SEM_CPF',@name,'1980-05-06',@mother,
                    1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSUTCDATETIME());
                """, connection, tx);
            gold.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value =
                V9OperationalBlockingChaveCohortParity.DiagnosticUuid(i);
            gold.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = SyntheticPersonName(i);
            gold.Parameters.Add("@mother", SqlDbType.NVarChar, 500).Value = SyntheticMotherName(i);
            Assert.That(await gold.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }
        await tx.CommitAsync();
    }

    private static async Task ProveGovernedProcessorHistoryAndPublicationAsync(
        SqlConnection connection, V9FittedSnapshot winner)
    {
        var model = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        Assert.That(model.MaximumPrefixLength, Is.Not.Null);
        var uuid = V9OperationalBlockingChaveCohortParity.DiagnosticUuid(1);
        var before = await ReadGovernedPublicationAndHistoryDigestAsync(connection);

        // This is historical evidence from the REAL operational Silver and
        // vínculo_fonte tables, never an in-memory alias list or truth label.
        // Both observations already belong to distinct source identities in
        // the earlier complete SQL fixture.
        await using (var tx = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await using (var link = new SqlCommand("""
                INSERT identidade.vinculo_fonte(
                    pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,
                    modelo_id,ativo,resolvido_em,motivo)
                SELECT po.pessoa_observacao_id,@uuid,
                       N'UUID_JORNADA_RETROALIMENTACAO',NULL,N'RESOLVIDO',
                       NULL,1,SYSDATETIMEOFFSET(),N'V9_DISPOSABLE_HISTORY_PROOF'
                  FROM silver.pessoa_observacao po
                 WHERE po.codigo_pessoa_origem IN(N'V9TEST_0001',N'V9TEST_0003');
                """, connection, tx))
            {
                link.Parameters.Add("@uuid", SqlDbType.UniqueIdentifier).Value = uuid;
                Assert.That(await link.ExecuteNonQueryAsync(), Is.EqualTo(2));
            }
            await using (var current = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM identidade.v_vinculo_corrente
                 WHERE pessoa_uuid=@uuid AND status=N'RESOLVIDO'
                   AND linkage_run_id IS NULL;
                """, connection, tx))
            {
                current.Parameters.Add("@uuid", SqlDbType.UniqueIdentifier).Value = uuid;
                Assert.That(await current.ExecuteScalarAsync(), Is.EqualTo(2L));
            }

            // Guard failures must happen BEFORE sp_recompor_gold_pessoa and
            // before even a temporary mutation of Gold.
            var badBefore = await ReadGoldReferenceSignatureAsync(connection, tx, uuid);
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await SqlProcessorRepository.RehearseV9GoldHistoryRefreshAsync(
                    connection, tx, uuid, model.MaximumPrefixLength!.Value,
                    new string('a', 64), CancellationToken.None)));
            Assert.That(await ReadGoldReferenceSignatureAsync(connection, tx, uuid),
                Is.EqualTo(badBefore));
            await using (var empty = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM identidade.blocking_chave
                 WHERE pessoa_uuid=@uuid;
                """, connection, tx))
            {
                empty.Parameters.Add("@uuid", SqlDbType.UniqueIdentifier).Value = uuid;
                Assert.That(await empty.ExecuteScalarAsync(), Is.EqualTo(0L));
            }

            // Calls the EXACT RefreshGoldPersonAsync production method:
            // sp_recompor_gold_pessoa first, then V9 projection permitted
            // only by the internal JornadaHarness test gate.
            var rowsWritten = await SqlProcessorRepository.RehearseV9GoldHistoryRefreshAsync(
                connection, tx, uuid, model.MaximumPrefixLength!.Value,
                model.ProjectionFingerprintSha256, CancellationToken.None);
            Assert.That(rowsWritten, Is.GreaterThan(0));

            await using (var recomposed = new SqlCommand("""
                SELECT estado_identidade,completude_nucleo,estado_concordancia
                  FROM gold.pessoa WHERE pessoa_uuid=@uuid;
                """, connection, tx))
            {
                recomposed.Parameters.Add("@uuid", SqlDbType.UniqueIdentifier).Value = uuid;
                await using var rd = await recomposed.ExecuteReaderAsync();
                Assert.That(await rd.ReadAsync(), Is.True);
                Assert.That(rd.GetString(0), Is.EqualTo("REFERENCIA"));
                Assert.That(rd.GetString(1), Is.EqualTo("COMPLETO"));
                Assert.That(rd.GetString(2), Is.EqualTo("DIVERGENTE"));
            }

            var rowsFound = new HashSet<(string Feature, string Value)>();
            var closedHistoricalAliases = 0;
            var currentAliases = 0;
            await using (var check = new SqlCommand("""
                SELECT atributo,valor_normalizado,semantica_temporal,
                       projection_schema_version,projection_fingerprint_sha256,
                       vigencia_inicio,vigencia_fim
                  FROM identidade.blocking_chave WHERE pessoa_uuid=@uuid;
                """, connection, tx))
            {
                check.Parameters.Add("@uuid", SqlDbType.UniqueIdentifier).Value = uuid;
                await using var rd = await check.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var feat = rd.GetString(0);
                    Assert.That(rowsFound.Add((feat, rd.GetString(1))), Is.True);
                    Assert.That(rd.GetString(3), Is.EqualTo(model.ProjectionSchemaVersion));
                    Assert.That(rd.GetString(4), Is.EqualTo(model.ProjectionFingerprintSha256));
                    // Historical aliases are RETAINED even when a newer Gold
                    // name has superseded their temporal validity. The
                    // operational query deliberately accepts old name aliases
                    // while stable features must remain current.
                    if (!rd.IsDBNull(6))
                    {
                        Assert.That(rd.GetDateTimeOffset(6),
                            Is.GreaterThanOrEqualTo(rd.GetDateTimeOffset(5)));
                        Assert.That(rd.GetString(2), Is.EqualTo("VERSIONED_ALIAS"));
                        closedHistoricalAliases++;
                    }
                    else if (rd.GetString(2) == "VERSIONED_ALIAS")
                        currentAliases++;

                    if (feat.StartsWith("name_prefix_", StringComparison.Ordinal)
                        || feat.StartsWith("mother_name_prefix_", StringComparison.Ordinal))
                        Assert.That(rd.GetString(2), Is.EqualTo("VERSIONED_ALIAS"));
                }
            }
            var expected = model.Project(new IdentityObservation(
                    null, "SEM_CPF", SyntheticPersonName(1),
                    new DateOnly(1980, 5, 6), SyntheticMotherName(1)))
                .Concat(model.Project(new IdentityObservation(
                    null, "SEM_CPF", SyntheticPersonName(3),
                    new DateOnly(1980, 5, 6), SyntheticMotherName(3))))
                .Select(key => (key.Feature, key.Value)).ToHashSet();
            Assert.That(rowsFound, Is.EquivalentTo(expected));
            Assert.That(rowsWritten, Is.EqualTo(expected.Count));
            Assert.That(closedHistoricalAliases, Is.GreaterThan(0),
                "Nomes antigos devem sobreviver com vigência encerrada.");
            Assert.That(currentAliases, Is.GreaterThan(0),
                "Nomes da referência Gold corrente devem permanecer vigentes.");

            // Only an explicit first use is allowed; a second invocation
            // cannot overwrite Gold history or previously written keys.
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await SqlProcessorRepository.RehearseV9GoldHistoryRefreshAsync(
                    connection, tx, uuid, model.MaximumPrefixLength!.Value,
                    model.ProjectionFingerprintSha256, CancellationToken.None)));

            // Neither a resolved historical source nor a successful V9 score
            // is sufficient to publish a probabilistic run.
            await using (var gate = new SqlCommand("""
                SELECT (SELECT COUNT_BIG(*) FROM identidade.linkage_run
                         WHERE status=N'PUBLICADO')
                     + (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                         WHERE resultado_publicacao IS NOT NULL);
                """, connection, tx))
                Assert.That(await gate.ExecuteScalarAsync(), Is.EqualTo(0L));
            await tx.RollbackAsync();
        }

        Assert.That(await ReadGovernedPublicationAndHistoryDigestAsync(connection),
            Is.EqualTo(before),
            "Processor V9 shadow must not change ANY Gold, Silver, links, blocking, Serving or publication header.");
        TestContext.WriteLine(
            "V9_PROCESSOR_GOVERNED_HISTORY_PUBLICATION_PASS: canonical Silver history 2 distinct aliases via current vínculos; actual sp_recompor_gold_pessoa and V9 Processor branch; fingerprint guard and reentry; full state hash unchanged after rollback; no publication; 100k blocked");
    }

    private static async Task<string> ReadGoldReferenceSignatureAsync(
        SqlConnection connection, SqlTransaction tx, Guid uuid)
    {
        await using var cmd = new SqlCommand("""
            SELECT nome_completo,nome_mae,estado_identidade,completude_nucleo,
                   atualizado_em
              FROM gold.pessoa WHERE pessoa_uuid=@id FOR JSON PATH;
            """, connection, tx);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = uuid;
        return (string)(await cmd.ExecuteScalarAsync()
            ?? throw new InvalidDataException("Gold de referência ausente."));
    }

    private static async Task<string> ReadGovernedPublicationAndHistoryDigestAsync(
        SqlConnection connection)
    {
        await using var cmd = new SqlCommand("""
            SELECT
              (SELECT g.pessoa_uuid,g.nome_completo,g.nome_mae,g.data_nascimento,
                      g.status_cpf,g.cpf,g.estado_identidade,g.estado_concordancia,
                      g.completude_nucleo,g.atualizado_em
                 FROM gold.pessoa g ORDER BY g.pessoa_uuid FOR JSON PATH),
              (SELECT b.blocking_chave_id,b.pessoa_uuid,b.normalizacao_versao,
                      b.atributo,b.valor_normalizado,b.semantica_temporal,
                      b.vigencia_inicio,b.vigencia_fim,
                      b.projection_schema_version,b.projection_fingerprint_sha256
                 FROM identidade.blocking_chave b ORDER BY b.blocking_chave_id FOR JSON PATH),
              (SELECT v.vinculo_id,v.pessoa_observacao_id,v.pessoa_uuid,
                      v.metodo_resolucao,v.status,v.ativo,v.linkage_run_id,
                      v.resolvido_em,v.motivo
                 FROM identidade.vinculo_fonte v ORDER BY v.vinculo_id FOR JSON PATH),
              (SELECT po.pessoa_observacao_id,po.nome_completo,po.nome_mae,
                      po.data_nascimento,po.source_as_of
                 FROM silver.pessoa_observacao po ORDER BY po.pessoa_observacao_id FOR JSON PATH),
              (SELECT COUNT_BIG(*) FROM serving.registro_integrado),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
                WHERE status=N'PUBLICADO'),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                WHERE publicado_em IS NOT NULL);
            """, connection) { CommandTimeout = 120 };
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        var all = new StringBuilder();
        for (var i = 0; i < reader.FieldCount; i++)
            all.Append(i).Append('|').Append(reader.IsDBNull(i)
                ? "<NULL>" : Convert.ToString(reader.GetValue(i),
                    System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            all.ToString()))).ToLowerInvariant();
    }

    private static async Task SeedDisposableOperationalBlockingProjectionAsync(
        SqlConnection connection, V9FittedSnapshot winner)
    {
        // No direct insertion of identidade.blocking_chave by the fixture:
        // the real Gold rows are loaded by the isolated operational materializer
        // and projected with the immutable V9 Contracts component.
        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        Assert.That(planner.MaximumPrefixLength, Is.Not.Null);
        var uuids = Enumerable.Range(1, 200)
            .Select(V9OperationalBlockingChaveCohortParity.DiagnosticUuid).ToArray();

        // Negative: a non-reference Gold person must abort the entire
        // operation and leave NO partial physical projection rows.
        await using (var tamper = new SqlCommand("""
            UPDATE gold.pessoa SET estado_identidade=N'INDEFINIDA'
             WHERE pessoa_uuid=@id;
            """, connection))
        {
            tamper.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = uuids[2];
            Assert.That(await tamper.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9DisposableGoldBlockingProjectionMaterializer.MaterializeAsync(
                connection, uuids, planner.MaximumPrefixLength!.Value,
                planner.ProjectionFingerprintSha256, CancellationToken.None)));
        await using (var noPartial = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave;
            """, connection))
            Assert.That(await noPartial.ExecuteScalarAsync(), Is.EqualTo(0L));

        await using (var restore = new SqlCommand("""
            UPDATE gold.pessoa SET estado_identidade=N'REFERENCIA'
             WHERE pessoa_uuid=@id;
            """, connection))
        {
            restore.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = uuids[2];
            Assert.That(await restore.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        // Processor-native compatibility seam: uses the SAME Gold/history
        // snapshot loader as the actual legacy Processor refresh, but writes
        // the exact frozen V9 projection for a NEW reference. Keep the whole
        // rehearsal in a transaction that must ROLLBACK: the independent
        // 200-Gold set-based materializer is tested separately afterwards.
        await using (var shadow = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BlockingProjectionPersistence.MaterializeNewPinnedV9SqlServerAsync(
                    connection, shadow, uuids[0], planner.MaximumPrefixLength!.Value,
                    new string('a', 64), CancellationToken.None)));

            var nativeRows = await BlockingProjectionPersistence.MaterializeNewPinnedV9SqlServerAsync(
                connection, shadow, uuids[0], planner.MaximumPrefixLength!.Value,
                planner.ProjectionFingerprintSha256, CancellationToken.None);
            Assert.That(nativeRows, Is.GreaterThan(0));

            var actualKeys = new List<string>();
            await using (var check = new SqlCommand("""
                SELECT atributo,valor_normalizado,projection_schema_version,
                       projection_fingerprint_sha256,semantica_temporal
                  FROM identidade.blocking_chave
                 WHERE pessoa_uuid=@id ORDER BY atributo,valor_normalizado;
                """, connection, shadow))
            {
                check.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = uuids[0];
                await using var reader = await check.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    actualKeys.Add(reader.GetString(0) + "|" + reader.GetString(1));
                    Assert.That(reader.GetString(2),
                        Is.EqualTo(V9CalibrationBlockingProjection.SchemaVersion));
                    Assert.That(reader.GetString(3),
                        Is.EqualTo(planner.ProjectionFingerprintSha256));
                    var feature = reader.GetString(0);
                    if (feature.StartsWith("name_prefix_", StringComparison.Ordinal)
                        || feature.StartsWith("mother_name_prefix_", StringComparison.Ordinal))
                        Assert.That(reader.GetString(4), Is.EqualTo("VERSIONED_ALIAS"));
                }
            }
            var expectedKeys = planner.Project(new IdentityObservation(
                null, "SEM_CPF", SyntheticPersonName(1), new DateOnly(1980, 5, 6),
                SyntheticMotherName(1)))
                .Select(key => key.Feature + "|" + key.Value).Distinct().ToArray();
            Assert.That(actualKeys, Is.EquivalentTo(expectedKeys));
            Assert.That(nativeRows, Is.EqualTo(expectedKeys.Length));

            // First-use only. Never replace an operational V9/legacy index
            // silently, even inside an isolated transaction.
            Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await BlockingProjectionPersistence.MaterializeNewPinnedV9SqlServerAsync(
                    connection, shadow, uuids[0], planner.MaximumPrefixLength!.Value,
                    planner.ProjectionFingerprintSha256, CancellationToken.None)));

            // EXISTING legacy path is unchanged and produces its V2 projection
            // alongside a separate V9 reference inside the same transaction.
            await BlockingProjectionPersistence.RefreshSqlServerAsync(
                connection, shadow, uuids[1], CancellationToken.None);
            await using (var legacy = new SqlCommand("""
                SELECT DISTINCT projection_schema_version,projection_fingerprint_sha256
                  FROM identidade.blocking_chave WHERE pessoa_uuid=@id;
                """, connection, shadow))
            {
                legacy.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = uuids[1];
                await using var rows = await legacy.ExecuteReaderAsync();
                Assert.That(await rows.ReadAsync(), Is.True);
                Assert.That(rows.GetString(0), Is.EqualTo(PersonResolutionProjectionContract.SchemaVersion));
                Assert.That(rows.GetString(1), Is.EqualTo(PersonResolutionProjectionContract.FingerprintSha256));
                Assert.That(await rows.ReadAsync(), Is.False);
            }
            await shadow.RollbackAsync();
        }
        await using (var unchanged = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.blocking_chave;
            """, connection))
            Assert.That(await unchanged.ExecuteScalarAsync(), Is.EqualTo(0L));

        var receipt = await V9DisposableGoldBlockingProjectionMaterializer.MaterializeAsync(
            connection, uuids, planner.MaximumPrefixLength!.Value,
            planner.ProjectionFingerprintSha256, CancellationToken.None);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(receipt.ReadGoldReferences, Is.EqualTo(200));
            Assert.That(receipt.MaterializedBlockingRows, Is.GreaterThan(200));
            Assert.That(receipt.ProjectionSchemaVersion,
                Is.EqualTo(V9CalibrationBlockingProjection.SchemaVersion));
            Assert.That(receipt.ProjectionFingerprintSha256,
                Is.EqualTo(planner.ProjectionFingerprintSha256));
            Assert.That(receipt.CanonicalKeysSha256, Has.Length.EqualTo(64));
            Assert.That(receipt.ProductionGoldPublicationCertified, Is.False);
        }));
        // Strict one-use: re-materializing the same active SQL index is
        // rejected rather than silently replacing existing published keys.
        Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await V9DisposableGoldBlockingProjectionMaterializer.MaterializeAsync(
                connection, uuids, planner.MaximumPrefixLength!.Value,
                planner.ProjectionFingerprintSha256, CancellationToken.None)));
        TestContext.WriteLine("V9_PROCESSOR_NATIVE_V9_PROJECTION_SHADOW_PASS: Gold/history reader -> V9 SQL keys; legacy V2 remains unchanged; shadow rollback leaves no keys; TEST 100k blocked");
        TestContext.WriteLine("V9_TEST_GOLD_BACKED_PROJECTION_PASS: 200 physical Gold references; V9 prefix keys; invalid-Gold rollback; exact fingerprint; re-entry rejected; scientific TEST 100k blocked");
    }

    /// <summary>
    /// Independent numeric and membership oracle for the *real SQL persisted*
    /// 200-row disposable V9 RunAsync fixture. It uses only immutable model
    /// parameters, hard-coded input edges and expected nominal states; neither
    /// ReplayTest nor RankPreview nor the grouping/scoring kernel calculates
    /// any expected score or decision. Not a production certification.
    /// </summary>
    private static async Task AssertNonCircularSqlCandidateOracleAsync(
        SqlConnection connection, Guid runId, V9FittedSnapshot winner)
    {
        var payload = winner.Payload;
        Assert.That(payload.UnitTfCalibrations, Has.Count.EqualTo(4));
        Assert.That(payload.JointCells, Has.Count.EqualTo(81));
        Assert.That(payload.CandidateGrouping?.CandidateGrouping,
            Is.EqualTo("BLIND_FS_COMPLETE_LINK_CANDIDATES"));

        decimal ExpectedPosterior(CompositeNominalLevelV9 nominal)
        {
            var cell = payload.JointCells.Single(x => x.Person == nominal && x.Mother == nominal);
            var prior = payload.Parameters[LinkageParameterCatalog.PriorMatchProbability];
            double odds = (double)(prior / (1m - prior))
                * (double)(cell.M / cell.U)
                * (double)(payload.Parameters["M_DATA_NASCIMENTO_EXACT"]
                    / payload.Parameters["U_DATA_NASCIMENTO_EXACT"]);
            var unit = nominal == CompositeNominalLevelV9.ExactFull
                ? NominalFrequencyUnitV9.FirstName
                : NominalFrequencyUnitV9.LastContentSurname;
            foreach (var side in new[] { CompositeNominalTfSideV9.Person, CompositeNominalTfSideV9.Mother })
            {
                var frozen = payload.UnitTfCalibrations!.Single(x =>
                    x.Side == side && x.Unit == unit);
                Assert.That(frozen.State, Is.EqualTo(V9SnapshotTfFitState.Active));
                var tf = frozen.Calibration!;
                var literalTerm = unit == NominalFrequencyUnitV9.FirstName
                    ? side == CompositeNominalTfSideV9.Person ? "MARIA" : "ANA"
                    : side == CompositeNominalTfSideV9.Person ? "SILVA" : "COSTA";
                var effectiveFrequency = Math.Max(tf.Frequencies[literalTerm], tf.MinimumFrequency);
                // Closed-form odds: U_ref(TRAIN) / f_IBGE(published), to the frozen weight.
                odds *= Math.Pow((double)(tf.NominalUReference!.Probability / effectiveFrequency),
                    (double)(tf.Weight ?? 0m));
            }
            return Math.Round((decimal)(odds / (1d + odds)), 8, MidpointRounding.AwayFromZero);
        }

        var fullScore = ExpectedPosterior(CompositeNominalLevelV9.ExactFull);
        var lastScore = ExpectedPosterior(CompositeNominalLevelV9.ExactLast);
        Assert.That(fullScore, Is.GreaterThan(0m).And.LessThan(payload.Threshold));
        Assert.That(lastScore, Is.GreaterThan(0m).And.LessThan(payload.Threshold));
        Assert.That(fullScore, Is.LessThan(payload.CandidateGrouping!.MinimumPairPosterior));
        Assert.That(lastScore, Is.LessThan(payload.CandidateGrouping!.MinimumPairPosterior));
        // The frozen synthetic graph has precisely these four directed candidates.
        // Grouping must leave all 200 IDs unmerged because neither edge qualifies
        // for the 0.99 complete-link floor. Synthetic truth is never consulted.
        var directed = new Dictionary<long, (int Candidate, decimal Score)>
        {
            [1] = (101, fullScore),
            [101] = (1, fullScore),
            [2] = (102, lastScore),
            [102] = (2, lastScore)
        };
        await using var query = new SqlCommand("""
            SELECT pessoa_observacao_id,status,pessoa_uuid_resolvido,
                   melhor_candidato_uuid,score_melhor,motivo
              FROM identidade.linkage_resultado
             WHERE linkage_run_id=@run
             ORDER BY pessoa_observacao_id;
            """, connection);
        query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await query.ExecuteReaderAsync();
        for (long id = 1; id <= 200; id++)
        {
            Assert.That(await reader.ReadAsync(), Is.True, $"Missing SQL row {id}");
            Assert.That(reader.GetInt64(0), Is.EqualTo(id));
            Assert.That(reader.GetString(1), Is.EqualTo("NAO_RESOLVIDO"), $"SQL row {id}");
            Assert.That(reader.IsDBNull(2), Is.True, $"Row {id} published a resolved identity");
            if (directed.TryGetValue(id, out var match))
            {
                // A non-resolved decision may retain its best candidate for
                // audit. It MUST NOT produce a resolved identity or merge groups.
                var literalGroupUuid = new Guid(match.Candidate, 0, 0,
                    0, 0, 0, 0, 0, 0, 0, 4);
                Assert.That(reader.IsDBNull(3), Is.False, $"Missing best candidate {id}");
                Assert.That(reader.GetGuid(3), Is.EqualTo(literalGroupUuid));
                Assert.That(reader.GetDecimal(4),
                    Is.EqualTo(match.Score).Within(0.00000001m), $"Score drift for {id}");
                Assert.That(reader.GetString(5), Is.EqualTo("ABAIXO_T_LINKAGE"), $"Reason {id}");
            }
            else
            {
                Assert.That(reader.IsDBNull(3), Is.True, $"Unexpected candidate on {id}");
                Assert.That(reader.GetDecimal(4), Is.Zero, $"Unexpected score on {id}");
                Assert.That(reader.GetString(5), Is.EqualTo("SEM_CANDIDATO_NO_RULESET_BLOCKING"),
                    $"Unreconciled abstention reason {id}");
            }
        }
        Assert.That(await reader.ReadAsync(), Is.False, "SQL result set is truncated or has surplus rows");
    }

    private static async Task AssertFrozenRunDecisionsAsync(
        SqlConnection connection, Guid runId, V9FrozenSourceReplayResult reference)
    {
        await using var query = new SqlCommand("""
            SELECT pessoa_observacao_id,status,pessoa_uuid_resolvido,
                   melhor_candidato_uuid,score_melhor,motivo
              FROM identidade.linkage_resultado
             WHERE linkage_run_id=@run ORDER BY pessoa_observacao_id;
            """, connection);
        query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await query.ExecuteReaderAsync();
        foreach (var expected in reference.Decisions.OrderBy(x => x.ObservationId))
        {
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt64(0), Is.EqualTo(expected.ObservationId));
            Assert.That(reader.GetString(1), Is.EqualTo(expected.Decision.Status.ToString()));
            Assert.That((object?)(reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2)),
                Is.EqualTo(expected.Decision.PessoaUuidResolvido));
            Assert.That((object?)(reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3)),
                Is.EqualTo(expected.Decision.MelhorCandidatoUuid));
            Assert.That(reader.GetDecimal(4), Is.EqualTo(expected.Decision.MelhorScore).Within(0.00000001m));
            Assert.That(reader.IsDBNull(5) ? null : reader.GetString(5),
                Is.EqualTo(expected.Decision.Motivo));
        }
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private static async Task<string> ReadPublicationFingerprintAsync(SqlConnection connection)
    {
        await using var cmd = new SqlCommand("""
            SELECT CONCAT(
              (SELECT COUNT_BIG(*) FROM gold.pessoa),'|',
              (SELECT COUNT_BIG(*) FROM serving.registro_integrado),'|',
              (SELECT COUNT_BIG(*) FROM identidade.v_vinculo_corrente));
            """, connection);
        return (string)(await cmd.ExecuteScalarAsync()
            ?? throw new InvalidDataException("Fingerprint de Gold/Serving ausente."));
    }

    private static async Task AssertRunRowsAsync(SqlConnection connection, Guid runId,
        string expectedStatus, int expectedRows)
    {
        await using var cmd = new SqlCommand("""
            SELECT r.status, r.avaliados,
                   (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                    WHERE linkage_run_id=r.linkage_run_id)
            FROM identidade.linkage_run r WHERE r.linkage_run_id=@id;
            """, connection);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(expectedStatus));
        Assert.That(reader.GetInt64(1), Is.EqualTo(expectedRows));
        Assert.That(reader.GetInt64(2), Is.EqualTo(expectedRows));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private sealed class PinnedV9DisposableLinkage(
        IOperationalSqlAdapter sql,
        ProbabilisticLinkageModelRef model,
        V9FittedSnapshotExecutionRuntime runtime,
        int failAfter) : IProbabilisticIdentityLinkage
    {
        private int scored;

        public Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct) =>
            throw new InvalidOperationException("E2E V9 proíbe modelo ATIVO e fallback.");

        public Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(int version, CancellationToken ct) =>
            version == model.Version ? Task.FromResult(model)
                : throw new InvalidOperationException("Versão de modelo V9 não pinada.");

        public async Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
            IdentityObservation observation, Guid modelId, CancellationToken ct)
        {
            if (modelId != model.ModelId || observation.Cpf is not null)
                throw new InvalidDataException("Modelo V9 alterado ou CPF inesperado.");
            if (Interlocked.Increment(ref scored) > failAfter)
                throw new InvalidOperationException("V9_SYNTHETIC_SECOND_BATCH_INJECTED_FAILURE");
            await using var connection = await sql.OpenAsync(ct);
            await using var db = connection.CreateCommand();
            db.CommandText = "SELECT DB_NAME();";
            if (!string.Equals((string?)await db.ExecuteScalarAsync(ct), "JornadaHarness",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("E2E V9 fora de JornadaHarness.");
            await using var cmd = connection.CreateCommand();
            cmd.CommandTimeout = 120;
            var query = runtime.BuildCandidateUuidQuery(
                cmd, observation, runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256);
            cmd.CommandText = "SELECT g.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae " +
                "FROM (" + query + ") c JOIN gold.pessoa g ON g.pessoa_uuid=c.pessoa_uuid " +
                "WHERE g.estado_identidade=N'REFERENCIA' ORDER BY g.pessoa_uuid;";
            var candidates = new List<V9SnapshotCandidate>();
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                    candidates.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : DateOnly.FromDateTime(reader.GetDateTime(2)),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
            return runtime.ResolveRetrievedCandidatesPreview(
                modelId, observation, new(runtime.SnapshotSha256,
                    runtime.ProjectionSchemaVersion, runtime.ProjectionFingerprintSha256,
                    true, candidates));
        }
    }
}

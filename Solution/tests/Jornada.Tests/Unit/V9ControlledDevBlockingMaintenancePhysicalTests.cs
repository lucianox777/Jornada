using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Jornada.Processor.Worker;
using Jornada.Bronze.Storage;
using Jornada.Pipeline.Coordination;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

public sealed partial class V9ControlledDevFullSqlR1Tests
{
    // This test enters the actual Processor repository with a synthetic
    // reserved delivery and validated package. It does not claim HTTP or
    // Bronze admission parity. No projection key is seeded by the fixture.
    private static async Task<ReservedBatch> ReserveRf1936FixtureAsync(SqlConnection db, string gestor)
    {
        var delivery = Guid.NewGuid();
        var batch = Guid.NewGuid();
        var lease = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string owner = "V9_RF1936_FIXTURE";
        await using var cmd = new SqlCommand("""
            DECLARE @g bigint=(SELECT gestor_id FROM ref.gestor WHERE codigo=@gestor);
            DECLARE @s bigint=(SELECT sistema_origem_id FROM ref.sistema_origem WHERE gestor_id=@g);
            DECLARE @v bigint=(SELECT gestor_pessoa_versao_id FROM ref.gestor_pessoa_versao WHERE gestor_id=@g);
            INSERT ingestao.entrega(entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,
                idempotency_key,payload_sha256,bytes_recebidos,status,data_referencia,recebido_em)
            VALUES(@delivery,@g,@s,@v,CONVERT(nvarchar(36),@delivery),REPLICATE('0',64),0,
                N'VALIDANDO',@now,@now);
            INSERT ingestao.lote(lote_id,entrega_id,lote_seq,lote_total,status,
                lease_id,lease_owner,lease_adquirido_em,heartbeat_em,lease_expira_em,tentativa_count)
            VALUES(@batch,@delivery,1,1,N'VALIDANDO',@lease,@owner,@now,@now,DATEADD(minute,5,@now),1);
            INSERT ingestao.lote_heartbeat(lote_id,lease_id,lease_owner,heartbeat_em,lease_expira_em)
            VALUES(@batch,@lease,@owner,@now,DATEADD(minute,5,@now));
            SELECT @g,@s,@v;
            """, db);
        cmd.Parameters.Add("@gestor", SqlDbType.NVarChar, 120).Value = gestor;
        cmd.Parameters.Add("@delivery", SqlDbType.UniqueIdentifier).Value = delivery;
        cmd.Parameters.Add("@batch", SqlDbType.UniqueIdentifier).Value = batch;
        cmd.Parameters.Add("@lease", SqlDbType.UniqueIdentifier).Value = lease;
        cmd.Parameters.Add("@owner", SqlDbType.NVarChar, 200).Value = owner;
        cmd.Parameters.Add("@now", SqlDbType.DateTimeOffset).Value = now;
        await using var rows = await cmd.ExecuteReaderAsync();
        Assert.That(await rows.ReadAsync(), Is.True);
        return new ReservedBatch(batch, delivery, lease, owner, 1, gestor,
            rows.GetInt64(0), rows.GetInt64(1), gestor, rows.GetInt64(2),
            null, null, null, null, null, 1, now, new string('0',64),
            "synthetic.zip", "not-used-in-repository-test", 0, "isolated-v9-synthetic",
            new byte[32], null, null, null, false, null, null, null);
    }

    private static ParsedPerson Rf1936Person(string code, string? cpf, string name, string mother) =>
        new(code, code, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
            null, cpf, cpf is null ? "SEM_CPF" : null, name, new DateOnly(1980, 5, 6), mother, [], []);

    private static ParsedPackage Rf1936Package(ReservedBatch batch, params ParsedPerson[] people) =>
        new(new IngestionPackageManifest(2, batch.PessoaSchemaVersao, batch.CodigoSistemaOrigem,
            null, null, null, batch.DataReferencia), people, []);

    private static async Task<string> Rf1936DigestAsync(SqlConnection db)
    {
        // Include every physical field and the coverage receipt; no nominal
        // values or synthetic CPF are printed in the diagnostic receipt.
        await using var cmd = new SqlCommand("""
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(
                (SELECT * FROM identidade.blocking_chave_v9 ORDER BY blocking_chave_id
                 FOR JSON PATH, INCLUDE_NULL_VALUES),
                N'|',
                (SELECT * FROM identidade.blocking_chave_v9_cobertura
                 ORDER BY normalizacao_versao,projection_schema_version,projection_fingerprint_sha256
                 FOR JSON PATH, INCLUDE_NULL_VALUES))),2);
            """, db);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string> Rf1936LegacyDigestAsync(SqlConnection db)
    {
        await using var cmd = new SqlCommand("""
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',
                (SELECT * FROM identidade.blocking_chave ORDER BY blocking_chave_id
                 FOR JSON PATH, INCLUDE_NULL_VALUES)),2);
            """, db);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task VerifyRf1936Async(SqlConnection db, IOperationalSqlAdapter sql,
        Guid attempt, V9FittedSnapshot winner)
    {
        await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(db, 0);
        Assert.That(await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9;"), Is.Zero);
        var temp = Path.Combine(Path.GetTempPath(), "jornada-rf1936-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var file = await V9ValidatedOperationalModelFile.ExportAsync(winner, temp);
            var selection = Path.Combine(temp, "selection.json");
            await File.WriteAllTextAsync(selection, JsonSerializer.Serialize(
                new V9ConfiguredArtifactLoader.Selection(file.FileName, winner.ContentSha256)));
            var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
            var prefix = planner.MaximumPrefixLength!.Value;
            var fingerprint = planner.ProjectionFingerprintSha256;
            var plan = new SqlProcessorRepository.V9MaintenanceSettings("Development", true, attempt, prefix, fingerprint);
            var processor = new SqlProcessorRepository(sql, new RegistryQualityEngine([]), plan);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Linkage:SelectedModelVersion"] = "9",
                ["Linkage:V9SelectionFile"] = selection,
                [V9BlockingMaintenanceGate.EnabledSetting] = "true",
                [V9BlockingMaintenanceGate.AttemptSetting] = attempt.ToString("D"),
                ["LinkageReplay:CaptureBronzeSources"] = "false"
            }).Build();

            async Task PersistAsync(SqlProcessorRepository repo, string gestor, params ParsedPerson[] people)
            {
                var batch = await ReserveRf1936FixtureAsync(db, gestor);
                await repo.PersistValidatedAsync(batch, Rf1936Package(batch, people), CancellationToken.None);
            }

            async Task<ProbabilisticLinkageBatchRunner> RunnerAsync()
            {
                var scorer = await V9ControlledDevCohortScorer.CreateAsync(config, sql, attempt,
                    CancellationToken.None, effectiveEnvironment: "Development");
                return new ProbabilisticLinkageBatchRunner(config, sql, scorer, new SqlPipelineCoordinator(sql),
                    new Dt05ReplaySql(sql),
                    new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(temp), temp),
                    new Dt05CandidateStateSnapshotPublisher(sql, temp),
                    new Dt05BlockingProjectionSnapshotPublisher(sql, temp),
                    new Dt05HistoricalCandidateStateVerifier(temp),
                    new Dt05HistoricalBlockingProjectionVerifier(temp),
                    NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
            }

            async Task<Guid> FullAsync(int expected)
            {
                var runner = await RunnerAsync();
                var result = await runner.RunV9ControlledDevAsync(new ProbabilisticLinkageRunRequest(
                    LinkageRunType.FULL, 9, null, null, null, 100, 1, null, "RF1936",
                    "REAL_AUTOMATIC_PROJECTION", Guid.NewGuid(), true), attempt, CancellationToken.None);
                Assert.That(result.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
                Assert.That(result.Elegiveis, Is.EqualTo(expected));
                Assert.That(result.Avaliados, Is.EqualTo(expected));
                return result.RunId;
            }

            async Task AssertCandidateAsync(IdentityObservation observation, Guid expected)
            {
                await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot);
                await V9BlockingMaintenanceGate.RequireAsync("Development", true, attempt, db, tx, CancellationToken.None);
                await BlockingProjectionPersistence.VerifyPinnedV9ProjectionAsync(db, tx, attempt, prefix, fingerprint,
                    CancellationToken.None);
                var result = await V9OperationalCandidateRead.ReadAsync(db, tx, planner, observation,
                    CancellationToken.None, useIsolatedProjection: true);
                Assert.That(result.CandidateUuids, Is.EqualTo(new[] { expected }));
                await tx.CommitAsync();
            }

            // Actual Processor persists a CPF reference and an unrelated
            // pending source. The next independent origin has no CPF.
            await PersistAsync(processor, "V9TEST",
                Rf1936Person("RF1936_CPF", "52998224725", "MARIA SILVA", "ANA COSTA"),
                Rf1936Person("RF1936_FOUNDER", null, "ALFA0003 SILVA", "CARLA0003 COSTA"));
            await PersistAsync(processor, "V9TEST2",
                Rf1936Person("RF1936_FIRST", null, "MARIA SILVA", "ANA COSTA"));
            var cpf = (await ReadPr1RowsAsync(db, """
                SELECT g.pessoa_uuid FROM gold.pessoa g JOIN identidade.cpf_ancora a
                    ON a.pessoa_uuid=g.pessoa_uuid
                 WHERE g.status_cpf=N'PRESENTE' AND g.estado_identidade=N'REFERENCIA';
                """)).Single()[0];
            var cpfUuid = (Guid)cpf!;
            var expectedKeys = V9CalibrationBlockingProjection.Project("MARIA SILVA", "ANA COSTA",
                new DateOnly(1980,5,6), prefix).Select(k => (k.Feature,k.Value)).ToHashSet();
            var keyRows = await ReadPr1RowsAsync(db, """
                SELECT atributo,valor_normalizado FROM identidade.blocking_chave_v9;
                """);
            Assert.That(keyRows.Select(r => ((string)r[0]!, (string)r[1]!)).ToHashSet().SetEquals(expectedKeys), Is.True);
            Assert.That(keyRows.Count, Is.EqualTo(expectedKeys.Count));
            Assert.That(await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.blocking_chave;"), Is.GreaterThan(0));
            await AssertCandidateAsync(new IdentityObservation(null,"SEM_CPF","MARIA SILVA",
                new DateOnly(1980,5,6),"ANA COSTA"), cpfUuid);
            var first = await FullAsync(2);
            var firstRows = await ReadPr1RowsAsync(db, Pr1Decisions, first);
            var target = firstRows.Single(r => Equals(r[0],"RF1936_FIRST"));
            Assert.That(target[2], Is.EqualTo("RESOLVIDO"));
            Assert.That(target[4], Is.EqualTo(cpfUuid));
            Assert.That(target[13], Is.EqualTo(cpfUuid));
            var founder = firstRows.Single(r => Equals(r[0],"RF1936_FOUNDER"));
            Assert.That(founder[10], Is.EqualTo("NOVA_IDENTIDADE"));
            var founderUuid = (Guid)founder[13]!;
            await AssertCandidateAsync(new IdentityObservation(null,"SEM_CPF","ALFA0003 SILVA",
                new DateOnly(1980,5,6),"CARLA0003 COSTA"), founderUuid);

            // The second FULL finds the Runner-founded reference without
            // replacing or manually materializing a single key.
            await PersistAsync(processor, "V9TEST2",
                Rf1936Person("RF1936_SECOND", null, "ALFA0003 SILVA", "CARLA0003 COSTA"));
            var second = await FullAsync(3);
            var secondRows = await ReadPr1RowsAsync(db, Pr1Decisions, second);
            var secondTarget = secondRows.Single(r => Equals(r[0],"RF1936_SECOND"));
            Assert.That(secondTarget[2], Is.EqualTo("RESOLVIDO"));
            Assert.That(secondTarget[4], Is.EqualTo(founderUuid));
            Assert.That(secondTarget[13], Is.EqualTo(founderUuid));
            var third = await FullAsync(3);
            var thirdRows = await ReadPr1RowsAsync(db, Pr1Decisions, third);
            foreach (var row in secondRows)
                Assert.That(thirdRows.Single(r => Equals(r[0],row[0])).Take(10), Is.EqualTo(row.Take(10)));
            var before = await Rf1936DigestAsync(db);
            var legacyBefore = await Rf1936LegacyDigestAsync(db);
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                await BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, tx, "Development", true,
                    attempt, prefix, fingerprint, CancellationToken.None);
                await BlockingProjectionPersistence.RefreshPinnedV9Async(db, tx, "Development", true,
                    attempt, prefix, fingerprint, CancellationToken.None);
                await tx.CommitAsync();
            }
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before), "Exact physical replay with no changed Gold is idempotent");
            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBefore), "V9 refresh cannot mutate legacy keys");

            // Nonempty-table negatives, including a DML tripwire. An
            // attempted mutation cannot be hidden by transaction rollback.
            await using (var tripwire = new SqlCommand("""
                CREATE TRIGGER identidade.tr_rf1936_no_dml ON identidade.blocking_chave_v9
                AFTER INSERT,UPDATE,DELETE AS THROW 52990,'Unexpected V9 DML in denied scenario.',1;
                """, db))
                await tripwire.ExecuteNonQueryAsync();
            await using (var tripwire = new SqlCommand("""
                CREATE TRIGGER identidade.tr_rf1936_coverage_no_dml ON identidade.blocking_chave_v9_cobertura
                AFTER INSERT,UPDATE,DELETE AS THROW 52991,'Unexpected V9 coverage DML in denied scenario.',1;
                """, db))
                await tripwire.ExecuteNonQueryAsync();
            foreach (var environment in new[] { "HML", "Production", "Homologation", "PROD", "", "Unknown" })
            {
                var deniedProcessor = new SqlProcessorRepository(sql, new RegistryQualityEngine([]),
                    plan with { EffectiveEnvironment = environment });
                var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() => PersistAsync(deniedProcessor,
                    "V9TEST", Rf1936Person("RF1936_DENIED_" + environment, null,"MARIA SILVA","ANA COSTA"))));
                Assert.That(error!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                var runnerError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    V9ControlledDevCohortScorer.CreateAsync(config, sql, attempt, CancellationToken.None, environment)));
                Assert.That(runnerError!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
                Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBefore));
            }
            // Default-off actual Processor path, only a new provisional
            // origin. The isolated projection must remain untouched.
            var defaultProcessor = new SqlProcessorRepository(sql, new RegistryQualityEngine([]));
            await PersistAsync(defaultProcessor, "V9TEST",
                Rf1936Person("RF1936_DEFAULT_OFF", null,"BETA0103 SILVA","DORA0103 COSTA"));
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
            var defaultConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Linkage:SelectedModelVersion"] = "9", ["Linkage:V9SelectionFile"] = selection
            }).Build();
            Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                V9ControlledDevCohortScorer.CreateAsync(defaultConfig, sql, attempt, CancellationToken.None, "Development")))!
                .Message, Is.EqualTo("RUNNER_V9_BLOCKING_EXPLICIT_PATH_REQUIRED"));

            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBefore));

            // Actual producer admissions with populated projection and both
            // DML tripwires armed. Wrong/missing resident metadata is tested
            // in this disposable DEV DB only; restore it even on failure.
            var negativeIndex = 0;
            async Task AssertWrongDatabaseAsync(Guid requestedAttempt)
            {
                var denied = new SqlProcessorRepository(sql, new RegistryQualityEngine([]),
                    plan with { AttemptId = requestedAttempt });
                var processorError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    PersistAsync(denied, "V9TEST", Rf1936Person(
                        "RF1936_BAD_DB_" + (++negativeIndex).ToString(CultureInfo.InvariantCulture),
                        null, "MARIA SILVA", "ANA COSTA"))));
                Assert.That(processorError!.Message, Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
                var runnerError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    V9ControlledDevCohortScorer.CreateAsync(config, sql, requestedAttempt,
                        CancellationToken.None, "Development")));
                Assert.That(runnerError!.Message, Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
                await using (var deniedTx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    var refreshError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                        BlockingProjectionPersistence.RefreshPinnedV9Async(db, deniedTx, "Development", true,
                            requestedAttempt, prefix, fingerprint, CancellationToken.None)));
                    Assert.That(refreshError!.Message, Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
                    await deniedTx.RollbackAsync();
                }
                Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
                Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBefore));
            }

            async Task SetResidentAsync(string name, string? value)
            {
                await using var change = new SqlCommand("""
                    IF @value IS NULL
                    BEGIN
                        IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=@name)
                            EXEC sys.sp_dropextendedproperty @name=@name;
                    END
                    ELSE IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=@name)
                        EXEC sys.sp_updateextendedproperty @name=@name,@value=@value;
                    ELSE
                        EXEC sys.sp_addextendedproperty @name=@name,@value=@value;
                    """, db);
                change.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = name;
                change.Parameters.Add("@value", SqlDbType.NVarChar, 200).Value = (object?)value ?? DBNull.Value;
                await change.ExecuteNonQueryAsync();
            }

            await AssertWrongDatabaseAsync(Guid.NewGuid()); // wrong physical database name for attempt
            foreach (var mismatch in new (string Name, string? Wrong, string Correct)[]
            {
                ("Jornada.EnvironmentProfile", null, "Development"),
                ("Jornada.EnvironmentProfile", "Production", "Development"),
                ("Jornada.V9.DevAttempt", null, attempt.ToString("D")),
                ("Jornada.V9.DevAttempt", Guid.NewGuid().ToString("D"), attempt.ToString("D"))
            })
            {
                try
                {
                    await SetResidentAsync(mismatch.Name, mismatch.Wrong);
                    await AssertWrongDatabaseAsync(attempt);
                }
                finally
                {
                    await SetResidentAsync(mismatch.Name, mismatch.Correct);
                }
                await V9DisposableAttemptDatabase.VerifyAsync(db, attempt, CancellationToken.None);
            }
            TestContext.WriteLine("V9_RF1936_C07_DB_NAME_RESIDENT_PROPERTIES_AND_PRODUCERS_PASS");

            // Physical negative for wrong attempt, and actual versioned
            // reversal script against already populated V9/legacy tables.
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, tx, "Development", true,
                        Guid.NewGuid(), prefix, fingerprint, CancellationToken.None)))!.Message,
                    Is.EqualTo("RUNNER_V9_DEV_DISPOSABLE_SQL_NOT_VERIFIED"));
                await tx.RollbackAsync();
            }
            async Task ExecuteScriptAsync(string relative)
            {
                var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", relative));
                var batch = string.Join("\n", script.Split('\n').Where(line =>
                    !string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase)));
                await using var command = new SqlCommand(batch, db);
                await command.ExecuteNonQueryAsync();
            }
            var legacyBeforeReversal = await Rf1936LegacyDigestAsync(db);
            await ExecuteScriptAsync("rollback/20261124_V9_Blocking_Chave_Disable.sql");
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBeforeReversal));
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, tx, "Development", true,
                        attempt, prefix, fingerprint, CancellationToken.None)))!.Message,
                    Is.EqualTo("RUNNER_V9_BLOCKING_SCHEMA_NOT_AVAILABLE"));
                await tx.RollbackAsync();
            }
            await ExecuteScriptAsync("migrations/20261124_V9_Blocking_Chave_Isolada.sql");
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBeforeReversal));
            await using (var drop = new SqlCommand("""
                DROP TRIGGER identidade.tr_rf1936_no_dml;
                DROP TRIGGER identidade.tr_rf1936_coverage_no_dml;
                """, db))
                await drop.ExecuteNonQueryAsync();
            await AssertCandidateAsync(new IdentityObservation(null,"SEM_CPF","MARIA SILVA",
                new DateOnly(1980,5,6),"ANA COSTA"), cpfUuid);

            // Deliberate integrity attack, not repair or fixture indexing.
            // The next real FULL must reject stale initialized coverage
            // before a new run is created or any new identity is published.
            await using (var damage = new SqlCommand("""
                DELETE FROM identidade.blocking_chave_v9
                WHERE blocking_chave_id=(SELECT MIN(blocking_chave_id) FROM identidade.blocking_chave_v9);
                """, db))
                Assert.That(await damage.ExecuteNonQueryAsync(), Is.EqualTo(1));
            var runsBefore = await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.linkage_run;");
            var corrupt = await RunnerAsync();
            var stale = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(() =>
                corrupt.RunV9ControlledDevAsync(new ProbabilisticLinkageRunRequest(
                    LinkageRunType.FULL,9,null,null,null,100,1,null,"RF1936","STALE_NEGATIVE",
                    Guid.NewGuid(),true),attempt,CancellationToken.None)));
            Assert.That(stale!.Message, Is.EqualTo("RUNNER_V9_BLOCKING_COVERAGE_STALE_OR_INCOMPLETE"));
            Assert.That(await CountSqlAsync(db,"SELECT COUNT_BIG(*) FROM identidade.linkage_run;"), Is.EqualTo(runsBefore));
            await File.WriteAllTextAsync(Path.Combine(Pr1Directory(),"rf1936-repository-full.json"),
                JsonSerializer.Serialize(new { winner = winner.ContentSha256, fingerprint, first, second, third,
                    processorRepository = true, httpAdmission = false, isolatedIndex = true,
                    physicalReplayDigest = before, c07NonemptyNegative = true,
                    c07ResidentDatabaseNegatives = negativeIndex, staleRejected = true,
                    reversalPreserved = true, scientificTest = false, certified = false }));
            TestContext.WriteLine("V9_RF1936_REPOSITORY_FIRST_AND_REPEATED_FULL_C07_PASS_NOT_HTTP_OR_SCALE");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

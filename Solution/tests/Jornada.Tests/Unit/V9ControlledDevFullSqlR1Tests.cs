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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Tests.Unit;

/// <summary>
/// Actual FULL -> frozen Silver graph -> independent FS -> raw linkage_resultado
/// -> DT-10 Gold publication in a DISTINCT per-attempt SQL database. This is a
/// synthetic DEV smoke, NOT scientific TEST/10k/production activation.
/// Never skip: only invoked by the existing disposable SQL CI job after setup.
/// </summary>
[TestFixture, Category("SQLSmoke")]
public sealed class V9ControlledDevFullSqlR1Tests
{
    private static readonly Guid ProtectedX = Guid.Parse("10101010-a001-4000-8000-000000000001");
    private static readonly Guid ExistingY = Guid.Parse("20202020-b002-4000-8000-000000000002");
    private static readonly Guid ExistingZ = Guid.Parse("30303030-c003-4000-8000-000000000003");

    [Test]
    public async Task FULL_200_real_SQL_publishes_Gold_with_frozen_r1_and_no_implicit_identity_inheritance()
    {
        var source = Environment.GetEnvironmentVariable("JORNADA_V9_FULL_SCHEMA_NONPUBLISH_SQL");
        var dbName = Environment.GetEnvironmentVariable("JORNADA_V9_DEV_ATTEMPT_DB");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException(
                "RUNNER_V9_DEV_FULL_PHYSICAL_SQL_CI_NOT_PROVISIONED: no Assert.Ignore or fake PASS.");
        var prefix = "JornadaV9Dev_";
        if (!dbName.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(dbName[prefix.Length..], "N", out var attempt)
            || attempt == Guid.Empty
            || new SqlConnectionStringBuilder(source).InitialCatalog != "JornadaHarness")
            throw new InvalidOperationException("RUNNER_V9_DEV_SQL_MUST_BE_DISPOSABLE");

        var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = dbName };
        var sql = new OperationalSqlAdapter(builder.ConnectionString);
        await using var connection = await sql.OpenAsync(CancellationToken.None);
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            connection, attempt, CancellationToken.None);
        await using (var check = new SqlCommand("""
            IF OBJECT_ID(N'identidade.linkage_run',N'U') IS NULL
              OR OBJECT_ID(N'identidade.linkage_resultado',N'U') IS NULL
              OR OBJECT_ID(N'gold.pessoa',N'U') IS NULL
              OR OBJECT_ID(N'identidade.modelo_linkage_v9_operacional',N'U') IS NULL
              OR OBJECT_ID(N'identidade.sp_publicar_resolucao_progressiva_linkage_lote',N'P') IS NULL
              THROW 52901,'Esquema SQL DEV integral ou DT10 ausente.',1;
            IF EXISTS (SELECT 1 FROM silver.pessoa_observacao)
              OR EXISTS (SELECT 1 FROM identidade.linkage_run)
              OR EXISTS (SELECT 1 FROM gold.pessoa)
              THROW 52902,'Tentativa SQL DEV já possui dados; não reutilizar.',1;
            """, connection))
            await check.ExecuteNonQueryAsync();

        // Diagnostic fixture only: frozen for THIS test, never campaign #38 or
        // a change to operational thresholds, scientific parameters or artifacts.
        // A strong exact/exact joint cell makes real positive Gold paths
        // observable while retaining the original V9 scoring and guards.
        var winner = DistinctivePhysicalFixtureWinner();
        var stored = await V9OperationalSqlModelConverter.MaterializeOnceAsync(connection, winner);
        if (stored.ModelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_MODEL_SHA_MISMATCH");
        await using (var anchor = new SqlCommand("""
            INSERT identidade.modelo_linkage(
              modelo_id,versao,status,algoritmo_versao,normalizacao_versao,
              deduplicacao_metodo,base_referencia,snapshot_referencia,gerado_em)
            VALUES(@id,9,N'RASCUNHO',@algorithm,@normalization,
              N'V9_DEV_R1_DISPOSABLE',N'SYNTHETIC_FULL_ONLY',
              @sha,SYSDATETIMEOFFSET());
            """, connection))
        {
            anchor.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = stored.ModelId;
            anchor.Parameters.Add("@algorithm", SqlDbType.NVarChar, 120).Value =
                LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion;
            anchor.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                IdentityComparison.NormalizationVersion;
            anchor.Parameters.Add("@sha", SqlDbType.Char, 64).Value = winner.ContentSha256;
            Assert.That(await anchor.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        // Reuses the established 200-observation SQL FKs/Processor-style origin
        // fixture; before the run, seed three REAL, already-established Gold
        // references inside this disposable DEV database, not a TEST corpus.
        await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(connection, 200);
        // Physically exercise an INELIGIBLE blocking search within the same
        // published FULL run: zero Gold hits must not mean complete search.
        await using (var missingEvidence = new SqlCommand("""
            UPDATE silver.pessoa_observacao
               SET nome_completo=NULL,nome_cmp=NULL,data_nascimento=NULL,
                   nome_mae=NULL,nome_mae_cmp=NULL,
                   conteudo_hash=LOWER(CONVERT(varchar(64),
                       HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                         N'V9_DEV_SYNTHETIC_NO_BLOCKING_EVIDENCE')),2))
             WHERE codigo_pessoa_origem=N'V9TEST_0200';
            IF @@ROWCOUNT<>1
                THROW 52903,'Fixture sem blocking deve conter uma origem.',1;
            """, connection))
            await missingEvidence.ExecuteNonQueryAsync();

        await SeedExistingGoldReferencesAndProtectedOriginAsync(connection);
        await MaterializeFrozenGoldBlockingKeysAsync(connection, winner);

        var temp = Path.Combine(Path.GetTempPath(),
            "jornada-v9-dev-full-r1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var receipt = await V9ValidatedOperationalModelFile.ExportAsync(winner, temp);
            var selected = Path.Combine(temp, "selection.json");
            await File.WriteAllTextAsync(selected, JsonSerializer.Serialize(
                new V9ConfiguredArtifactLoader.Selection(receipt.FileName, winner.ContentSha256)));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Linkage:SelectedModelVersion"] = "9",
                    ["Linkage:V9SelectionFile"] = selected,
                    ["LinkageReplay:CaptureBronzeSources"] = "false"
                }).Build();
            // First force an actual physical SQL failure on the second
            // persisted batch. A failed run may retain *raw* first-page
            // evidence but must never publish partial Gold/Serving.
            await AssertSecondBatchFailureRollsBackPublicationAsync(
                connection, configuration, sql, attempt, temp);

            var scorer = await V9ControlledDevCohortScorer.CreateAsync(
                configuration, sql, attempt, CancellationToken.None);
            var runner = new ProbabilisticLinkageBatchRunner(
                configuration, sql, scorer,
                new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
                new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(temp), temp),
                new Dt05CandidateStateSnapshotPublisher(sql, temp),
                new Dt05BlockingProjectionSnapshotPublisher(sql, temp),
                new Dt05HistoricalCandidateStateVerifier(temp),
                new Dt05HistoricalBlockingProjectionVerifier(temp),
                NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
            var request = new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
                "V9_DEV_DISPOSABLE_CI", "R1_FULL_REAL_SQL_GOLD_ONLY",
                Guid.NewGuid(), true);
            var result = await runner.RunV9ControlledDevAsync(
                request, attempt, CancellationToken.None);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(result.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
                Assert.That(result.Elegiveis, Is.EqualTo(200));
                Assert.That(result.Avaliados, Is.EqualTo(200));
                Assert.That(result.Resolvidos + result.NaoResolvidos + result.Conflitos,
                    Is.EqualTo(result.Elegiveis));
            }));
            await using var verify = new SqlCommand("""
                SELECT
                  (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                   WHERE linkage_run_id=@run),
                  (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
                   WHERE linkage_run_id=@run AND publicado_em IS NOT NULL
                     AND resultado_publicacao IS NOT NULL
                     AND status_publicacao IS NOT NULL
                     AND motivo_publicacao IS NOT NULL),
                  (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva
                   WHERE estado=N'REFERENCIA' AND canonical_uuid IS NOT NULL),
                  (SELECT COUNT_BIG(*) FROM gold.pessoa
                   WHERE estado_identidade=N'REFERENCIA');
                """, connection);
            verify.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = result.RunId;
            await using var rows = await verify.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rows.GetInt64(0), Is.EqualTo(200),
                    "Every frozen Silver observation must have one raw SQL result.");
                Assert.That(rows.GetInt64(1), Is.EqualTo(200),
                    "Actual DT-10 publication must complete every result.");
                Assert.That(rows.GetInt64(2), Is.GreaterThan(0),
                    "At least one origin must be published to a reference.");
                Assert.That(rows.GetInt64(3), Is.GreaterThan(0),
                    "Gold must reflect physically published reference identities.");
            }));
            Assert.That(await rows.ReadAsync(), Is.False);
            await rows.DisposeAsync();
            // The SQL publisher must leave the no-pass observation INDEFINIDA:
            // an empty candidate list without eligible passes is NOT absence
            // proven by complete blocking.
            await using var noPass = new SqlCommand("""
                SELECT r.status,r.motivo,r.resultado_publicacao,
                       r.status_publicacao,r.pessoa_uuid_publicado,p.estado
                  FROM identidade.linkage_resultado r
                  JOIN silver.pessoa_observacao po
                    ON po.pessoa_observacao_id=r.pessoa_observacao_id
                  JOIN identidade.pessoa_origem_progressiva p
                    ON p.pessoa_origem_id=po.pessoa_origem_id
                 WHERE r.linkage_run_id=@run
                   AND po.codigo_pessoa_origem=N'V9TEST_0200';
                """, connection);
            noPass.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = result.RunId;
            await using (var denied = await noPass.ExecuteReaderAsync())
            {
                Assert.That(await denied.ReadAsync(), Is.True);
                Assert.Multiple((Action)(() =>
                {
                    Assert.That(denied.GetString(0), Is.EqualTo("NAO_RESOLVIDO"));
                    Assert.That(denied.GetString(1), Is.EqualTo("EVIDENCIA_INSUFICIENTE_PARA_BLOCKING"));
                    Assert.That(denied.GetString(2), Is.EqualTo("INDEFINIDA"));
                    Assert.That(denied.GetString(3), Is.EqualTo("NAO_RESOLVIDO"));
                    Assert.That(denied.IsDBNull(4), Is.True);
                    Assert.That(denied.GetString(5), Is.EqualTo("INDEFINIDA"));
                }));
                Assert.That(await denied.ReadAsync(), Is.False);
            }
            await AssertGoldR1S1S2S6AndWriteOracleEvidenceAsync(connection, result.RunId);
            TestContext.WriteLine(
                "V9_DEV_FULL_R1_PHYSICAL_SQL_GOLD_PASS: S1/S2/S6 verified; real FULL run, " +
                "200 Silver origins, frozen graph/FS, raw results, DT-10, Gold; " +
                "TEST/10k/production not executed");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task<ProbabilisticLinkageBatchRunner> NewDevRunnerAsync(
        IConfiguration configuration, IOperationalSqlAdapter sql,
        Guid attempt, string temp)
    {
        var scorer = await V9ControlledDevCohortScorer.CreateAsync(
            configuration, sql, attempt, CancellationToken.None);
        return new ProbabilisticLinkageBatchRunner(
            configuration, sql, scorer,
            new SqlPipelineCoordinator(sql), new Dt05ReplaySql(sql),
            new Dt05ReplayManifestPublisher(new FileSystemBronzeObjectStore(temp), temp),
            new Dt05CandidateStateSnapshotPublisher(sql, temp),
            new Dt05BlockingProjectionSnapshotPublisher(sql, temp),
            new Dt05HistoricalCandidateStateVerifier(temp),
            new Dt05HistoricalBlockingProjectionVerifier(temp),
            NullLogger<ProbabilisticLinkageBatchRunner>.Instance);
    }

    private static async Task AssertSecondBatchFailureRollsBackPublicationAsync(
        SqlConnection connection, IConfiguration configuration,
        IOperationalSqlAdapter sql, Guid attempt, string temp)
    {
        // Prevent the 101st raw result from passing CheckConstraints on the
        // *real* SqlBulkCopy. Do not replace a scorer with a fake failure.
        await using var breakpoint = new SqlCommand("""
            SELECT pessoa_observacao_id FROM silver.pessoa_observacao
            ORDER BY pessoa_observacao_id
            OFFSET 100 ROWS FETCH NEXT 1 ROWS ONLY;
            """, connection);
        var failAt = Convert.ToInt64(await breakpoint.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(failAt, Is.GreaterThan(0L));
        var constraint = "ck_v9_r1_disposable_fail_batch_2";
        await using (var setup = new SqlCommand(
            $"ALTER TABLE identidade.linkage_resultado WITH CHECK " +
            $"ADD CONSTRAINT {constraint} CHECK (pessoa_observacao_id < {failAt});",
            connection))
            await setup.ExecuteNonQueryAsync();
        Exception? injectedFailure = null;
        try
        {
            var failing = await NewDevRunnerAsync(configuration, sql, attempt, temp);
            try
            {
                await failing.RunV9ControlledDevAsync(
                    new ProbabilisticLinkageRunRequest(
                        LinkageRunType.FULL, 9, null, null, null, 100, 1, null,
                        "V9_DEV_FAILCLOSED_BATCH2", "DISPOSABLE_SQL_NEGATIVE",
                        Guid.NewGuid(), true),
                    attempt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                injectedFailure = ex;
            }
        }
        finally
        {
            await using var drop = new SqlCommand(
                $"ALTER TABLE identidade.linkage_resultado DROP CONSTRAINT {constraint};",
                connection);
            await drop.ExecuteNonQueryAsync();
        }
        Assert.That(injectedFailure, Is.Not.Null,
            "Physical CheckConstraints must fail specifically in batch 2.");
        Assert.That(injectedFailure!.ToString(), Does.Contain(constraint),
            "Reject unrelated exceptions as proof of batch-2 rollback.");
        await using (var verify = new SqlCommand("""
            DECLARE @failed UNIQUEIDENTIFIER=(
              SELECT TOP(1) linkage_run_id FROM identidade.linkage_run
               WHERE escopo_json LIKE N'%V9_DEV_FAILCLOSED_BATCH2%'
               ORDER BY iniciado_em DESC);
            SELECT
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
               WHERE linkage_run_id=@failed),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
               WHERE linkage_run_id=@failed AND publicado_em IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM gold.pessoa),
              (SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE linkage_run_id=@failed AND status=N'FALHOU');
            """, connection))
        {
            await using var rd = await verify.ExecuteReaderAsync();
            Assert.That(await rd.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rd.GetInt64(0), Is.EqualTo(100),
                    "Only batch 1 raw evidence may remain after batch 2 fails.");
                Assert.That(rd.GetInt64(1), Is.Zero,
                    "No partial DT-10/Gold publication is permitted.");
                Assert.That(rd.GetInt64(2), Is.EqualTo(3),
                    "Only original X/Y/Z Gold may exist before recovery.");
                Assert.That(rd.GetInt64(3), Is.EqualTo(3),
                    "Only the three pre-established links may exist.");
                Assert.That(rd.GetInt64(4), Is.EqualTo(1),
                    "Failed runner must be explicitly marked FALHOU.");
            }));
            Assert.That(await rd.ReadAsync(), Is.False);
        }
        TestContext.WriteLine(
            "V9_DEV_R1_ROLLBACK_BATCH2_PHYSICAL_SQL_PASS: 100 raw/0 published/3 baseline Gold");
    }

    /// <summary>
    /// Diagnostic-only fitted evidence, never the scientific #38 winner.
    /// Exact name AND mother evidence dominates the otherwise uniform model;
    /// all frozen blocking passes, TF slots, decision margin and thresholds
    /// remain those of the original operational test fixture. This is only a
    /// synthetic SQL-positive path, not scientific model validation.
    /// </summary>
    private static V9FittedSnapshot DistinctivePhysicalFixtureWinner()
    {
        var baseline = V9FittedSnapshotTests.CompleteTf4OperationalWinner();
        // Only this disposable fixture extends synthetic vocabulary so the
        // *unchanged* V9 positive-TF fail-closed guard can score the actual
        // persisted ALFA0003/CARLA0003 Gold Z. The production winner/TF hashes
        // and published IBGE data are never changed or claimed as evidence.
        var fixtureTf = baseline.Payload.UnitTfCalibrations!.Select(slot =>
        {
            if (slot.Unit != NominalFrequencyUnitV9.FirstName)
                return slot;
            var term = slot.Side == CompositeNominalTfSideV9.Person
                ? "ALFA0003" : "CARLA0003";
            var active = slot.Calibration
                ?? throw new InvalidDataException("V9_DEV_R1_SYNTHETIC_TF_MISSING");
            var frequencies = active.Frequencies.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            var trainU = active.TrainUnitUByValue.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            frequencies.Add(term, .25m);
            trainU.Add(term, .1m);
            return slot with
            {
                Calibration = active with
                {
                    Frequencies = frequencies,
                    TrainUnitUByValue = trainU
                }
            };
        }).ToArray();
        var observations = Enum.GetValues<CompositeNominalLevelV9>()
            .SelectMany(person => Enum.GetValues<CompositeNominalLevelV9>()
                .Select(mother =>
                {
                    var exact = person == CompositeNominalLevelV9.ExactFull
                        && mother == CompositeNominalLevelV9.ExactFull;
                    return new V9JointObservedCell(person, mother,
                        exact ? 1_000_000 : 1,
                        exact ? 1 : 1_000_000);
                })).ToArray();
        var support = V9JointSupportEstimation.Estimate(observations,
            new V9JointSupportPolicy(.99m, 0, 0), 1m,
            baseline.Payload.TrainCorpusSha256);
        return V9FittedSnapshot.Create(baseline.Payload with
        {
            JointCells = support.Probabilities,
            JointSupport = support.Provenance,
            UnitTfCalibrations = fixtureTf
        });
    }

    /// <summary>
    /// X has a governed protected origin (S6); Y and Z are established Gold
    /// references with existing active governed links. The Gold row Y exactly
    /// matches Silver 0001+0101, forcing a real global group and X/Y veto (S2).
    /// Z exactly matches Silver 0003 and must be associated independently (S1).
    /// No FK stubs/group GUIDs are substituted for actual Gold references.
    /// </summary>
    private static async Task SeedExistingGoldReferencesAndProtectedOriginAsync(
        SqlConnection connection)
    {
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var seed = new SqlCommand("""
            INSERT identidade.pessoa(pessoa_uuid,status)
            VALUES(@x,N'ATIVO'),(@y,N'ATIVO'),(@z,N'ATIVO');
            INSERT gold.pessoa(
                pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,
                fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em)
            VALUES
              (@x,NULL,N'SEM_CPF',N'FELICIA PEREIRA','1970-01-02',N'BEATRIZ FONSECA',
                 1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSUTCDATETIME()),
              (@y,NULL,N'SEM_CPF',N'MARIA SILVA','1980-05-06',N'ANA COSTA',
                 1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSUTCDATETIME()),
              (@z,NULL,N'SEM_CPF',N'ALFA0003 SILVA','1980-05-06',N'CARLA0003 COSTA',
                 1,N'BASELINE_FONTE_UNICA',N'REFERENCIA',N'COMPLETO',SYSUTCDATETIME());
            INSERT identidade.vinculo_fonte(
                pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,
                modelo_id,ativo,resolvido_em,motivo)
            SELECT po.pessoa_observacao_id,
                CASE po.codigo_pessoa_origem
                  WHEN N'V9TEST_0001' THEN @x
                  WHEN N'V9TEST_0005' THEN @y
                  ELSE @z END,
                N'CORRECAO_GOVERNADA',NULL,N'RESOLVIDO',NULL,1,
                SYSDATETIMEOFFSET(),N'REFERENCIA_SINTETICA_DEV_EFEMERA'
              FROM silver.pessoa_observacao po
             WHERE po.codigo_pessoa_origem IN(
                   N'V9TEST_0001',N'V9TEST_0005',N'V9TEST_0006');
            IF @@ROWCOUNT<>3 THROW 52904,'Protecao Gold DEV incompleta.',1;
            DECLARE @origin bigint=(
                SELECT pessoa_origem_id FROM silver.pessoa_observacao
                WHERE codigo_pessoa_origem=N'V9TEST_0001');
            DECLARE @v bigint;
            EXEC identidade.sp_publicar_referencia_progressiva_deterministica
                 @pessoa_origem_id=@origin,
                 @canonical_uuid=@x,
                 @evidencia_referencia=N'REFERENCIA_PROTEGIDA_DEV_S6',
                 @politica_versao=N'V9_DEV_S6_SYNTHETIC',
                 @versao_resultado=@v OUTPUT;
            IF @v IS NULL OR @v<1
                THROW 52905,'Protecao progressiva Gold DEV nao publicada.',1;
            """, connection, tx);
        seed.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = ProtectedX;
        seed.Parameters.Add("@y", SqlDbType.UniqueIdentifier).Value = ExistingY;
        seed.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
        await seed.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// The physical candidate reader requires persisted blocking_chave: a Gold
    /// row alone is NOT a blocking candidate. Read existing Gold in this
    /// disposable SQL database, project it through the already versioned V9
    /// contract and materialize real SQL keys with the EXACT winner fingerprint.
    /// Do not manually invent candidate UUID lists or force Resolve decisions.
    /// This is isolated diagnostic fixture setup, not production publication.
    /// </summary>
    private static async Task MaterializeFrozenGoldBlockingKeysAsync(
        SqlConnection connection, V9FittedSnapshot winner)
    {
        var runtime = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), winner.ContentSha256);
        var maximum = runtime.MaximumPrefixLength
            ?? throw new InvalidDataException("V9_DEV_R1_PROJECTION_LENGTH_MISSING");
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        var sources = new List<(Guid Id, string Name, string Mother,
            DateOnly Birth, DateTimeOffset AsOf)>();
        await using (var read = new SqlCommand("""
            SELECT pessoa_uuid,nome_completo,nome_mae,data_nascimento,atualizado_em
              FROM gold.pessoa
             WHERE pessoa_uuid IN (@x,@y,@z)
               AND estado_identidade=N'REFERENCIA'
               AND completude_nucleo=N'COMPLETO'
             ORDER BY pessoa_uuid;
            """, connection, tx))
        {
            read.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = ProtectedX;
            read.Parameters.Add("@y", SqlDbType.UniqueIdentifier).Value = ExistingY;
            read.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            await using var rows = await read.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                if (rows.IsDBNull(0) || rows.IsDBNull(1) || rows.IsDBNull(2)
                    || rows.IsDBNull(3) || rows.IsDBNull(4))
                    throw new InvalidDataException("V9_DEV_R1_EXISTING_GOLD_INCOMPLETE");
                sources.Add((rows.GetGuid(0), rows.GetString(1), rows.GetString(2),
                    DateOnly.FromDateTime(rows.GetDateTime(3)),
                    rows.GetDateTimeOffset(4)));
            }
        }
        if (sources.Count != 3 || sources.Select(x => x.Id).Distinct().Count() != 3)
            throw new InvalidDataException("V9_DEV_R1_EXISTING_GOLD_SET_INCOMPLETE");
        var written = 0;
        foreach (var gold in sources)
        {
            var keys = V9CalibrationBlockingProjection.Project(
                gold.Name, gold.Mother, gold.Birth, maximum).Distinct().ToArray();
            if (keys.Length == 0)
                throw new InvalidDataException("V9_DEV_R1_GOLD_PROJECTION_EMPTY");
            foreach (var key in keys)
            {
                var isAlias = key.Feature.StartsWith(
                    "name_prefix_", StringComparison.Ordinal)
                    || key.Feature.StartsWith(
                        "mother_name_prefix_", StringComparison.Ordinal)
                    || BlockingFeatureTemporalCatalog.Get(key.Feature)
                        == BlockingFeatureTemporalSemantics.VersionedAlias;
                await using var insert = new SqlCommand("""
                    INSERT identidade.blocking_chave(
                        pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                        semantica_temporal,vigencia_inicio,vigencia_fim,
                        projection_schema_version,projection_fingerprint_sha256)
                    VALUES (@id,@normalization,@feature,@value,@temporal,
                            @from,NULL,@schema,@fingerprint);
                    """, connection, tx);
                insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = gold.Id;
                insert.Parameters.Add("@normalization", SqlDbType.NVarChar, 80).Value =
                    IdentityComparison.NormalizationVersion;
                insert.Parameters.Add("@feature", SqlDbType.NVarChar, 80).Value = key.Feature;
                insert.Parameters.Add("@value", SqlDbType.NVarChar, 500).Value = key.Value;
                insert.Parameters.Add("@temporal", SqlDbType.NVarChar, 30).Value =
                    isAlias ? "VERSIONED_ALIAS" : "STABLE_IDENTITY_DATUM";
                insert.Parameters.Add("@from", SqlDbType.DateTimeOffset).Value = gold.AsOf;
                insert.Parameters.Add("@schema", SqlDbType.NVarChar, 120).Value =
                    runtime.ProjectionSchemaVersion;
                insert.Parameters.Add("@fingerprint", SqlDbType.Char, 64).Value =
                    runtime.ProjectionFingerprintSha256;
                if (await insert.ExecuteNonQueryAsync() != 1)
                    throw new InvalidDataException("V9_DEV_R1_GOLD_PROJECTION_WRITE_MISMATCH");
                written++;
            }
        }
        if (written < 3)
            throw new InvalidDataException("V9_DEV_R1_GOLD_PROJECTION_NOT_PHYSICAL");
        await tx.CommitAsync();
    }

    private sealed record PhysicalDecision(string RawStatus, Guid? RawUuid,
        string? RawReason, string Published, string PublishedStatus,
        Guid? PublishedUuid, string ProgressiveStatus, Guid? Canonical);

    private static async Task AssertGoldR1S1S2S6AndWriteOracleEvidenceAsync(
        SqlConnection connection, Guid runId)
    {
        var decisions = new Dictionary<string, PhysicalDecision>(StringComparer.Ordinal);
        await using (var query = new SqlCommand("""
            SELECT po.codigo_pessoa_origem,r.status,r.pessoa_uuid_resolvido,
                   r.motivo,r.resultado_publicacao,r.status_publicacao,
                   r.pessoa_uuid_publicado,p.estado,p.canonical_uuid
              FROM identidade.linkage_resultado r
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=r.pessoa_observacao_id
              JOIN identidade.pessoa_origem_progressiva p
                ON p.pessoa_origem_id=po.pessoa_origem_id
             WHERE r.linkage_run_id=@run
               AND po.codigo_pessoa_origem IN(
                     N'V9TEST_0001',N'V9TEST_0003',N'V9TEST_0101')
             ORDER BY po.codigo_pessoa_origem;
            """, connection))
        {
            query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                var id = rows.GetString(0);
                var decision = new PhysicalDecision(
                    rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetGuid(2),
                    rows.IsDBNull(3) ? null : rows.GetString(3),
                    rows.GetString(4), rows.GetString(5),
                    rows.IsDBNull(6) ? null : rows.GetGuid(6),
                    rows.GetString(7), rows.IsDBNull(8) ? null : rows.GetGuid(8));
                if (!decisions.TryAdd(id, decision))
                    throw new InvalidDataException("V9_DEV_R1_PHYSICAL_ORACLE_DUPLICATE_SOURCE");
            }
        }
        Assert.That(decisions.Count, Is.EqualTo(3),
            "The SQL-published S1/S2/S6 recorte must be complete.");

        var s1 = decisions["V9TEST_0003"];
        var s2 = decisions["V9TEST_0101"];
        var s6 = decisions["V9TEST_0001"];
        Assert.Multiple((Action)(() =>
        {
            // S1: independently approved individual FS into pre-existing Z.
            Assert.That(s1.RawStatus, Is.EqualTo("RESOLVIDO"));
            Assert.That(s1.RawUuid, Is.EqualTo(ExistingZ));
            Assert.That(s1.Published, Is.EqualTo("ASSOCIACAO_EXISTENTE"));
            Assert.That(s1.PublishedStatus, Is.EqualTo("RESOLVIDO"));
            Assert.That(s1.PublishedUuid, Is.EqualTo(ExistingZ));
            Assert.That(s1.ProgressiveStatus, Is.EqualTo("REFERENCIA"));
            Assert.That(s1.Canonical, Is.EqualTo(ExistingZ));
            // S2: two members share the blind V9 group but X is protected
            // while the unprotected observation individually proposes Y.
            Assert.That(s2.RawStatus, Is.EqualTo("CONFLITO"));
            Assert.That(s2.RawReason, Is.EqualTo("V9_R1_GRUPO_GOLD_CONFLITANTE"));
            Assert.That(s2.RawUuid, Is.Null);
            Assert.That(s2.Published, Is.EqualTo("INDEFINIDA"));
            Assert.That(s2.PublishedStatus, Is.EqualTo("CONFLITO"));
            Assert.That(s2.PublishedUuid, Is.Null);
            Assert.That(s2.ProgressiveStatus, Is.EqualTo("INDEFINIDA"));
            Assert.That(s2.Canonical, Is.Null);
            // S6: actual persisted governed REFERENCIA X survives conflicting
            // raw FS and the unchanged SQL publication precedence.
            Assert.That(s6.RawStatus, Is.EqualTo("CONFLITO"));
            Assert.That(s6.RawReason, Is.EqualTo("V9_R1_GRUPO_GOLD_CONFLITANTE"));
            Assert.That(s6.Published, Is.EqualTo("INDEFINIDA"));
            Assert.That(s6.PublishedUuid, Is.Null);
            Assert.That(s6.ProgressiveStatus, Is.EqualTo("REFERENCIA"));
            Assert.That(s6.Canonical, Is.EqualTo(ProtectedX));
        }));

        // Existing independent offline decision-oracle contract: a sealed
        // expected recorte (before reading SQL) versus physically observed
        // publication. Synthetic inputs have no ZIP or scientific TEST status.
        var evidenceDirectory = Environment.GetEnvironmentVariable(
            "JORNADA_V9_R1_SQL_ORACLE_OUTPUT");
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
            throw new InvalidOperationException("V9_DEV_R1_ORACLE_PATH_NOT_PROVISIONED");
        Directory.CreateDirectory(evidenceDirectory);
        var truth = new[]
        {
            new { observationId = "S1", decision = "MATCH",
                identityId = (string?)ExistingZ.ToString("D") },
            new { observationId = "S2", decision = "ABSTAIN", identityId = (string?)null },
            new { observationId = "S6", decision = "MATCH",
                identityId = (string?)ProtectedX.ToString("D") }
        };
        var observed = new[]
        {
            new { observationId = "S1",
                decision = s1.Published == "ASSOCIACAO_EXISTENTE" ? "MATCH" : "ABSTAIN",
                identityId = s1.Published == "ASSOCIACAO_EXISTENTE"
                    ? s1.PublishedUuid?.ToString("D") : null },
            new { observationId = "S2",
                decision = s2.PublishedUuid.HasValue ? "MATCH" : "ABSTAIN",
                identityId = s2.PublishedUuid?.ToString("D") },
            new { observationId = "S6",
                decision = s6.ProgressiveStatus == "REFERENCIA" ? "MATCH" : "ABSTAIN",
                identityId = s6.ProgressiveStatus == "REFERENCIA"
                    ? s6.Canonical?.ToString("D") : null }
        };
        // The immutable gabarito is a SEPARATE file and the seal is its
        // actual byte-for-byte SHA-256, not a fixed label or a circular
        // self-hash embedded in truth.json. Existing offline oracle expects
        // a zipSha256 field: for this ZIP-free DEV recorte it carries the
        // gabarito content digest, never a claim that a ZIP was processed.
        var truthPath = Path.Combine(evidenceDirectory, "truth.json");
        var truthBytes = JsonSerializer.SerializeToUtf8Bytes(
            new { expectedDecisions = truth });
        await File.WriteAllBytesAsync(truthPath, truthBytes);
        var sealedDigest = Convert.ToHexString(SHA256.HashData(truthBytes))
            .ToLowerInvariant();
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDirectory, "truth.sha256"),
            sealedDigest + "  truth.json\n");
        await File.WriteAllTextAsync(
            Path.Combine(evidenceDirectory, "truth-oracle.json"),
            JsonSerializer.Serialize(new { zipSha256 = sealedDigest, expectedDecisions = truth }));
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "observed.json"),
            JsonSerializer.Serialize(new { zipSha256 = sealedDigest, observedDecisions = observed }));
        TestContext.WriteLine("V9_DEV_R1_S1_S2_S6_SQL_ORACLE_INPUTS_WRITTEN");
    }
}

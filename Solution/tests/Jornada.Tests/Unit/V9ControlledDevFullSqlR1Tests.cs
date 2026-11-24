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
public sealed partial class V9ControlledDevFullSqlR1Tests
{
    private static readonly Guid ProtectedX = Guid.Parse("10101010-a001-4000-8000-000000000001");
    private static readonly Guid ExistingY = Guid.Parse("20202020-b002-4000-8000-000000000002");
    // Z is the actual initial_uuid of the pre-founded synthetic 0004 origin.
    // Each physical scenario runs in its own disposable attempt.
    private static Guid ExistingZ;

    [Test]
    public Task FULL_200_real_SQL_publishes_Gold_with_frozen_r1_and_no_implicit_identity_inheritance() =>
        RunFixtureAsync("existing");

    [Test]
    public Task FULL_Gold_characterization_PR1()
    {
        var scenario = Environment.GetEnvironmentVariable("JORNADA_V9_GOLD_CHARACTERIZATION");
        if (scenario is not ("stable" or "permuted" or "historical"))
            throw new InvalidOperationException("V9_GOLD_PR1_SCENARIO_REQUIRED");
        return RunFixtureAsync(scenario);
    }

    [Test]
    public Task FULL_T3_indirect_probabilistic_inheritance() => RunFixtureAsync("t3");

    private static async Task RunFixtureAsync(string scenario)
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
        var winner = scenario == "t3" ? T3DiagnosticWinner() : DistinctivePhysicalFixtureWinner();
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

        if (scenario == "t3")
        {
            await CharacterizeT3Async(connection, sql, attempt, winner);
            return;
        }

        // Reuses the established 200-observation SQL FKs/Processor-style origin
        // fixture; before the run, seed three REAL, already-established Gold
        // references inside this disposable DEV database, not a TEST corpus.
        await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(
            connection, 200, permute: scenario == "permuted");
        if (scenario is "stable" or "permuted")
        {
            await using var fixedDates = new SqlCommand("""
                UPDATE silver.pessoa_observacao
                   SET source_as_of=DATEADD(second,
                       CONVERT(int,RIGHT(codigo_pessoa_origem,4)),
                       CONVERT(datetimeoffset,'2026-01-01T00:00:00+00:00'));
                """, connection);
            await fixedDates.ExecuteNonQueryAsync();
        }
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

        // S5: two distinct managers share a true exact/exact Silver
        // identity but have NO physically eligible Gold blocking hit.
        // S3 remains a separate weak-Gold candidate on ALFA0008/CARLA0008.
        await using (var withoutGold = new SqlCommand("""
            UPDATE po
               SET nome_completo=N'OMEGA0007 SILVA',
                   nome_cmp=N'OMEGA0007 SILVA',
                   nome_mae=N'VERONICA0007 COSTA',
                   nome_mae_cmp=N'VERONICA0007 COSTA',
                   conteudo_hash=LOWER(CONVERT(varchar(64),
                       HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                            N'V9_R1_S5_NO_GOLD_'+po.codigo_pessoa_origem)),2))
              FROM silver.pessoa_observacao po
             WHERE po.codigo_pessoa_origem IN(N'V9TEST_0007',N'V9TEST_0107');
            IF @@ROWCOUNT<>2 THROW 52913,'Physical S5 Silver-only pair missing.',1;
            """, connection))
            await withoutGold.ExecuteNonQueryAsync();

        // S9: 0006 already has a governed link to Z in the seed below.
        // 0106 is a separate origin/manager with exact Silver evidence to
        // 0006 but not exact/exact to Gold Z. No candidate or raw decision
        // is injected: the real frozen graph and FS must establish the case.
        await using (var indication = new SqlCommand("""
            UPDATE silver.pessoa_observacao
               SET nome_completo=N'ALFA0006 SILVA',nome_cmp=N'ALFA0006 SILVA',
                   nome_mae=N'CARLA0006 COSTA',nome_mae_cmp=N'CARLA0006 COSTA',
                   conteudo_hash=LOWER(CONVERT(varchar(64),
                       HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                           N'V9_R1_S9_INDICATION_0106')),2))
             WHERE codigo_pessoa_origem=N'V9TEST_0106';
            IF @@ROWCOUNT<>1 THROW 52914,'Physical S9 Silver partner missing.',1;
            -- 0006 is HISTORICAL evidence of the governed Z link. Keep the
            -- newer 0003 attributes canonical when Gold is recomposed. If
            -- 0006 wins by source_as_of, the second FULL is a changed-Gold
            -- experiment, not an idempotence test on stable evidence.
            UPDATE historical
               SET source_as_of=CASE WHEN @historical=1 THEN
                   CASE WHEN historical.source_as_of>canonical.source_as_of THEN historical.source_as_of
                        ELSE DATEADD(second,1,canonical.source_as_of) END
                   ELSE DATEADD(day,-1,canonical.source_as_of) END
              FROM silver.pessoa_observacao historical
              CROSS JOIN silver.pessoa_observacao canonical
             WHERE historical.codigo_pessoa_origem=N'V9TEST_0006'
               AND canonical.codigo_pessoa_origem=N'V9TEST_0003';
            IF @@ROWCOUNT<>1 THROW 52915,'Physical S9 historical chronology missing.',1;
            """, connection))
        {
            indication.Parameters.Add("@historical", SqlDbType.Bit).Value = scenario == "historical";
            await indication.ExecuteNonQueryAsync();
        }

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
            // S1 must have an independently founded origin for Z, not a Gold
            // summary manually labelled as evidence. Reuse fixture origin 0004
            // to keep the 200/201 no-CPF run census, then publish its own
            // initial UUID by a real one-row scoped FULL before X/Y exist.
            await FoundZFromIndependentOriginAsync(connection, configuration,
                sql, attempt, temp);
            await SeedExistingGoldReferencesAndProtectedOriginAsync(connection);
            await AssertB1FixtureCpfCompositionAsync(connection);
            await AssertRecomposedReferenceAsync(connection, ProtectedX);
            await AssertRecomposedReferenceAsync(connection, ExistingY);
            await MaterializeFrozenGoldBlockingKeysAsync(connection, winner);
            await AssertS2IndependentYAuthorityAsync(connection, winner);

            if (scenario is "stable" or "permuted")
            {
                await AssertStrictFullAndPermutationAsync(connection, configuration,
                    sql, attempt, temp, winner, scenario);
                return;
            }

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
            await AssertPhysicalS3S4S5AndCrossBatchAsync(connection, result.RunId);
            await AssertPhysicalIndicationReasonsAsync(connection, result.RunId);
            await AssertB1GovernedNoCpfReevaluatedAsync(connection, result.RunId);
            await AssertB1FixtureCpfCompositionAsync(connection);
            if (scenario == "historical")
            {
                await CharacterizeHistoricalGoldAsync(connection, configuration,
                    sql, attempt, temp, winner, result.RunId);
                return;
            }
            await AssertRecomposedZAsync(connection);
            await AssertGoldR1S1S2S6AndWriteOracleEvidenceAsync(connection, result.RunId);
            await AssertZeroOneOriginVersionsAndVariableBatchWidthsAsync(
                connection, configuration, sql, attempt, temp, result.RunId);
            await AssertS8UnauthenticatedGoldRollsBackBeforeScoringAsync(
                connection, configuration, sql, attempt, temp);
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

    private static async Task AssertPhysicalS3S4S5AndCrossBatchAsync(
        SqlConnection connection, Guid runId)
    {
        var outcomes = new Dictionary<string, (
            long SourceId, string RawStatus, string? RawReason,
            Guid? Best, string Publication, Guid? Published)>();
        await using (var query = new SqlCommand("""
            SELECT po.codigo_pessoa_origem,po.pessoa_observacao_id,
                   r.status,r.motivo,r.melhor_candidato_uuid,
                   r.resultado_publicacao,r.pessoa_uuid_publicado
              FROM identidade.linkage_resultado r
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=r.pessoa_observacao_id
             WHERE r.linkage_run_id=@run
               AND po.codigo_pessoa_origem IN(
                   N'V9TEST_0001',N'V9TEST_0101',
                   N'V9TEST_0007',N'V9TEST_0107',
                   N'V9TEST_0008',N'V9TEST_0108')
             ORDER BY po.codigo_pessoa_origem;
            """, connection))
        {
            query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            await using var rows = await query.ExecuteReaderAsync();
            while (await rows.ReadAsync())
            {
                Assert.That(outcomes.TryAdd(rows.GetString(0),
                    (rows.GetInt64(1), rows.GetString(2),
                     rows.IsDBNull(3) ? null : rows.GetString(3),
                     rows.IsDBNull(4) ? null : rows.GetGuid(4),
                     rows.GetString(5),
                     rows.IsDBNull(6) ? null : rows.GetGuid(6))), Is.True);
            }
        }
        Assert.That(outcomes.Count, Is.EqualTo(6));
        var weak = outcomes["V9TEST_0008"];
        var groupFirst = outcomes["V9TEST_0007"];
        var other = outcomes["V9TEST_0107"];
        var singleton = outcomes["V9TEST_0108"];
        var x = outcomes["V9TEST_0001"];
        var y = outcomes["V9TEST_0101"];
        Assert.Multiple((Action)(() =>
        {
            // S3: even a physically authenticated weaker Gold Z is a
            // candidate, not permission to invent a new Gold identity.
            Assert.That(weak.Best, Is.EqualTo(ExistingZ));
            Assert.That(weak.RawStatus, Is.EqualTo("NAO_RESOLVIDO"));
            Assert.That(weak.Publication, Is.EqualTo("INDEFINIDA"));
            Assert.That(weak.Published, Is.Null);
            // S5: a pair of pending Silver rows (one from each manager)
            // is not a new group-level identity, regardless of SQL edge.
            Assert.That(groupFirst.Best, Is.Null);
            Assert.That(groupFirst.RawReason, Is.EqualTo("V9_R1_INDEFINIDO_SEM_INDICIO"));
            Assert.That(groupFirst.Publication, Is.EqualTo("INDEFINIDA"));
            Assert.That(groupFirst.Published, Is.Null);
            Assert.That(other.Best, Is.Null);
            Assert.That(other.RawReason, Is.EqualTo("V9_R1_INDEFINIDO_SEM_INDICIO"));
            Assert.That(other.Publication, Is.EqualTo("INDEFINIDA"));
            Assert.That(other.Published, Is.Null);
            // S4: complete singleton search with eligible passes and no
            // Gold/Silver neighbor produces only its own progressive initial.
            Assert.That(singleton.RawReason,
                Does.StartWith("SEM_CANDIDATO_"));
            Assert.That(singleton.Publication, Is.EqualTo("NOVA_IDENTIDADE"));
            Assert.That(singleton.Published, Is.Not.Null);
            // S2 physical X/Y group spans 100+100 persist batches; it was
            // prepared globally before either batch was written.
            Assert.That(x.SourceId, Is.LessThan(y.SourceId));
            Assert.That(x.SourceId, Is.LessThanOrEqualTo(100L));
            Assert.That(y.SourceId, Is.GreaterThan(100L));
        }));
        TestContext.WriteLine(
            "V9_DEV_R1_PHYSICAL_S3_S4_S5_CROSS_BATCH_PASS: weak Gold abstains; " +
            "Silver-only group indefinite; complete singleton new identity; group spans pages");
    }

    // Display is independently checked against the canonical recomposition
    // precedence, not a particular Silver record or the scoring evidence.
    private static Task AssertRecomposedZAsync(SqlConnection connection) =>
        AssertRecomposedReferenceAsync(connection, ExistingZ);

    private static async Task AssertRecomposedReferenceAsync(SqlConnection connection, Guid referencePerson)
    {
        await using var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot);
        var provenance = await Pr1ZProvenanceAsync(connection, snapshot, referencePerson);
        Assert.That(provenance.Select(r => r[0]), Is.EquivalentTo(
            new[] { "NOME_COMPLETO", "DATA_NASCIMENTO", "NOME_MAE" }));
        Assert.That(provenance.All(r => Equals(r[6], 1)), Is.True,
            "Each displayed field must equal the current canonical source selected by verification/recency/ID precedence.");
        await using (var state = new SqlCommand("""
            SELECT estado_identidade,completude_nucleo FROM gold.pessoa WHERE pessoa_uuid=@z;
            """, connection, snapshot))
        {
            state.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = referencePerson;
            await using var rows = await state.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True);
            Assert.That(rows.GetString(0), Is.EqualTo("REFERENCIA"));
            Assert.That(rows.GetString(1), Is.EqualTo("COMPLETO"));
            Assert.That(await rows.ReadAsync(), Is.False);
        }
        await snapshot.CommitAsync();
        TestContext.WriteLine("V9_B1_DISPLAY_RECOMPOSITION_PASS");
    }

    private static async Task<HashSet<Guid>> ReadGoldUuidSetAsync(SqlConnection connection)
    {
        var ids = new HashSet<Guid>();
        await using var query = new SqlCommand("SELECT pessoa_uuid FROM gold.pessoa;", connection);
        await using var rows = await query.ExecuteReaderAsync();
        while (await rows.ReadAsync())
            Assert.That(ids.Add(rows.GetGuid(0)), Is.True);
        return ids;
    }

    // The two CPF-bearing histories are outside the FULL source cohort.
    // Gold legitimately composes their CPF, but the authority used by b1
    // is the current governed link, not a fabricated CPF anchor.
    private static async Task AssertB1FixtureCpfCompositionAsync(SqlConnection connection)
    {
        await using var query = new SqlCommand("""
            SELECT COUNT_BIG(*)
              FROM gold.pessoa g
              JOIN silver.pessoa_observacao po ON po.cpf=g.cpf
              JOIN identidade.v_vinculo_corrente vc
                ON vc.pessoa_observacao_id=po.pessoa_observacao_id
               AND vc.pessoa_uuid=g.pessoa_uuid
             WHERE ((g.pessoa_uuid=@x AND po.codigo_pessoa_origem=N'V9TEST_0001')
                 OR (g.pessoa_uuid=@y AND po.codigo_pessoa_origem=N'V9TEST_0005'))
               AND po.versao_interna=2 AND g.status_cpf=N'PRESENTE'
               AND vc.status=N'RESOLVIDO' AND vc.metodo_resolucao=N'CORRECAO_GOVERNADA'
               AND NOT EXISTS (SELECT 1 FROM identidade.cpf_ancora a
                               WHERE a.pessoa_uuid=g.pessoa_uuid)
               AND NOT EXISTS (SELECT 1 FROM identidade.identity_map m
                               WHERE m.pessoa_uuid=g.pessoa_uuid AND m.tipo=N'CPF'
                                 AND m.vigencia_fim IS NULL AND m.estado=N'ATIVO');
            SELECT COUNT_BIG(*) FROM gold.pessoa
             WHERE pessoa_uuid=@z AND cpf IS NULL AND status_cpf=N'SEM_CPF';
            """, connection);
        query.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = ProtectedX;
        query.Parameters.Add("@y", SqlDbType.UniqueIdentifier).Value = ExistingY;
        query.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
        await using var rows = await query.ExecuteReaderAsync();
        Assert.That(await rows.ReadAsync(), Is.True);
        Assert.That(rows.GetInt64(0), Is.EqualTo(2),
            "X/Y compose CPF from governed Silver history, without CPF authority");
        Assert.That(await rows.NextResultAsync(), Is.True);
        Assert.That(await rows.ReadAsync(), Is.True);
        Assert.That(rows.GetInt64(0), Is.EqualTo(1),
            "Z remains a no-CPF Gold reference");
        TestContext.WriteLine("V9_B1_FIXTURE_CPF_COMPOSITION_PASS: X/Y PRESENTE; Z SEM_CPF; no CPF anchor for X/Y");
    }

    // In every main FULL, 0006 itself is a no-CPF source and receives
    // independent raw FS evaluation. Its protected current link is preserved
    // by DT-10 even when the probabilistic result is not used for publication.
    private static async Task AssertB1GovernedNoCpfReevaluatedAsync(
        SqlConnection connection, Guid runId)
    {
        await using var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot);
        long secureObservationId;
        await using (var query = new SqlCommand("""
            SELECT po.pessoa_observacao_id,r.status,r.pessoa_uuid_resolvido,
                   r.score_melhor,r.resultado_publicacao,r.motivo_publicacao,
                   r.pessoa_uuid_publicado,r.publicado_em
              FROM silver.pessoa_observacao po
              JOIN identidade.linkage_run_item i ON i.pessoa_observacao_id=po.pessoa_observacao_id
              JOIN identidade.linkage_run lr ON lr.linkage_run_id=i.linkage_run_id
              JOIN identidade.linkage_resultado r ON r.linkage_run_id=i.linkage_run_id
                 AND r.pessoa_observacao_id=i.pessoa_observacao_id
              JOIN identidade.v_vinculo_corrente vc
                ON vc.pessoa_observacao_id=po.pessoa_observacao_id
             WHERE i.linkage_run_id=@run AND lr.status=N'PUBLICADO'
               AND po.codigo_pessoa_origem=N'V9TEST_0006' AND po.cpf IS NULL
               AND vc.pessoa_uuid=@z AND vc.status=N'RESOLVIDO'
               AND vc.metodo_resolucao=N'CORRECAO_GOVERNADA';
            """, connection, snapshot))
        {
            query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            query.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            await using var rows = await query.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True, "0006 must be physically selected and evaluated in this FULL");
            secureObservationId = rows.GetInt64(0);
            Assert.That(rows.GetString(1), Is.EqualTo("RESOLVIDO"));
            Assert.That(rows.GetGuid(2), Is.EqualTo(ExistingZ));
            Assert.That(rows.IsDBNull(3), Is.False, "0006 must have its own raw FS score");
            Assert.That(rows.GetString(4), Is.EqualTo("INDEFINIDA"));
            Assert.That(rows.GetString(5), Is.EqualTo("PRECEDENCIA_CORRECAO_GOVERNADA"));
            Assert.That(rows.IsDBNull(6), Is.True, "No probabilistic override of the governed link");
            Assert.That(rows.IsDBNull(7), Is.False);
            Assert.That(await rows.ReadAsync(), Is.False);
        }
        var census = await V9ControlledDevSecureOriginReader.ReadAsync(connection, snapshot,
            new[] { ExistingZ }, CancellationToken.None);
        var secure = census[ExistingZ].Single(o => o.Observations.Any(e => e.ObservationId == secureObservationId));
        Assert.That(secure.HasGovernedCorrection, Is.True);
        Assert.That(secure.HasCpf || secure.HasUuidFeedback || secure.HasPublishedFounderProof, Is.False);
        Assert.That(V9ControlledDevSecureEvidenceScore.IsSecureOrigin(secure), Is.True);
        await snapshot.CommitAsync();
        TestContext.WriteLine("V9_B1_NO_CPF_GOVERNED_REEVALUATED_PASS " + runId);
    }

    private static async Task AssertPhysicalIndicationReasonsAsync(
        SqlConnection connection, Guid runId)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["V9TEST_0007"] = "V9_R1_INDEFINIDO_SEM_INDICIO",
            ["V9TEST_0107"] = "V9_R1_INDEFINIDO_SEM_INDICIO"
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await using var query = new SqlCommand("""
            SELECT po.codigo_pessoa_origem,r.status,r.motivo,
                   r.pessoa_uuid_resolvido,r.resultado_publicacao,
                   r.status_publicacao,r.motivo_publicacao,
                   r.pessoa_uuid_publicado,p.estado,p.canonical_uuid,
                   r.publicado_em
              FROM identidade.linkage_resultado r
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=r.pessoa_observacao_id
              JOIN identidade.pessoa_origem_progressiva p
                ON p.pessoa_origem_id=po.pessoa_origem_id
             WHERE r.linkage_run_id=@run
               AND po.codigo_pessoa_origem IN(
                   N'V9TEST_0007',N'V9TEST_0107',N'V9TEST_0106');
            """, connection);
        query.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        await using var rows = await query.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            var code = rows.GetString(0);
            Assert.That(seen.Add(code), Is.True, code);
            if (code == "V9TEST_0106")
            {
                // S9: the protected 0006 origin is exact Silver evidence.
                // The approval must be individual and publish to Z, not a
                // group indication or a new identity.
                Assert.Multiple((Action)(() =>
                {
                    Assert.That(rows.GetString(1), Is.EqualTo("RESOLVIDO"), code);
                    Assert.That(rows.GetGuid(3), Is.EqualTo(ExistingZ), code);
                    Assert.That(rows.GetString(4), Is.EqualTo("ASSOCIACAO_EXISTENTE"), code);
                    Assert.That(rows.GetString(5), Is.EqualTo("RESOLVIDO"), code);
                    Assert.That(rows.GetGuid(7), Is.EqualTo(ExistingZ), code);
                    Assert.That(rows.GetString(8), Is.EqualTo("REFERENCIA"), code);
                    Assert.That(rows.GetGuid(9), Is.EqualTo(ExistingZ), code);
                    Assert.That(rows.IsDBNull(10), Is.False, code);
                }));
                continue;
            }
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rows.GetString(1), Is.EqualTo("NAO_RESOLVIDO"), code);
                Assert.That(rows.GetString(2), Is.EqualTo(expected[code]), code);
                Assert.That(rows.IsDBNull(3), Is.True, code);
                Assert.That(rows.GetString(4), Is.EqualTo("INDEFINIDA"), code);
                Assert.That(rows.GetString(5), Is.EqualTo("NAO_RESOLVIDO"), code);
                Assert.That(rows.GetString(6), Is.EqualTo(expected[code]), code);
                Assert.That(rows.IsDBNull(7), Is.True, code);
                Assert.That(rows.GetString(8), Is.EqualTo("INDEFINIDA"), code);
                Assert.That(rows.IsDBNull(9), Is.True, code);
                Assert.That(rows.IsDBNull(10), Is.False, code);
            }));
        }
        Assert.That(seen, Is.EquivalentTo(expected.Keys.Append("V9TEST_0106")));
        await rows.DisposeAsync();
        // Verify the actual protected independent evidence, not just a changed
        // status. A raw approval without this origin would not prove S9.
        await using var evidence = new SqlCommand("""
            SELECT COUNT_BIG(*)
              FROM silver.pessoa_observacao target
              JOIN silver.pessoa_observacao secure
                ON secure.nome_completo=target.nome_completo
               AND secure.nome_mae=target.nome_mae
               AND secure.data_nascimento=target.data_nascimento
              JOIN identidade.v_vinculo_corrente v
                ON v.pessoa_observacao_id=secure.pessoa_observacao_id
             WHERE target.codigo_pessoa_origem=N'V9TEST_0106'
               AND secure.codigo_pessoa_origem=N'V9TEST_0006'
               AND target.pessoa_origem_id<>secure.pessoa_origem_id
               AND v.status=N'RESOLVIDO' AND v.metodo_resolucao=N'CORRECAO_GOVERNADA'
               AND v.pessoa_uuid=@z;
            """, connection);
        evidence.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
        Assert.That(await evidence.ExecuteScalarAsync(), Is.EqualTo(1L));
        TestContext.WriteLine("V9_DEV_B1_SQL_S5_S9_PASS: 2 SEM_INDICIO; 0106 individually approved to Z by protected 0006 evidence");
    }

    private static async Task AssertS8UnauthenticatedGoldRollsBackBeforeScoringAsync(
        SqlConnection connection, IConfiguration configuration,
        IOperationalSqlAdapter sql, Guid attempt, string temp)
    {
        var phantom = Guid.Parse("e4e4e4e4-e004-4000-8000-000000000004");
        var beforeGold = await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM gold.pessoa;");
        var beforeRuns = await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM identidade.linkage_run;");
        // A real SQL blocking hit without a corresponding Gold reference
        // must never be silently discarded, scored or published as a no-hit.
        // This is a disposable integrity attack against the candidate JOIN.
        await using (var seed = new SqlCommand("""
            INSERT identidade.pessoa(pessoa_uuid,status)
                VALUES(@phantom,N'ATIVO');
            INSERT identidade.blocking_chave(
                pessoa_uuid,normalizacao_versao,atributo,valor_normalizado,
                semantica_temporal,vigencia_inicio,vigencia_fim,
                projection_schema_version,projection_fingerprint_sha256)
            SELECT @phantom,normalizacao_versao,atributo,valor_normalizado,
                   semantica_temporal,vigencia_inicio,vigencia_fim,
                   projection_schema_version,projection_fingerprint_sha256
              FROM identidade.blocking_chave
             WHERE pessoa_uuid=@z;
            IF @@ROWCOUNT<1 THROW 52916,'S8 requires physical V9 blocking keys.',1;
            """, connection))
        {
            seed.Parameters.Add("@phantom", SqlDbType.UniqueIdentifier).Value = phantom;
            seed.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            await seed.ExecuteNonQueryAsync();
        }
        Exception? rejection = null;
        try
        {
            var target = await CountSqlAsync(connection, """
                SELECT TOP(1) pessoa_observacao_id
                  FROM silver.pessoa_observacao
                 WHERE codigo_pessoa_origem=N'V9TEST_0003'
                   AND versao_interna=2;
                """);
            var runner = await NewDevRunnerAsync(configuration, sql, attempt, temp);
            try
            {
                await runner.RunV9ControlledDevAsync(
                    new ProbabilisticLinkageRunRequest(
                        LinkageRunType.FULL, 9, target, null, null,
                        100, 1, null, "V9_DEV_R1_S8_UNAUTHENTICATED_GOLD",
                        "FAILCLOSED_SQL_GOLD_JOIN",
                        Guid.NewGuid(), true),
                    attempt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                rejection = ex;
            }
        }
        finally
        {
            await using var cleanup = new SqlCommand("""
                DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@phantom;
                DELETE FROM identidade.pessoa WHERE pessoa_uuid=@phantom;
                """, connection);
            cleanup.Parameters.Add("@phantom", SqlDbType.UniqueIdentifier).Value = phantom;
            await cleanup.ExecuteNonQueryAsync();
        }
        Assert.That(rejection, Is.Not.Null,
            "S8 must abort an unauthenticated SQL blocking UUID.");
        Assert.That(rejection!.ToString(), Does.Contain("GOLD").IgnoreCase,
            "An unrelated failure is not proof of the Gold integrity gate.");
        await using (var check = new SqlCommand("""
            SELECT
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE solicitado_por=N'V9_DEV_R1_S8_UNAUTHENTICATED_GOLD'
                 AND status=N'FALHOU'),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado r
               JOIN identidade.linkage_run lr
                 ON lr.linkage_run_id=r.linkage_run_id
              WHERE lr.solicitado_por=N'V9_DEV_R1_S8_UNAUTHENTICATED_GOLD'
                AND r.publicado_em IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE solicitado_por=N'V9_DEV_R1_S8_UNAUTHENTICATED_GOLD');
            """, connection))
        {
            await using var rows = await check.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rows.GetInt64(0), Is.EqualTo(1));
                Assert.That(rows.GetInt64(1), Is.Zero);
                Assert.That(rows.GetInt64(2), Is.EqualTo(1));
            }));
        }
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM gold.pessoa;"), Is.EqualTo(beforeGold));
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM identidade.linkage_run;"), Is.EqualTo(beforeRuns + 1L));
        TestContext.WriteLine(
            "V9_DEV_R1_S8_INTEGRITY_SQL_PASS: orphan blocking candidate rejected before publication");
    }

    private static async Task<long> CountSqlAsync(
        SqlConnection connection, string sql)
    {
        await using var read = new SqlCommand(sql, connection);
        return Convert.ToInt64(await read.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task AssertZeroOneOriginVersionsAndVariableBatchWidthsAsync(
        SqlConnection connection, IConfiguration configuration,
        IOperationalSqlAdapter sql, Guid attempt, string temp, Guid baselineRun)
    {
        var baselineDecisions = await ReadPr1RowsAsync(connection, Pr1Decisions, baselineRun);
        var baselineGold = await CountSqlAsync(
            connection, "SELECT COUNT_BIG(*) FROM gold.pessoa;");
        var baselineGoldUuids = await ReadGoldUuidSetAsync(connection);
        var baselineRuns = await CountSqlAsync(
            connection, "SELECT COUNT_BIG(*) FROM identidade.linkage_run;");
        await AppendOriginVersionAsync(connection);

        // Full population becomes 201: 133 then 68, not the previous
        // 100+100. Both versions of the SAME persistent origin must resolve
        // to Z without any forbidden V9 group inheritance.
        var repeated = await NewDevRunnerAsync(configuration, sql, attempt, temp);
        var full = await repeated.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, null, null, 133, 1, null,
                "V9_DEV_R1_REPEAT_133", "FULL_VERSIONED_ORIGIN",
                Guid.NewGuid(), true),
            attempt, CancellationToken.None);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(full.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
            Assert.That(full.Elegiveis, Is.EqualTo(201));
            Assert.That(full.Avaliados, Is.EqualTo(201));
        }));
        await using (var check = new SqlCommand("""
            SELECT
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
               WHERE linkage_run_id=@run),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado r
               JOIN silver.pessoa_observacao po
                 ON po.pessoa_observacao_id=r.pessoa_observacao_id
               WHERE r.linkage_run_id=@run
                 AND po.codigo_pessoa_origem=N'V9TEST_0003'
                 AND r.status_publicacao=N'RESOLVIDO'
                 AND r.pessoa_uuid_publicado=@z),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE linkage_run_id=@run AND batch_size=133),
              (SELECT COUNT_BIG(*) FROM identidade.pessoa_origem_progressiva p
               JOIN silver.pessoa_observacao po
                 ON po.pessoa_origem_id=p.pessoa_origem_id
               WHERE po.codigo_pessoa_origem=N'V9TEST_0003'
                 AND p.estado=N'REFERENCIA' AND p.canonical_uuid=@z);
            """, connection))
        {
            check.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = full.RunId;
            check.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            await using var rd = await check.ExecuteReaderAsync();
            Assert.That(await rd.ReadAsync(), Is.True);
            Assert.Multiple((Action)(() =>
            {
                Assert.That(rd.GetInt64(0), Is.EqualTo(201));
                Assert.That(rd.GetInt64(1), Is.EqualTo(2),
                    "Both physical versions must publish to existing Z.");
                Assert.That(rd.GetInt64(2), Is.EqualTo(1),
                    "The recorded SQL run must have the unequal page size 133.");
                Assert.That(rd.GetInt64(3), Is.EqualTo(2),
                    "A single persistent origin must carry both versions.");
            }));
            Assert.That(await rd.ReadAsync(), Is.False);
        }
        // Check the exact set, not just the count. Keep diagnostics scoped
        // to synthetic source codes; do not dump UUIDs or person attributes.
        var repeatedGoldUuids = await ReadGoldUuidSetAsync(connection);
        if (!baselineGoldUuids.SetEquals(repeatedGoldUuids))
        {
            await using var diagnostic = new SqlCommand("""
                SELECT po.codigo_pessoa_origem,r.motivo,r.resultado_publicacao,
                       CASE WHEN r.pessoa_uuid_publicado=@z THEN 1 ELSE 0 END,
                       CASE WHEN g.pessoa_uuid IS NULL THEN 0 ELSE 1 END
                  FROM identidade.linkage_resultado r
                  JOIN silver.pessoa_observacao po
                    ON po.pessoa_observacao_id=r.pessoa_observacao_id
                  JOIN identidade.pessoa_origem_progressiva p
                    ON p.pessoa_origem_id=po.pessoa_origem_id
                  LEFT JOIN gold.pessoa g ON g.pessoa_uuid=p.initial_uuid
                 WHERE r.linkage_run_id=@run
                   AND po.codigo_pessoa_origem IN(N'V9TEST_0006',N'V9TEST_0106');
                """, connection);
            diagnostic.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = full.RunId;
            diagnostic.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            await using var rows = await diagnostic.ExecuteReaderAsync();
            while (await rows.ReadAsync())
                TestContext.WriteLine(
                    $"V9_DEV_R1_REPEAT_DIAGNOSTIC {rows.GetString(0)} " +
                    $"reason={(rows.IsDBNull(1) ? "<null>" : rows.GetString(1))} " +
                    $"publication={rows.GetString(2)} toZ={rows.GetInt32(3)} initialGoldExists={rows.GetInt32(4)}");
        }
        Assert.That(repeatedGoldUuids.SetEquals(baselineGoldUuids), Is.True,
            "Repeated FULL must preserve the exact Gold UUID set.");
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM gold.pessoa;"), Is.EqualTo(baselineGold),
            "Repeated FULL must be idempotent at Gold identity level.");
        await AssertRecomposedZAsync(connection);
        await AssertPhysicalIndicationReasonsAsync(connection, full.RunId);
        await AssertB1GovernedNoCpfReevaluatedAsync(connection, full.RunId);
        await AssertB1FixtureCpfCompositionAsync(connection);
        var repeatedDecisions = await ReadPr1RowsAsync(connection, Pr1Decisions, full.RunId);
        AssertB1StableIdentityDecisions(baselineDecisions, repeatedDecisions);

        // Concurrency fail-closed: while another owner holds BOTH real SQL
        // coordination locks, a second V9 request is refused before creating
        // a run, even though its selected model and DB pin are valid.
        var beforeBlocked = await CountSqlAsync(
            connection, "SELECT COUNT_BIG(*) FROM identidade.linkage_run;");
        await using (var held = await new SqlPipelineCoordinator(sql)
            .AcquireExclusiveJobAsync("V9_DEV_R1_LOCK_OWNER",
                TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            var contender = await NewDevRunnerAsync(
                configuration, sql, attempt, temp,
                exclusiveIntentTimeout: TimeSpan.FromMilliseconds(250));
            Exception? blockedFailure = null;
            try
            {
                await contender.RunV9ControlledDevAsync(
                    new ProbabilisticLinkageRunRequest(
                        LinkageRunType.FULL, 9, long.MaxValue, null, null,
                        100, 1, null, "V9_DEV_R1_CONCURRENCY", "LOCK_DENIAL",
                        Guid.NewGuid(), true),
                    attempt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                blockedFailure = ex;
            }
            Assert.That(blockedFailure,
                Is.TypeOf<PipelineCoordinationBusyException>(),
                "A second job cannot run or publish while a SQL lease is held.");
            Assert.That(await CountSqlAsync(connection,
                "SELECT COUNT_BIG(*) FROM identidade.linkage_run;"),
                Is.EqualTo(beforeBlocked),
                "Denied concurrent job must not even create a run header.");
        }
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM gold.pessoa;"), Is.EqualTo(baselineGold));
        TestContext.WriteLine("V9_DEV_R1_SQL_CONCURRENCY_GATE_PASS: exclusive lock denied without run/Gold changes");

        // Empty scoped FULL is real (no guessed success). A nonexistent
        // Gestor code is a valid empty SQL scope. Do NOT use an unknown
        // PessoaObservacaoId: the run header itself has a real Silver FK.
        var empty = await NewDevRunnerAsync(configuration, sql, attempt, temp);
        var zero = await empty.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, null, "V9_NONEXISTENT_GESTOR", null, 100, 1, null,
                "V9_DEV_R1_EMPTY", "ZERO_SELECTED_ROWS",
                Guid.NewGuid(), true),
            attempt, CancellationToken.None);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(zero.Elegiveis, Is.Zero);
            Assert.That(zero.Avaliados, Is.Zero);
            Assert.That(zero.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
        }));
        await using (var verifyEmpty = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
             WHERE linkage_run_id=@run;
            """, connection))
        {
            verifyEmpty.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = zero.RunId;
            Assert.That(await verifyEmpty.ExecuteScalarAsync(), Is.EqualTo(0L));
        }

        // Single-item scoped FULL uses the very same real FS/Gold/DT-10 path;
        // it does not require fabricating a second source or inferred group.
        long secondVersionId;
        await using (var version = new SqlCommand("""
            SELECT pessoa_observacao_id FROM silver.pessoa_observacao
             WHERE codigo_pessoa_origem=N'V9TEST_0003' AND versao_interna=2;
            """, connection))
            secondVersionId = Convert.ToInt64(await version.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        var singleton = await NewDevRunnerAsync(configuration, sql, attempt, temp);
        var one = await singleton.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(
                LinkageRunType.FULL, 9, secondVersionId, null, null, 200, 1, null,
                "V9_DEV_R1_SINGLE", "ONE_SELECTED_ROW",
                Guid.NewGuid(), true),
            attempt, CancellationToken.None);
        Assert.Multiple((Action)(() =>
        {
            Assert.That(one.Elegiveis, Is.EqualTo(1));
            Assert.That(one.Avaliados, Is.EqualTo(1));
            Assert.That(one.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
        }));
        await using (var verifyOne = new SqlCommand("""
            SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
             WHERE linkage_run_id=@run AND pessoa_observacao_id=@source
               AND resultado_publicacao=N'ASSOCIACAO_EXISTENTE'
               AND status=N'RESOLVIDO' AND pessoa_uuid_resolvido=@z
               AND status_publicacao=N'RESOLVIDO'
               AND motivo_publicacao=N'REFERENCIA_PROGRESSIVA_CONFIRMADA'
               AND pessoa_uuid_publicado=@z;
            """, connection))
        {
            verifyOne.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = one.RunId;
            verifyOne.Parameters.Add("@source", SqlDbType.BigInt).Value = secondVersionId;
            verifyOne.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
            Assert.That(await verifyOne.ExecuteScalarAsync(), Is.EqualTo(1L));
        }
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM gold.pessoa;"), Is.EqualTo(baselineGold),
            "Zero/one scope and repeated FULL must not create second Gold UUID.");
        Assert.That(await CountSqlAsync(connection,
            "SELECT COUNT_BIG(*) FROM identidade.linkage_run;"),
            Is.EqualTo(baselineRuns + 3L));
        TestContext.WriteLine(
            "V9_DEV_R1_0_1_VERSIONS_BATCH_133_PHYSICAL_SQL_PASS: " +
            "0 and 1 selected, 201 FULL at 133+68, same-origin version2, Gold idempotence");
    }

    private static async Task<ProbabilisticLinkageBatchRunner> NewDevRunnerAsync(
        IConfiguration configuration, IOperationalSqlAdapter sql,
        Guid attempt, string temp,
        TimeSpan? exclusiveIntentTimeout = null)
    {
        var scorer = await V9ControlledDevCohortScorer.CreateAsync(
            configuration, sql, attempt, CancellationToken.None);
        return new ProbabilisticLinkageBatchRunner(
            configuration, sql, scorer,
            new SqlPipelineCoordinator(sql,
                exclusiveIntentTimeout: exclusiveIntentTimeout), new Dt05ReplaySql(sql),
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
               WHERE status=N'FALHOU'
               ORDER BY iniciado_em DESC);
            SELECT
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
               WHERE linkage_run_id=@failed),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado
               WHERE linkage_run_id=@failed AND publicado_em IS NOT NULL),
              (SELECT COUNT_BIG(*) FROM gold.pessoa),
              (SELECT COUNT_BIG(*) FROM identidade.vinculo_fonte),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE linkage_run_id=@failed AND status=N'FALHOU'),
              (SELECT COUNT_BIG(*) FROM identidade.linkage_run
               WHERE solicitado_por=N'V9_DEV_FAILCLOSED_BATCH2');
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
                Assert.That(rd.GetInt64(3), Is.EqualTo(5),
                    "Only the five pre-established governed links may exist.");
                Assert.That(rd.GetInt64(4), Is.EqualTo(1),
                    "Failed runner must be explicitly marked FALHOU.");
                Assert.That(rd.GetInt64(5), Is.EqualTo(1),
                    "Exactly one run must originate from the injected failure.");
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
            // Re-running FULL after DT-10 creates real new Gold from
            // previously unmatched Silver. Each of those Gold references
            // must be scoreable under this SYNTHETIC-only TF fixture; otherwise
            // the intended idempotence test is blocked by a missing fake IBGE
            // vocabulary, not by a linkage violation. Never alter official #38.
            var terms = slot.Side == CompositeNominalTfSideV9.Person
                ? Enumerable.Range(1, 100)
                    .Select(i => "ALFA" + i.ToString("D4",
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Concat(Enumerable.Range(101, 100).Select(i =>
                        "BETA" + i.ToString("D4",
                            System.Globalization.CultureInfo.InvariantCulture)))
                    .Concat(new[] { "OMEGA0007", "SILVANA", "SILVIA" })
                : Enumerable.Range(1, 100)
                    .Select(i => "CARLA" + i.ToString("D4",
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Concat(Enumerable.Range(101, 100).Select(i =>
                        "DORA" + i.ToString("D4",
                            System.Globalization.CultureInfo.InvariantCulture)))
                    .Concat(new[] { "VERONICA0007", "COSTELA", "COSTUMO" });
            var active = slot.Calibration
                ?? throw new InvalidDataException("V9_DEV_R1_SYNTHETIC_TF_MISSING");
            var frequencies = active.Frequencies.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            var trainU = active.TrainUnitUByValue.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            foreach (var term in terms)
            {
                frequencies.TryAdd(term, .25m);
                trainU.TryAdd(term, .001m);
            }
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
    private static async Task FoundZFromIndependentOriginAsync(SqlConnection connection,
        IConfiguration configuration, IOperationalSqlAdapter sql, Guid attempt, string temp)
    {
        long observationId;
        Guid initialUuid;
        await using (var seed = new SqlCommand("""
            UPDATE founder
               SET nome_completo=source.nome_completo,nome_cmp=source.nome_cmp,
                   nome_mae=source.nome_mae,nome_mae_cmp=source.nome_mae_cmp,
                   data_nascimento=source.data_nascimento,
                   source_as_of=DATEADD(day,-2,source.source_as_of),
                   conteudo_hash=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',
                       N'V9_B1_INDEPENDENT_FOUNDER_0004'),2))
              FROM silver.pessoa_observacao founder
              CROSS JOIN silver.pessoa_observacao source
             WHERE founder.codigo_pessoa_origem=N'V9TEST_0004'
               AND source.codigo_pessoa_origem=N'V9TEST_0003';
            IF @@ROWCOUNT<>1 THROW 52940,'Independent Z founder missing.',1;
            SELECT po.pessoa_observacao_id,p.initial_uuid
              FROM silver.pessoa_observacao po
              JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
             WHERE po.codigo_pessoa_origem=N'V9TEST_0004' AND p.estado=N'PROVISORIA';
            """, connection))
        {
            await using var rows = await seed.ExecuteReaderAsync();
            Assert.That(await rows.ReadAsync(), Is.True);
            observationId = rows.GetInt64(0);
            initialUuid = rows.GetGuid(1);
            Assert.That(await rows.ReadAsync(), Is.False);
        }
        Assert.That(await CountSqlAsync(connection, "SELECT COUNT_BIG(*) FROM gold.pessoa;"), Is.Zero);
        var runner = await NewDevRunnerAsync(configuration, sql, attempt, temp);
        var run = await runner.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(LinkageRunType.FULL, 9, observationId,
                null, null, 100, 1, null, "V9_DEV_B1_Z_FOUNDER", "S1_INDEPENDENT_FOUNDER",
                Guid.NewGuid(), true), attempt, CancellationToken.None);
        Assert.That(run.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
        Assert.That(run.Elegiveis, Is.EqualTo(1));
        Assert.That(run.Avaliados, Is.EqualTo(1));
        await using (var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot))
        {
            var census = await V9ControlledDevSecureOriginReader.ReadAsync(
                connection, snapshot, new[] { initialUuid }, CancellationToken.None);
            var founder = census[initialUuid].Single();
            Assert.That(founder.HasPublishedFounderProof, Is.True);
            Assert.That(founder.InitialUuid, Is.EqualTo(initialUuid));
            Assert.That(V9ControlledDevSecureEvidenceScore.IsSecureOrigin(founder), Is.True);
            Assert.That(founder.Observations.Single().ObservationId, Is.EqualTo(observationId));
            await snapshot.CommitAsync();
        }
        ExistingZ = initialUuid;
        // Same explicit diagnostic projection fixture used in T3; not a claim
        // of automatic blocking maintenance (#1936 remains a separate phase).
        await using var removeLegacy = new SqlCommand("""
            DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@z;
            """, connection);
        removeLegacy.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
        Assert.That(await removeLegacy.ExecuteNonQueryAsync(), Is.GreaterThan(0));
        TestContext.WriteLine("V9_B1_S1_INDEPENDENT_Z_FOUNDER " + run.RunId);
    }

    private static async Task SeedExistingGoldReferencesAndProtectedOriginAsync(
        SqlConnection connection)
    {
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var seed = new SqlCommand("""
            INSERT identidade.pessoa(pessoa_uuid,status)
            VALUES(@x,N'ATIVO'),(@y,N'ATIVO');
            -- The two reference summaries are recomposed from Silver below.
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

            -- Y's original protected 0005 record has different nominal
            -- evidence from 0101. Add a second CPF-bearing observation to
            -- that same governed origin, with the matching nominal fields.
            -- It is outside the no-CPF FULL cohort, not a competing Silver
            -- group member. Its authority is the current governed link on
            -- 0005, NOT the CPF value or a fabricated deterministic link.
            -- This is synthetic SQL fixture setup, not Processor admission.
            INSERT silver.pessoa_observacao(
                pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
                nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            SELECT governed.pessoa_origem_id,governed.lote_id,governed.gestor_id,
                   governed.codigo_pessoa_origem,2,
                   LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',
                       N'V9_B1_Y_GOVERNED_HISTORY'),2)),
                   '52998224725',NULL,
                   matching.nome_completo,matching.nome_cmp,matching.data_nascimento,
                   matching.nome_mae,matching.nome_mae_cmp,
                   DATEADD(second,1,governed.source_as_of)
              FROM silver.pessoa_observacao governed
              CROSS JOIN silver.pessoa_observacao matching
             WHERE governed.codigo_pessoa_origem=N'V9TEST_0005'
               AND matching.codigo_pessoa_origem=N'V9TEST_0101';
            IF @@ROWCOUNT<>1 THROW 52941,'Independent Y history missing.',1;

            -- Real governed history supplies X's distinct display projection.
            -- The synthetic CPF only excludes this history from no-CPF FULL,
            -- never supplies CPF_DETERMINISTICO authority.
            INSERT silver.pessoa_observacao(
                pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                versao_interna,conteudo_hash,cpf,cpf_ausente_motivo,
                nome_completo,nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            SELECT pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,2,
                   LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',
                       N'V9_B1_X_GOVERNED_HISTORY'),2)),
                   '11144477735',NULL,N'FELICIA PEREIRA',N'FELICIA PEREIRA',
                   '1970-01-02',N'BEATRIZ FONSECA',N'BEATRIZ FONSECA',
                   DATEADD(second,1,source_as_of)
              FROM silver.pessoa_observacao
             WHERE codigo_pessoa_origem=N'V9TEST_0001' AND versao_interna=1;
            IF @@ROWCOUNT<>1 THROW 52942,'Independent X history missing.',1;
            INSERT identidade.vinculo_fonte(
                pessoa_observacao_id,pessoa_uuid,metodo_resolucao,score,status,
                modelo_id,ativo,resolvido_em,motivo)
            SELECT pessoa_observacao_id,
                   CASE WHEN codigo_pessoa_origem=N'V9TEST_0001' THEN @x ELSE @y END,
                   N'CORRECAO_GOVERNADA',NULL,N'RESOLVIDO',NULL,1,
                   SYSDATETIMEOFFSET(),N'REFERENCIA_SINTETICA_DEV_EFEMERA'
              FROM silver.pessoa_observacao
             WHERE codigo_pessoa_origem IN(N'V9TEST_0001',N'V9TEST_0005')
               AND versao_interna=2;
            IF @@ROWCOUNT<>2 THROW 52943,'Governed X/Y history links missing.',1;
            -- Select the persistent origin, not its versioned observations.
            -- Require exactly one origin rather than masking a duplicate with TOP.
            IF (SELECT COUNT_BIG(*) FROM silver.pessoa_origem
                WHERE codigo_pessoa_origem=N'V9TEST_0001')<>1
                THROW 52944,'Expected exactly one protected X origin.',1;
            DECLARE @origin bigint=(
                SELECT pessoa_origem_id FROM silver.pessoa_origem
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
            EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@x;
            EXEC identidade.sp_recompor_gold_pessoa @pessoa_uuid=@y;
            """, connection, tx);
        seed.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = ProtectedX;
        seed.Parameters.Add("@y", SqlDbType.UniqueIdentifier).Value = ExistingY;
        seed.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
        await seed.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    // Prove the opposing Y vote BEFORE the real FULL, using actual SQL
    // candidate membership and secure origin records, not the Gold summary.
    private static async Task AssertS2IndependentYAuthorityAsync(SqlConnection connection,
        V9FittedSnapshot winner)
    {
        await using var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot);
        var sourceRows = await ReadPr1RowsAsync(connection, """
            SELECT nome_completo,data_nascimento,nome_mae
              FROM silver.pessoa_observacao
             WHERE codigo_pessoa_origem=N'V9TEST_0101' AND versao_interna=1 AND cpf IS NULL;
            """, tx: snapshot);
        var source = sourceRows.Single();
        var observation = new IdentityObservation(null, "SEM_CPF",
            (string?)source[0], DateOnly.FromDateTime((DateTime)source[1]!), (string?)source[2]);
        var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
        var candidates = await V9OperationalCandidateRead.ReadAsync(connection, snapshot,
            planner, observation, CancellationToken.None);
        Assert.That(candidates.CandidateUuids, Does.Contain(ExistingY));
        var census = await V9ControlledDevSecureOriginReader.ReadAsync(connection, snapshot,
            candidates.CandidateUuids, CancellationToken.None);
        var y = census[ExistingY].Single();
        Assert.That(y.HasGovernedCorrection, Is.True);
        Assert.That(y.HasCpf, Is.False, "The independent authority is the governed link");
        Assert.That(y.Observations.Count, Is.EqualTo(2));
        Assert.That(V9ControlledDevSecureEvidenceScore.IsSecureOrigin(y), Is.True);
        var result = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(winner,
            V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256),
            observation, candidates.CandidateUuids, census);
        Assert.That(result.Decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
        Assert.That(result.Decision.PessoaUuidResolvido, Is.EqualTo(ExistingY));
        await snapshot.CommitAsync();
        TestContext.WriteLine("V9_B1_S2_INDEPENDENT_Y_AUTHORITY_PASS");
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
        SqlConnection connection, V9FittedSnapshot winner, Guid? only = null)
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
             WHERE ((@only IS NULL AND pessoa_uuid IN (@x,@y,@z))
                    OR pessoa_uuid=@only)
               AND estado_identidade=N'REFERENCIA'
               AND completude_nucleo=N'COMPLETO'
             ORDER BY pessoa_uuid;
            """, connection, tx))
        {
            read.Parameters.Add("@only", SqlDbType.UniqueIdentifier).Value = (object?)only ?? DBNull.Value;
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
        var expected = only.HasValue ? 1 : 3;
        if (sources.Count != expected || sources.Select(x => x.Id).Distinct().Count() != expected)
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

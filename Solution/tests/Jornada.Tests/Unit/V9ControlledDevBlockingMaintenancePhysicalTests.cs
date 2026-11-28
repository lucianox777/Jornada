using System.Data;
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

    private static ParsedPerson Rf1936Person(string code, string? cpf, string name, string mother,
        IReadOnlyList<ParsedTransversalAttribute>? attributes = null) =>
        new(code, code, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(),
            null, cpf, cpf is null ? "SEM_CPF" : null, name, new DateOnly(1980, 5, 6), mother,
            attributes ?? [], []);

    private static ParsedTransversalAttribute Rf1936SocialName() => new(
        "RF1936-SOCIAL", PersonResolutionContractCatalog.SocialName, "ALFA SOCIAL",
        "COMPROVADO", "DOCUMENTO",
        DateTimeOffset.Parse("2026-11-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-11-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-11-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        null, null, null);

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

    // Exercise the real legacy SQL candidate loader and the frozen V8
    // decision implementation with the existing neutral-missing test parameters.
    // No production model is loaded or activated; only a digest is retained.
    private static async Task<string> Rf1936V8ReceiptAsync(SqlConnection db, Guid founderUuid)
    {
        var p = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [LinkageParameterCatalog.PriorMatchProbability] = .01m,
            [LinkageParameterCatalog.PriorBlockMin] = .000001m,
            [LinkageParameterCatalog.PriorBlockMax] = .25m,
            [LinkageParameterCatalog.Threshold] = .95m,
            [LinkageParameterCatalog.ConflictMargin] = .03m,
            [LinkageParameterCatalog.LogOddsConflictMargin] = .03m,
            [LinkageParameterCatalog.DecisionEvidenceScoring] = 1m,
            [LinkageParameterCatalog.NeutralMissingEvidenceScoring] = 1m,
            [LinkageParameterCatalog.BirthSemanticEvidenceScoring] = 1m
        };
        foreach (var state in LinkageParameterCatalog.NameStates)
        {
            p["M_NOME_" + state] = .25m;
            p["U_NOME_" + state] = .25m;
            p["M_NOME_MAE_" + state] = .25m;
            p["U_NOME_MAE_" + state] = .25m;
        }
        foreach (var state in BirthDateSemanticEvidence.States)
        {
            p["M_NASCIMENTO_SEMANTICO_" + state] = .5m;
            p["U_NASCIMENTO_SEMANTICO_" + state] = .5m;
        }
        var model = LinkageModelPolicy.Create(
            Guid.Parse("88000000-0000-4000-8000-000000000008"), 8,
            LinkageParameterCatalog.NeutralMissingDecisionEvidenceAlgorithmVersion, p);
        var ruleset = LinkageDynamicRuleSet.CreateWithPasses(
            "RF1936-V8-CONTROL", "INTEGRATION-TEST",
            [LinkageBlockingPass.Create("full-name", [BlockingFeatureNames.FullName])],
            Array.Empty<KeyValuePair<string, decimal>>()) with
        {
            ProjectionSchemaVersion = PersonResolutionProjectionContract.SchemaVersion,
            ProjectionFingerprintSha256 = PersonResolutionProjectionContract.FingerprintSha256
        };
        var birth = new DateOnly(1980, 5, 6);
        var observations = new IdentityObservation[]
        {
            new(null, "SEM_CPF", "ALFA0003 SILVA", birth, "CARLA0003 COSTA"),
            new(null, "SEM_CPF", "ALFA0003 SILVA", birth, null),
            new(null, "SEM_CPF", "MARIA SILVA", birth, "ANA COSTA"),
            new(null, "SEM_CPF", "OMEGA9108 SILVA", birth, "ANA COSTA"),
            new(null, "SEM_CPF", "UNMATCHED9109", birth, null)
        };
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < observations.Length; i++)
        {
            var observation = observations[i];
            var candidates = await BlockingProjectionCandidateLoader.LoadAsync(db, ruleset,
                observation, 20, 60, CancellationToken.None);
            if (i == 0)
                Assert.That(candidates.Select(c => c.PessoaUuid), Does.Contain(founderUuid));
            var decision = ProbabilisticLinkageDecisions.Resolve(model, observation, candidates);
            sha.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { candidates, decision }));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    // Independent read-only per-person rederivation of all temporal keys from
    // the same Gold and linked Silver history. It uses the frozen pure projector,
    // not the set-based persistence census or its coverage receipt, and requires
    // exact SetEquals against the physical result.
    private static async Task VerifyRf1936ProjectionParityAsync(SqlConnection db, Guid attempt,
        V9FittedSnapshotExecutionRuntime planner)
    {
        var ct = CancellationToken.None;
        var prefix = planner.MaximumPrefixLength!.Value;
        var fingerprint = planner.ProjectionFingerprintSha256;
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot);
        await V9BlockingMaintenanceGate.RequireAsync("Development", true, attempt, db, tx, ct);
        var expected = new HashSet<(Guid Person, string Feature, string Value, string Temporal,
            DateTimeOffset From, DateTimeOffset? To)>();
        var people = new List<(Guid Id,string? Name,string? Mother,DateOnly? Birth,DateTimeOffset AsOf)>();
        await using (var gold = new SqlCommand("""
            SELECT pessoa_uuid,nome_completo,nome_mae,data_nascimento,atualizado_em
              FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';
            """, db, tx))
        await using (var rows = await gold.ExecuteReaderAsync(ct))
            while (await rows.ReadAsync(ct))
                people.Add((rows.GetGuid(0), rows.IsDBNull(1) ? null : rows.GetString(1),
                    rows.IsDBNull(2) ? null : rows.GetString(2),
                    rows.IsDBNull(3) ? null : DateOnly.FromDateTime(rows.GetDateTime(3)),
                    rows.GetDateTimeOffset(4)));

        static bool IsAlias(string feature) =>
            feature.StartsWith("name_prefix_", StringComparison.Ordinal)
            || feature.StartsWith("mother_name_prefix_", StringComparison.Ordinal)
            || BlockingFeatureTemporalCatalog.Get(feature) == BlockingFeatureTemporalSemantics.VersionedAlias;

        foreach (var person in people)
        {
            var current = V9CalibrationBlockingProjection.Project(person.Name, person.Mother, person.Birth, prefix);
            foreach (var key in current.Where(k => !IsAlias(k.Feature)))
                expected.Add((person.Id,key.Feature,key.Value,"STABLE_IDENTITY_DATUM",person.AsOf,null));
            var occurrences = current.Where(k => IsAlias(k.Feature))
                .Select(k => (k.Feature,k.Value,AsOf:person.AsOf,Current:true)).ToList();
            await using (var history = new SqlCommand("""
                SELECT po.nome_completo,po.nome_mae,po.source_as_of
                  FROM silver.pessoa_observacao po JOIN identidade.v_vinculo_corrente vc
                    ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                 WHERE vc.pessoa_uuid=@person AND vc.status=N'RESOLVIDO';
                """, db, tx))
            {
                history.Parameters.Add("@person", SqlDbType.UniqueIdentifier).Value = person.Id;
                await using var rows = await history.ExecuteReaderAsync(ct);
                while (await rows.ReadAsync(ct))
                {
                    var at = rows.GetDateTimeOffset(2);
                    foreach (var k in V9CalibrationBlockingProjection.Project(
                        rows.IsDBNull(0) ? null : rows.GetString(0),
                        rows.IsDBNull(1) ? null : rows.GetString(1), person.Birth, prefix)
                        .Where(k => IsAlias(k.Feature)))
                        occurrences.Add((k.Feature,k.Value,at,false));
                }
            }
            foreach (var alias in occurrences.GroupBy(x => (x.Feature,x.Value)))
                expected.Add((person.Id,alias.Key.Feature,alias.Key.Value,"VERSIONED_ALIAS",
                    alias.Min(x => x.AsOf),alias.Any(x => x.Current) ? null : alias.Max(x => x.AsOf)));
        }

        var actual = new HashSet<(Guid Person, string Feature, string Value, string Temporal,
            DateTimeOffset From, DateTimeOffset? To)>();
        await using (var keys = new SqlCommand("""
            SELECT pessoa_uuid,atributo,valor_normalizado,semantica_temporal,vigencia_inicio,vigencia_fim,
                   normalizacao_versao,projection_schema_version,projection_fingerprint_sha256
              FROM identidade.blocking_chave_v9;
            """, db, tx))
        await using (var rows = await keys.ExecuteReaderAsync(ct))
            while (await rows.ReadAsync(ct))
            {
                Assert.That(rows.GetString(6), Is.EqualTo(IdentityComparison.NormalizationVersion));
                Assert.That(rows.GetString(7), Is.EqualTo(planner.ProjectionSchemaVersion));
                Assert.That(rows.GetString(8), Is.EqualTo(fingerprint));
                Assert.That(actual.Add((rows.GetGuid(0),rows.GetString(1),rows.GetString(2),rows.GetString(3),
                    rows.GetDateTimeOffset(4),rows.IsDBNull(5) ? null : rows.GetDateTimeOffset(5))), Is.True);
            }
        Assert.That(actual.Count, Is.EqualTo(expected.Count));
        Assert.That(actual.SetEquals(expected), Is.True, "Independent full temporal projection parity");
        var manifest = await V9OperationalBlockingManifest.ReadIsolatedAsync(db, tx, planner, ct);
        Assert.That(manifest.ActiveKeyRows, Is.EqualTo(expected.LongCount(k => k.To is null)));
        Assert.That(manifest.CanonicalManifestSha256, Has.Length.EqualTo(64));
        await tx.CommitAsync(ct);
    }

    private static async Task VerifyRf1936Async(SqlConnection db, IOperationalSqlAdapter sql,
        Guid attempt, V9FittedSnapshot winner)
    {
        await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(db, 0);
        await using (var attribute = new SqlCommand("""
            IF NOT EXISTS (
                SELECT 1 FROM ref.atributo_transversal
                WHERE atributo_codigo=N'NOME_SOCIAL'
            )
                INSERT ref.atributo_transversal(
                    atributo_codigo,nome,formato_codigo,regra_temporal_codigo,
                    cardinalidade,chave_instancia_codigo)
                VALUES(
                    N'NOME_SOCIAL',N'Nome social',N'TEXTO_UTF8_V1',
                    N'REFERENCIA_EVIDENCIA_OU_VERIFICADO_EM_V1',N'SINGLE',N'UNICA_V1');
            """, db))
        {
            await attribute.ExecuteNonQueryAsync();
        }
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
            var processor = new SqlProcessorRepository(sql, new RegistryQualityEngine([]));
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
                Rf1936Person("RF1936_CPF", "52998224725", "MARIA SILVA", "ANA COSTA",
                    [Rf1936SocialName()]),
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
            Assert.That(await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9;"), Is.Zero,
                "The Processor must not write V9, even for a CPF reference");
            Assert.That(await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.blocking_chave;"), Is.GreaterThan(0));
            var first = await FullAsync(2);
            Assert.That(await CountSqlAsync(db, """
                SELECT COUNT_BIG(*) FROM identidade.blocking_chave
                WHERE atributo LIKE N'nome_social%';
                """), Is.GreaterThan(0), "The transversal control must exist in the legacy projection");
            Assert.That(await CountSqlAsync(db, """
                SELECT COUNT_BIG(*) FROM identidade.blocking_chave_v9
                WHERE atributo LIKE N'nome_social%';
                """), Is.Zero, "The frozen V9 projection must ignore transversal attributes");
            await using (var keys = new SqlCommand("""
                SELECT atributo,valor_normalizado FROM identidade.blocking_chave_v9 WHERE pessoa_uuid=@person;
                """, db))
            {
                keys.Parameters.Add("@person", SqlDbType.UniqueIdentifier).Value = cpfUuid;
                var actual = new HashSet<(string,string)>();
                await using var rows = await keys.ExecuteReaderAsync();
                while (await rows.ReadAsync()) actual.Add((rows.GetString(0),rows.GetString(1)));
                Assert.That(actual.SetEquals(expectedKeys), Is.True);
            }
            await AssertCandidateAsync(new IdentityObservation(null,"SEM_CPF","MARIA SILVA",
                new DateOnly(1980,5,6),"ANA COSTA"), cpfUuid);
            await VerifyRf1936ProjectionParityAsync(db, attempt, planner);
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
            var beforeProcessorDelta = await Rf1936DigestAsync(db);
            await PersistAsync(processor, "V9TEST",
                Rf1936Person("RF1936_CPF_ALIAS", "52998224725", "ZETA9107 SILVA", "ANA COSTA"));
            await PersistAsync(processor, "V9TEST2",
                Rf1936Person("RF1936_SECOND", null, "ALFA0003 SILVA", "CARLA0003 COSTA"));
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(beforeProcessorDelta),
                "Legitimate Gold/history changes by Processor must not write the V9 index");
            // Inject a bounded failure at the real DML boundary. No partial
            // projection, new result or publication may survive the rollback.
            var legacyAfterProcessorDelta = await Rf1936LegacyDigestAsync(db);
            var resultsBeforeFailure = await CountSqlAsync(db, "SELECT COUNT_BIG(*) FROM identidade.linkage_resultado;");
            await using (var fail = new SqlCommand("""
                CREATE TRIGGER identidade.tr_rf1936_sync_fault ON identidade.blocking_chave_v9
                AFTER INSERT,UPDATE,DELETE AS THROW 52992,'Synthetic V9 synchronization failure.',1;
                """, db))
                await fail.ExecuteNonQueryAsync();
            try
            {
                var failingRunner = await RunnerAsync();
                var fault = Assert.ThrowsAsync<SqlException>((Func<Task>)(() =>
                    failingRunner.RunV9ControlledDevAsync(new ProbabilisticLinkageRunRequest(
                        LinkageRunType.FULL,9,null,null,null,100,1,null,"RF1936","SYNTHETIC_SYNC_FAILURE",
                        Guid.NewGuid(),true),attempt,CancellationToken.None)));
                Assert.That(fault!.Number, Is.EqualTo(52992));
                Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(beforeProcessorDelta));
                Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyAfterProcessorDelta));
                Assert.That(await CountSqlAsync(db,"SELECT COUNT_BIG(*) FROM identidade.linkage_resultado;"),
                    Is.EqualTo(resultsBeforeFailure));
            }
            finally
            {
                await using var dropFault = new SqlCommand("DROP TRIGGER identidade.tr_rf1936_sync_fault;", db);
                await dropFault.ExecuteNonQueryAsync();
            }
            var second = await FullAsync(3);
            await VerifyRf1936ProjectionParityAsync(db, attempt, planner);
            Assert.That(await Rf1936DigestAsync(db), Is.Not.EqualTo(beforeProcessorDelta),
                "Runner must synchronize the legitimate delta");
            await AssertCandidateAsync(new IdentityObservation(null,"SEM_CPF","ZETA9107 SILVA",
                new DateOnly(1980,5,6),"ANA COSTA"), cpfUuid);
            var secondRows = await ReadPr1RowsAsync(db, Pr1Decisions, second);
            var secondTarget = secondRows.Single(r => Equals(r[0],"RF1936_SECOND"));
            Assert.That(secondTarget[2], Is.EqualTo("RESOLVIDO"));
            Assert.That(secondTarget[4], Is.EqualTo(founderUuid));
            Assert.That(secondTarget[13], Is.EqualTo(founderUuid));
            var third = await FullAsync(3);
            var thirdRows = await ReadPr1RowsAsync(db, Pr1Decisions, third);
            foreach (var row in secondRows)
                Assert.That(thirdRows.Single(r => Equals(r[0],row[0])).Take(10), Is.EqualTo(row.Take(10)));
            // Isolate the V9 writer from legitimate Gold mutations: capture
            // V8 only after Processor finishes, then reconcile the changed
            // V9 index with no publication or mutation of the Gold universe.
            // The first/repeated FULL proofs above never rely on this control.
            var priorV9 = await Rf1936DigestAsync(db);
            await PersistAsync(processor, "V9TEST",
                Rf1936Person("RF1936_V8_CONTROL_DELTA", "52998224725", "OMEGA9108 SILVA", "ANA COSTA"));
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(priorV9));
            var legacyControl = await Rf1936LegacyDigestAsync(db);
            var v8Control = await Rf1936V8ReceiptAsync(db, founderUuid);
            await using (var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot))
            {
                await BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, tx, "Development", true,
                    attempt, prefix, fingerprint, CancellationToken.None);
                await tx.CommitAsync();
            }
            Assert.That(await Rf1936DigestAsync(db), Is.Not.EqualTo(priorV9));
            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyControl));
            Assert.That(await Rf1936V8ReceiptAsync(db, founderUuid), Is.EqualTo(v8Control),
                "V8 SQL candidates and decisions are unchanged by an actual V9 projection write");
            await VerifyRf1936ProjectionParityAsync(db, attempt, planner);

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
                await using (var deniedTx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                        BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, deniedTx, environment, true,
                            attempt, prefix, fingerprint, CancellationToken.None)));
                    Assert.That(error!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                    var refreshError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                        BlockingProjectionPersistence.RefreshPinnedV9Async(db, deniedTx, environment, true,
                            attempt, prefix, fingerprint, CancellationToken.None)));
                    Assert.That(refreshError!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                    await deniedTx.RollbackAsync();
                }
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

            await using (var deniedDefault = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var error = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(db, deniedDefault, "Development", false,
                        attempt, prefix, fingerprint, CancellationToken.None)));
                Assert.That(error!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                var refreshError = Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)(() =>
                    BlockingProjectionPersistence.RefreshPinnedV9Async(db, deniedDefault, "Development", false,
                        attempt, prefix, fingerprint, CancellationToken.None)));
                Assert.That(refreshError!.Message, Does.StartWith(V9BlockingMaintenanceGate.ProductiveNotCertified));
                await deniedDefault.RollbackAsync();
            }
            Assert.That(await Rf1936DigestAsync(db), Is.EqualTo(before));
            Assert.That(await Rf1936LegacyDigestAsync(db), Is.EqualTo(legacyBefore));

            // Runner producer admissions with populated projection and both
            // DML tripwires armed. Wrong/missing resident metadata is tested
            // in this disposable DEV DB only; restore it even on failure.
            var negativeIndex = 0;
            async Task AssertWrongDatabaseAsync(Guid requestedAttempt)
            {
                negativeIndex++;
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
            TestContext.WriteLine("V9_RF1936_C07_DB_NAME_RESIDENT_PROPERTIES_AND_RUNNER_PASS");

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
                var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "rf1936-sql", relative));
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
                SELECT @@ROWCOUNT;
                """, db))
                // The actual reversal/migration leaves session NOCOUNT ON:
                // verify SQL's affected-row count, not ExecuteNonQuery's -1.
                Assert.That(Convert.ToInt32(await damage.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(1));
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
                    processorRepository = true, processorWritesV9 = false, runnerSynchronizesGoldDelta = true,
                    httpAdmission = false, isolatedIndex = true,
                    physicalReplayDigest = before, c07NonemptyNegative = true,
                    c07ResidentDatabaseNegatives = negativeIndex, staleRejected = true,
                    reversalPreserved = true, fullTemporalParity = true, isolatedManifest = true,
                    rollbackAndRecovery = true, v8LegacySqlCandidateDecisionParity = true, scientificTest = false, certified = false }));
            TestContext.WriteLine("V9_RF1936_REPOSITORY_FIRST_AND_REPEATED_FULL_C07_PASS_NOT_HTTP_OR_SCALE");
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}

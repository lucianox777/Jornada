using System.Data;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Unit;

public sealed partial class V9ControlledDevFullSqlR1Tests
{
    // T3 uses synthetic observations, never official #38 fit or real people.
    // Two high-support cells represent one exact nominal side and one partial
    // side; the two-partial cell is weak. Actual FS/TF decides every pair.
    private static readonly IdentityObservation T3S = new(null, "SEM_CPF",
        "ALFA0003 SILVA", new DateOnly(1980, 5, 6), "CARLA0003 COSTA");
    private static readonly IdentityObservation T3A = new(null, "SEM_CPF",
        "ALFA0003 SILVA", new DateOnly(1980, 5, 6), "CARLA0003 LIMA");
    private static readonly IdentityObservation T3B = new(null, "SEM_CPF",
        "ALFA0003 SOUZA", new DateOnly(1980, 5, 6), "CARLA0003 LIMA");

    private static (CompositeNominalLevelV9 Person, CompositeNominalLevelV9 Mother) T3Cell(
        IdentityObservation left, IdentityObservation right) =>
        (CompositeNominalComparisonV9.Classify(left.NomeCompleto, right.NomeCompleto),
         CompositeNominalComparisonV9.Classify(left.NomeMae, right.NomeMae));

    private static V9FittedSnapshot T3DiagnosticWinner()
    {
        var basis = DistinctivePhysicalFixtureWinner();
        var aS = T3Cell(T3A, T3S);
        var bA = T3Cell(T3B, T3A);
        var bS = T3Cell(T3B, T3S);
        Assert.That(bS, Is.Not.EqualTo(aS));
        Assert.That(bS, Is.Not.EqualTo(bA));
        var cells = Enum.GetValues<CompositeNominalLevelV9>().SelectMany(p =>
            Enum.GetValues<CompositeNominalLevelV9>().Select(m =>
            {
                var strong = (p,m) == aS || (p,m) == bA ||
                    (p,m) == (CompositeNominalLevelV9.ExactFull, CompositeNominalLevelV9.ExactFull);
                return new V9JointObservedCell(p,m,strong ? 1_000_000 : 1,
                    strong ? 1 : 1_000_000);
            })).ToArray();
        var fit = V9JointSupportEstimation.Estimate(cells,
            new V9JointSupportPolicy(.99m, 0, 0), 1m, basis.Payload.TrainCorpusSha256);
        return V9FittedSnapshot.Create(basis.Payload with {
            JointCells = fit.Probabilities, JointSupport = fit.Provenance
        });
    }

    private static async Task InsertT3OriginAsync(SqlConnection db, string code,
        string manager, IdentityObservation evidence, int day)
    {
        // The normal source identities, delivery and batch FKs are reused;
        // no Gold, linkage result or probabilistic link is injected.
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        await using var cmd = new SqlCommand("""
            DECLARE @system bigint,@base bigint,@gestor bigint,@batch uniqueidentifier;
            SELECT @system=s.sistema_origem_id,@base=sb.base_pessoa_origem_id,
                   @gestor=s.gestor_id,@batch=l.lote_id
              FROM ref.sistema_origem s
              JOIN ref.sistema_origem_base_pessoa sb ON sb.sistema_origem_id=s.sistema_origem_id AND sb.padrao=1
              JOIN ingestao.entrega e ON e.sistema_origem_id=s.sistema_origem_id
              JOIN ingestao.lote l ON l.entrega_id=e.entrega_id
              JOIN ref.gestor g ON g.gestor_id=s.gestor_id
             WHERE g.codigo=@manager;
            IF @batch IS NULL THROW 52930,'T3 synthetic manager missing.',1;
            INSERT silver.pessoa_origem(sistema_origem_id,base_pessoa_origem_id,codigo_pessoa_origem)
                VALUES(@system,@base,@code);
            DECLARE @origin bigint=SCOPE_IDENTITY();
            INSERT silver.pessoa_observacao(pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                versao_interna,conteudo_hash,cpf_ausente_motivo,nome_completo,nome_cmp,
                data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            VALUES(@origin,@batch,@gestor,@code,1,
                LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),@code)),2)),
                N'SEM_CPF',@name,@name,'1980-05-06',@mother,@mother,
                DATEADD(day,@day,CONVERT(datetimeoffset,'2026-01-01T00:00:00+00:00')));
            EXEC identidade.sp_assegurar_origem_progressiva @pessoa_origem_id=@origin;
            """, db, tx);
        cmd.Parameters.Add("@code", SqlDbType.NVarChar, 120).Value = code;
        cmd.Parameters.Add("@manager", SqlDbType.NVarChar, 120).Value = manager;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = evidence.NomeCompleto!;
        cmd.Parameters.Add("@mother", SqlDbType.NVarChar, 400).Value = evidence.NomeMae!;
        cmd.Parameters.Add("@day", SqlDbType.Int).Value = day;
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private static async Task AssertT3CandidateAsync(SqlConnection db, V9FittedSnapshot winner,
        IdentityObservation observation, Guid x)
    {
        var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot);
        var receipt = await V9OperationalCandidateRead.ReadAsync(db, tx, planner, observation,
            CancellationToken.None);
        Assert.That(receipt.CandidateUuids, Is.EqualTo(new[] { x }), "T3 actual candidate union");
        await tx.CommitAsync();
    }

    private static async Task CharacterizeT3Async(SqlConnection db, IOperationalSqlAdapter sql,
        Guid attempt, V9FittedSnapshot winner)
    {
        var temp = Path.Combine(Path.GetTempPath(), "jornada-v9-t3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var exported = await V9ValidatedOperationalModelFile.ExportAsync(winner, temp);
            var selection = Path.Combine(temp, "selection.json");
            await File.WriteAllTextAsync(selection, JsonSerializer.Serialize(
                new V9ConfiguredArtifactLoader.Selection(exported.FileName, winner.ContentSha256)));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["Linkage:SelectedModelVersion"] = "9", ["Linkage:V9SelectionFile"] = selection,
                ["LinkageReplay:CaptureBronzeSources"] = "false"
            }).Build();
            await V9FullSchemaNonPublishingRunnerTests.SeedControlledDevFullFixtureAsync(db, 1);
            await using (var source = new SqlCommand("""
                UPDATE silver.pessoa_observacao
                   SET nome_completo=@name,nome_cmp=@name,nome_mae=@mother,nome_mae_cmp=@mother,
                       source_as_of=CONVERT(datetimeoffset,'2026-01-01T00:00:00+00:00'),
                       conteudo_hash=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',
                           CONVERT(varbinary(max),N'V9_T3_SYNTHETIC_S')),2))
                 WHERE codigo_pessoa_origem=N'V9TEST_0001';
                """, db))
            {
                source.Parameters.Add("@name", SqlDbType.NVarChar, 400).Value = T3S.NomeCompleto!;
                source.Parameters.Add("@mother", SqlDbType.NVarChar, 400).Value = T3S.NomeMae!;
                Assert.That(await source.ExecuteNonQueryAsync(), Is.EqualTo(1));
            }
            // Only S exists. A real complete FULL creates its own reference.
            var foundingRun = await Pr1FullAsync(config, sql, attempt, temp, 1, 100);
            var founder = (await ReadPr1RowsAsync(db, Pr1Decisions, foundingRun)).Single();
            Assert.That(founder[10], Is.EqualTo("NOVA_IDENTIDADE"));
            Assert.That(founder[11], Is.EqualTo("RESOLVIDO"));
            Assert.That(founder[12], Is.EqualTo("NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"));
            var x = (Guid)founder[13]!;
            var model = V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256);
            // Independent pair probes use the identical immutable diagnostic
            // scorer. No score, decision, or A/B membership is fabricated.
            ProbabilisticLinkageDecision Pair(IdentityObservation left, IdentityObservation right) =>
                winner.ResolvePreview(model, left, new[] {
                    new V9SnapshotCandidate(x, right.NomeCompleto, right.DataNascimento, right.NomeMae) });
            var aS = Pair(T3A,T3S);
            var bS = Pair(T3B,T3S);
            var bA = Pair(T3B,T3A);
            var probes = new { winner = winner.ContentSha256,
                aS = new { status = aS.Status.ToString(), aS.MelhorScore },
                bS = new { status = bS.Status.ToString(), bS.MelhorScore },
                bA = new { status = bA.Status.ToString(), bA.MelhorScore } };
            await File.WriteAllTextAsync(Path.Combine(Pr1Directory(),"t3-pairs.json"),
                JsonSerializer.Serialize(probes));
            TestContext.WriteLine("V9_B1_T3_PAIRS " + JsonSerializer.Serialize(probes));
            Assert.That(aS.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
            Assert.That(bS.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            Assert.That(bA.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));

            // DT-10 currently refreshes the LEGACY index for NOVA_IDENTIDADE.
            // This T3-only disposable fixture explicitly replaces that founder's
            // legacy keys by the pinned diagnostic V9 projection of its actual
            // persisted Gold. Never do this in the runtime, or for other people.
            // Neither the logical UNIQUE key nor the frozen passes are changed.
            await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
                db, attempt, CancellationToken.None);
            await using (var legacy = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM identidade.blocking_chave WHERE pessoa_uuid=@x;
                """, db))
            {
                legacy.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = x;
                Assert.That((long)(await legacy.ExecuteScalarAsync())!, Is.GreaterThan(0L),
                    "T3 founding FULL produced its legacy projection");
            }
            await using (var remove = new SqlCommand("""
                DELETE FROM identidade.blocking_chave WHERE pessoa_uuid=@x;
                """, db))
            {
                remove.Parameters.Add("@x", SqlDbType.UniqueIdentifier).Value = x;
                var removed = await remove.ExecuteNonQueryAsync();
                Assert.That(removed, Is.GreaterThan(0));
                TestContext.WriteLine($"V9_B1_T3_FIXTURE_LEGACY_KEYS_REPLACED={removed}");
            }
            await MaterializeFrozenGoldBlockingKeysAsync(db, winner, x);
            await InsertT3OriginAsync(db,"V9T3_A","V9TEST",T3A,1);
            await InsertT3OriginAsync(db,"V9T3_B","V9TEST2",T3B,2);
            await AssertT3CandidateAsync(db,winner,T3A,x);
            await AssertT3CandidateAsync(db,winner,T3B,x);
            var first = await Pr1FullAsync(config,sql,attempt,temp,3,100);
            var before = await ReadPr1RowsAsync(db,Pr1Decisions,first);
            var linksBefore = await ReadPr1RowsAsync(db,Pr1Links);
            var a1 = before.Single(row => Equals(row[0],"V9T3_A"));
            var b1 = before.Single(row => Equals(row[0],"V9T3_B"));
            Assert.That(a1[2], Is.EqualTo("RESOLVIDO"));
            Assert.That(a1[4], Is.EqualTo(x));
            Assert.That(a1[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
            Assert.That(a1[13], Is.EqualTo(x));
            Assert.That(linksBefore.Single(row => Equals(row[0],"V9T3_A"))[3],
                Is.EqualTo("LINKAGE_PROBABILISTICO"));
            Assert.That(b1[2], Is.EqualTo("NAO_RESOLVIDO"));
            Assert.That(b1[10], Is.EqualTo("INDEFINIDA"));
            Assert.That(b1[13], Is.Null);
            // A is newer than S and the only other resolved origin in X.
            var gold = (await ReadPr1RowsAsync(db,Pr1Gold)).Single(row => Equals(row[0],x));
            Assert.That(gold[2], Is.EqualTo(T3A.NomeCompleto));
            Assert.That(gold[4], Is.EqualTo(T3A.NomeMae));
            Assert.That(gold[4], Is.Not.EqualTo(T3S.NomeMae));
            await AssertT3CandidateAsync(db,winner,T3B,x);
            // Read the actual b1 origin provenance on the same disposable SQL
            // database before the second FULL. The baseline is immutable
            // at the historical SHA/run; this run tests the b1 fix.
            await using (var snapshot = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot))
            {
                var census = await V9ControlledDevSecureOriginReader.ReadAsync(
                    db, snapshot, new[] { x }, CancellationToken.None);
                var origins = census[x];
                Assert.That(origins.Count, Is.EqualTo(2));
                var root = origins.Single(o => o.InitialUuid == x);
                Assert.That(root.HasPublishedFounderProof, Is.True);
                Assert.That(root.Observations.Count, Is.EqualTo(1));
                var probabilistic = origins.Single(o => o.OriginId != root.OriginId);
                Assert.That(probabilistic.HasPublishedFounderProof, Is.False);
                Assert.That(probabilistic.HasPriorProbabilisticAssociation, Is.True);
                Assert.That(probabilistic.HasCpf || probabilistic.HasGovernedCorrection
                    || probabilistic.HasUuidFeedback, Is.False);
                var b1A = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
                    winner, model, T3A, new[] { x }, census);
                var b1B = V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
                    winner, model, T3B, new[] { x }, census);
                Assert.That(b1A.Decision.Status, Is.EqualTo(ResolutionStatus.RESOLVIDO));
                Assert.That(b1A.Decision.PessoaUuidResolvido, Is.EqualTo(x));
                Assert.That(b1B.Decision.Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
                Assert.That(b1B.Decision.PessoaUuidResolvido, Is.Null);
                Assert.That(b1B.EvidenceReceipt.SecureRecords, Is.EqualTo(1));
                Assert.That(b1B.EvidenceReceipt.ComparedRecords, Is.EqualTo(1));
                await snapshot.CommitAsync();
                await File.WriteAllTextAsync(Path.Combine(Pr1Directory(), "t3-b1-origin-preview.json"),
                    JsonSerializer.Serialize(new { a = b1A.Decision, b = b1B.Decision,
                        evidence = b1B.EvidenceReceipt, nonpublishing = true }));
            }
            var second = await Pr1FullAsync(config,sql,attempt,temp,3,100);
            var after = await ReadPr1RowsAsync(db,Pr1Decisions,second);
            var linksAfter = await ReadPr1RowsAsync(db,Pr1Links);
            var b2 = after.Single(row => Equals(row[0],"V9T3_B"));
            var proof = new { probes, founder = x, foundingRun, first, second,
                firstDecisions = before, secondDecisions = after,
                firstLinks = linksBefore, secondLinks = linksAfter,
                recomposedFrom = "V9T3_A", safeOriginal = "V9TEST_0001",
                originalPairStillRejected = Pair(T3B,T3S).Status == ResolutionStatus.NAO_RESOLVIDO };
            await File.WriteAllTextAsync(Path.Combine(Pr1Directory(),"t3-b1-publication.json"),
                JsonSerializer.Serialize(proof));
            TestContext.WriteLine("V9_B1_T3_PUBLICATION " + JsonSerializer.Serialize(proof));
            // Real second FULL: B must not inherit the identity from A's
            // recomposed summary. No alternative new identity may be born.
            Assert.That(b2[2], Is.EqualTo("NAO_RESOLVIDO"));
            Assert.That(b2[4], Is.Null);
            Assert.That(b2[10], Is.EqualTo("INDEFINIDA"));
            Assert.That(b2[13], Is.Null);
            Assert.That(b2[14], Is.EqualTo("INDEFINIDA"));
            Assert.That(b2[15], Is.Null);
            Assert.That(Pair(T3B,T3S).Status, Is.EqualTo(ResolutionStatus.NAO_RESOLVIDO));
            TestContext.WriteLine("V9_B1_T3_INDIRECT_INHERITANCE_BLOCKED");
        }
        finally
        {
            Directory.Delete(temp, recursive:true);
        }
    }
}

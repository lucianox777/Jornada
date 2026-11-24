using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Linkage.Runner;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Tests.Unit;

public sealed partial class V9ControlledDevFullSqlR1Tests
{
    private static async Task AppendOriginVersionAsync(SqlConnection connection)
    {
        await using var append = new SqlCommand("""
            INSERT silver.pessoa_observacao(
                pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,
                versao_interna,conteudo_hash,cpf_ausente_motivo,nome_completo,
                nome_cmp,data_nascimento,nome_mae,nome_mae_cmp,source_as_of)
            SELECT pessoa_origem_id,lote_id,gestor_id,codigo_pessoa_origem,2,
                   LOWER(CONVERT(varchar(64),
                       HASHBYTES('SHA2_256',CONVERT(varbinary(max),
                                 N'V9TEST_0003_VERSION_2_R1')),2)),
                   cpf_ausente_motivo,nome_completo,nome_cmp,data_nascimento,
                   nome_mae,nome_mae_cmp,DATEADD(second,1,source_as_of)
              FROM silver.pessoa_observacao
             WHERE codigo_pessoa_origem=N'V9TEST_0003'
               AND versao_interna=1;
            IF @@ROWCOUNT<>1
                THROW 52909,'DEV source version was not inserted exactly once.',1;
            """, connection);
        await append.ExecuteNonQueryAsync();
    }

    private static async Task<List<object?[]>> ReadPr1RowsAsync(
        SqlConnection db, string query, Guid? run = null, SqlTransaction? tx = null,
        Guid? referencePerson = null)
    {
        await using var cmd = new SqlCommand(query, db, tx) { CommandTimeout = 120 };
        if (run.HasValue)
            cmd.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = run.Value;
        cmd.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = referencePerson ?? ExistingZ;
        var rows = new List<object?[]>();
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
        {
            var row = new object?[rd.FieldCount];
            for (var i = 0; i < row.Length; i++)
                row[i] = rd.IsDBNull(i) ? null : rd.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static async Task<Dictionary<Guid, string>> Pr1LogicalIdsAsync(
        SqlConnection db, SqlTransaction? tx = null)
    {
        var ids = new Dictionary<Guid, string> {
            [ProtectedX] = "X", [ExistingY] = "Y", [ExistingZ] = "Z"
        };
        foreach (var row in await ReadPr1RowsAsync(db, """
            SELECT p.initial_uuid,o.codigo_pessoa_origem
              FROM identidade.pessoa_origem_progressiva p
              JOIN silver.pessoa_origem o ON o.pessoa_origem_id=p.pessoa_origem_id;
            """, tx: tx))
        {
            // Z is now a real founder's own initial UUID, not a synthetic
            // external reference. Retain a stable symbolic identity in hashes.
            if ((Guid)row[0]! == ExistingZ)
                Assert.That(row[1], Is.EqualTo("V9TEST_0004"));
            else
                Assert.That(ids.TryAdd((Guid)row[0]!, (string)row[1]!), Is.True);
        }
        return ids;
    }

    private static string Pr1Canonical(IEnumerable<object?[]> rows,
        IReadOnlyDictionary<Guid, string> ids) =>
        string.Join("\n", rows.Select(row => JsonSerializer.Serialize(
            row.Select(value => value is Guid id ? ids[id] : value).ToArray()))
            .OrderBy(line => line, StringComparer.Ordinal));

    private static string Pr1Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private const string Pr1Decisions = """
        SELECT po.codigo_pessoa_origem,po.versao_interna,
               r.status,r.motivo,r.pessoa_uuid_resolvido,
               r.melhor_candidato_uuid,r.score_melhor,r.segundo_candidato_uuid,
               r.score_segundo,r.margem,r.resultado_publicacao,r.status_publicacao,
               r.motivo_publicacao,r.pessoa_uuid_publicado,p.estado,p.canonical_uuid
          FROM identidade.linkage_resultado r
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
          JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
         WHERE r.linkage_run_id=@run;
        """;

    private const string Pr1Links = """
        SELECT po.codigo_pessoa_origem,po.versao_interna,
               vc.pessoa_uuid,vc.metodo_resolucao,vc.status,vc.motivo,
               p.estado,p.canonical_uuid,p.initial_uuid
          FROM silver.pessoa_observacao po
          JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
          LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id;
        """;

    private const string Pr1Gold = """
        SELECT g.pessoa_uuid,g.estado_identidade,g.nome_completo,g.data_nascimento,g.nome_mae,
               g.fontes_distintas,g.estado_concordancia,g.completude_nucleo,ip.status
          FROM gold.pessoa g JOIN identidade.pessoa ip ON ip.pessoa_uuid=g.pessoa_uuid;
        """;

    private sealed record Pr1Input(string Candidates, string State, string IndicationCandidates);

    // Diagnostic-only components of the original composite digest. The
    // original input/output comparisons below are intentionally unchanged.
    private sealed record Pr1Component(string Name, int KeyLength, string[] Fields,
        List<object?[]> Rows);

    private static void Pr1AddComponent(List<Pr1Component>? target,
        string name, int keyLength, string[] fields, IEnumerable<object?[]> rows,
        IReadOnlyDictionary<Guid, string> ids)
    {
        if (target is null) return;
        var normalized = rows.Select(row => row.Select(value =>
            value is Guid id ? ids[id] : value).ToArray()).ToList();
        Assert.That(normalized.All(row => row.Length == fields.Length), Is.True,
            "PR1 diagnostic column contract");
        target.Add(new(name, keyLength, fields, normalized));
    }

    private static string Pr1ComponentDigest(Pr1Component component) =>
        Pr1Hash(Pr1Canonical(component.Rows, new Dictionary<Guid, string>()));

    private static object? Pr1Sanitize(string field, object? value)
    {
        // Only synthetic logical IDs and operational enums/reasons/scores
        // are emitted. All actual source attributes are represented by hashes.
        if (value is null) return null;
        return field is "pessoa_uuid" or "metodo_resolucao" or "status" or "motivo"
            or "estado" or "canonical_uuid" or "initial_uuid" or "estado_identidade"
            or "fontes_distintas" or "estado_concordancia" or "completude_nucleo"
            or "identity_status" or "raw_uuid" or "best_uuid" or "second_uuid"
            or "score_melhor" or "score_segundo" or "margem" or "resultado_publicacao"
            or "status_publicacao" or "motivo_publicacao" or "published_uuid"
            or "passes" or "candidate_count" or "candidate_uuids"
            ? value : "sha256:" + Pr1Hash(JsonSerializer.Serialize(value));
    }

    private static object Pr1CompareComponents(string phase,
        List<Pr1Component> before, List<Pr1Component> after)
    {
        Assert.That(before.Select(x => x.Name), Is.EqualTo(after.Select(x => x.Name)));
        var components = new List<object>();
        foreach (var a in before)
        {
            var b = after.Single(x => x.Name == a.Name);
            Assert.That(b.Fields, Is.EqualTo(a.Fields));
            Assert.That(b.KeyLength, Is.EqualTo(a.KeyLength));
            var left = a.Rows.GroupBy(row => JsonSerializer.Serialize(row.Take(a.KeyLength)))
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var right = b.Rows.GroupBy(row => JsonSerializer.Serialize(row.Take(b.KeyLength)))
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var changes = new List<object>();
            foreach (var key in left.Keys.Union(right.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                var l = left.GetValueOrDefault(key) ?? Array.Empty<object?[]>();
                var r = right.GetValueOrDefault(key) ?? Array.Empty<object?[]>();
                var lh = Pr1Hash(Pr1Canonical(l, new Dictionary<Guid, string>()));
                var rh = Pr1Hash(Pr1Canonical(r, new Dictionary<Guid, string>()));
                if (lh == rh) continue;
                if (l.Length != 1 || r.Length != 1)
                {
                    changes.Add(new { key, beforeCount = l.Length, afterCount = r.Length,
                        beforeHash = lh, afterHash = rh });
                    continue;
                }
                var fields = Enumerable.Range(a.KeyLength, a.Fields.Length - a.KeyLength)
                    .Where(i => !Equals(l[0][i], r[0][i]))
                    .Select(i => new { field = a.Fields[i],
                        before = Pr1Sanitize(a.Fields[i], l[0][i]),
                        after = Pr1Sanitize(a.Fields[i], r[0][i]) }).ToArray();
                changes.Add(new { key, fields });
            }
            var ah = Pr1ComponentDigest(a);
            var bh = Pr1ComponentDigest(b);
            components.Add(new { component = a.Name, beforeHash = ah, afterHash = bh,
                beforeCount = a.Rows.Count, afterCount = b.Rows.Count,
                changedKeys = changes.Count, changes });
            TestContext.WriteLine($"V9_GOLD_PR1_DIAG {phase} {a.Name} {ah}/{bh} changedKeys={changes.Count}");
        }
        return new { phase, components };
    }

    private static async Task<Pr1Input> Pr1InputAsync(SqlConnection db, V9FittedSnapshot winner,
        List<Pr1Component>? diagnostic = null)
    {
        // Complete candidate UUIDs from the production reader, read-only
        // SNAPSHOT. No competing writer in this unique disposable database.
        var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot);
        var ids = await Pr1LogicalIdsAsync(db, tx);
        var source = await ReadPr1RowsAsync(db, """
            SELECT po.codigo_pessoa_origem,po.versao_interna,po.cpf,po.cpf_ausente_motivo,
                   po.nome_completo,po.data_nascimento,po.nome_mae,g.codigo,po.source_as_of
              FROM silver.pessoa_observacao po JOIN ref.gestor g ON g.gestor_id=po.gestor_id;
            """, tx: tx);
        var candidates = new List<object?[]>();
        // Hash all Silver evidence (including Y's CPF-bearing history),
        // but authenticate blocking only for the actual no-CPF run cohort.
        foreach (var row in source.Where(r => r[2] is null))
        {
            var observation = new IdentityObservation((string?)row[2], (string?)row[3],
                (string?)row[4], row[5] is DateTime birth ? DateOnly.FromDateTime(birth) : null,
                (string?)row[6]);
            var receipt = await V9OperationalCandidateRead.ReadAsync(db, tx, planner,
                observation, CancellationToken.None);
            candidates.Add(new object?[] { row[0], row[1], planner.Plan(observation).Count,
                receipt.CandidateCount, string.Join(",", receipt.CandidateUuids.Select(id => ids[id])
                    .OrderBy(id => id, StringComparer.Ordinal)) });
        }
        var gold = await ReadPr1RowsAsync(db, Pr1Gold, tx: tx);
        var links = await ReadPr1RowsAsync(db, Pr1Links, tx: tx);
        Pr1AddComponent(diagnostic, "candidates", 2,
            new[] { "origin", "version", "passes", "candidate_count", "candidate_uuids" }, candidates, ids);
        Pr1AddComponent(diagnostic, "silver", 2,
            new[] { "origin", "version", "cpf", "cpf_ausente_motivo", "nome_completo",
                "data_nascimento", "nome_mae", "gestor", "source_as_of" }, source, ids);
        Pr1AddComponent(diagnostic, "gold", 1,
            new[] { "pessoa_uuid", "estado_identidade", "nome_completo", "data_nascimento",
                "nome_mae", "fontes_distintas", "estado_concordancia", "completude_nucleo",
                "identity_status" }, gold, ids);
        Pr1AddComponent(diagnostic, "links", 2,
            new[] { "origin", "version", "pessoa_uuid", "metodo_resolucao", "status" },
            links.Select(row => row.Take(5).ToArray()), ids);
        Pr1AddComponent(diagnostic, "link_reasons", 2,
            new[] { "origin", "version", "motivo" },
            links.Select(row => new[] { row[0], row[1], row[5] }), ids);
        Pr1AddComponent(diagnostic, "progressive", 2,
            new[] { "origin", "version", "estado", "canonical_uuid", "initial_uuid" },
            links.Select(row => row.Take(2).Concat(row.Skip(6)).ToArray()), ids);
        await tx.CommitAsync();
        return new(Pr1Hash(Pr1Canonical(candidates, ids)),
            Pr1Hash(Pr1Canonical(source, ids) + "\n" + Pr1Canonical(gold, ids)
                + "\n" + Pr1Canonical(links, ids)),
            Pr1Canonical(candidates.Where(x => Equals(x[0], "V9TEST_0106")), ids));
    }

    private static async Task<string> Pr1OutputAsync(SqlConnection db, Guid run, int expected,
        List<Pr1Component>? diagnostic = null)
    {
        var ids = await Pr1LogicalIdsAsync(db);
        var decisions = await ReadPr1RowsAsync(db, Pr1Decisions, run);
        var items = await ReadPr1RowsAsync(db, """
            SELECT po.codigo_pessoa_origem,po.versao_interna
              FROM identidade.linkage_run_item ri
              JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ri.pessoa_observacao_id
             WHERE ri.linkage_run_id=@run;
            """, run);
        Assert.That(decisions.Count, Is.EqualTo(expected));
        Assert.That(items.Count, Is.EqualTo(expected));
        Assert.That(Pr1Hash(Pr1Canonical(decisions.Select(x => x.Take(2).ToArray()), ids)),
            Is.EqualTo(Pr1Hash(Pr1Canonical(items, ids))), "Exact frozen source membership");
        Pr1AddComponent(diagnostic, "decisions", 2,
            new[] { "origin", "version", "status", "motivo", "raw_uuid", "best_uuid",
                "score_melhor", "second_uuid", "score_segundo", "margem",
                "resultado_publicacao", "status_publicacao", "motivo_publicacao",
                "published_uuid", "estado", "canonical_uuid" }, decisions, ids);
        return Pr1Hash(Pr1Canonical(decisions, ids) + "\n"
            + Pr1Canonical(await ReadPr1RowsAsync(db, Pr1Links), ids) + "\n"
            + Pr1Canonical(await ReadPr1RowsAsync(db, Pr1Gold), ids));
    }

    private static async Task<Guid> Pr1FullAsync(IConfiguration config, IOperationalSqlAdapter sql,
        Guid attempt, string temp, int count, int batch)
    {
        var runner = await NewDevRunnerAsync(config, sql, attempt, temp);
        var full = await runner.RunV9ControlledDevAsync(
            new ProbabilisticLinkageRunRequest(LinkageRunType.FULL, 9, null, null,
                null, batch, 1, null, "V9_DEV_GOLD_PR1", "CHARACTERIZATION_ONLY",
                Guid.NewGuid(), true), attempt, CancellationToken.None);
        Assert.That(full.Status, Is.EqualTo(LinkageRunStatus.PUBLICADO));
        Assert.That(full.Elegiveis, Is.EqualTo(count));
        Assert.That(full.Avaliados, Is.EqualTo(count));
        return full.RunId;
    }

    private static string Pr1Directory()
    {
        var path = Environment.GetEnvironmentVariable("JORNADA_V9_GOLD_PR1_OUTPUT");
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            throw new InvalidOperationException("V9_GOLD_PR1_OUTPUT_REQUIRED");
        return path;
    }

    // PO 2026-11-24 15h: class (a) only for the two physically confirmed
    // publication transitions. No changed score, raw decision, destination,
    // link method/status, origin state, Silver or Gold is accepted.
    private static void AssertPr1KnownPublicationTransitions(
        List<Pr1Component> before, List<Pr1Component> between,
        List<Pr1Component> warm, List<Pr1Component> first)
    {
        foreach (var component in before.Where(x => x.Name != "link_reasons"))
            Assert.That(Pr1ComponentDigest(between.Single(x => x.Name == component.Name)),
                Is.EqualTo(Pr1ComponentDigest(component)), "T1 invariant " + component.Name);

        static Dictionary<string, object?[]> ByOrigin(Pr1Component component) =>
            component.Rows.ToDictionary(row => JsonSerializer.Serialize(row.Take(2)),
                row => row, StringComparer.Ordinal);
        var oldReasons = ByOrigin(before.Single(x => x.Name == "link_reasons"));
        var newReasons = ByOrigin(between.Single(x => x.Name == "link_reasons"));
        var oldDecisions = ByOrigin(warm.Single(x => x.Name == "decisions"));
        var newDecisions = ByOrigin(first.Single(x => x.Name == "decisions"));
        var links = ByOrigin(before.Single(x => x.Name == "links"));
        var progressive = ByOrigin(before.Single(x => x.Name == "progressive"));
        Assert.That(newReasons.Keys, Is.EquivalentTo(oldReasons.Keys));
        Assert.That(newDecisions.Keys, Is.EquivalentTo(oldDecisions.Keys));
        Assert.That(oldDecisions.Keys.All(oldReasons.ContainsKey), Is.True);
        foreach (var outsideRun in oldReasons.Keys.Except(oldDecisions.Keys))
            Assert.That(newReasons[outsideRun], Is.EqualTo(oldReasons[outsideRun]),
                "T1 outside-cohort secure evidence link unchanged " + outsideRun);
        var created = 0;
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in oldDecisions.Keys)
        {
            var a = oldDecisions[key];
            var b = newDecisions[key];
            var oldReason = oldReasons[key][2];
            var newReason = newReasons[key][2];
            if (Equals(oldReason, newReason))
            {
                Assert.That(b, Is.EqualTo(a), "T1 unexpected decision change " + key);
                continue;
            }
            // The only permitted changes are to publication result/reason.
            Assert.That(b.Where((_, i) => i is not (10 or 12)),
                Is.EqualTo(a.Where((_, i) => i is not (10 or 12))),
                "T1 unexpected raw, score, destination or progressive change " + key);
            Assert.That(a[12], Is.EqualTo(oldReason));
            Assert.That(b[12], Is.EqualTo(newReason));
            Assert.That(a[11], Is.EqualTo("RESOLVIDO"));
            Assert.That(a[14], Is.EqualTo("REFERENCIA"));
            Assert.That(a[13], Is.EqualTo(a[15]));
            Assert.That(links[key][2], Is.EqualTo(a[13]));
            Assert.That(links[key][3], Is.EqualTo("LINKAGE_PROBABILISTICO"));
            Assert.That(links[key][4], Is.EqualTo("RESOLVIDO"));
            Assert.That(progressive[key][3], Is.EqualTo(a[13]));
            Assert.That(b[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
            if (Equals(oldReason, "NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"))
            {
                Assert.That(newReason, Is.EqualTo("REFERENCIA_PROGRESSIVA_PRESERVADA"));
                Assert.That(a[10], Is.EqualTo("NOVA_IDENTIDADE"));
                Assert.That(a[13], Is.EqualTo(progressive[key][4]), "Founder keeps own initial UUID");
                Assert.That(a[2], Is.EqualTo("NAO_RESOLVIDO"));
                Assert.That((string?)a[3], Does.StartWith("SEM_CANDIDATO_"));
                created++;
            }
            else
            {
                Assert.That(a[0], Is.AnyOf("V9TEST_0003", "V9TEST_0106"));
                Assert.That(oldReason, Is.EqualTo("ASSOCIACAO_EXISTENTE_LINKAGE"));
                Assert.That(newReason, Is.EqualTo("REFERENCIA_PROGRESSIVA_CONFIRMADA"));
                Assert.That(a[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
                Assert.That(a[2], Is.EqualTo("RESOLVIDO"));
                Assert.That(a[4], Is.EqualTo(a[13]));
                Assert.That(a[13], Is.EqualTo("Z"));
                Assert.That(confirmed.Add((string)a[0]!), Is.True);
            }
        }
        // The b1 fixture pre-founds Z and approves S9 already on the warm
        // FULL. Require every actual warm founder to undergo precisely the
        // legitimate transition above, not a historical fixture count.
        Assert.That(created, Is.GreaterThan(0));
        Assert.That(created, Is.EqualTo(oldDecisions.Values.Count(r =>
            Equals(r[10], "NOVA_IDENTIDADE"))), "T1 all warm founders accounted for");
        Assert.That(confirmed, Is.EquivalentTo(new[] { "V9TEST_0003", "V9TEST_0106" }),
            "T1 individually approved S1/S9 confirmations");
        TestContext.WriteLine($"V9_GOLD_PR1_T1_CLASS_A founder={created} confirmed={confirmed.Count}");
    }

    private static async Task AssertStrictFullAndPermutationAsync(
        SqlConnection db, IConfiguration config, IOperationalSqlAdapter sql,
        Guid attempt, string temp, V9FittedSnapshot winner, string scenario)
    {
        // Warm-up establishes references, not counted as a repeated FULL.
        var initial = await Pr1InputAsync(db, winner);
        var warm = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var warmDiagnostics = new List<Pr1Component>();
        var firstDiagnostics = new List<Pr1Component>();
        var secondDiagnostics = new List<Pr1Component>();
        var beforeDiagnostics = new List<Pr1Component>();
        var betweenDiagnostics = new List<Pr1Component>();
        var afterDiagnostics = new List<Pr1Component>();
        var warmOutput = await Pr1OutputAsync(db, warm, 200, warmDiagnostics);
        await AssertRecomposedZAsync(db);
        var before = await Pr1InputAsync(db, winner, beforeDiagnostics);
        var first = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var firstOutput = await Pr1OutputAsync(db, first, 200, firstDiagnostics);
        await AssertRecomposedZAsync(db);
        var firstGold = await ReadGoldUuidSetAsync(db);
        var between = await Pr1InputAsync(db, winner, betweenDiagnostics);
        var second = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var secondOutput = await Pr1OutputAsync(db, second, 200, secondDiagnostics);
        await AssertRecomposedZAsync(db);
        var after = await Pr1InputAsync(db, winner, afterDiagnostics);
        // Save granular evidence before the existing assertions can stop the test.
        var diagnostic = new { scenario, winner = winner.ContentSha256,
            transitions = new[] {
                Pr1CompareComponents("before_to_between", beforeDiagnostics, betweenDiagnostics),
                Pr1CompareComponents("between_to_after", betweenDiagnostics, afterDiagnostics),
                Pr1CompareComponents("warm_to_first_output", warmDiagnostics, firstDiagnostics),
                Pr1CompareComponents("first_to_second_output", firstDiagnostics, secondDiagnostics)
            } };
        await File.WriteAllTextAsync(Path.Combine(Pr1Directory(), scenario + "-diagnostic.json"),
            JsonSerializer.Serialize(diagnostic));
        var secondGold = await ReadGoldUuidSetAsync(db);
        var evidence = new { scenario, winner = winner.ContentSha256, initial, warmOutput,
            before, between, after, firstOutput, secondOutput,
            firstGoldCount = firstGold.Count, secondGoldCount = secondGold.Count };
        await File.WriteAllTextAsync(Path.Combine(Pr1Directory(), scenario + ".json"),
            JsonSerializer.Serialize(evidence));
        TestContext.WriteLine($"V9_GOLD_PR1_T1 {scenario} candidates={before.Candidates}/{between.Candidates} state={before.State}/{between.State} output={firstOutput}/{secondOutput}");
        Assert.Multiple((Action)(() =>
        {
            Assert.That(between.Candidates, Is.EqualTo(before.Candidates), "T1 candidate universe changed");
            // Composite state may differ only by the documented first-repeat
            // publication reasons, checked individually (all other fields strict).
            AssertPr1KnownPublicationTransitions(beforeDiagnostics, betweenDiagnostics,
                warmDiagnostics, firstDiagnostics);
            Assert.That(after, Is.EqualTo(between), "T1 evidence changed after repeat");
            Assert.That(secondOutput, Is.EqualTo(firstOutput), "T1 decisions, motives, links or partition changed");
            Assert.That(firstGold.SetEquals(secondGold), Is.True, "T1 exact Gold UUID set changed");
        }));
        var canonical = JsonSerializer.Serialize(new { winner = winner.ContentSha256,
            initial, warmOutput, before, output = firstOutput });
        var baseline = Path.Combine(Pr1Directory(), "t1-baseline.json");
        if (scenario == "stable")
            await File.WriteAllTextAsync(baseline, canonical);
        else
            Assert.That(Pr1Hash(canonical), Is.EqualTo(Pr1Hash(await File.ReadAllTextAsync(baseline))),
                "T1 ingestion permutation changed logical universe or result");
        TestContext.WriteLine("V9_GOLD_PR1_T1_PASS " + scenario);
    }

    private static Task<List<object?[]>> Pr1ZProvenanceAsync(SqlConnection db, SqlTransaction? snapshot = null,
        Guid? referencePerson = null) =>
        ReadPr1RowsAsync(db, """
            WITH obs_ids AS (
                SELECT vc.pessoa_observacao_id FROM identidade.v_vinculo_corrente vc
                 WHERE vc.pessoa_uuid=@z AND vc.status='RESOLVIDO'
                UNION
                SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
                 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
                 WHERE p.estado='REFERENCIA' AND p.canonical_uuid=@z
                UNION
                SELECT po.pessoa_observacao_id FROM silver.pessoa_observacao po
                 JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
                 WHERE p.initial_uuid=@z AND p.estado IN('PROVISORIA','INDEFINIDA')
            ), ranked AS (
                SELECT a.campo,po.codigo_pessoa_origem,po.versao_interna,vc.metodo_resolucao,
                       vc.status,po.source_as_of,
                       CASE WHEN a.valor=a.gold_valor THEN 1 ELSE 0 END matches_gold,
                       ROW_NUMBER() OVER(PARTITION BY a.campo
                         ORDER BY CASE WHEN v.pessoa_campo_verificacao_id IS NULL THEN 1 ELSE 0 END,
                                  v.verificado_em DESC,po.source_as_of DESC,po.pessoa_observacao_id DESC) rn
                FROM obs_ids i JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=i.pessoa_observacao_id
                CROSS JOIN (SELECT * FROM gold.pessoa WHERE pessoa_uuid=@z) g
                CROSS APPLY (VALUES
                    ('NOME_COMPLETO',po.nome_completo,g.nome_completo),
                    ('DATA_NASCIMENTO',CONVERT(nvarchar(10),po.data_nascimento,23),CONVERT(nvarchar(10),g.data_nascimento,23)),
                    ('NOME_MAE',po.nome_mae,g.nome_mae)) a(campo,valor,gold_valor)
                LEFT JOIN silver.pessoa_campo_verificacao_observacao v
                  ON v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=a.campo
                LEFT JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
                   AND vc.pessoa_uuid=@z
                WHERE a.valor IS NOT NULL
            )
            SELECT campo,codigo_pessoa_origem,versao_interna,metodo_resolucao,status,source_as_of,matches_gold
              FROM ranked WHERE rn=1 ORDER BY campo;
            """, tx: snapshot, referencePerson: referencePerson);

    // Same-origin new version is not a reason to alter pre-existing raw
    // scores/decisions in this controlled fixture. Check every common row,
    // allowing only the known NOVA_IDENTIDADE -> preservation or existing
    // association -> confirmation publication reason transitions.
    private static void AssertB1StableIdentityDecisions(
        IReadOnlyList<object?[]> before, IReadOnlyList<object?[]> after)
    {
        var oldRows = before.ToDictionary(r => (r[0], r[1]));
        var newRows = after.ToDictionary(r => (r[0], r[1]));
        Assert.That(oldRows.Keys.All(newRows.ContainsKey), Is.True);
        Assert.That(newRows.Keys.Except(oldRows.Keys).ToArray(),
            Is.EqualTo(new[] { ((object?)"V9TEST_0003", (object?)2L) })
                .Or.EqualTo(new[] { ((object?)"V9TEST_0003", (object?)2) }));
        foreach (var key in oldRows.Keys)
        {
            var a = oldRows[key];
            var b = newRows[key];
            Assert.That(b.Where((_, i) => i is not (10 or 12)),
                Is.EqualTo(a.Where((_, i) => i is not (10 or 12))),
                "b1 repeated raw decision/score/published UUID/progressive state " + key);
            if (Equals(a[10], b[10]) && Equals(a[12], b[12]))
                continue;
            Assert.That(b[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
            if (Equals(a[10], "NOVA_IDENTIDADE"))
            {
                Assert.That(a[12], Is.EqualTo("NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"));
                Assert.That(b[12], Is.EqualTo("REFERENCIA_PROGRESSIVA_PRESERVADA"));
            }
            else
            {
                Assert.That(a[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
                Assert.That(a[12], Is.EqualTo("ASSOCIACAO_EXISTENTE_LINKAGE"));
                // DT-10 elects the highest internal version in the run.
                // 0003 v1 is now a follower of the appended v2.
                Assert.That(b[12], Is.EqualTo(Equals(a[0], "V9TEST_0003")
                    ? "REFERENCIA_PROGRESSIVA_PROPAGADA_NA_ORIGEM"
                    : "REFERENCIA_PROGRESSIVA_CONFIRMADA"), "DT-10 leader/follower " + key);
                Assert.That(a[0], Is.AnyOf("V9TEST_0003", "V9TEST_0106"));
            }
        }
        var appended = after.Single(r => Equals(r[0], "V9TEST_0003") && Convert.ToInt64(r[1], System.Globalization.CultureInfo.InvariantCulture) == 2);
        Assert.That(appended[2], Is.EqualTo("RESOLVIDO"));
        Assert.That(appended[4], Is.EqualTo(ExistingZ));
        Assert.That(appended[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
        Assert.That(appended[11], Is.EqualTo("RESOLVIDO"));
        Assert.That(appended[12], Is.EqualTo("REFERENCIA_PROGRESSIVA_CONFIRMADA"),
            "0003 v2 is the DT-10 leader");
        Assert.That(appended[13], Is.EqualTo(ExistingZ));
        Assert.That(appended[15], Is.EqualTo(ExistingZ));
        var follower = after.Single(r => Equals(r[0], "V9TEST_0003")
            && Convert.ToInt64(r[1], System.Globalization.CultureInfo.InvariantCulture) == 1);
        Assert.That(follower[12], Is.EqualTo("REFERENCIA_PROGRESSIVA_PROPAGADA_NA_ORIGEM"));
        Assert.That(follower[13], Is.EqualTo(appended[13]));
        Assert.That(follower[4], Is.EqualTo(ExistingZ), "Publication propagation is not raw FS inheritance");
    }

    private static async Task CharacterizeHistoricalGoldAsync(
        SqlConnection db, IConfiguration config, IOperationalSqlAdapter sql,
        Guid attempt, string temp, V9FittedSnapshot winner, Guid firstRun)
    {
        var beforeGold = await ReadPr1RowsAsync(db, Pr1Gold);
        var beforeLinks = await ReadPr1RowsAsync(db, Pr1Links);
        var beforeDecisions = await ReadPr1RowsAsync(db, Pr1Decisions, firstRun);
        var beforeProvenance = await Pr1ZProvenanceAsync(db);
        await AppendOriginVersionAsync(db);
        var secondInput = await Pr1InputAsync(db, winner);
        var secondRun = await Pr1FullAsync(config, sql, attempt, temp, 201, 133);
        var afterGold = await ReadPr1RowsAsync(db, Pr1Gold);
        var afterLinks = await ReadPr1RowsAsync(db, Pr1Links);
        var afterDecisions = await ReadPr1RowsAsync(db, Pr1Decisions, secondRun);
        var afterProvenance = await Pr1ZProvenanceAsync(db);
        var removed = beforeGold.Where(row => !afterGold.Any(x => Equals(x[0], row[0]))).ToArray();
        var added = afterGold.Where(row => !beforeGold.Any(x => Equals(x[0], row[0]))).ToArray();
        var removedIds = removed.Select(row => (Guid)row[0]!).ToHashSet();
        var affected = beforeLinks.Where(row => row[8] is Guid id && removedIds.Contains(id))
            .Select(row => (string)row[0]!).Concat(new[] {"V9TEST_0003","V9TEST_0006","V9TEST_0106"})
            .ToHashSet(StringComparer.Ordinal);
        // Only synthetic codes, UUIDs, methods, states, and scores in receipts.
        var proof = new {
            winner = winner.ContentSha256, firstRun, secondRun,
            beforeCount = beforeGold.Count, afterCount = afterGold.Count,
            removed = removed.Select(x => new { uuid = x[0], state = x[1], identityStatus = x[8] }),
            added = added.Select(x => new { uuid = x[0], state = x[1], identityStatus = x[8] }),
            beforeProvenance, afterProvenance, secondInput,
            beforeDecisions = beforeDecisions.Where(x => affected.Contains((string)x[0]!)),
            afterDecisions = afterDecisions.Where(x => affected.Contains((string)x[0]!)),
            beforeLinks = beforeLinks.Where(x => affected.Contains((string)x[0]!)),
            afterLinks = afterLinks.Where(x => affected.Contains((string)x[0]!))
        };
        var json = JsonSerializer.Serialize(proof);
        await File.WriteAllTextAsync(Path.Combine(Pr1Directory(), "historical.json"), json);
        TestContext.WriteLine("V9_GOLD_PR1_T2_SYNTHETIC " + json);
        Assert.That(beforeDecisions.Count, Is.EqualTo(200));
        Assert.That(afterDecisions.Count, Is.EqualTo(201));
        // b1 approves 0106 from protected 0006 already in the first FULL:
        // the historical 201->200 removal must not recur in round two.
        Assert.That(removed, Is.Empty, "T2 unexpected loss of a Gold UUID");
        Assert.That(added, Is.Empty, "T2 unexpected new Gold UUID");
        Assert.That(afterGold.Count, Is.EqualTo(beforeGold.Count));
        AssertB1StableIdentityDecisions(beforeDecisions, afterDecisions);
        foreach (var rows in new[] { beforeDecisions, afterDecisions })
        {
            var s9 = rows.Single(x => Equals(x[0], "V9TEST_0106"));
            Assert.That(s9[2], Is.EqualTo("RESOLVIDO"));
            Assert.That(s9[4], Is.EqualTo(ExistingZ));
            Assert.That(s9[13], Is.EqualTo(ExistingZ));
            Assert.That(s9[15], Is.EqualTo(ExistingZ));
        }
        Assert.That(beforeProvenance.Count, Is.EqualTo(3));
        Assert.That(beforeProvenance.All(x => Equals(x[6], 1)), Is.True);
        Assert.That(afterProvenance.Count, Is.EqualTo(3));
        Assert.That(afterProvenance.All(x => Equals(x[6], 1)), Is.True);
        await AssertRecomposedZAsync(db);
        TestContext.WriteLine("V9_B1_T2_STABLE_IDENTITY_RECOMPOSITION_PASS");
    }
}

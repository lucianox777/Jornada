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
        SqlConnection db, string query, Guid? run = null, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(query, db, tx) { CommandTimeout = 120 };
        if (run.HasValue)
            cmd.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = run.Value;
        cmd.Parameters.Add("@z", SqlDbType.UniqueIdentifier).Value = ExistingZ;
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
            Assert.That(ids.TryAdd((Guid)row[0]!, (string)row[1]!), Is.True);
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

    private static async Task<Pr1Input> Pr1InputAsync(SqlConnection db, V9FittedSnapshot winner)
    {
        // Complete candidate UUIDs from the production reader, read-only
        // SNAPSHOT. No competing writer in this unique disposable database.
        var planner = V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(), winner.ContentSha256);
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync(IsolationLevel.Snapshot);
        var ids = await Pr1LogicalIdsAsync(db, tx);
        var source = await ReadPr1RowsAsync(db, """
            SELECT po.codigo_pessoa_origem,po.versao_interna,po.cpf,po.cpf_ausente_motivo,
                   po.nome_completo,po.data_nascimento,po.nome_mae,g.codigo,po.source_as_of
              FROM silver.pessoa_observacao po JOIN ref.gestor g ON g.gestor_id=po.gestor_id
             WHERE po.cpf IS NULL;
            """, tx: tx);
        var candidates = new List<object?[]>();
        foreach (var row in source)
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
        await tx.CommitAsync();
        return new(Pr1Hash(Pr1Canonical(candidates, ids)),
            Pr1Hash(Pr1Canonical(source, ids) + "\n" + Pr1Canonical(gold, ids)
                + "\n" + Pr1Canonical(links, ids)),
            Pr1Canonical(candidates.Where(x => Equals(x[0], "V9TEST_0106")), ids));
    }

    private static async Task<string> Pr1OutputAsync(SqlConnection db, Guid run, int expected)
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

    private static async Task AssertStrictFullAndPermutationAsync(
        SqlConnection db, IConfiguration config, IOperationalSqlAdapter sql,
        Guid attempt, string temp, V9FittedSnapshot winner, string scenario)
    {
        // Warm-up establishes references, not counted as a repeated FULL.
        var initial = await Pr1InputAsync(db, winner);
        var warm = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var warmOutput = await Pr1OutputAsync(db, warm, 200);
        var before = await Pr1InputAsync(db, winner);
        var first = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var firstOutput = await Pr1OutputAsync(db, first, 200);
        var firstGold = await ReadGoldUuidSetAsync(db);
        var between = await Pr1InputAsync(db, winner);
        var second = await Pr1FullAsync(config, sql, attempt, temp, 200, 100);
        var secondOutput = await Pr1OutputAsync(db, second, 200);
        var after = await Pr1InputAsync(db, winner);
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
            Assert.That(between.State, Is.EqualTo(before.State), "T1 comparison evidence changed");
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

    private static Task<List<object?[]>> Pr1ZProvenanceAsync(SqlConnection db) =>
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
            """);

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
        Assert.That(beforeGold.Count, Is.EqualTo(201), "T2 initial count not reproduced");
        Assert.That(afterGold.Count, Is.EqualTo(200), "T2 final count not reproduced");
        Assert.That(removed.Length, Is.EqualTo(1), "T2 must account for every removed Gold UUID");
        Assert.That(added, Is.Empty, "T2 unexpected added Gold UUID");
        Assert.That(removed[0][1], Is.AnyOf("PROVISORIA","INDEFINIDA"),
            "T2 established reference removed: stop for PO");
        var origin = beforeLinks.Single(x => Equals(x[8], removed[0][0]));
        Assert.That(origin[6], Is.AnyOf("PROVISORIA","INDEFINIDA"));
        var result = afterDecisions.Single(x => Equals(x[0], origin[0]) && Equals(x[1], origin[1]));
        Assert.That(result[2], Is.EqualTo("RESOLVIDO"), "T2 removal lacks own FS approval");
        Assert.That(result[4], Is.EqualTo(ExistingZ), "T2 unexpected raw destination");
        Assert.That(result[10], Is.EqualTo("ASSOCIACAO_EXISTENTE"));
        Assert.That(result[13], Is.EqualTo(ExistingZ));
        Assert.That(result[14], Is.EqualTo("REFERENCIA"));
        Assert.That(result[15], Is.EqualTo(ExistingZ));
        Assert.That(beforeProvenance.Count, Is.EqualTo(3));
        Assert.That(beforeProvenance.All(x => Equals(x[6], 1)), Is.True);
        Assert.That(afterProvenance.Count, Is.EqualTo(3));
        Assert.That(afterProvenance.All(x => Equals(x[6], 1)), Is.True);
        TestContext.WriteLine("V9_GOLD_PR1_T2_CHARACTERIZED_NOT_POLICY_APPROVAL");
    }
}

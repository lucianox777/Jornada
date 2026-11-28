using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Jornada.Linkage.Runner;

public static partial class V9DevE2EFullCommand
{
    private static async Task ComparativeFullAsync(string label, string root, IConfiguration config,
        OperationalSqlAdapter sql, string connectionString, V9FittedSnapshot snapshot, Guid modelId, Guid attempt)
    {
        if (!V9Phase4Matrix.SixArmAdmitted || label is not ("repeat-2" or "staged-1" or "staged-2") ||
            snapshot.ContentSha256 != Winner)
            throw new InvalidDataException("RUNNER_V9_COMPARATIVE_ADMISSION");
        var first = label == "staged-1";
        var output = Path.Combine(root, label);
        if (Directory.Exists(output)) throw new InvalidDataException("RUNNER_V9_COMPARATIVE_REUSE");
        Directory.CreateDirectory(output);
        using var fixture = JsonDocument.Parse(await ReadPrivateAsync(root, "wave-preflight-receipt.json"));
        var pre = fixture.RootElement;
        var total = pre.GetProperty("packagesByWave").EnumerateArray()
            .Where(w => !first || w.GetProperty("wave").GetInt32() == 1)
            .SelectMany(w => w.GetProperty("packages").EnumerateArray())
            .Sum(p => p.GetProperty("versions").GetInt32());
        if (pre.GetProperty("seed").GetInt32() != 85229684 || pre.GetProperty("baseIdentities").GetInt32() != 10000 ||
            total <= 0) throw new InvalidDataException("RUNNER_V9_COMPARATIVE_FIXTURE");
        Guid? prior = null;
        if (!first)
        {
            var directory = label == "repeat-2" ? root : Path.Combine(root, "staged-1");
            using var predecessor = JsonDocument.Parse(await ReadPrivateAsync(directory, "runner-receipt.json"));
            prior = predecessor.RootElement.GetProperty("runId").GetGuid();
        }
        await using var conn = await sql.OpenAsync(CancellationToken.None);
        await V9DisposableAttemptDatabase.VerifyAsync(conn, attempt, CancellationToken.None);
        await using var admission = new SqlCommand("""
            IF (SELECT COUNT_BIG(*) FROM silver.pessoa_observacao)<>@total
              OR EXISTS(SELECT 1 FROM silver.pessoa_observacao WHERE pessoa_origem_id IS NULL
                  OR codigo_pessoa_origem IS NULL OR codigo_pessoa_origem NOT LIKE N'SYNTH-%')
              OR EXISTS(SELECT 1 FROM ingestao.lote WHERE status<>N'PROCESSADO')
              OR EXISTS(SELECT 1 FROM ingestao.entrega WHERE status<>N'PROCESSADA')
              OR (SELECT COUNT_BIG(*) FROM identidade.linkage_run)<>CASE WHEN @prior IS NULL THEN 0 ELSE 1 END
              OR (@prior IS NOT NULL AND NOT EXISTS (SELECT 1 FROM identidade.linkage_run r
                  JOIN identidade.modelo_linkage m ON m.modelo_id=r.modelo_id
                  WHERE r.linkage_run_id=@prior AND r.tipo_run=N'FULL' AND r.status=N'PUBLICADO'
                    AND m.versao=9 AND m.status=N'RASCUNHO' AND m.snapshot_referencia=@winner))
              OR (@prior IS NULL AND (EXISTS(SELECT 1 FROM identidade.modelo_linkage WHERE versao=9)
                  OR EXISTS(SELECT 1 FROM identidade.modelo_linkage_v9_operacional)
                  OR EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
                  OR EXISTS(SELECT 1 FROM identidade.blocking_chave_v9_cobertura)))
              THROW 52991,'COMPARATIVE_INPUT_STATE',1;
            SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE cpf IS NULL;
            """, conn);
        admission.Parameters.Add("@total", SqlDbType.BigInt).Value = total;
        admission.Parameters.Add("@prior", SqlDbType.UniqueIdentifier).Value = (object?)prior ?? DBNull.Value;
        admission.Parameters.Add("@winner", SqlDbType.Char, 64).Value = Winner;
        var eligible = (long)(await admission.ExecuteScalarAsync())!;
        if (first) await StageFrozenModelAsync(conn, snapshot, modelId);
        var runnerSql = new OperationalSqlAdapter(new SqlConnectionStringBuilder(connectionString)
            { ApplicationName = "JornadaV9Phase4Runner" }.ConnectionString);
        var scorer = await V9ControlledDevCohortScorer.CreateAsync(config, runnerSql, attempt,
            CancellationToken.None, effectiveEnvironment: "Development");
        double[]? timing = null;
        scorer.DiagnosticTimingObserver = (read, snapshotMs, group, score, translate) =>
            timing = [read, snapshotMs, group, score, translate];
        IReadOnlyList<V9ControlledDevCohortScorer.GoldScore>? physicalScores = null;
        IReadOnlyDictionary<long, IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>? physicalCandidates = null;
        Guid? capturedRun = null;
        object? counters = null;
        scorer.DiagnosticObserver = (id, ids, edgesSha, groups, directions, scores, candidates) =>
        {
            if (capturedRun.HasValue || eligible != ids.Count || candidates.Count != ids.Count ||
                groups.Count != ids.Count || directions.Count % 2 != 0 || edgesSha.Length != 64)
                throw new InvalidDataException("RUNNER_V9_COMPARATIVE_CENSUS");
            capturedRun = id;
            physicalScores = scores;
            physicalCandidates = candidates;
            counters = new { silverEdges = directions.Count / 2, silverDirectionsCompared = directions.Count,
                candidatePeople = candidates.Values.Sum(values => (long)values.Count), goldRecordScores = scores.Count };
        };
        var storage = Directory.CreateDirectory(Path.Combine(output, "runner-storage")).FullName;
        var runner = CreateFullRunner(config, runnerSql, scorer, storage);
        var request = new ProbabilisticLinkageRunRequest(LinkageRunType.FULL, 9, null, null, null,
            100, 1, null, "V9_DEV_SIX_ARM", label, Guid.NewGuid(), true);
        var run = await V9Phase4Matrix.MeasurePhysicalFullAsync(output, sql, attempt,
            () => runner.RunV9ControlledDevAsync(request, attempt, CancellationToken.None), () => timing, CancellationToken.None);
        if (run.Status != LinkageRunStatus.PUBLICADO || run.Elegiveis != eligible || run.Avaliados != eligible ||
            run.RunId != capturedRun || counters is null)
            throw new InvalidDataException("RUNNER_V9_COMPARATIVE_PUBLICATION");
        var query = await ReadPrivateAsync(root, "comparative-census.sql");
        if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(query))) !=
            "de94b6995cbd9435f0dbd12e9f59ffe075baafb4bbfb23bd53b068da3ab85fb0")
            throw new InvalidDataException("RUNNER_V9_COMPARATIVE_CENSUS_PIN");
        await using var census = new SqlCommand(query, conn) { CommandTimeout = 120 };
        census.Parameters.Add("@attempt", SqlDbType.UniqueIdentifier).Value = attempt;
        census.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = run.RunId;
        census.Parameters.Add("@previous", SqlDbType.UniqueIdentifier).Value = (object?)prior ?? DBNull.Value;
        var json = new StringBuilder();
        await using (var reader = await census.ExecuteReaderAsync())
            while (await reader.ReadAsync()) json.Append(reader.GetString(0));
        await WriteNewAsync(output, "sql-census.json", json.ToString());
        var observations = new Dictionary<long, IdentityObservation>();
        await using (var attrs = new SqlCommand("""
            SELECT pessoa_observacao_id,nome_completo,data_nascimento,nome_mae
            FROM silver.pessoa_observacao ORDER BY pessoa_observacao_id;
            """, conn))
        await using (var rd = await attrs.ExecuteReaderAsync())
            while (await rd.ReadAsync())
                observations.Add(rd.GetInt64(0), new(null, "SEM_CPF",
                    rd.IsDBNull(1) ? null : rd.GetString(1),
                    rd.IsDBNull(2) ? null : DateOnly.FromDateTime(rd.GetDateTime(2)),
                    rd.IsDBNull(3) ? null : rd.GetString(3)));
        var bySource = physicalScores!.ToLookup(x => x.Source);
        var directMask = new HashSet<long>();
        using (var current = JsonDocument.Parse(json.ToString()))
        foreach (var row in current.RootElement.EnumerateArray())
        {
            if (row.GetProperty("publicationReason").GetString() != "ASSOCIACAO_EXISTENTE_LINKAGE")
                continue;
            var id = row.GetProperty("observationId").GetInt64();
            var candidates = physicalCandidates![id];
            if (candidates.Count != 1) continue;
            if (row.GetProperty("runResult").GetString() != "ASSOCIACAO_EXISTENTE" ||
                row.GetProperty("currentMethod").GetString() != "LINKAGE_PROBABILISTICO" ||
                row.GetProperty("publishedUuid").GetGuid() != candidates[0].PessoaUuid ||
                row.GetProperty("currentUuid").GetGuid() != candidates[0].PessoaUuid)
                throw new InvalidDataException("RUNNER_V9_COMPARATIVE_DIRECT_GROUND");
            var scores = bySource[id].Select(x => new V9ControlledDevSecureEvidenceScore.RecordScore(
                x.Record, x.Person, x.Posterior, x.LogOdds)).ToArray();
            if (V9Phase4Matrix.MatchesDirectVeto(observations[id], scores, key => observations[key]))
                directMask.Add(id);
        }
        await WriteNewAsync(output, "direct-mask-private.json", JsonSerializer.Serialize(directMask));
        await WriteNewAsync(output, "runner-receipt.json", JsonSerializer.Serialize(new {
            attempt, label, runId = run.RunId, previousRunId = prior, winnerSha256 = Winner,
            eligible, evaluated = run.Avaliados, total, counters, mode = "FULL", scientificTest = false, activated = false, status = "PUBLICADO" }));
    }
}

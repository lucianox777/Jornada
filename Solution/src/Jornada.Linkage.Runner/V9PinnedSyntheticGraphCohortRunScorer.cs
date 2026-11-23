using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Run-scoped, bounded and explicitly nonpublishing TEST-only extension.
/// The real RunAsync freezes ALL eligible Silver observations before calling
/// PrepareAsync ONCE, then resolves those exact observation IDs in SQL batches.
/// Legacy IProbabilisticIdentityLinkage behavior is not changed.
/// </summary>
public interface IV9NonPublishingFrozenCohortScorer : IProbabilisticIdentityLinkage
{
    Task PrepareAsync(Guid runId, ProbabilisticLinkageModelRef model,
        IReadOnlyList<V9FrozenRunObservation> frozenRows, CancellationToken ct);

    Task<ProbabilisticLinkageDecision> ResolvePreparedAsync(Guid runId,
        long pessoaObservacaoId, IdentityObservation observation,
        Guid modelId, CancellationToken ct);
}

/// <summary>Immutable observation identity read from the real linkage_run_item universe.</summary>
public sealed record V9FrozenRunObservation(long PessoaObservacaoId, IdentityObservation Observation);

/// <summary>
/// TEST-only adapter that binds the real RunAsync lifecycle to one globally
/// scored V9 BLIND_FS_COMPLETE_LINK_CANDIDATES cohort (never per-row Resolve).
/// Candidate edges are EXPLICIT SYNTHETIC fixture input; this class does NOT
/// certify operational SQL candidate retrieval or production DI integration.
/// Strictly JornadaHarness, never any Gold/Serving publication.
/// </summary>
public sealed class V9PinnedSyntheticGraphCohortRunScorer : IV9NonPublishingFrozenCohortScorer
{
    public const int MaximumSyntheticObservations = 512;
    private const string FixtureAnchor = "V9_PINNED_NONPUBLISH_E2E";
    private readonly IOperationalSqlAdapter sql;
    private readonly Guid pinnedModelId;
    private readonly string pinnedWinnerSha;
    private readonly (long A, long B)[] syntheticEdges;
    private readonly bool readPhysicalSqlGraph;
    public V9FrozenCohortSqlCandidateGraph.Receipt? SqlGraphEvidence { get; private set; }
    public V9OperationalBlockingChaveCohortParity.Receipt? BlockingChaveParityEvidence { get; private set; }
    private readonly int failAfter;
    private readonly ConcurrentDictionary<long, byte> consumed = new();
    private IReadOnlyDictionary<long, (IdentityObservation Observation, ProbabilisticLinkageDecision Decision)>? prepared;
    // TEST_ONLY raw global grouping, separate from nullable linkage_resultado UUIDs.
    // Do not publish, activate or use as independently certified evidence.
    private IReadOnlyDictionary<long, Guid>? preparedGrouping;
    private Guid preparedRunId;
    private int scored;

    public V9PinnedSyntheticGraphCohortRunScorer(IOperationalSqlAdapter sql,
        Guid pinnedModelId, string pinnedWinnerSha256,
        IReadOnlyCollection<(long A, long B)> explicitSyntheticEdges,
        int failAfter = int.MaxValue)
    {
        this.sql = sql ?? throw new ArgumentNullException(nameof(sql));
        ArgumentNullException.ThrowIfNull(explicitSyntheticEdges);
        if (pinnedModelId == Guid.Empty || pinnedWinnerSha256 is not { Length: 64 }
            || pinnedModelId != V9OperationalSqlModelConverter.ModelIdFromWinnerSha(pinnedWinnerSha256)
            || failAfter < 0)
            throw new InvalidDataException("Modelo V9 TEST pinado ou limite de falha inválido.");
        this.pinnedModelId = pinnedModelId;
        pinnedWinnerSha = pinnedWinnerSha256;
        syntheticEdges = explicitSyntheticEdges.ToArray();
        this.failAfter = failAfter;
    }

    /// <summary>
    /// New physical SQL signature-join mode: no caller-supplied edges.
    /// 200-observation disposable SQL smoke only, not production candidate SQL.
    /// </summary>
    public V9PinnedSyntheticGraphCohortRunScorer(IOperationalSqlAdapter sql,
        Guid pinnedModelId, string pinnedWinnerSha256, int failAfter = int.MaxValue)
        : this(sql, pinnedModelId, pinnedWinnerSha256,
            Array.Empty<(long A, long B)>(), failAfter)
    {
        readPhysicalSqlGraph = true;
    }

    public Task<ProbabilisticLinkageModelRef> GetActiveModelAsync(CancellationToken ct) =>
        throw new InvalidOperationException("Modelo ativo é proibido no V9 agrupado TEST_ONLY.");

    public async Task<ProbabilisticLinkageModelRef> GetModelByVersionAsync(
        int version, CancellationToken ct)
    {
        if (version != 1)
            throw new InvalidDataException("Versão da ponte TEST não pinada.");
        await using var connection = await sql.OpenAsync(ct);
        await EnsureHarnessAndAnchorAsync(connection, ct);
        var runtime = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, pinnedModelId, pinnedWinnerSha, ct);
        return new(pinnedModelId, version,
            LinkageParameterCatalog.CompositeNominalDecisionEvidenceAlgorithmVersion,
            runtime.FrozenWinner.Payload.Threshold, runtime.FrozenWinner.Payload.ConflictMargin)
        {
            BlockingContract = new("V9_SYNTHETIC_NONPUBLISH_" + pinnedWinnerSha, pinnedWinnerSha,
                runtime.FrozenWinner.Payload.ProjectionSchemaVersion,
                runtime.FrozenWinner.Payload.ProjectionFingerprintSha256)
        };
    }

    public Task<ProbabilisticLinkageDecision> ResolveWithoutCpfAsync(
        IdentityObservation observation, Guid modeloId, CancellationToken ct) =>
        throw new InvalidOperationException(
            "V9 agrupado exige preparação integral do universo e ResolvePreparedAsync por ID SQL.");

    public async Task PrepareAsync(Guid runId, ProbabilisticLinkageModelRef model,
        IReadOnlyList<V9FrozenRunObservation> frozenRows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(frozenRows);
        if (prepared is not null || preparedRunId != Guid.Empty)
            throw new InvalidOperationException("Preparação V9 não pode ser reexecutada ou reutilizada.");
        if (runId == Guid.Empty || model.ModelId != pinnedModelId
            || model.Version != 1 || frozenRows.Count is < 2 or > MaximumSyntheticObservations
            || frozenRows.Any(row => row is null || row.PessoaObservacaoId <= 0
                || row.Observation is null || row.Observation.Cpf is not null)
            || frozenRows.Select(r => r.PessoaObservacaoId).Distinct().Count() != frozenRows.Count
            || !frozenRows.Select(r => r.PessoaObservacaoId).SequenceEqual(
                frozenRows.Select(r => r.PessoaObservacaoId).OrderBy(id => id)))
            throw new InvalidDataException("Coorte V9 exige universo SQL integral, ordenado e SEM CPF.");

        // One-use guard set BEFORE any await; a failed prepare cannot be silently retried.
        preparedRunId = runId;
        ct.ThrowIfCancellationRequested();
        await using var connection = await sql.OpenAsync(ct);
        await EnsureHarnessAndAnchorAsync(connection, ct);
        var pinned = await V9OperationalPinnedCohortRuntime.LoadPinnedAsync(
            connection, pinnedModelId, pinnedWinnerSha, ct);
        var winner = pinned.FrozenWinner;
        if (winner.Payload.CandidateGrouping?.CandidateGrouping != "BLIND_FS_COMPLETE_LINK_CANDIDATES"
            || model.Threshold != winner.Payload.Threshold
            || model.ConflictMargin != winner.Payload.ConflictMargin
            || model.BlockingContract?.ProjectionSchemaVersion != winner.Payload.ProjectionSchemaVersion
            || model.BlockingContract?.ProjectionFingerprintSha256 != winner.Payload.ProjectionFingerprintSha256)
            throw new InvalidDataException("Contrato global V9 difere do vencedor SQL imutável.");

        // Read manager identity from real frozen run_item/Silver, never from
        // synthetic truth labels or a supplied precomputed lookup.
        var managers = new Dictionary<long, string>();
        await using (var cmd = new SqlCommand("""
            SELECT po.pessoa_observacao_id,g.codigo
              FROM identidade.linkage_run_item ri
              JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ri.pessoa_observacao_id
              JOIN ref.gestor g ON g.gestor_id=po.gestor_id
             WHERE ri.linkage_run_id=@run ORDER BY po.pessoa_observacao_id;
            """, connection))
        {
            cmd.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    throw new InvalidDataException("Universo SQL V9 com ID ou gestor nulo.");
                var observationId = reader.GetInt64(0);
                var manager = reader.GetString(1);
                if (observationId <= 0 || string.IsNullOrWhiteSpace(manager))
                    throw new InvalidDataException("Universo SQL V9 com ID ou gestor inválido.");
                if (!managers.TryAdd(observationId, manager))
                    throw new InvalidDataException("Universo SQL V9 com ID duplicado.");
            }
        }
        if (managers.Count != frozenRows.Count
            || frozenRows.Any(row => !managers.ContainsKey(row.PessoaObservacaoId)))
            throw new InvalidDataException("Universo Silver e frozen linkage_run_item divergiram.");

        var idToIndex = frozenRows.Select((row, i) => (row.PessoaObservacaoId, Index: i + 1))
            .ToDictionary(x => x.PessoaObservacaoId, x => x.Index);
        var edges = new List<(int A, int B)>(syntheticEdges.Length);
        var seen = new HashSet<(long A, long B)>();
        foreach (var edge in syntheticEdges)
        {
            if (edge.A >= edge.B || !seen.Add(edge)
                || !idToIndex.TryGetValue(edge.A, out var a)
                || !idToIndex.TryGetValue(edge.B, out var b)
                || string.Equals(managers[edge.A], managers[edge.B], StringComparison.Ordinal))
                throw new InvalidDataException("Grafo sintético não corresponde à coorte SQL congelada.");
            edges.Add((a, b));
        }
        var cohort = frozenRows.Select((row, i) => new V9PhysicalCalibrationObservation(
            i + 1, "UNUSED_SYNTHETIC_LABEL_" + (i + 1), "TEST",
            managers[row.PessoaObservacaoId], row.Observation.NomeCompleto,
            row.Observation.NomeMae, row.Observation.DataNascimento)).ToArray();
        if (readPhysicalSqlGraph)
        {
            // The entire frozen Silver cohort, not separate RunAsync pages,
            // is projected into SQL temp tables; no synthetic edge input.
            var physical = await V9FrozenCohortSqlCandidateGraph.ReadAsync(
                connection, winner, frozenRows, managers, ct);
            if (physical.Edges.Count == 0)
                throw new InvalidDataException(
                    "Smoke SQL positivo V9 exige pelo menos uma aresta física real.");
            // Before any SQL linkage_resultado batch is persisted, require
            // exact, bidirectional, complete equality with the UNMODIFIED
            // production blocking_chave INTERSECT/UNION candidate query.
            var operational = await V9OperationalBlockingChaveCohortParity.VerifyAsync(
                connection, winner, frozenRows, managers, physical.Edges, ct);
            if (operational.UndirectedSqlEdges != physical.Edges.Count
                || !string.Equals(operational.CanonicalEdgesSha256,
                    physical.CanonicalEdgesSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Digest do grafo operacional V9 divergiu da assinatura física.");
            // Score from the verified operational SQL graph; the physical graph
            // is an independent oracle and must never become a fallback.
            if (operational.OperationalEdges.Count != operational.UndirectedSqlEdges
                || !physical.Edges.SequenceEqual(operational.OperationalEdges))
                throw new InvalidDataException(
                    "Grafo operacional consumido divergiu do oráculo físico.");
            edges = operational.OperationalEdges.ToList();
            SqlGraphEvidence = physical;
            BlockingChaveParityEvidence = operational;
        }
        // EXACTLY one global grouping over all edges, including cross-batch links.
        // Route frozen TEST scoring through the production-side linkage class,
        // not directly through the test adapter's own replay invocation.
        // This does not imply parity with legacy per-row ResolveWithoutCpfAsync.
        var operationalLinkage = new SqlProbabilisticIdentityLinkage(
            new ConfigurationBuilder().Build(), sql,
            NullLogger<SqlProbabilisticIdentityLinkage>.Instance);
        var global = await operationalLinkage.ResolvePinnedV9FrozenTestCohortAsync(
            pinnedModelId, pinnedWinnerSha, cohort, edges, ct);
        if (global.SnapshotSha256 != pinnedWinnerSha
            || global.Decisions.Count != cohort.Length
            || global.IdentityByObservation.Count != cohort.Length)
            throw new InvalidDataException("Preparação V9 global incompleta.");
        var map = new Dictionary<long, (IdentityObservation, ProbabilisticLinkageDecision)>();
        foreach (var item in global.Decisions)
        {
            var row = frozenRows[item.ObservationId - 1];
            if (!map.TryAdd(row.PessoaObservacaoId, (row.Observation,
                    item.Decision with { ModeloId = pinnedModelId })))
                throw new InvalidDataException("Decisão V9 agrupada duplicada.");
        }
        if (map.Count != frozenRows.Count)
            throw new InvalidDataException("Decisões V9 não cobrem exatamente o universo.");

        // Nullable pessoa_uuid_resolvido is a decision, NOT a complete-link
        // grouping. Capture the already-computed operational grouping without
        // accessing any synthetic truth or changing the scoring kernel.
        var groups = new Dictionary<long, Guid>(frozenRows.Count);
        for (var index = 0; index < frozenRows.Count; index++)
        {
            var position = index + 1;
            if (!global.IdentityByObservation.TryGetValue(position, out var group)
                || group == Guid.Empty
                || !groups.TryAdd(frozenRows[index].PessoaObservacaoId, group))
                throw new InvalidDataException("Agrupamento V9 global incompleto ou inválido.");
        }
        if (groups.Count != frozenRows.Count)
            throw new InvalidDataException("Universo V9 e agrupamentos não reconciliados.");
        preparedGrouping = new ReadOnlyDictionary<long, Guid>(groups);
        prepared = map;
    }

    /// <summary>
    /// TEST_ONLY, nonpublishing, read-only diagnostic. The method verifies the
    /// actual completed SQL run, its frozen universe and persisted decisions.
    /// This is NOT a productive or certified E2E exporter of groups.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, Guid>> ReadCompletedGroupingDiagnosticAsync(
        Guid runId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var groups = preparedGrouping;
        if (runId == Guid.Empty || runId != preparedRunId || groups is null
            || prepared is null || groups.Count != prepared.Count
            || consumed.Count != groups.Count || Volatile.Read(ref scored) != groups.Count)
            throw new InvalidOperationException("Agrupamento V9 TEST incompleto ou run divergente.");

        await using var connection = await sql.OpenAsync(ct);
        await EnsureHarnessAndAnchorAsync(connection, ct);
        await using var command = new SqlCommand("""
            SELECT lr.status,lr.modelo_id,lr.modelo_versao,
                   lr.registros_elegiveis,lr.avaliados,lr.publicado_em,
                   (SELECT COUNT_BIG(*) FROM identidade.linkage_run_item i
                     WHERE i.linkage_run_id=lr.linkage_run_id),
                   (SELECT COUNT_BIG(*) FROM identidade.linkage_resultado r
                     WHERE r.linkage_run_id=lr.linkage_run_id)
            FROM identidade.linkage_run lr
            WHERE lr.linkage_run_id=@run;
            """, connection);
        command.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)
            || reader.GetString(0) != "CONCLUIDO_SEM_PUBLICACAO"
            || reader.GetGuid(1) != pinnedModelId || reader.GetInt32(2) != 1
            || reader.GetInt64(3) != groups.Count || reader.GetInt64(4) != groups.Count
            || !reader.IsDBNull(5)
            || reader.GetInt64(6) != groups.Count || reader.GetInt64(7) != groups.Count
            || await reader.ReadAsync(ct))
            throw new InvalidOperationException(
                "Run SQL V9 sem publicação não está terminal, íntegro ou pinado.");
        return groups;
    }

    /// <summary>
    /// Map SQL-only synthetic source IDs to completed TEST_ONLY V9 groups.
    /// Source values originate exclusively from the run_item/Silver join, never
    /// from the external bridge ground-truth sidecar. No Gold state is inferred.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, Guid>> ReadCompletedSourceGroupingDiagnosticAsync(
        Guid runId, CancellationToken ct)
    {
        var groups = await ReadCompletedGroupingDiagnosticAsync(runId, ct);
        await using var connection = await sql.OpenAsync(ct);
        await EnsureHarnessAndAnchorAsync(connection, ct);
        await using var command = new SqlCommand("""
            SELECT ri.pessoa_observacao_id,po.codigo_pessoa_origem,po.cpf
            FROM identidade.linkage_run_item ri
            JOIN silver.pessoa_observacao po
              ON po.pessoa_observacao_id=ri.pessoa_observacao_id
            WHERE ri.linkage_run_id=@run
            ORDER BY ri.pessoa_observacao_id;
            """, connection);
        command.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = runId;
        var sources = new Dictionary<string, Guid>(groups.Count, StringComparer.Ordinal);
        var observedIds = new HashSet<long>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || !reader.IsDBNull(2))
                    throw new InvalidDataException("Fonte SQL TEST V9 com CPF ou ID inválido.");
                var id = reader.GetInt64(0);
                var source = reader.GetString(1);
                if (!IsSyntheticDevSource(source)
                    || !groups.TryGetValue(id, out var group)
                    || !observedIds.Add(id) || !sources.TryAdd(source, group))
                    throw new InvalidDataException(
                        "Fonte opaca SQL do universo V9 ausente, duplicada ou não sintética.");
            }
        }
        if (sources.Count != groups.Count || observedIds.Count != groups.Count
            || groups.Keys.Any(id => !observedIds.Contains(id)))
            throw new InvalidDataException("Fonte opaca SQL não cobre a coorte congelada.");
        // Recheck terminal run state after reading Silver. This is not a claim
        // of atomic Silver/Gold provenance or operational E2E certification.
        _ = await ReadCompletedGroupingDiagnosticAsync(runId, ct);
        return new ReadOnlyDictionary<string, Guid>(sources);
    }

    private static bool IsSyntheticDevSource(string code) =>
        (code.Length == 38 && code.StartsWith("SYNTH-", StringComparison.Ordinal)
            && code[6..].All(c =>
                (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
        || (code.Length == 11 && code.StartsWith("V9TEST_", StringComparison.Ordinal)
            && code[7..].All(c => c >= '0' && c <= '9'));

    public Task<ProbabilisticLinkageDecision> ResolvePreparedAsync(Guid runId,
        long pessoaObservacaoId, IdentityObservation observation, Guid modelId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rows = prepared;
        if (rows is null || runId != preparedRunId || modelId != pinnedModelId
            || !rows.TryGetValue(pessoaObservacaoId, out var item)
            || !SameObservation(item.Observation, observation))
            throw new InvalidDataException("Resolve V9 exige a mesma linha Silver congelada e o mesmo run.");
        if (!consumed.TryAdd(pessoaObservacaoId, 0))
            throw new InvalidDataException("Observação V9 avaliada mais de uma vez no run.");
        if (Interlocked.Increment(ref scored) > failAfter)
            throw new InvalidOperationException("V9_SYNTHETIC_SECOND_BATCH_INJECTED_FAILURE");
        return Task.FromResult(item.Decision);
    }

    private static bool SameObservation(IdentityObservation a, IdentityObservation b) =>
        a.Cpf == b.Cpf && a.CpfAusenteMotivo == b.CpfAusenteMotivo
        && a.NomeCompleto == b.NomeCompleto && a.NomeMae == b.NomeMae
        && a.DataNascimento == b.DataNascimento
        && (a.ResolutionAttributes ?? Array.Empty<IdentityResolutionAttributeValue>())
            .SequenceEqual(b.ResolutionAttributes ?? Array.Empty<IdentityResolutionAttributeValue>());

    private async Task EnsureHarnessAndAnchorAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT CASE WHEN DB_NAME()=N'JornadaHarness'
             AND EXISTS (SELECT 1 FROM identidade.modelo_linkage
                 WHERE modelo_id=@id AND status=N'RASCUNHO'
                   AND deduplicacao_metodo=@marker AND snapshot_referencia=@sha)
             THEN 1 ELSE 0 END;
            """, connection);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = pinnedModelId;
        cmd.Parameters.Add("@marker", SqlDbType.NVarChar, 80).Value = FixtureAnchor;
        cmd.Parameters.Add("@sha", SqlDbType.Char, 64).Value = pinnedWinnerSha;
        // Reject wrong database BEFORE ANY model table read (SQL batch itself
        // contains table refs; SQL Server resolves them on execution).
        await using var guard = new SqlCommand("SELECT DB_NAME();", connection);
        if (!string.Equals((string?)await guard.ExecuteScalarAsync(ct), "JornadaHarness",
                StringComparison.Ordinal))
            throw new InvalidOperationException("V9 TEST de coorte só pode usar JornadaHarness.");
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) != 1)
            throw new InvalidDataException("Anchor RASCUNHO de fixture V9 ausente ou adulterado.");
    }
}

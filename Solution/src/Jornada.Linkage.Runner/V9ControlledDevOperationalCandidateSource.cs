using System.Collections.ObjectModel;
using System.Data;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// A DEV candidate source, NOT a second scorer. Executes the original
/// production BlockingProjectionCandidateQueryBuilder against real Gold UUIDs.
/// No synthetic TEST UUID, direct signature-join fallback, model refit or Gold
/// mutation is possible here. Only the isolated derived index may be synchronized
/// under C07 in the same snapshot. The DEV scorer still has to reconcile complete-link
/// groups with real identities before publication is permitted.
/// </summary>
public static class V9ControlledDevOperationalCandidateSource
{
    // The current Gold schema admits PARCIAL references with nullable core
    // attributes. Missing evidence must reach the frozen FS scorer as null,
    // never be invented or silently eliminate a real REFERENCIA candidate.
    public sealed record GoldCandidate(Guid PessoaUuid, string? NomeCompleto,
        DateOnly? DataNascimento, string? NomeMae);

    /// <summary>
    /// Gold REFERENCIA is mandatory, but its core can legitimately be PARCIAL.
    /// Preserve missing fields as missing FS evidence; never synthesize values.
    /// </summary>
    public static GoldCandidate PreserveReferenceEvidence(
        Guid uuid, string? state, string? name, DateOnly? birth, string? mother)
    {
        if (uuid == Guid.Empty || state is null)
            throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_MISSING");
        if (!string.Equals(state, "REFERENCIA", StringComparison.Ordinal))
            throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_STALE");
        return new GoldCandidate(uuid, name, birth, mother);
    }

    // The caller cannot supply a manager lookup or truth label: managers and
    // source field integrity are read from the exact SQL run_item/Silver scope.
    public sealed record VerifiedRunCandidates(
        IReadOnlyDictionary<long, string> ManagerByObservation,
        IReadOnlyDictionary<long, IReadOnlyList<GoldCandidate>> CandidatesByObservation,
        IReadOnlyDictionary<long, V9OperationalCandidateRead.Receipt> EvidenceByObservation,
        IReadOnlyDictionary<long, IdentityObservation> AuthenticatedSilverByObservation,
        // Authenticated pre-existing active RESOLVIDO links and REFERENCIA
        // progressive canonicals, both sourced inside the same SQL SNAPSHOT.
        // null means NOT physically read; empty means no such links were found.
        // NEVER populate missing rows from blocking hits or inferred groups.
        IReadOnlyDictionary<long, Guid>? ExistingResolvedGoldLinksByObservation = null)
    {
        /// <summary>Physically authenticated Silver-to-Silver graph for this SNAPSHOT.</summary>
        public V9ControlledDevSilverGraph.Receipt? SilverGraph { get; init; }
        public V9OperationalBlockingManifest.Receipt? IsolatedBlockingManifest { get; init; }
        public IReadOnlySet<long>? NoEligibleBlockingPassObservationIds { get; init; }
        internal IReadOnlyDictionary<Guid, IReadOnlyList<V9ControlledDevSecureOriginReader.OriginFacts>>? SecureOriginFactsByPerson { get; init; }
    }

    /// <summary>Fail closed if any frozen source attribute or manager has drifted in Silver.</summary>
    public static string VerifyFrozenSilverObservation(
        V9FrozenRunObservation frozen, long sqlId, string? manager,
        IdentityObservation sqlObservation)
    {
        ArgumentNullException.ThrowIfNull(frozen);
        ArgumentNullException.ThrowIfNull(sqlObservation);
        if (frozen.PessoaObservacaoId <= 0 || sqlId != frozen.PessoaObservacaoId
            || string.IsNullOrWhiteSpace(manager)
            || !string.IsNullOrWhiteSpace(sqlObservation.Cpf)
            || !Equals(frozen.Observation, sqlObservation))
            throw new InvalidDataException("RUNNER_V9_DEV_SILVER_OR_MANAGER_DRIFT");
        return manager;
    }

    /// <summary>
    /// Validate only links already RESOLVIDO in identidade.vinculo_fonte.
    /// This guard never invents a destination for an unresolved observation.
    /// The caller must source status, UUID and Gold state from the SAME SQL SNAPSHOT.
    /// </summary>
    public static void RecordExistingResolvedGoldLink(
        Dictionary<long, Guid> authenticated,
        ISet<long> activeLinkRowsSeen,
        IReadOnlySet<long> frozenIds,
        long observationId, string? status, Guid? linkedUuid, string? goldState)
    {
        ArgumentNullException.ThrowIfNull(authenticated);
        ArgumentNullException.ThrowIfNull(activeLinkRowsSeen);
        ArgumentNullException.ThrowIfNull(frozenIds);
        // There is no filtered UNIQUE constraint on active vinculo_fonte by
        // observation. Two active rows with different statuses are ambiguous
        // even when only one of them is RESOLVIDO; never select one silently.
        if (!frozenIds.Contains(observationId)
            || status is not ("RESOLVIDO" or "NAO_RESOLVIDO" or "CONFLITO")
            || !activeLinkRowsSeen.Add(observationId))
            throw new InvalidDataException("RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED");
        if (status != "RESOLVIDO")
        {
            if (linkedUuid is not null || goldState is not null)
                throw new InvalidDataException("RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED");
            return;
        }
        if (linkedUuid is not Guid uuid || uuid == Guid.Empty
            || goldState != "REFERENCIA"
            || !authenticated.TryAdd(observationId, uuid))
            throw new InvalidDataException("RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED");
    }


    /// <summary>
    /// Read-only bridge to the EXISTING operational cohort-graph verifier.
    /// Only a fully pre-resolved Gold-bound run may be represented as graph
    /// sources. An unresolved Silver row has NO authenticated Gold UUID and
    /// cannot be assigned the UUID of a blocking hit or a diagnostic group.
    ///
    /// This is an INPUT ADAPTER, not physical certification: VerifiedRunCandidates
    /// is a publicly constructible record, and the existing SQL SNAPSHOT graph
    /// verifier MUST independently reauthenticate Silver, links, the entire
    /// Gold reference population and the operational candidate census before
    /// any result can be trusted. Never use this output as a publishing scorer.
    /// </summary>
    public static IReadOnlyList<V9OperationalCohortGraphPreview.Source>
        BuildFullyResolvedGraphSourcesForPhysicalReverification(
            IReadOnlyList<V9FrozenRunObservation> frozenRows,
            VerifiedRunCandidates verifiedSource)
    {
        ArgumentNullException.ThrowIfNull(frozenRows);
        ArgumentNullException.ThrowIfNull(verifiedSource);
        var links = verifiedSource.ExistingResolvedGoldLinksByObservation;
        if (frozenRows.Count == 0 || links is null
            || links.Count != frozenRows.Count
            || verifiedSource.ManagerByObservation.Count != frozenRows.Count
            || verifiedSource.AuthenticatedSilverByObservation.Count != frozenRows.Count
            || verifiedSource.CandidatesByObservation.Count != frozenRows.Count
            || verifiedSource.EvidenceByObservation.Count != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_BINDING_NOT_AUTHENTICATED");

        var sources = new List<V9OperationalCohortGraphPreview.Source>(frozenRows.Count);
        long previous = 0;
        foreach (var row in frozenRows)
        {
            if (row is null || row.PessoaObservacaoId <= previous
                || row.Observation is null || row.Observation.Cpf is not null
                || row.Observation.ResolutionAttributes is { Count: > 0 }
                || !links.TryGetValue(row.PessoaObservacaoId, out var goldUuid)
                || goldUuid == Guid.Empty
                || !verifiedSource.ManagerByObservation.TryGetValue(
                    row.PessoaObservacaoId, out var manager)
                || string.IsNullOrWhiteSpace(manager)
                || !verifiedSource.AuthenticatedSilverByObservation.TryGetValue(
                    row.PessoaObservacaoId, out var authenticatedSilver)
                || authenticatedSilver is null
                || !Equals(row.Observation, authenticatedSilver)
                || !verifiedSource.CandidatesByObservation.ContainsKey(row.PessoaObservacaoId)
                || !verifiedSource.EvidenceByObservation.ContainsKey(row.PessoaObservacaoId))
                throw new InvalidDataException("RUNNER_V9_DEV_GRAPH_BINDING_NOT_AUTHENTICATED");

            sources.Add(new V9OperationalCohortGraphPreview.Source(
                row.PessoaObservacaoId, goldUuid, manager, authenticatedSilver));
            previous = row.PessoaObservacaoId;
        }
        return Array.AsReadOnly(sources.ToArray());
    }

    /// <summary>
    /// A Gold-joined candidate list must be exactly the original operational
    /// UUID union, including order. Missing/non-REFERENCIA Gold cannot be
    /// silently treated as an eligible abstention or dropped candidate.
    /// </summary>
    public static void VerifyCanonicalCandidateMembership(
        IReadOnlyList<Guid> verifiedOperationalUuids,
        IReadOnlyList<GoldCandidate> joinedGoldCandidates)
    {
        ArgumentNullException.ThrowIfNull(verifiedOperationalUuids);
        ArgumentNullException.ThrowIfNull(joinedGoldCandidates);
        // The operational candidate UUID union must be a set; matching two
        // equally duplicated lists would not establish a valid Gold graph.
        if (verifiedOperationalUuids.Count != joinedGoldCandidates.Count
            || verifiedOperationalUuids.Any(uuid => uuid == Guid.Empty)
            || verifiedOperationalUuids.Distinct().Count() != verifiedOperationalUuids.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_CANDIDATE_SET_MISMATCH");
        for (var i = 0; i < verifiedOperationalUuids.Count; i++)
            if (verifiedOperationalUuids[i] == Guid.Empty
                || joinedGoldCandidates[i] is null
                || joinedGoldCandidates[i].PessoaUuid != verifiedOperationalUuids[i])
                throw new InvalidDataException("RUNNER_V9_DEV_CANDIDATE_SET_MISMATCH");
    }

    public static async Task<VerifiedRunCandidates> ReadAsync(
        SqlConnection connection,
        V9OperationalPinnedCohortRuntime pinnedWinner,
        Guid attemptId,
        Guid runId,
        IReadOnlyList<V9FrozenRunObservation> frozenRows,
        int maximumCandidatesPerObservation,
        CancellationToken cancellationToken, bool useIsolatedProjection = false,
        string? effectiveEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(pinnedWinner);
        ArgumentNullException.ThrowIfNull(frozenRows);
        if (attemptId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_ATTEMPT_MISSING");
        if (runId == Guid.Empty)
            throw new InvalidOperationException("RUNNER_V9_DEV_RUN_MISSING");
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCandidatesPerObservation, 1);

        // Source IDs are globally ordered immutable run_item IDs, never
        // page-local indexes. The entire SQL run_item membership is checked
        // below before any operational candidate/Gold query.
        // An empty FULL/INCREMENTAL DEV run is legitimate. Authenticate its
        // run header and zero run_item count in SQL instead of mistaking it
        // for a malformed submission (BuildDev already supports 0 rows).
        if (frozenRows.Any(x => x is null || x.PessoaObservacaoId <= 0
                || x.Observation is null || x.Observation.Cpf is not null
                || x.Observation.ResolutionAttributes is { Count: > 0 })
            || !frozenRows.Select(x => x.PessoaObservacaoId)
                .SequenceEqual(frozenRows.Select(x => x.PessoaObservacaoId).OrderBy(x => x))
            || frozenRows.Select(x => x.PessoaObservacaoId).Distinct().Count() != frozenRows.Count)
            throw new InvalidDataException("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID");

        // This is the same physical, read-only gate as controlled DEV RunAsync,
        // before executing even the first candidate query.
        await V9ControlledDevSelection.VerifyDisposableAttemptDatabaseAsync(
            connection, attemptId, cancellationToken);

        // SQL Server error 3964: session-local temp-table DDL must precede
        // SNAPSHOT. The temp-table preparation itself performs no operational DDL/DML.
        await V9ControlledDevSilverGraph.PrepareTempTableAsync(
            connection, cancellationToken);

        // One consistent operational snapshot for header, run_item,
        // Silver managers, blocking and Gold. If SNAPSHOT is unavailable, SQL
        // must fail closed; never fall back to independent READ COMMITTED pages.
        await using var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Snapshot, cancellationToken);

        // An empty submitted universe is not proof that a run exists.
        // Authenticate the exact run/model/mode/state and stored eligible count.
        await using (var header = new SqlCommand("""
            SELECT modelo_id,modelo_versao,tipo_run,status,registros_elegiveis
              FROM identidade.linkage_run
             WHERE linkage_run_id=@run_id;
            """, connection, snapshot))
        {
            header.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await using var record = await header.ExecuteReaderAsync(cancellationToken);
            if (!await record.ReadAsync(cancellationToken)
                || record.IsDBNull(0) || record.GetGuid(0) != pinnedWinner.ModelId
                || record.IsDBNull(1) || record.GetInt32(1) != 9
                || record.IsDBNull(2)
                || record.GetString(2) is not ("FULL" or "INCREMENTAL")
                || record.IsDBNull(3) || record.GetString(3) != "EXECUTANDO"
                || record.IsDBNull(4) || record.GetInt64(4) != frozenRows.Count
                || await record.ReadAsync(cancellationToken))
                throw new InvalidDataException("RUNNER_V9_DEV_RUN_HEADER_MISMATCH");
        }

        // Reconcile EXACT SQL membership, not a page-local or caller-truncated
        // sample. A single missing/extra row is fatal before candidate scoring.
        await using (var items = new SqlCommand("""
            SELECT pessoa_observacao_id
              FROM identidade.linkage_run_item
             WHERE linkage_run_id=@run_id
             ORDER BY pessoa_observacao_id;
            """, connection, snapshot))
        {
            items.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            var index = 0;
            await using var ids = await items.ExecuteReaderAsync(cancellationToken);
            while (await ids.ReadAsync(cancellationToken))
            {
                if (ids.IsDBNull(0) || index >= frozenRows.Count
                    || ids.GetInt64(0) != frozenRows[index].PessoaObservacaoId)
                    throw new InvalidDataException("RUNNER_V9_DEV_RUN_UNIVERSE_MISMATCH");
                index++;
            }
            if (index != frozenRows.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_RUN_UNIVERSE_MISMATCH");
        }

        // Authenticated managers come from real SQL only. Validate the
        // fields materialized for scoring against current Silver; missing,
        // duplicated or stale references must fail before Gold candidates.
        var managers = new Dictionary<long, string>(frozenRows.Count);
        // Retain the actual SQL-authenticated Silver values from this snapshot.
        // A later preview must not score different attributes with only matching IDs.
        var authenticatedSilver = new Dictionary<long, IdentityObservation>(frozenRows.Count);
        await using (var silver = new SqlCommand("""
            SELECT po.pessoa_observacao_id,g.codigo,po.cpf,
                   po.cpf_ausente_motivo,po.nome_completo,
                   po.data_nascimento,po.nome_mae
              FROM identidade.linkage_run_item ri
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=ri.pessoa_observacao_id
              JOIN ref.gestor g ON g.gestor_id=po.gestor_id
             WHERE ri.linkage_run_id=@run_id
             ORDER BY po.pessoa_observacao_id;
            """, connection, snapshot))
        {
            silver.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            var index = 0;
            await using var rows = await silver.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
            {
                if (index >= frozenRows.Count || rows.IsDBNull(0) || rows.IsDBNull(1))
                    throw new InvalidDataException("RUNNER_V9_DEV_SILVER_OR_MANAGER_DRIFT");
                var loaded = new IdentityObservation(
                    rows.IsDBNull(2) ? null : rows.GetString(2),
                    rows.IsDBNull(3) ? null : rows.GetString(3),
                    rows.IsDBNull(4) ? null : rows.GetString(4),
                    rows.IsDBNull(5) ? null : DateOnly.FromDateTime(rows.GetDateTime(5)),
                    rows.IsDBNull(6) ? null : rows.GetString(6),
                    frozenRows[index].Observation.ResolutionAttributes);
                var id = rows.GetInt64(0);
                var manager = VerifyFrozenSilverObservation(
                    frozenRows[index], id, rows.GetString(1), loaded);
                if (!managers.TryAdd(id, manager) || !authenticatedSilver.TryAdd(id, loaded))
                    throw new InvalidDataException("RUNNER_V9_DEV_SILVER_OR_MANAGER_DRIFT");
                index++;
            }
            if (index != frozenRows.Count)
                throw new InvalidDataException("RUNNER_V9_DEV_SILVER_OR_MANAGER_DRIFT");
        }

        // A blocking candidate is NOT proof of identity membership. In the
        // same read-only SNAPSHOT, collect only ACTIVE, already RESOLVIDO SQL
        // links to real Gold REFERENCIA persons for this exact run universe.
        // An unlinked pending Silver observation deliberately stays unbound.
        var existingResolvedGold = new Dictionary<long, Guid>();
        var activeLinkRowsSeen = new HashSet<long>();
        var expectedFrozenIds = frozenRows.Select(r => r.PessoaObservacaoId).ToHashSet();
        await using (var links = new SqlCommand("""
            SELECT ri.pessoa_observacao_id,v.status,v.pessoa_uuid,g.estado_identidade
              FROM identidade.linkage_run_item ri
              JOIN identidade.vinculo_fonte v
                ON v.pessoa_observacao_id=ri.pessoa_observacao_id AND v.ativo=1
              LEFT JOIN gold.pessoa g ON g.pessoa_uuid=v.pessoa_uuid
             WHERE ri.linkage_run_id=@run_id
             ORDER BY ri.pessoa_observacao_id,v.vinculo_id;
            """, connection, snapshot))
        {
            links.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await using var linkRows = await links.ExecuteReaderAsync(cancellationToken);
            while (await linkRows.ReadAsync(cancellationToken))
            {
                if (linkRows.IsDBNull(0) || linkRows.IsDBNull(1))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_LINK_NOT_AUTHENTICATED");
                RecordExistingResolvedGoldLink(existingResolvedGold, activeLinkRowsSeen,
                    expectedFrozenIds, linkRows.GetInt64(0), linkRows.GetString(1),
                    linkRows.IsDBNull(2) ? null : linkRows.GetGuid(2),
                    linkRows.IsDBNull(3) ? null : linkRows.GetString(3));
            }
        }

        // A persisted progressiva REFERENCIA can be the current canonical
        // identity even without a RESOLVIDO active vinculo_fonte on this
        // observation. The unchanged SQL publisher gives that state
        // precedence: make it a consistency vote in the same SQL SNAPSHOT,
        // so r1 cannot create a conflicting association in its group.
        await using (var progressiva = new SqlCommand("""
            SELECT ri.pessoa_observacao_id,p.canonical_uuid,g.estado_identidade
              FROM identidade.linkage_run_item ri
              JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=ri.pessoa_observacao_id
              JOIN identidade.pessoa_origem_progressiva p
                ON p.pessoa_origem_id=po.pessoa_origem_id
              LEFT JOIN gold.pessoa g
                ON g.pessoa_uuid=p.canonical_uuid
             WHERE ri.linkage_run_id=@run_id
               AND p.estado=N'REFERENCIA'
             ORDER BY ri.pessoa_observacao_id;
            """, connection, snapshot))
        {
            progressiva.Parameters.Add("@run_id", SqlDbType.UniqueIdentifier).Value = runId;
            await using var references = await progressiva.ExecuteReaderAsync(cancellationToken);
            while (await references.ReadAsync(cancellationToken))
            {
                var id = references.GetInt64(0);
                if (!expectedFrozenIds.Contains(id)
                    || references.IsDBNull(1)
                    || references.IsDBNull(2)
                    || references.GetString(2) != "REFERENCIA")
                    throw new InvalidDataException("RUNNER_V9_DEV_PROGRESSIVA_REFERENCE_INVALID");
                var canonical = references.GetGuid(1);
                if (canonical == Guid.Empty)
                    throw new InvalidDataException("RUNNER_V9_DEV_PROGRESSIVA_REFERENCE_INVALID");
                if (existingResolvedGold.TryGetValue(id, out var existing))
                {
                    if (existing != canonical)
                        throw new InvalidDataException("RUNNER_V9_DEV_PROGRESSIVA_GOLD_CONFLICT");
                }
                else if (!existingResolvedGold.TryAdd(id, canonical))
                    throw new InvalidDataException("RUNNER_V9_DEV_PROGRESSIVA_REFERENCE_DUPLICATE");
            }
        }

        var winner = pinnedWinner.FrozenWinner;
        if (pinnedWinner.ModelId !=
                V9OperationalSqlModelConverter.ModelIdFromWinnerSha(pinnedWinner.WinnerSha256))
            throw new InvalidDataException("RUNNER_V9_DEV_WINNER_MODEL_MISMATCH");
        var planner = V9FittedSnapshotExecutionRuntime.Load(
            winner.SerializeUtf8(), pinnedWinner.WinnerSha256);

        V9OperationalBlockingManifest.Receipt? blockingManifest = null;
        if (useIsolatedProjection)
        {
            await V9BlockingMaintenanceGate.RequireAsync(effectiveEnvironment, true, attemptId,
                connection, snapshot, cancellationToken);
            await BlockingProjectionPersistence.InitializeOrVerifyPinnedV9Async(connection, snapshot,
                effectiveEnvironment, true, attemptId, planner.MaximumPrefixLength
                    ?? throw new InvalidDataException("RUNNER_V9_BLOCKING_PROJECTION_PIN_MISMATCH"),
                planner.ProjectionFingerprintSha256, cancellationToken);
            blockingManifest = await V9OperationalBlockingManifest.ReadIsolatedAsync(
                connection, snapshot, planner, cancellationToken);
        }

        var result = new Dictionary<long, IReadOnlyList<GoldCandidate>>(frozenRows.Count);
        var evidence = new Dictionary<long, V9OperationalCandidateRead.Receipt>(frozenRows.Count);
        var noEligiblePass = new HashSet<long>();
        foreach (var row in frozenRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (planner.Plan(row.Observation).Count == 0)
                noEligiblePass.Add(row.PessoaObservacaoId);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 120;
            command.Transaction = snapshot;
            // The established operational reader independently meters the
            // full Gold-reference UUID union and verifies its SQL cardinality
            // inside this SAME SNAPSHOT. It is not a new candidate oracle.
            var authenticated = await V9OperationalCandidateRead.ReadAsync(
                connection, snapshot, planner, row.Observation, cancellationToken, useIsolatedProjection);
            if (authenticated.CandidateCount > maximumCandidatesPerObservation)
                throw new InvalidDataException("RUNNER_V9_DEV_CANDIDATE_GRAPH_TRUNCATED");
            if (!evidence.TryAdd(row.PessoaObservacaoId, authenticated))
                throw new InvalidDataException("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID");
            var candidateSql = useIsolatedProjection
                ? planner.BuildIsolatedCandidateUuidQuery(command, row.Observation)
                : planner.BuildCandidateUuidQuery(command,
                    row.Observation, planner.ProjectionSchemaVersion,
                    planner.ProjectionFingerprintSha256);
            command.CommandText = """
                SELECT c.pessoa_uuid,g.nome_completo,g.data_nascimento,g.nome_mae,
                       g.estado_identidade
                  FROM (
                """ + candidateSql + """
                ) AS c LEFT JOIN gold.pessoa AS g
                  ON g.pessoa_uuid=c.pessoa_uuid
                 ORDER BY c.pessoa_uuid;
                """;

            var candidates = new List<GoldCandidate>();
            var seen = new HashSet<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(4))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_REFERENCE_MISSING");
                var id = reader.GetGuid(0);
                if (id == Guid.Empty || !seen.Add(id))
                    throw new InvalidDataException("RUNNER_V9_DEV_GOLD_CANDIDATE_DUPLICATE");
                if (candidates.Count >= maximumCandidatesPerObservation)
                    throw new InvalidDataException("RUNNER_V9_DEV_CANDIDATE_GRAPH_TRUNCATED");
                // Optional core attributes are nullable in the real Gold
                // schema for completude_nucleo=PARCIAL; preserve null evidence.
                var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                var birth = reader.IsDBNull(2) ? (DateOnly?)null
                    : DateOnly.FromDateTime(reader.GetDateTime(2));
                var mother = reader.IsDBNull(3) ? null : reader.GetString(3);
                candidates.Add(PreserveReferenceEvidence(
                    id, reader.GetString(4), name, birth, mother));
            }
            VerifyCanonicalCandidateMembership(authenticated.CandidateUuids, candidates);
            if (!result.TryAdd(row.PessoaObservacaoId,
                    Array.AsReadOnly(candidates.ToArray())))
                throw new InvalidDataException("RUNNER_V9_DEV_FROZEN_UNIVERSE_INVALID");
        }
        // Read the provenance of all persistent origins for the exact Gold
        // UUID union before committing this same snapshot. The b1 scorer
        // consumes these facts, with no fallback to the Gold summary.
        var secureOriginFacts = await V9ControlledDevSecureOriginReader.ReadAsync(
            connection, snapshot, result.Values.SelectMany(v => v)
                .Select(v => v.PessoaUuid).Distinct().ToArray(), cancellationToken);
        // Physical Silver graph uses the same verified source values and
        // SNAPSHOT as the Gold candidate census: no inter-snapshot repair.
        var silverGraph = await V9ControlledDevSilverGraph.ReadAsync(
            connection, snapshot, winner, frozenRows, managers, cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new VerifiedRunCandidates(
            new ReadOnlyDictionary<long, string>(managers),
            new ReadOnlyDictionary<long, IReadOnlyList<GoldCandidate>>(result),
            new ReadOnlyDictionary<long, V9OperationalCandidateRead.Receipt>(evidence),
            new ReadOnlyDictionary<long, IdentityObservation>(authenticatedSilver),
            new ReadOnlyDictionary<long, Guid>(existingResolvedGold))
        {
            SilverGraph = silverGraph,
            IsolatedBlockingManifest = blockingManifest,
            NoEligibleBlockingPassObservationIds = noEligiblePass,
            SecureOriginFactsByPerson = secureOriginFacts
        };
    }
}

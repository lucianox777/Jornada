using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Independent enumeration for the first, clean, CPF-founded DEV 1k FULL.
/// Shares only the frozen feature projection/temporal catalog, not the
/// candidate SQL builder, production origin reader, b1 scorer or resolver.
/// Never uses truth labels or writes identities/index keys.
/// </summary>
public static class V9DevE2EFirstFullMembershipOracle
{
    public sealed record Source(long Id, long Origin, string Manager, IdentityObservation Observation,
        Guid? Current, string Status, string Method, Guid? Canonical, string OriginState);
    public sealed record Reference(Guid Id, string? Name, string? Mother, DateOnly? Birth);
    public sealed record Receipt(int Observations, long CandidatePeople, long SecureRecords,
        string EvidenceMembershipSha256, int SilverEdges, string SilverEdgesSha256,
        bool SilverSqlReplayEqual, bool ActualConsumedSilverEdgesVerified = false)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<V9DevE2EScalarOracle.Rank> ExpectedRanks { get; init; } = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<int> ExpectedGroupRoots { get; init; } = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public IReadOnlyList<long> ExpectedObservationIds { get; init; } = [];
        public int IndependentGroupCount { get; init; }
        public string? IndependentGroupSha256 { get; init; }
        public bool FrozenGroupReplayEqual { get; init; }
        public V9DevE2EGroupOracle.EdgeScoreReceipt? SilverDirectionalReplay { get; init; }
    }

    private const string Policy = "V9_DEV_B1_SECURE_PERSISTENT_ORIGIN_RECORD_RANK_V1";

    public static bool MatchesAnyPass(IReadOnlySet<BlockingProjectionKey> source,
        IReadOnlySet<BlockingProjectionKey> target, IReadOnlyList<LinkageBlockingPass> passes) =>
        passes.Any(pass => pass.Fields.All(field =>
            source.Any(key => key.Feature == field && target.Contains(key))));

    public static Receipt Compute(V9FittedSnapshot winner, IReadOnlyList<Source> sources,
        IReadOnlyList<Reference> references)
    {
        var p = winner.Payload;
        var prefix = V9CalibrationBlockingProjection.ResolveFrozenPrefixLength(
            p.ProjectionSchemaVersion, p.ProjectionFingerprintSha256);
        var fields = p.BlockingPasses.SelectMany(x => x.Fields).ToHashSet(StringComparer.Ordinal);
        // This independently enumerates the pinned three-pass model only;
        // the temporal classification is from the frozen projection contract.
        if (prefix <= 0 || p.BlockingPasses.Count != 3 ||
            !fields.SetEquals(new[] { "birth_year", "name_first", "name_last",
                "mother_name_phonetic_ptbr", "mother_name_last", "name_phonetic_ptbr" }))
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_PASS_UNSUPPORTED");
        HashSet<BlockingProjectionKey> Project(string? name, string? mother, DateOnly? birth) =>
            V9CalibrationBlockingProjection.Project(name, mother, birth, prefix)
                .Where(x => fields.Contains(x.Feature)).ToHashSet();
        var rows = sources.OrderBy(x => x.Id).ToArray();
        if (rows.Length == 0 || rows.Any(x => x.Id <= 0 || x.Origin <= 0)
            || rows.Select(x => x.Id).Distinct().Count() != rows.Length)
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_SOURCE_INVALID");
        var refIds = references.Select(x => x.Id).ToHashSet();
        if (refIds.Count == 0 || refIds.Contains(Guid.Empty) || refIds.Count != references.Count)
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_REFERENCE_INVALID");
        // The fixture is exclusively the FIRST FULL after HTTP. No previously
        // published founder, probabilistic assignment, governed correction or
        // UUID feedback is admitted by this narrower independent oracle.
        if (rows.Any(x => x.Status == "RESOLVIDO" &&
              (x.Method != "CPF_DETERMINISTICO" || x.Current is not Guid id || !refIds.Contains(id))
            || x.Status is not ("RESOLVIDO" or "NAO_RESOLVIDO")
            || x.OriginState is not ("REFERENCIA" or "PROVISORIA")
            || x.Method is "CORRECAO_GOVERNADA" or "UUID_JORNADA_RETROALIMENTACAO" or "LINKAGE_PROBABILISTICO" or "CONFLITO_GOVERNADO"))
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_FIRST_FULL_ONLY");

        var keys = references.ToDictionary(x => x.Id, x => Project(x.Name, x.Mother, x.Birth));
        foreach (var row in rows.Where(x => x.Status == "RESOLVIDO"))
            keys[row.Current!.Value].UnionWith(Project(row.Observation.NomeCompleto,
                row.Observation.NomeMae, null).Where(k =>
                    BlockingFeatureTemporalCatalog.Get(k.Feature) == BlockingFeatureTemporalSemantics.VersionedAlias));
        var records = rows.GroupBy(x => x.Origin).ToArray();
        var evidence = references.ToDictionary(x => x.Id, _ => new List<Source>());
        foreach (var origin in records)
        {
            var states = origin.Select(x => (x.OriginState, x.Canonical)).Distinct().ToArray();
            if (states.Length != 1)
                throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_ORIGIN_DRIFT");
            var (state, canonical) = states[0];
            var protectedTargets = origin.Where(x => x.Status == "RESOLVIDO")
                .Select(x => x.Current!.Value).Distinct().ToArray();
            foreach (var target in protectedTargets)
            {
                if (protectedTargets.Length == 1 &&
                    ((state == "REFERENCIA" && canonical == target) ||
                     (state == "PROVISORIA" && canonical is null)))
                    evidence[target].AddRange(origin);
            }
        }
        var pending = rows.Where(x => x.Observation.Cpf is null).ToArray();
        var pendingKeys = pending.Select(x => Project(x.Observation.NomeCompleto,
            x.Observation.NomeMae, x.Observation.DataNascimento)).ToArray();
        long peopleCount = 0, recordCount = 0;
        var ranks = new List<V9DevE2EScalarOracle.Rank>(pending.Length);
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(IncrementalHash hash, string line) => hash.AppendData(Encoding.UTF8.GetBytes(line));
        for (var i = 0; i < pending.Length; i++)
        {
            var candidates = keys.Where(x => MatchesAnyPass(pendingKeys[i], x.Value, p.BlockingPasses))
                .Select(x => x.Key).OrderBy(x => x).ToArray();
            peopleCount += candidates.Length;
            ranks.Add(V9DevE2EScalarOracle.ComputeRank(p, pending[i], candidates, evidence,
                p.BlockingPasses.Any(pass => pass.Fields.All(field => pendingKeys[i].Any(key => key.Feature == field)))));
            using var local = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(local, Policy + "\n");
            foreach (var person in candidates) Append(local, $"P:{person:D}\n");
            foreach (var person in candidates)
            foreach (var record in evidence[person].OrderBy(x => x.Origin).ThenBy(x => x.Id))
            {
                recordCount++;
                Append(local, FormattableString.Invariant($"E:{person:D}:{record.Origin}:{record.Id}\n"));
            }
            Append(aggregate, FormattableString.Invariant(
                $"{pending[i].Id}:{Convert.ToHexStringLower(local.GetHashAndReset())}\n"));
        }
        var edges = new List<(int A,int B)>();
        using var graph = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < pending.Length; i++)
        for (var j = i + 1; j < pending.Length; j++)
            if (pending[i].Manager != pending[j].Manager &&
                MatchesAnyPass(pendingKeys[i], pendingKeys[j], p.BlockingPasses))
            {
                edges.Add((i+1,j+1));
                Append(graph, FormattableString.Invariant($"{i + 1}:{j + 1}\n"));
            }
        var grouping = V9DevE2EGroupOracle.Compute(p,pending,edges,ranks);
        return new(pending.Length, peopleCount, recordCount,
            Convert.ToHexStringLower(aggregate.GetHashAndReset()), edges.Count,
            Convert.ToHexStringLower(graph.GetHashAndReset()), false)
        {
            ExpectedObservationIds = pending.Select(x => x.Id).ToArray(),
            ExpectedRanks = grouping.Decisions,
            ExpectedGroupRoots = grouping.RootByIndex,
            IndependentGroupCount = grouping.GroupCount,
            IndependentGroupSha256 = grouping.PartitionSha256
        };
    }

    public static async Task<Receipt> ReadAsync(IOperationalSqlAdapter sql,
        V9FittedSnapshot winner, Guid attempt, int total, int eligible, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await V9DisposableAttemptDatabase.VerifyAsync(connection, attempt, ct);
        await V9ControlledDevSilverGraph.PrepareTempTableAsync(connection, ct);
        await using var snapshot = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot, ct);
        await V9DisposableAttemptDatabase.VerifyAsync(connection, attempt, ct, snapshot);
        await using (var admission = new SqlCommand("""
            SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM identidade.linkage_run)
              AND NOT EXISTS(SELECT 1 FROM identidade.blocking_chave_v9)
              AND NOT EXISTS(SELECT 1 FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA' AND cpf IS NULL)
              THEN 1 ELSE 0 END;
            """, connection, snapshot))
            if (Convert.ToInt32(await admission.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_FIRST_FULL_ONLY");
        var rows = new List<Source>();
        var refs = new List<Reference>();
        static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        static Guid? Uuid(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);
        static DateOnly? Birth(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
        await using (var command = new SqlCommand("""
            SELECT po.pessoa_observacao_id,po.pessoa_origem_id,g.codigo,
                   po.cpf,po.cpf_ausente_motivo,po.nome_completo,po.data_nascimento,po.nome_mae,
                   vc.pessoa_uuid,vc.status,vc.metodo_resolucao,p.canonical_uuid,p.estado
              FROM silver.pessoa_observacao po
              JOIN ref.gestor g ON g.gestor_id=po.gestor_id
              JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
              JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
             ORDER BY po.pessoa_observacao_id;
            """, connection, snapshot))
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                    new IdentityObservation(Str(reader,3),Str(reader,4),Str(reader,5),Birth(reader,6),Str(reader,7)),
                    Uuid(reader,8),reader.GetString(9),reader.GetString(10),Uuid(reader,11),reader.GetString(12)));
        }
        await using (var command = new SqlCommand("""
            SELECT pessoa_uuid,nome_completo,nome_mae,data_nascimento
            FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';
            """, connection, snapshot))
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                refs.Add(new(reader.GetGuid(0),Str(reader,1),Str(reader,2),Birth(reader,3)));
        }
        if (rows.Count != total || rows.Count(x => x.Observation.Cpf is null) != eligible)
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_CENSUS_MISMATCH");
        var expected = Compute(winner, rows, refs);
        var pending = rows.Where(x => x.Observation.Cpf is null).OrderBy(x => x.Id).ToArray();
        // Independently enumerate each pair, then compare the physical SQL
        // signature join on exactly the same input/SNAPSHOT. This is a
        // separate pre-FULL replay, not a capture of the actual consumed graph.
        var observed = await V9ControlledDevSilverGraph.ReadAsync(connection, snapshot, winner,
            pending.Select(x => new V9FrozenRunObservation(x.Id,x.Observation)).ToArray(),
            pending.ToDictionary(x => x.Id,x => x.Manager), ct);
        if (expected.SilverEdges != observed.Edges.Count ||
            expected.SilverEdgesSha256 != observed.CanonicalEdgesSha256)
            throw new InvalidDataException("RUNNER_V9_E2E_MEMBERSHIP_SILVER_EDGE_DIVERGENCE");
        var directional = V9DevE2EGroupOracle.VerifyDirectionalReplay(winner,pending,observed.Edges);
        V9DevE2EGroupOracle.VerifyFrozenReplay(winner,pending,observed.Edges,expected.ExpectedGroupRoots);
        await snapshot.CommitAsync(ct);
        return expected with { SilverSqlReplayEqual = true, FrozenGroupReplayEqual = true,
            SilverDirectionalReplay = directional };
    }
}

using System.Collections.ObjectModel;
using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>
/// Read-only b1 provenance census, bound to the already authenticated Gold
/// candidate UUID union and the caller's SQL SNAPSHOT. No Gold summary is
/// evidence. This stage returns origin facts, not an authorization to score.
/// </summary>
internal static class V9ControlledDevSecureOriginReader
{
    internal sealed record OriginFacts(
        Guid PersonUuid, long OriginId, Guid InitialUuid, Guid? CanonicalUuid,
        string State, long Version, bool HasCpf, bool HasGovernedCorrection,
        bool HasUuidFeedback, bool HasConflictingProtectedLink,
        bool HasPriorProbabilisticAssociation, bool HasPublishedFounderProof,
        IReadOnlyList<V9ControlledDevSecureEvidenceScore.Evidence> Observations);

    // Read ALL versions, not only blocking hits, latest observations or
    // observations with a protected link. The authority is per origin.
    // Retain separate provenance flags until the b1 eligibility policy has
    // been checked against physical revocation and UUID feedback scenarios.
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OriginFacts>>> ReadAsync(
        SqlConnection connection, SqlTransaction snapshot,
        IReadOnlyCollection<Guid> candidatePeople, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(candidatePeople);
        if (!ReferenceEquals(snapshot.Connection, connection)
            || snapshot.IsolationLevel != IsolationLevel.Snapshot)
            throw new InvalidOperationException("RUNNER_V9_DEV_B1_SNAPSHOT_REQUIRED");
        var ids = candidatePeople.ToHashSet();
        if (ids.Count != candidatePeople.Count || ids.Contains(Guid.Empty))
            throw new InvalidDataException("RUNNER_V9_DEV_B1_CANDIDATE_UNIVERSE_INVALID");

        var byPerson = ids.ToDictionary(x => x, _ => new List<OriginFacts>());
        if (ids.Count == 0)
            return Freeze(byPerson);

        // The effective current link view implements the existing protected
        // link precedence. Keep canonical state and direct link provenance
        // separate: neither a matching UUID nor a probabilistic link is
        // sufficient to establish secure origin membership.
        await using var command = new SqlCommand("""
            WITH candidates AS (
                SELECT pessoa_uuid
                  FROM OPENJSON(@people) WITH (pessoa_uuid uniqueidentifier '$')
            ), origin_targets AS (
                SELECT c.pessoa_uuid,p.pessoa_origem_id
                  FROM candidates c
                  JOIN identidade.pessoa_origem_progressiva p
                    ON p.canonical_uuid=c.pessoa_uuid AND p.estado=N'REFERENCIA'
                UNION
                SELECT c.pessoa_uuid,po.pessoa_origem_id
                  FROM candidates c
                  JOIN identidade.v_vinculo_corrente v
                    ON v.pessoa_uuid=c.pessoa_uuid AND v.status=N'RESOLVIDO'
                  JOIN silver.pessoa_observacao po
                    ON po.pessoa_observacao_id=v.pessoa_observacao_id
                 WHERE po.pessoa_origem_id IS NOT NULL
            ), origin_links AS (
                SELECT t.pessoa_uuid,t.pessoa_origem_id,
                       MAX(CASE WHEN v.status=N'RESOLVIDO' AND v.pessoa_uuid=t.pessoa_uuid
                                AND v.metodo_resolucao=N'CPF_DETERMINISTICO' THEN 1 ELSE 0 END) AS cpf,
                       MAX(CASE WHEN v.status=N'RESOLVIDO' AND v.pessoa_uuid=t.pessoa_uuid
                                AND v.metodo_resolucao=N'CORRECAO_GOVERNADA' THEN 1 ELSE 0 END) AS correction,
                       MAX(CASE WHEN v.status=N'RESOLVIDO' AND v.pessoa_uuid=t.pessoa_uuid
                                AND v.metodo_resolucao=N'UUID_JORNADA_RETROALIMENTACAO' THEN 1 ELSE 0 END) AS feedback,
                       MAX(CASE WHEN v.metodo_resolucao IN (N'CPF_DETERMINISTICO',
                                N'CORRECAO_GOVERNADA',N'UUID_JORNADA_RETROALIMENTACAO',N'CONFLITO_GOVERNADO')
                                AND (v.status=N'CONFLITO' OR v.metodo_resolucao=N'CONFLITO_GOVERNADO'
                                  OR (v.status=N'RESOLVIDO' AND v.pessoa_uuid<>t.pessoa_uuid))
                                THEN 1 ELSE 0 END) AS conflict
                  FROM origin_targets t
                  JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=t.pessoa_origem_id
                  LEFT JOIN identidade.v_vinculo_corrente v
                    ON v.pessoa_observacao_id=po.pessoa_observacao_id
                 GROUP BY t.pessoa_uuid,t.pessoa_origem_id
            ), origin_facts AS (
                SELECT t.pessoa_uuid,p.pessoa_origem_id,p.initial_uuid,p.canonical_uuid,p.estado,p.versao,
                       l.cpf,l.correction,l.feedback,l.conflict,
                       CASE WHEN EXISTS (
                           SELECT 1
                             FROM identidade.pessoa_origem_progressiva_evento e
                             JOIN identidade.linkage_run lr ON lr.linkage_run_id=e.linkage_run_id
                            WHERE e.pessoa_origem_id=p.pessoa_origem_id
                              AND e.resultado=N'ASSOCIACAO_EXISTENTE'
                              AND e.canonical_uuid=t.pessoa_uuid AND lr.status=N'PUBLICADO'
                       ) THEN 1 ELSE 0 END AS prior_association,
                       CASE WHEN p.estado=N'REFERENCIA' AND p.canonical_uuid=t.pessoa_uuid
                                 AND p.initial_uuid=t.pessoa_uuid AND EXISTS (
                           SELECT 1
                             FROM identidade.pessoa_origem_progressiva_evento e
                             JOIN identidade.linkage_run lr ON lr.linkage_run_id=e.linkage_run_id
                             JOIN identidade.linkage_resultado r ON r.linkage_run_id=lr.linkage_run_id
                             JOIN silver.pessoa_observacao founder
                               ON founder.pessoa_observacao_id=r.pessoa_observacao_id
                            WHERE e.pessoa_origem_id=p.pessoa_origem_id AND e.versao=p.versao
                              AND e.tipo=N'RESOLUCAO' AND e.estado=N'REFERENCIA'
                              AND e.resultado=N'NOVA_IDENTIDADE' AND e.completo=1
                              AND e.canonical_uuid=p.initial_uuid AND e.target_uuid IS NULL
                              AND lr.status=N'PUBLICADO' AND lr.publicado_em IS NOT NULL
                              AND lr.avaliados=lr.registros_elegiveis
                              AND founder.pessoa_origem_id=p.pessoa_origem_id
                              AND r.pessoa_origem_id_publicado=p.pessoa_origem_id
                              AND r.progressiva_versao=e.versao
                              AND r.modelo_id=lr.modelo_id AND r.modelo_versao=lr.modelo_versao
                              AND r.resultado_publicacao=N'NOVA_IDENTIDADE'
                              AND r.status_publicacao=N'RESOLVIDO'
                              AND r.motivo_publicacao=N'NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA'
                              AND r.pessoa_uuid_publicado=p.initial_uuid AND r.publicado_em IS NOT NULL
                              AND r.status=N'NAO_RESOLVIDO' AND r.motivo LIKE N'SEM[_]CANDIDATO[_]%'
                              AND r.melhor_candidato_uuid IS NULL AND r.segundo_candidato_uuid IS NULL
                              AND r.pessoa_uuid_resolvido IS NULL
                              AND e.universo_referencia=r.universo_referencia
                              AND NULLIF(LTRIM(RTRIM(e.universo_referencia)),N'') IS NOT NULL
                              AND e.evidencia_referencia=CONCAT(N'LINKAGE_RUN:',
                                  CONVERT(nvarchar(36),lr.linkage_run_id),N':OBS:',
                                  CONVERT(nvarchar(30),r.pessoa_observacao_id))
                              AND NOT EXISTS (
                                  SELECT 1 FROM identidade.linkage_resultado other
                                  JOIN silver.pessoa_observacao os
                                    ON os.pessoa_observacao_id=other.pessoa_observacao_id
                                  WHERE other.linkage_run_id=lr.linkage_run_id
                                    AND os.pessoa_origem_id=p.pessoa_origem_id
                                    AND (other.melhor_candidato_uuid IS NOT NULL
                                      OR other.segundo_candidato_uuid IS NOT NULL
                                      OR other.pessoa_uuid_resolvido IS NOT NULL))
                       ) THEN 1 ELSE 0 END AS founder_proof
                  FROM origin_targets t
                  JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=t.pessoa_origem_id
                  JOIN origin_links l ON l.pessoa_uuid=t.pessoa_uuid AND l.pessoa_origem_id=t.pessoa_origem_id
            )
            SELECT f.pessoa_uuid,f.pessoa_origem_id,f.initial_uuid,f.canonical_uuid,f.estado,f.versao,
                   f.cpf,f.correction,f.feedback,f.conflict,f.prior_association,f.founder_proof,
                   po.pessoa_observacao_id,po.nome_completo,po.data_nascimento,po.nome_mae
              FROM origin_facts f
              JOIN silver.pessoa_observacao po ON po.pessoa_origem_id=f.pessoa_origem_id
             ORDER BY f.pessoa_uuid,f.pessoa_origem_id,po.pessoa_observacao_id;
            """, connection, snapshot) { CommandTimeout = 120 };
        command.Parameters.Add("@people", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(ids.OrderBy(x => x).ToArray());
        var facts = new Dictionary<(Guid Person, long Origin), (OriginFacts Facts,
            List<V9ControlledDevSecureEvidenceScore.Evidence> Records)>();
        var seen = new HashSet<(Guid Person, long Observation)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var person = reader.GetGuid(0);
            var origin = reader.GetInt64(1);
            var observation = reader.GetInt64(12);
            if (!ids.Contains(person) || origin <= 0 || observation <= 0
                || !seen.Add((person, observation)))
                throw new InvalidDataException("RUNNER_V9_DEV_B1_ORIGIN_CENSUS_INVALID");
            var key = (person, origin);
            if (!facts.TryGetValue(key, out var entry))
            {
                var records = new List<V9ControlledDevSecureEvidenceScore.Evidence>();
                var header = new OriginFacts(person, origin, reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), reader.GetInt64(5),
                    reader.GetInt32(6) == 1, reader.GetInt32(7) == 1, reader.GetInt32(8) == 1,
                    reader.GetInt32(9) == 1, reader.GetInt32(10) == 1, reader.GetInt32(11) == 1,
                    records.AsReadOnly());
                entry = (header, records);
                facts.Add(key, entry);
                byPerson[person].Add(header);
            }
            entry.Records.Add(new V9ControlledDevSecureEvidenceScore.Evidence(observation, origin,
                new V9SnapshotCandidate(person, reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : DateOnly.FromDateTime(reader.GetDateTime(14)),
                    reader.IsDBNull(15) ? null : reader.GetString(15))));
        }
        return Freeze(byPerson);
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<OriginFacts>> Freeze(
        Dictionary<Guid, List<OriginFacts>> source) =>
        new ReadOnlyDictionary<Guid, IReadOnlyList<OriginFacts>>(
            source.ToDictionary(x => x.Key, x => (IReadOnlyList<OriginFacts>)x.Value.AsReadOnly()));
}

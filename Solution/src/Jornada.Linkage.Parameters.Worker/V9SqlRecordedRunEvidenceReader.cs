using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jornada.Contracts;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>Terminal MODEL_VALIDATION run counters from HML, NOT a certified candidate universe.</summary>
public sealed record V9SqlRecordedRunHeader(
    Guid RunId, Guid ModelId, int ModelVersion,
    string RunType, string RunStatus,
    long EligibleObservations, long EvaluatedObservations,
    long ResolvedObservations, long Conflicts, long UnresolvedObservations,
    bool HasPublicationTimestamp);

/// <summary>
/// A PII-bearing in-memory diagnostic observation. Do not publish or log
/// ObservationId or the contained raw UUIDs; only the digest may be exported.
/// </summary>
public sealed record V9SqlRecordedRunOutcome(
    long ObservationId, ProbabilisticLinkageDecision FinalDecision);

public sealed record V9SqlRecordedRunEvidence(
    string Version,
    V9SqlRecordedRunHeader Header,
    IReadOnlyList<V9SqlRecordedRunOutcome> Outcomes,
    string CompleteFinalDecisionFingerprintSha256,
    bool HasCompleteCandidateUnion,
    bool OperationallyCertified);

/// <summary>
/// Read-only bridge to the REAL persisted Runner results in HML. It verifies
/// that the run is terminal, nonpublishing, MODEL_VALIDATION, and complete.
/// SQL stores only best/second UUIDs and scores. Therefore this evidence alone
/// CANNOT prove the complete SQL candidate union, base-identity attribution,
/// true labels or m/u compatibility and cannot be fed directly into
/// V9RecordedRunnerOutcomeBridge.Bind without an additional authenticated
/// candidate/source snapshot. Production/Development databases are rejected.
/// </summary>
public static class V9SqlRecordedRunEvidenceReader
{
    public const string Version = "V9_HML_SQL_TERMINAL_RUN_FINAL_DECISIONS_READ_ONLY_V1";

    internal const string EnvironmentProfileSql = """
        SELECT CONVERT(nvarchar(32),
            (SELECT value FROM sys.extended_properties
             WHERE class=0 AND name=N'Jornada.EnvironmentProfile'));
        """;

    internal const string CompletedResultsSql = """
        SELECT r.linkage_run_id,r.modelo_id,r.modelo_versao,r.tipo_run,r.status,
               r.registros_elegiveis,r.avaliados,r.resolvidos,r.conflitos,
               r.nao_resolvidos,r.publicado_em,
               x.pessoa_observacao_id,x.modelo_id,x.modelo_versao,
               x.pessoa_uuid_resolvido,x.melhor_candidato_uuid,
               x.score_melhor,x.segundo_candidato_uuid,x.score_segundo,
               x.margem,x.status,x.motivo
        FROM identidade.linkage_run AS r
        LEFT JOIN identidade.linkage_resultado AS x
          ON x.linkage_run_id=r.linkage_run_id
        WHERE r.linkage_run_id=@run
          AND r.tipo_run=N'MODEL_VALIDATION'
          AND r.status=N'CONCLUIDO_SEM_PUBLICACAO'
          AND r.publicado_em IS NULL
        ORDER BY x.pessoa_observacao_id;
        """;

    public static async Task<V9SqlRecordedRunEvidence> ReadCompletedHmlAsync(
        SqlConnection connection, Guid recordedRunId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (recordedRunId == Guid.Empty)
            throw new ArgumentException("Run HML precisa ter UUID.", nameof(recordedRunId));
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("A conexão SQL de leitura precisa estar aberta.");

        // Fail BEFORE touching identity data: database-resident marker, never
        // caller-provided environment variables or an assumed DB name.
        await using (var env = new SqlCommand(EnvironmentProfileSql, connection))
        {
            var profile = await env.ExecuteScalarAsync(ct);
            if (profile is not string value
                || !string.Equals(value, "HML", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Extração de decisões finais permitida somente em banco residente Jornada.EnvironmentProfile=HML.");
        }

        var records = new List<V9SqlRecordedRunOutcome>();
        V9SqlRecordedRunHeader? header = null;
        await using (var command = new SqlCommand(CompletedResultsSql, connection))
        {
            command.Parameters.Add("@run", SqlDbType.UniqueIdentifier).Value = recordedRunId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var current = new V9SqlRecordedRunHeader(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt64(5),
                    reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8),
                    reader.GetInt64(9), !reader.IsDBNull(10));
                if (header is null)
                    header = current;
                else if (current != header)
                    throw new InvalidDataException("Metadados do run variaram durante leitura HML.");

                if (reader.IsDBNull(11))
                    continue;
                if (reader.IsDBNull(16) || reader.IsDBNull(20))
                    throw new InvalidDataException("Outcome do Runner contém campos obrigatórios nulos.");
                if (!Enum.TryParse<ResolutionStatus>(reader.GetString(20), false, out var status)
                    || !Enum.IsDefined(status))
                    throw new InvalidDataException("Status de resultado SQL não pertence ao contrato Runner.");
                records.Add(new V9SqlRecordedRunOutcome(
                    reader.GetInt64(11),
                    new ProbabilisticLinkageDecision(
                        status,
                        reader.IsDBNull(14) ? null : reader.GetGuid(14),
                        reader.IsDBNull(15) ? null : reader.GetGuid(15),
                        reader.GetDecimal(16),
                        reader.IsDBNull(17) ? null : reader.GetGuid(17),
                        reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                        reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                        reader.GetGuid(12),
                        reader.IsDBNull(21) ? null : reader.GetString(21))));
                if (reader.GetGuid(12) != current.ModelId
                    || reader.GetInt32(13) != current.ModelVersion)
                    throw new InvalidDataException("Outcome pertence a outro modelo ou versão.");
            }
        }
        if (header is null)
            throw new InvalidDataException("Run HML informado não existe.");
        return ValidateCompletedHml(header, records);
    }

    /// <summary>
    /// Deterministic verifier exercised without SQL in unit tests. A
    /// complete count does NOT certify that per-observation candidates were
    /// exhaustive or that labels/identity bases are independently sampled.
    /// </summary>
    public static V9SqlRecordedRunEvidence ValidateCompletedHml(
        V9SqlRecordedRunHeader header,
        IReadOnlyList<V9SqlRecordedRunOutcome> records)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(records);
        if (header.RunId == Guid.Empty || header.ModelId == Guid.Empty
            || header.ModelVersion <= 0
            || header.RunType != "MODEL_VALIDATION"
            || header.RunStatus != "CONCLUIDO_SEM_PUBLICACAO"
            || header.HasPublicationTimestamp
            || header.EligibleObservations <= 0
            || header.EligibleObservations != header.EvaluatedObservations
            || header.ResolvedObservations < 0
            || header.Conflicts < 0 || header.UnresolvedObservations < 0
            || header.ResolvedObservations + header.Conflicts
                + header.UnresolvedObservations != header.EvaluatedObservations
            || records.Count != header.EvaluatedObservations
            || records.Any(x => x is null || x.ObservationId <= 0
                || x.FinalDecision is null
                || x.FinalDecision.ModeloId != header.ModelId
                || x.FinalDecision.MelhorScore is < 0m or > 1m
                || x.FinalDecision.SegundoScore is < 0m or > 1m
                || x.FinalDecision.Margem is < 0m
                || x.FinalDecision.PessoaUuidResolvido == Guid.Empty
                || x.FinalDecision.MelhorCandidatoUuid == Guid.Empty
                || x.FinalDecision.SegundoCandidatoUuid == Guid.Empty)
            || records.Select(x => x.ObservationId).Distinct().Count() != records.Count
            || records.LongCount(x => x.FinalDecision.Status == ResolutionStatus.RESOLVIDO)
                != header.ResolvedObservations
            || records.LongCount(x => x.FinalDecision.Status == ResolutionStatus.CONFLITO)
                != header.Conflicts
            || records.LongCount(x => x.FinalDecision.Status == ResolutionStatus.NAO_RESOLVIDO)
                != header.UnresolvedObservations)
            throw new InvalidDataException("Run HML final está incompleto, publicado ou com contagens/resultados inconsistentes.");

        foreach (var row in records)
        {
            var result = row.FinalDecision;
            if (!Enum.IsDefined(result.Status)
                || ((result.Status == ResolutionStatus.RESOLVIDO)
                    != (result.PessoaUuidResolvido is not null))
                || result.Status == ResolutionStatus.RESOLVIDO
                    && (result.MelhorCandidatoUuid != result.PessoaUuidResolvido
                        || result.MelhorCandidatoUuid is null)
                || result.MelhorCandidatoUuid is null
                    && (result.SegundoCandidatoUuid is not null
                        || result.MelhorScore != 0m)
                || result.MelhorCandidatoUuid is not null
                    && result.SegundoCandidatoUuid == result.MelhorCandidatoUuid
                || (result.SegundoCandidatoUuid is null)
                    != (result.SegundoScore is null)
                || result.SegundoScore > result.MelhorScore)
                throw new InvalidDataException("Resultado final pós-guards é incoerente com candidato e status.");
        }

        var canonical = new StringBuilder(Version).Append('\n')
            .Append(header.RunId.ToString("D")).Append('|')
            .Append(header.ModelId.ToString("D")).Append('|')
            .Append(header.ModelVersion).Append('|')
            .Append(header.RunType).Append('|').Append(header.RunStatus).Append('|')
            .Append(header.EvaluatedObservations).Append('|')
            .Append(header.ResolvedObservations).Append('|')
            .Append(header.Conflicts).Append('|')
            .Append(header.UnresolvedObservations).Append('\n');
        foreach (var row in records.OrderBy(x => x.ObservationId))
        {
            var d = row.FinalDecision;
            canonical.Append(row.ObservationId).Append('|')
                .Append(d.Status).Append('|')
                .Append(d.PessoaUuidResolvido?.ToString("D") ?? "-").Append('|')
                .Append(d.MelhorCandidatoUuid?.ToString("D") ?? "-").Append('|')
                .Append(d.MelhorScore.ToString("G29", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(d.SegundoCandidatoUuid?.ToString("D") ?? "-").Append('|')
                .Append(d.SegundoScore?.ToString("G29", CultureInfo.InvariantCulture) ?? "-")
                .Append('|')
                .Append(d.Margem?.ToString("G29", CultureInfo.InvariantCulture) ?? "-")
                .Append('|')
                .Append(d.Motivo ?? "-").Append('\n');
        }
        var digest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        return new V9SqlRecordedRunEvidence(
            Version, header, Array.AsReadOnly(records.OrderBy(x => x.ObservationId).ToArray()),
            digest, HasCompleteCandidateUnion: false, OperationallyCertified: false);
    }
}

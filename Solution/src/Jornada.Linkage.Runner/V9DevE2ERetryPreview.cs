using System.Data;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Nonpublishing diagnostic retry of still-indefinite origins on
/// post-FULL references. Same frozen b1, broad index, Silver grouping, r1.
/// No new run, truth, new identities or durable writes; not a second FULL.</summary>
internal static class V9DevE2ERetryPreview
{
    private sealed record Row(long Id, long Origin, int Version, string Manager, IdentityObservation Observation);
    internal sealed record Projection(long ObservationId, long OriginId, Guid? ProposedUuid);
    internal static async Task<IReadOnlyList<Projection>> ReadAsync(IOperationalSqlAdapter sql,
        V9FittedSnapshot winner, Guid attempt, Guid runId, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(ct);
        await V9DisposableAttemptDatabase.VerifyAsync(connection,attempt,ct);
        await V9ControlledDevSilverGraph.PrepareTempTableAsync(connection,ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Snapshot,ct);
        await V9DisposableAttemptDatabase.VerifyAsync(connection,attempt,ct,tx);
        await using (var gate = new SqlCommand("""
            SELECT CASE WHEN (SELECT COUNT_BIG(*) FROM identidade.linkage_run)=1
              AND EXISTS(SELECT 1 FROM identidade.linkage_run WHERE linkage_run_id=@run
                AND tipo_run=N'FULL' AND status=N'PUBLICADO' AND modelo_versao=9)
              THEN 1 ELSE 0 END;
            """,connection,tx))
        {
            gate.Parameters.Add("@run",SqlDbType.UniqueIdentifier).Value=runId;
            if (Convert.ToInt32(await gate.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture)!=1)
                throw new InvalidDataException("RUNNER_V9_E2E_RETRY_SINGLE_FULL_REQUIRED");
        }
        var rows = new List<Row>();
        await using (var query = new SqlCommand("""
            SELECT po.pessoa_observacao_id,po.pessoa_origem_id,po.versao_interna,g.codigo,
                   po.cpf_ausente_motivo,po.nome_completo,po.data_nascimento,po.nome_mae
            FROM silver.pessoa_observacao po
            JOIN ref.gestor g ON g.gestor_id=po.gestor_id
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            JOIN identidade.v_vinculo_corrente v ON v.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.cpf IS NULL AND p.estado=N'INDEFINIDA' AND v.status=N'NAO_RESOLVIDO'
              AND NOT EXISTS(SELECT 1 FROM silver.pessoa_observacao sibling
                JOIN identidade.v_vinculo_corrente protectedLink
                    ON protectedLink.pessoa_observacao_id=sibling.pessoa_observacao_id
                WHERE sibling.pessoa_origem_id=po.pessoa_origem_id AND
                protectedLink.metodo_resolucao IN(N'CPF_DETERMINISTICO',N'UUID_JORNADA_RETROALIMENTACAO',
                    N'CORRECAO_GOVERNADA',N'CONFLITO_GOVERNADO'))
            ORDER BY po.pessoa_observacao_id;
            """,connection,tx))
        {
            await using var reader=await query.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
            {
                string? Str(int i)=>reader.IsDBNull(i)?null:reader.GetString(i);
                rows.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetInt32(2),reader.GetString(3),
                    new IdentityObservation(null,Str(4),Str(5),reader.IsDBNull(6)?null:DateOnly.FromDateTime(reader.GetDateTime(6)),Str(7))));
            }
        }
        var planner=V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(),winner.ContentSha256);
        await V9OperationalBlockingManifest.ReadIsolatedAsync(connection,tx,planner,ct);
        var candidateIds=new Dictionary<long,IReadOnlyList<Guid>>();
        var candidates=new Dictionary<long,IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>();
        var noPass=new HashSet<long>();
        foreach(var row in rows)
        {
            if(planner.Plan(row.Observation).Count==0) noPass.Add(row.Id);
            var receipt=await V9OperationalCandidateRead.ReadAsync(connection,tx,planner,row.Observation,ct,true);
            candidateIds.Add(row.Id,receipt.CandidateUuids);
            // Translation uses UUID membership only; b1 evidence comes from origin reader.
            candidates.Add(row.Id,receipt.CandidateUuids.Select(x=>new V9ControlledDevOperationalCandidateSource.GoldCandidate(x,null,null,null)).ToArray());
        }
        var facts=await V9ControlledDevSecureOriginReader.ReadAsync(connection,tx,
            candidateIds.Values.SelectMany(x=>x).Distinct().ToArray(),ct);
        var graph=await V9ControlledDevSilverGraph.ReadAsync(connection,tx,winner,
            rows.Select(x=>new V9FrozenRunObservation(x.Id,x.Observation)).ToArray(),
            rows.ToDictionary(x=>x.Id,x=>x.Manager),ct);
        var cohort=rows.Select((x,i)=>new V9PhysicalCalibrationObservation(i+1,"DEV_UNLABELED_"+(i+1),"DEV",
            x.Manager,x.Observation.NomeCompleto,x.Observation.NomeMae,x.Observation.DataNascimento)).ToArray();
        var grouped=V9FrozenSourceResolveReplay.ReplayDev(winner,cohort,graph.Edges);
        var groups=rows.Select((x,i)=>(x.Id,Group:grouped.IdentityByObservation[i+1])).ToDictionary(x=>x.Id,x=>x.Group);
        var modelId=V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256);
        var decisions=rows.ToDictionary(x=>x.Id,x=>V9ControlledDevSecureEvidenceScore.ScoreFromOrigins(
            winner,modelId,x.Observation,candidateIds[x.Id],facts).Decision);
        var incident=graph.Edges.SelectMany(e=>new[]{rows[e.A-1].Id,rows[e.B-1].Id}).ToHashSet();
        var translated=V9ControlledDevGroupGoldR1.Translate(groups,decisions,candidates,new Dictionary<long,Guid>(),incident,noPass);
        var output=new List<Projection>();
        foreach(var origin in rows.GroupBy(x=>x.Origin))
        {
            var leader=origin.OrderByDescending(x=>x.Version).ThenByDescending(x=>x.Id).First();
            var decision=translated[leader.Id];
            Guid? target=decision.Status==ResolutionStatus.RESOLVIDO?decision.PessoaUuidResolvido:null;
            foreach(var row in origin) output.Add(new(row.Id,row.Origin,target));
        }
        await tx.CommitAsync(ct);
        return output.OrderBy(x=>x.ObservationId).ToArray();
    }
}

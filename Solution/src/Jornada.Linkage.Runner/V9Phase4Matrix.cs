using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jornada.Contracts;
using Jornada.Linkage.Parameters.Worker;
using Jornada.Operational.Sql;
using Microsoft.Data.SqlClient;

namespace Jornada.Linkage.Runner;

/// <summary>Phase 4 only: physically authenticated inputs, isolated in-memory
/// alternative states, frozen scorer/complete-link/r1. No mutation API.</summary>
internal static class V9Phase4Matrix
{
    private const string Sem = "V9_R1_INDEFINIDO_SEM_INDICIO";
    private const string Ind = "V9_R1_INDEFINIDO_COM_INDICIO";
    private sealed record Row(long Id,long Origin,int Version,int Wave,string Manager,
        IdentityObservation Observation,bool HasCpf,Guid Initial,Guid? Current,Guid? Canonical,
        string Status,string Method,string State,DateTimeOffset AsOf,DateTimeOffset? NameVerified,
        DateTimeOffset? BirthVerified,DateTimeOffset? MotherVerified);
    private sealed record Ref(Guid Person,string? Name,DateOnly? Birth,string? Mother);
    private sealed record Frame(Guid Attempt,Row[] Rows,Ref[] InitialRefs,double SnapshotMs,long VersionStoreStartBytes,
        long VersionStoreEndBytes,long ProcessMemoryBytes);
    private sealed record CellGraph(Row[] Pending,Dictionary<long,HashSet<BlockingProjectionKey>> Keys,
        Dictionary<long,Guid> Groups,HashSet<long> Incident,HashSet<long> NoPass,
        int Edges,double Milliseconds,string DirectionSha256,
        Dictionary<Guid,int> GroupSizes,Dictionary<Guid,decimal?> GroupMinPosterior);
    private sealed record Link(Guid Person,bool Secure,string Method,decimal? AssociationScore=null);
    private sealed record IndexEntry(Ref Summary,HashSet<BlockingProjectionKey> Keys,Row[] All,Row[] Secure);
    private sealed record Projection(long Id,Guid? Person,string RawStatus,string RawReason,string EffectiveReason,
        string Method,decimal Score,decimal? AssociationScore,int Fs,int Candidates,int SecureCandidates,int PriorCardinality,
        Guid? Best,int OnlyViaNonsecure,long Origin,int Wave,long LeaderId,int GroupSize,
        decimal? GroupMinPosterior,Guid[] CandidateIds,bool SecureOrigin);
    private sealed record RoundResult(Projection[] Rows,object Resources);
    private static void Require(bool ok,string reason)
    { if(!ok) throw new InvalidDataException("RUNNER_V9_PHASE4_"+reason); }
    private static void Admit() => Require(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"
        && Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_MATRIX")=="true"
        && Environment.GetEnvironmentVariable("JORNADA_RUNTIME_MODE")=="DEV"
        && (Environment.GetEnvironmentVariable("JORNADA_E2E_HTTP_VOLUME_COUNT")=="1000"||V9DevE2EFullCommand.ScaleAdmitted),"ADMISSION");
    private static JsonDocument Read(string root,string name)
    {
        var f=new FileInfo(Path.Combine(root,name));
        Require(f.Exists&&f.LinkTarget is null&&f.Length>0&&f.Length<=(V9DevE2EFullCommand.ScaleAdmitted?128L:32L)*1024*1024,"PRIVATE_INPUT");
        return JsonDocument.Parse(File.ReadAllBytes(f.FullName));
    }
    private static async Task WriteAsync(string root,string name,object data)
    {
        await using var stream=new FileStream(Path.Combine(root,name),FileMode.CreateNew,FileAccess.Write);
        await JsonSerializer.SerializeAsync(stream,data);
    }
    private static async Task<long> VersionStore(SqlConnection conn,SqlTransaction? tx,CancellationToken ct)
    {
        await using var c=new SqlCommand("SELECT COALESCE(SUM(CONVERT(bigint,reserved_page_count)),0)*8192 FROM sys.dm_tran_version_store_space_usage WHERE database_id=DB_ID();",conn,tx);
        return Convert.ToInt64(await c.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
    }
    internal static async Task<ProbabilisticLinkageRunSummary> MeasurePhysicalFullAsync(string root,
        IOperationalSqlAdapter sql,Guid attempt,Func<Task<ProbabilisticLinkageRunSummary>> run,
        Func<double[]?> timing,CancellationToken ct)
    {
        Admit();
        await using var conn=await sql.OpenAsync(ct);
        await V9DisposableAttemptDatabase.VerifyAsync(conn,attempt,ct);
        var sw=Stopwatch.StartNew();
        var samples=new List<object>();
        long peakStore=0,peakMemory=0;
        async Task Sample()
        {
            var bytes=await VersionStore(conn,null,ct);
            var memory=Environment.WorkingSet;
            peakStore=Math.Max(peakStore,bytes);peakMemory=Math.Max(peakMemory,memory);
            samples.Add(new {ms=sw.Elapsed.TotalMilliseconds,versionStoreBytes=bytes,workingSetBytes=memory});
        }
        await Sample();
        var task=run();
        while(!task.IsCompleted)
        {
            await Task.WhenAny(task,Task.Delay(1000,ct));
            await Sample();
        }
        var result=await task;sw.Stop();
        var t=timing();
        Require(t is {Length:5}&&t.All(x=>double.IsFinite(x)&&x>=0)&&t[0]>=t[1],"PHYSICAL_TIMING");
        var v=t!;
        await WriteAsync(root,"matrix-full-physical-resource.json",new {
            fullElapsedMs=sw.Elapsed.TotalMilliseconds,physicalReadMs=v[0],physicalSnapshotMs=v[1],
            silverGroupingMs=v[2],goldScoringMs=v[3],translationMs=v[4],
            remainingFullLifecycleMs=Math.Max(0,sw.Elapsed.TotalMilliseconds-v[0]-v[2]-v[3]-v[4]),
            sampledPeakVersionStoreBytes=peakStore,sampledPeakWorkingSetBytes=peakMemory,
            processPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
            samplingPeriodMs=1000,samples});
        return result;
    }
    internal static async Task CaptureAsync(string root,IOperationalSqlAdapter sql,Guid attempt,CancellationToken ct)
    {
        Admit();
        using var p=Read(root,"wave-preflight-receipt.json");
        var waves=p.RootElement.GetProperty("packagesByWave").EnumerateArray()
            .SelectMany(w=>w.GetProperty("packages").EnumerateArray().Select(x=>(
                Manager:x.GetProperty("manager").GetString()!,File:x.GetProperty("fileName").GetString()!,
                Wave:w.GetProperty("wave").GetInt32()))).ToDictionary(x=>(x.Manager,x.File),x=>x.Wave);
        Require(waves.Count==8,"WAVES");
        await using var conn=await sql.OpenAsync(ct);
        await V9DisposableAttemptDatabase.VerifyAsync(conn,attempt,ct);
        var start=await VersionStore(conn,null,ct);
        var sw=Stopwatch.StartNew();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Snapshot,ct);
        await V9DisposableAttemptDatabase.VerifyAsync(conn,attempt,ct,tx);
        var rows=new List<Row>();
        var refs=new List<Ref>();
        await using(var query=new SqlCommand("""
            SELECT po.pessoa_observacao_id,po.pessoa_origem_id,po.versao_interna,g.codigo,b.nome_arquivo,
                   po.cpf,po.cpf_ausente_motivo,po.nome_completo,po.data_nascimento,po.nome_mae,
                   p.initial_uuid,vc.pessoa_uuid,p.canonical_uuid,vc.status,vc.metodo_resolucao,p.estado,
                   po.source_as_of,
                   (SELECT MAX(v.verificado_em) FROM silver.pessoa_campo_verificacao_observacao v
                    WHERE v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=N'NOME_COMPLETO'),
                   (SELECT MAX(v.verificado_em) FROM silver.pessoa_campo_verificacao_observacao v
                    WHERE v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=N'DATA_NASCIMENTO'),
                   (SELECT MAX(v.verificado_em) FROM silver.pessoa_campo_verificacao_observacao v
                    WHERE v.pessoa_observacao_id=po.pessoa_observacao_id AND v.campo_codigo=N'NOME_MAE')
            FROM silver.pessoa_observacao po
            JOIN ref.gestor g ON g.gestor_id=po.gestor_id
            JOIN ingestao.lote l ON l.lote_id=po.lote_id
            JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
            JOIN identidade.pessoa_origem_progressiva p ON p.pessoa_origem_id=po.pessoa_origem_id
            JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id
            WHERE po.codigo_pessoa_origem LIKE N'SYNTH-%'
            ORDER BY po.pessoa_observacao_id;
            """,conn,tx))
        {
            await using var rd=await query.ExecuteReaderAsync(ct);
            while(await rd.ReadAsync(ct))
            {
                string? Str(int i)=>rd.IsDBNull(i)?null:rd.GetString(i);
                Guid? U(int i)=>rd.IsDBNull(i)?null:rd.GetGuid(i);
                DateTimeOffset? T(int i)=>rd.IsDBNull(i)?null:rd.GetDateTimeOffset(i);
                var manager=rd.GetString(3);
                var status=rd.GetString(13);
                var method=rd.GetString(14);
                Require(waves.TryGetValue((manager,rd.GetString(4)),out var wave)
                    && (status is "RESOLVIDO" or "NAO_RESOLVIDO")
                    && (status!="RESOLVIDO"||method=="CPF_DETERMINISTICO")
                    && method is not ("CORRECAO_GOVERNADA" or "CONFLITO_GOVERNADO" or "UUID_JORNADA_RETROALIMENTACAO" or "LINKAGE_PROBABILISTICO"),"FRAME_FIRST_FULL");
                rows.Add(new(rd.GetInt64(0),rd.GetInt64(1),rd.GetInt32(2),wave,manager,
                    new IdentityObservation(null,Str(6),Str(7),rd.IsDBNull(8)?null:DateOnly.FromDateTime(rd.GetDateTime(8)),Str(9)),
                    !rd.IsDBNull(5),rd.GetGuid(10),status=="RESOLVIDO"?U(11):null,U(12),status,method,rd.GetString(15),
                    rd.GetDateTimeOffset(16),T(17),T(18),T(19)));
            }
        }
        await using(var q=new SqlCommand("SELECT pessoa_uuid,nome_completo,data_nascimento,nome_mae FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';",conn,tx))
        {
            await using var rd=await q.ExecuteReaderAsync(ct);
            while(await rd.ReadAsync(ct)) refs.Add(new(rd.GetGuid(0),rd.IsDBNull(1)?null:rd.GetString(1),
                rd.IsDBNull(2)?null:DateOnly.FromDateTime(rd.GetDateTime(2)),rd.IsDBNull(3)?null:rd.GetString(3)));
        }
        var population=p.RootElement;
        Require(rows.Count==population.GetProperty("sourceVersions").GetInt32()
            && rows.Count(x=>x.HasCpf)==population.GetProperty("cpfVersions").GetInt32()
            && rows.Select(x=>x.Id).Distinct().Count()==rows.Count
            && rows.Select(x=>x.Origin).Distinct().Count()==population.GetProperty("persistentOrigins").GetInt32()
            && refs.Count>0 && (V9DevE2EFullCommand.ScaleAdmitted || (rows.Count==2349&&rows.Count(x=>x.HasCpf)==315
            && rows.Select(x=>x.Origin).Distinct().Count()==1957)),"FRAME_CENSUS");
        foreach(var origin in rows.GroupBy(x=>x.Origin))
        {
            Require(origin.Select(x=>x.Initial).Distinct().Count()==1
                && origin.Select(x=>x.Manager).Distinct().Count()==1
                && origin.Select(x=>x.HasCpf).Distinct().Count()==1
                && origin.Select(x=>x.Canonical).Distinct().Count()==1,"FRAME_ORIGIN");
            if(origin.First().HasCpf) Require(origin.All(x=>x.Current.HasValue&&x.Current==origin.First().Current),"FRAME_CPF");
        }
        var end=await VersionStore(conn,tx,ct);
        await tx.CommitAsync(ct);sw.Stop();
        await WriteAsync(root,"matrix-frame-private.json",new Frame(attempt,rows.ToArray(),refs.ToArray(),
            sw.Elapsed.TotalMilliseconds,start,end,Environment.WorkingSet));
    }
    private static Ref Summary(Guid person,Row[] rows)
    {
        Require(rows.Length>0,"SUMMARY_EMPTY");
        T? Pick<T>(Func<Row,T?> field,Func<Row,DateTimeOffset?> verified) =>
            rows.Where(x=>field(x) is not null).OrderByDescending(x=>verified(x).HasValue)
                .ThenByDescending(verified).ThenByDescending(x=>x.AsOf).ThenByDescending(x=>x.Id)
                .Select(field).FirstOrDefault();
        // Nullable date is a value type; keep its absence rather than default DateOnly.
        var birth=rows.Where(x=>x.Observation.DataNascimento.HasValue)
            .OrderByDescending(x=>x.BirthVerified.HasValue).ThenByDescending(x=>x.BirthVerified)
            .ThenByDescending(x=>x.AsOf).ThenByDescending(x=>x.Id).Select(x=>x.Observation.DataNascimento).FirstOrDefault();
        return new(person,Pick<string>(x=>x.Observation.NomeCompleto,x=>x.NameVerified),birth,
            Pick<string>(x=>x.Observation.NomeMae,x=>x.MotherVerified));
    }
    private static HashSet<BlockingProjectionKey> Keys(V9FittedSnapshotExecutionRuntime planner,IdentityObservation o)
        =>planner.Project(o).ToHashSet();
    private static IdentityObservation Obs(Ref r)=>new(null,"SEM_CPF",r.Name,r.Birth,r.Mother);
    private static Dictionary<Guid,IndexEntry> Index(Row[] visible,Dictionary<long,Link> links,
        V9FittedSnapshotExecutionRuntime planner,bool secureOnly)
    {
        var index=new Dictionary<Guid,IndexEntry>();
        foreach(var group in visible.Where(x=>links.ContainsKey(x.Origin)).GroupBy(x=>links[x.Origin].Person))
        {
            var all=group.OrderBy(x=>x.Id).ToArray();
            var secure=all.Where(x=>links[x.Origin].Secure).ToArray();
            var used=secureOnly?secure:all;
            if(used.Length==0)continue;
            var summary=Summary(group.Key,used);
            var keys=Keys(planner,Obs(summary));
            foreach(var row in used)
                keys.UnionWith(Keys(planner,row.Observation with {DataNascimento=null}).Where(k=>
                    planner.TemporalSemantics(k.Feature)==BlockingFeatureTemporalSemantics.VersionedAlias));
            index.Add(group.Key,new(Summary(group.Key,all),keys,all,secure));
        }
        return index;
    }
    // Equivalent union-of-passes, intersection-of-fields lookup. No changes
    // to passes, feature values or temporal semantics; independent physical
    // candidate membership is rechecked for every baseline source below.
    private static Func<HashSet<BlockingProjectionKey>,Guid[]> Lookup(
        Dictionary<Guid,IndexEntry> index,IReadOnlyList<LinkageBlockingPass> passes)
    {
        var byKey=new Dictionary<BlockingProjectionKey,HashSet<Guid>>();
        foreach(var (person,entry) in index)
        foreach(var key in entry.Keys)
        {
            if(!byKey.TryGetValue(key,out var people))byKey.Add(key,people=new HashSet<Guid>());
            people.Add(person);
        }
        return source=>
        {
            var result=new HashSet<Guid>();
            foreach(var pass in passes)
            {
                HashSet<Guid>? hits=null;
                foreach(var field in pass.Fields)
                {
                    var fieldHits=new HashSet<Guid>();
                    foreach(var key in source.Where(k=>k.Feature==field))
                        if(byKey.TryGetValue(key,out var values))fieldHits.UnionWith(values);
                    if(hits is null)hits=fieldHits;else hits.IntersectWith(fieldHits);
                    if(hits.Count==0)break;
                }
                if(hits is not null)result.UnionWith(hits);
            }
            return result.OrderBy(x=>x).ToArray();
        };
    }
    private static CellGraph Graph(V9FittedSnapshot winner,V9FittedSnapshotExecutionRuntime planner,Row[] visible)
    {
        var sw=Stopwatch.StartNew();
        var pending=visible.Where(x=>!x.HasCpf).OrderBy(x=>x.Id).ToArray();
        var keys=pending.ToDictionary(x=>x.Id,x=>Keys(planner,x.Observation));
        var passes=winner.Payload.BlockingPasses;
        var edges=new List<(int A,int B)>();
        for(var i=0;i<pending.Length;i++)
        for(var j=i+1;j<pending.Length;j++)
            if(pending[i].Manager!=pending[j].Manager&&V9DevE2EFirstFullMembershipOracle.MatchesAnyPass(keys[pending[i].Id],keys[pending[j].Id],passes))
                edges.Add((i+1,j+1));
        var cohort=pending.Select((x,i)=>new V9PhysicalCalibrationObservation(i+1,"DEV_UNLABELED_"+(i+1),"DEV",x.Manager,
            x.Observation.NomeCompleto,x.Observation.NomeMae,x.Observation.DataNascimento)).ToArray();
        IReadOnlyList<V9DevDirectionalScore>? dirs=null;
        var grouped=V9FrozenSourceResolveReplay.ReplayDev(winner,cohort,edges,x=>dirs=x);
        var groups=pending.Select((x,i)=>(x.Id,Group:grouped.IdentityByObservation[i+1])).ToDictionary(x=>x.Id,x=>x.Group);
        var incident=edges.SelectMany(e=>new[]{pending[e.A-1].Id,pending[e.B-1].Id}).ToHashSet();
        var noPass=pending.Where(x=>planner.Plan(x.Observation).Count==0).Select(x=>x.Id).ToHashSet();
        var sizes=groups.Values.GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count());
        var internalDirections=dirs!.Where(d=>groups[pending[d.Source-1].Id]==groups[pending[d.Target-1].Id])
            .GroupBy(d=>groups[pending[d.Source-1].Id]).ToDictionary(x=>x.Key,x=>x.ToArray());
        var minimum=sizes.ToDictionary(x=>x.Key,x=>x.Value==1?(decimal?)null:
            internalDirections[x.Key].Min(d=>d.Posterior));
        Require(sizes.All(x=>x.Value==1|| (internalDirections[x.Key].Length==x.Value*(x.Value-1)
            && internalDirections[x.Key].All(d=>d.Posterior.HasValue))),"GROUP_INTERNAL_CENSUS");
        return new(pending,keys,groups,incident,noPass,edges.Count,sw.Elapsed.TotalMilliseconds,
            V9DevE2EGroupOracle.DirectionDigest(dirs!),sizes,minimum);
    }
    private static Guid NewGroup(string trajectory,Guid group)
    {
        var bytes=SHA256.HashData(Encoding.UTF8.GetBytes("V9_PHASE4_EXPERIMENT/"+trajectory+"/"+group.ToString("D")));
        return new Guid(bytes.AsSpan(0,16));
    }
    private static RoundResult Round(V9FittedSnapshot winner,V9FittedSnapshotExecutionRuntime planner,Row[] visible,
        CellGraph graph,Dictionary<long,Link> links,string trajectory,string s4b,string evidence,string prior,string policy,string blocking,string groupSafety,
        JsonElement? physical,JsonElement? diagnostic,JsonElement? actualGraph)
    {
        var sw=Stopwatch.StartNew();var samples=new List<object>();
        void Sample(string phase)=>samples.Add(new {phase,ms=sw.Elapsed.TotalMilliseconds,workingSetBytes=Environment.WorkingSet,managedBytes=GC.GetTotalMemory(false)});
        // New arrivals from an existing origin inherit its established identity.
        foreach(var origin in visible.Where(x=>x.HasCpf).GroupBy(x=>x.Origin))
        {
            var id=origin.First().Current!.Value;
            if(links.TryGetValue(origin.Key,out var priorLink)) Require(priorLink.Person==id&&priorLink.Secure,"CPF_DRIFT");
            else links.Add(origin.Key,new(id,true,"CPF"));
        }
        var allIndex=Index(visible,links,planner,false);
        var secureIndex=Index(visible,links,planner,true);
        var chosen=blocking=="secure"?secureIndex:allIndex;
        var broadLookup=Lookup(allIndex,winner.Payload.BlockingPasses);
        var secureLookup=Lookup(secureIndex,winner.Payload.BlockingPasses);
        Sample("index");
        var model=V9OperationalSqlModelConverter.ModelIdFromWinnerSha(winner.ContentSha256);
        var raw=new Dictionary<long,ProbabilisticLinkageDecision>();
        var candidates=new Dictionary<long,IReadOnlyList<V9ControlledDevOperationalCandidateSource.GoldCandidate>>();
        var established=graph.Pending.Where(x=>links.ContainsKey(x.Origin)).ToDictionary(x=>x.Id,x=>links[x.Origin].Person);
        var metadata=new Dictionary<long,(int Fs,int Candidates,int SecureCandidates,int PriorCount,int Only)>();
        var physicalScores=physical.HasValue?new List<V9ControlledDevCohortScorer.GoldScore>():null;
        foreach(var row in graph.Pending)
        {
            var key=graph.Keys[row.Id];
            var broad=broadLookup(key);var restricted=secureLookup(key);
            var people=blocking=="secure"?restricted:broad;
            candidates.Add(row.Id,people.Select(x=>new V9ControlledDevOperationalCandidateSource.GoldCandidate(x,chosen[x].Summary.Name,chosen[x].Summary.Birth,chosen[x].Summary.Mother)).ToArray());
            var records=new List<V9ControlledDevSecureEvidenceScore.Evidence>();
            foreach(var person in people)
            {
                var entry=chosen[person];
                if(evidence=="summary")
                {
                    var r=entry.All[0];var s=entry.Summary;
                    records.Add(new(r.Id,r.Origin,new V9SnapshotCandidate(person,s.Name,s.Birth,s.Mother)));
                }
                else foreach(var r in evidence=="b1"?entry.Secure:entry.All)
                    records.Add(new(r.Id,r.Origin,new V9SnapshotCandidate(person,r.Observation.NomeCompleto,
                        r.Observation.DataNascimento,r.Observation.NomeMae)));
            }
            var scored=V9ControlledDevSecureEvidenceScore.Score(winner,model,row.Observation,people,records,
                observe:physicalScores is null?null:values=>physicalScores.AddRange(values.Select(v=>new V9ControlledDevCohortScorer.GoldScore(row.Id,v.ObservationId,v.Person,v.Posterior,v.LogOdds))));
            raw.Add(row.Id,scored.Decision);
            metadata.Add(row.Id,((int)scored.EvidenceReceipt.ComparedRecords,people.Length,restricted.Length,
                prior=="records"?records.Count:people.Length,broad.Except(restricted).Count()));
        }
        Sample("score");
        var translated=V9ControlledDevGroupGoldR1.Translate(graph.Groups,raw,candidates,established,graph.Incident,graph.NoPass);
        if(physical.HasValue)
        {
            Require(actualGraph.HasValue&&diagnostic.HasValue&&graph.DirectionSha256==actualGraph.Value.GetProperty("directionSha256").GetString()
                && V9DevE2EFirstFullMembershipOracle.GoldScoreDigest(physicalScores!)==actualGraph.Value.GetProperty("goldRecordScoreSha256").GetString(),"BASELINE_SCORES");
            var d=diagnostic.GetValueOrDefault().EnumerateArray().ToDictionary(x=>x.GetProperty("observationId").GetInt64());
            var roots=graph.Groups.GroupBy(x=>x.Value).SelectMany(g=>g.Select(x=>(x.Key,Root:g.Min(y=>y.Key)))).ToDictionary(x=>x.Key,x=>x.Root);
            var ordinal=graph.Pending.Select((x,i)=>(x.Id,i)).ToDictionary(x=>x.Id,x=>x.i);
            foreach(var row in graph.Pending)
            {
                var expected=d[row.Id];
                Require(expected.GetProperty("groupRoot").GetInt32()==ordinal[roots[row.Id]]
                    && expected.GetProperty("rankReason").GetString()==translated[row.Id].Motivo
                    && candidates[row.Id].Select(x=>x.PessoaUuid).OrderBy(x=>x).SequenceEqual(expected.GetProperty("candidates").EnumerateArray().Select(x=>x.GetGuid()).OrderBy(x=>x)),"BASELINE_GROUP_R1");
            }
        }
        var proposed=new Dictionary<long,Link>();
        foreach(var group in graph.Groups.GroupBy(x=>x.Value))
        {
            var ids=group.Select(x=>x.Key).ToArray();
            var targets=ids.Where(established.ContainsKey).Select(x=>established[x])
                .Concat(ids.Where(x=>translated[x].Status==ResolutionStatus.RESOLVIDO).Select(x=>translated[x].PessoaUuidResolvido!.Value)).Distinct().ToArray();
            if(s4b=="ii"&&ids.Length>1&&targets.Length==0&&ids.All(x=>translated[x].Motivo==Sem&&candidates[x].Count==0))
            {
                var id=NewGroup(trajectory,group.Key);
                Require(!allIndex.ContainsKey(id)&&!visible.Any(x=>x.Initial==id),"GROUP_ID_COLLISION");
                foreach(var x in ids)proposed[x]=new(id,false,"S4B_GROUP");
            }
            foreach(var x in ids)
            {
                var t=translated[x];
                if(t.Status==ResolutionStatus.RESOLVIDO)proposed[x]=new(t.PessoaUuidResolvido!.Value,false,"FS_ASSOCIATION",t.MelhorScore);
                else if(policy=="r2"&&targets.Length==1&&t.Status==ResolutionStatus.NAO_RESOLVIDO&&t.Motivo==Ind&&!established.ContainsKey(x))
                    proposed[x]=new(targets[0],false,"R2_INHERITANCE");
            }
        }
        var additions=new Dictionary<long,Link>();
        foreach(var origin in graph.Pending.GroupBy(x=>x.Origin))
        {
            if(links.ContainsKey(origin.Key))continue;
            var leader=origin.OrderByDescending(x=>x.Version).ThenByDescending(x=>x.Id).First();
            if(proposed.TryGetValue(leader.Id,out var target))
                additions.Add(origin.Key,target);
            else if(translated[leader.Id].Status==ResolutionStatus.NAO_RESOLVIDO
                && translated[leader.Id].Motivo?.StartsWith("SEM_CANDIDATO_",StringComparison.Ordinal)==true
                && origin.All(x=>candidates[x.Id].Count==0))
                additions.Add(origin.Key,new(leader.Initial,true,"SINGLETON_FOUNDER"));
        }
        // Authority is hypothetical only. Existing or later-associated origins
        // never gain security here. One founder retains the 1k definition.
        Require(groupSafety is "leader" or "all","GROUP_SECURITY_VARIANT");
        foreach(var group in additions.Where(x=>x.Value.Method=="S4B_GROUP").GroupBy(x=>x.Value.Person).ToArray())
        {
            var origin=group.Min(x=>x.Key);
            foreach(var member in group)
                if(member.Key==origin||groupSafety=="all")
                    additions[member.Key]=member.Value with {Secure=true,Method=member.Key==origin?
                        "S4B_HYPOTHETICAL_FOUNDER":"S4B_HYPOTHETICAL_SECURE_MEMBER"};
        }
        foreach(var x in additions)links.Add(x.Key,x.Value);
        Sample("project");
        var leaderByOrigin=graph.Pending.GroupBy(x=>x.Origin).ToDictionary(x=>x.Key,x=>x.OrderByDescending(y=>y.Version).ThenByDescending(y=>y.Id).First().Id);
        var output=visible.Select(x=>
        {
            links.TryGetValue(x.Origin,out var link);
            var t=translated.GetValueOrDefault(x.Id);
            var m=metadata.GetValueOrDefault(x.Id);
            var rawReason=t?.Motivo??(x.HasCpf?"CPF":"RESOLVIDO");
            var leaderScore=x.HasCpf?0m:translated[leaderByOrigin[x.Origin]].MelhorScore;
            var effective=link is null?translated[leaderByOrigin[x.Origin]].Motivo??"SEM_MOTIVO":
                link.Method;
            return new Projection(x.Id,link?.Person,t?.Status.ToString()??"CPF",rawReason,effective,link?.Method??"INDEFINIDA",
                leaderScore,link?.AssociationScore,m.Fs,m.Candidates,m.SecureCandidates,m.PriorCount,t?.MelhorCandidatoUuid,m.Only,
                x.Origin,x.Wave,x.HasCpf?x.Id:leaderByOrigin[x.Origin],
                x.HasCpf?1:graph.GroupSizes[graph.Groups[x.Id]],x.HasCpf?null:graph.GroupMinPosterior[graph.Groups[x.Id]],
                Environment.GetEnvironmentVariable("JORNADA_V9_PHASE4_SCALE") is "audit1k" or "comparable" or "challenge"
                    ? candidates.GetValueOrDefault(x.Id)?.Select(c=>c.PessoaUuid).ToArray()??[] : [],
                link?.Secure??false);
        }).ToArray();
        if(physical.HasValue)
        {
            var actual=physical.Value.EnumerateArray().ToDictionary(x=>x.GetProperty("observationId").GetInt64());
            Require(actual.Count==output.Length&&output.All(x=>
            {
                var a=actual[x.Id];return x.Person==(a.GetProperty("currentStatus").GetString()=="RESOLVIDO"?a.GetProperty("currentUuid").GetGuid():(Guid?)null);
            }),"BASELINE_PUBLICATION");
        }
        var keyBytes=chosen.Sum(x=>x.Value.Keys.Sum(k=>16L+Encoding.UTF8.GetByteCount(k.Feature)+Encoding.UTF8.GetByteCount(k.Value)+2));
        Sample("complete");
        return new(output,new {roundMs=sw.Elapsed.TotalMilliseconds,graphSharedMs=graph.Milliseconds,graphEdges=graph.Edges,
            fsCompared=metadata.Values.Sum(x=>(long)x.Fs),secureRecordsPerPerson=allIndex.Values.Select(x=>x.Secure.Length).OrderBy(x=>x).ToArray(),
            keysPerPerson=chosen.Values.Select(x=>x.Keys.Count).OrderBy(x=>x).ToArray(),logicalIndexBytes=keyBytes,
            memorySamples=samples,processPeakWorkingSetBytes=Process.GetCurrentProcess().PeakWorkingSet64,
            variantSqlExecuted=false,variantSnapshotMs=0,variantTempdbVersionStoreBytes=0});
    }
    internal static async Task ExecuteAsync(string root,IOperationalSqlAdapter sql,V9FittedSnapshot winner,Guid attempt,CancellationToken ct)
    {
        Admit();
        using var f=Read(root,"matrix-frame-private.json");
        var frame=f.RootElement.Deserialize<Frame>()!;
        using var pre=Read(root,"wave-preflight-receipt.json");
        Require(frame.Attempt==attempt&&frame.Rows.Length==pre.RootElement.GetProperty("sourceVersions").GetInt32()
            &&(V9DevE2EFullCommand.ScaleAdmitted||frame.Rows.Length==2349)
            &&winner.Payload.Parameters.GetValueOrDefault(LinkageParameterCatalog.DecisionEvidenceScoring)==1m,"EXEC_FRAME");
        using var plan=Read(root,"phase4-matrix-bound-plan.json");
        var p=plan.RootElement;
        Require(p.GetProperty("attempt").GetGuid()==attempt&&p.GetProperty("status").GetString()=="S4B_VERIFIED_MATRIX_PLANNED_ONLY"
            &&p.GetProperty("firstContrast").GetProperty("status").GetString()=="MEASURED"
            &&p.GetProperty("cells").GetArrayLength()==(V9DevE2EFullCommand.ScaleAdmitted?72:48),"EXEC_GATE");
        using var census=Read(root,"sql-census.json");using var diag=Read(root,"fn-private.json");using var actualGraph=Read(root,"actual-graph-receipt.json");
        var planner=V9FittedSnapshotExecutionRuntime.Load(winner.SerializeUtf8(),winner.ContentSha256);
        var initial=frame.Rows.Where(x=>x.HasCpf).GroupBy(x=>x.Origin).ToDictionary(x=>x.Key,x=>new Link(x.First().Current!.Value,true,"CPF"));
        var computed=Index(frame.Rows,initial,planner,false);
        Require(computed.Count==frame.InitialRefs.Length&&frame.InitialRefs.All(x=>computed.TryGetValue(x.Person,out var z)&&z.Summary==x),"INITIAL_SUMMARY");
        var visible=new[]{Array.Empty<Row>(),frame.Rows.Where(x=>x.Wave==1).ToArray(),frame.Rows};
        var graphs=new[]{null,Graph(winner,planner,visible[1]),Graph(winner,planner,visible[2])};
        await using var conn=await sql.OpenAsync(ct);
        await V9DisposableAttemptDatabase.VerifyAsync(conn,attempt,ct);
        long indexBytes;
        await using(var q=new SqlCommand("SELECT COALESCE(SUM(CONVERT(bigint,reserved_page_count)),0)*8192 FROM sys.dm_db_partition_stats WHERE object_id=OBJECT_ID(N'identidade.blocking_chave_v9');",conn))
            indexBytes=Convert.ToInt64(await q.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture);
        var store=await VersionStore(conn,null,ct);
        using var fullResource=Read(root,"matrix-full-physical-resource.json");
        await WriteAsync(root,"matrix-physical-resource.json",new {physicalFull=fullResource.RootElement,frame.SnapshotMs,frame.VersionStoreStartBytes,frame.VersionStoreEndBytes,
            postFullVersionStoreBytes=store,physicalBaselineIndexReservedBytes=indexBytes,frame.ProcessMemoryBytes,
            resourceScope="PHYSICAL_FULL_BASELINE_AND_CAPTURE_POST_FULL"});
        await using var output=new StreamWriter(new FileStream(Path.Combine(root,"matrix-private.jsonl"),FileMode.CreateNew,FileAccess.Write));
        var baselineDone=false;var total=0;
        foreach(var cell in p.GetProperty("cells").EnumerateArray())
        {
            var factors=cell.GetProperty("factors");string F(string name)=>factors.GetProperty(name).GetString()!;
            foreach(var t in cell.GetProperty("trajectories").EnumerateArray())
            {
                var links=new Dictionary<long,Link>();
                var trajectory=t.GetProperty("trajectoryId").GetString()!;
                foreach(var s in t.GetProperty("rounds").EnumerateArray())
                {
                    var wave=s.GetProperty("throughWave").GetInt32();
                    var baseline=cell.GetProperty("officialBaseline").GetBoolean()&&t.GetProperty("schedule").GetString()=="single";
                    Require(baselineDone||baseline,"BASELINE_MUST_BE_FIRST");
                    var res=Round(winner,planner,visible[wave],graphs[wave]!,links,trajectory+"/"+wave.ToString(CultureInfo.InvariantCulture),F("s4b"),F("evidence"),F("prior"),F("policy"),F("blocking"),
                        factors.TryGetProperty("groupSecurity",out var gs)?gs.GetString()!:"leader",
                        baseline?census.RootElement:null,baseline?diag.RootElement:null,baseline?actualGraph.RootElement:null);
                    if(baseline)baselineDone=true;
                    await output.WriteLineAsync(JsonSerializer.Serialize(new {cell=cell.GetProperty("cell").GetString(),schedule=t.GetProperty("schedule").GetString(),
                        stateId=s.GetProperty("stateId").GetString(),previousStateId=s.GetProperty("previousStateId").ValueKind==JsonValueKind.Null?null:s.GetProperty("previousStateId").GetString(),
                        throughWave=wave,round=s.GetProperty("round").GetInt32(),baselineExactParity=baseline,
                        rows=res.Rows,resources=res.Resources}));
                    total++;
                }
            }
        }
        Require(total==(V9DevE2EFullCommand.ScaleAdmitted?216:144)&&baselineDone,"EXEC_CENSUS");
        Console.WriteLine(V9DevE2EFullCommand.ScaleAdmitted?
            "V9_PHASE4_MATRIX_72_CELLS_216_STATES_NONPUBLISHING":"V9_PHASE4_MATRIX_48_CELLS_144_STATES_NONPUBLISHING");
    }
}

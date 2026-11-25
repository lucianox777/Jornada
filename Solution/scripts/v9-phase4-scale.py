#!/usr/bin/env python3
"""PO-authorized independent DEV scale experiment. No truth is given to the runner."""
import argparse, hashlib, importlib.util, itertools, json, math, os, shutil, uuid
from collections import Counter, defaultdict
from pathlib import Path

def module(name,file):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file))
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

M=module("matrix","v9-phase4-matrix-oracle.py")
I=M.I;P=M.G.P;S=module("s4b","v9-phase4-s4b-1k.py")
W=module("wave","v9-e2e-1k-wave-preflight.py")
H=module("http","v9-e2e-1k-http.py")
require=I.require
PROFILES={"audit1k":(1000,355,"clean"),"diagnostic1k":(1000,355,"clean"),
          "diagnostic2k":(2000,355,"clean"),"diagnostic4k":(4000,355,"clean"),"optimized1k":(1000,355,"clean"),
          "comparable":(10000,355,"clean"),"challenge":(10000,7311026,"field")}
DIAGNOSTIC_PROFILES=("diagnostic1k","diagnostic2k","diagnostic4k","optimized1k")
CAP=8
LIMIT=.001446
def profile():
    label=os.environ["JORNADA_V9_PHASE4_SCALE"]
    require((label=="audit1k" or (label in DIAGNOSTIC_PROFILES and
                os.environ.get("JORNADA_V9_FULL_DIAGNOSTIC")=="true"))
            and label in PROFILES and os.environ.get("GITHUB_ACTIONS")=="true"
            and os.environ.get("GITHUB_REPOSITORY")=="lucianox777/Jornada"
            and os.environ.get("GITHUB_EVENT_NAME")=="pull_request"
            and os.environ.get("JORNADA_RUNTIME_MODE")=="DEV"
            and os.environ.get("JORNADA_E2E_HTTP_VOLUME_COUNT")==str(PROFILES[label][0]),"SCALE_SCOPE")
    return label,PROFILES[label]
def write(path,x):
    with path.open("x",encoding="utf-8") as f:json.dump(x,f,sort_keys=True,allow_nan=False)
def read(path):return I.load(I.read(path))
def preflight(root):
    label,(n,seed,error)=profile()
    # Only this isolated process broadens bounded fixture parsing; 1k defaults untouched.
    W.BRIDGE.PEOPLE=n;W.BRIDGE.SEED=seed
    W.BRIDGE.MAX_SOURCE_CSV_BYTES=128*1024*1024
    W.BRIDGE.MAX_TRUTH_BYTES=128*1024*1024
    W.BRIDGE.MAX_PERSON_MEMBER_BYTES=32*1024*1024
    manifest=W.BRIDGE.read_json(root/"corpus"/"generation-manifest.json")
    require(manifest["seed"]==seed and manifest["error_profile"]==error,"SCALE_GENERATION_PROFILE")
    pre=W.verify(root)
    require(pre["baseIdentities"]==n and pre["waveCount"]==2
            and len(pre["packagesByWave"])==2 and all(len(x["packages"])==4 for x in pre["packagesByWave"])
            and pre["sourceVersions"]<=n*CAP,"SCALE_WAVE_CAP")
    # Preserve exactly original person-only ZIPs; no facts or truth inside packages.
    for wave in pre["packagesByWave"]:
        folder=root/"ingestion"/f"wave-{wave['wave']:02}"
        target=folder/"delivery";target.mkdir(mode=0o700)
        for name in ["bridge-manifest.json","bridge-truth.jsonl",*[x["fileName"] for x in wave["packages"]]]:
            require(Path(name).name==name,"SCALE_FILE")
            shutil.copyfile(folder/name,target/name)
    pre.update(scaleProfile=label,seed=seed,errorProfile=error,sourceFingerprint=manifest["input_fingerprint_sha256"],
               independentGeneration=True,campaign38PopulationUsed=False)
    write(root/"delivery-preflight-receipt.json",pre)

def truth_frame(root,n):
    census=read(root/"sql-census.json")
    waves=[];keys={}
    for i in (1,2):
        d=root/"fixture"/"ingestion"/f"wave-{i:02}"/"delivery"
        raw=I.read(d/"bridge-truth.jsonl")
        waves.append((read(d/"bridge-manifest.json"),raw))
        for line in raw.splitlines():
            x=I.load(line);k=I.version_key(x["GestorCodigo"],x["PackageFileName"],x["OpaquePersonId"])
            require(k not in keys,"SCALE_TRUTH_DUPLICATE");keys[k]=(x["BasePersonId"],i)
    identity=I.compare(waves,census,read(root/"runner-receipt.json"),required_identities=n)
    truth={}
    for x in census:
        t,i=keys[I.version_key(x["manager"],x["package"],x["opaque"])]
        truth[x["observationId"]]=dict(base=t,wave=i,manager=x["manager"],origin=x["originId"],cpf=x["hasCpf"])
    require(len(truth)==len(census) and len({v["base"] for v in truth.values()})==n
            and max(Counter(v["base"] for v in truth.values()).values())<=CAP
            and max(Counter((v["base"],v["manager"],v["wave"]) for v in truth.values()).values())==1,"SCALE_TRUTH_CAP")
    return census,identity,truth
def plan(root):
    label,(n,seed,error)=profile()
    require(label not in DIAGNOSTIC_PROFILES,"SCALE_DIAGNOSTIC_ONLY")
    I.MAX_BYTES=128*1024*1024
    pre=read(root/"wave-preflight-receipt.json");lineage=read(root/"http-lineage.json")
    graph=read(root/"actual-graph-receipt.json");member=read(root/"membership-receipt.json")
    census,identity,truth=truth_frame(root,n)
    require(pre["scaleProfile"]==label and pre["baseIdentities"]==n
        and lineage["preflightSha256"]==hashlib.sha256((root/"wave-preflight-receipt.json").read_bytes()).hexdigest()
        and lineage["attempt"]==os.environ["JORNADA_V9_DEV_ATTEMPT"]
        and identity["runId"]==graph["runId"] and identity["sourceVersions"]==lineage["sourceVersions"]==pre["sourceVersions"]
        and member["status"]=="PASS" and all(member[k] is True for k in ("actualConsumedSilverEdgesVerified",
        "actualConsumedScoresVerified","actualConsumedGroupsVerified","actualGoldRecordScoresVerified","independentR1DecisionVerified")),"SCALE_PHYSICAL_GATE")
    # A measured bad identity is a result, not a permission to ignore a failed integrity gate.
    write(root/"identity-receipt.json",identity)
    diag=read(root/"fn-private.json")
    contrast=S.contrast(census,diag,{k:v["base"] for k,v in truth.items()})
    base=contrast["variants"]["official_indefinite"]
    require(all(base[k]==identity["pairMetrics"][k] for k in ("tp","fp","fn","truePairs")),"SCALE_S4B_BASE")
    if label=="audit1k":
        require((base["tp"],base["fp"],base["fn"])==(563,0,1842),"SCALE_1K_REPRODUCTION")
    write(root/"s4b-receipt.json",contrast)
    binding=P.sha(P.canonical({"preflight":pre,"truth":identity["truthSha256ByWave"],"lineage":lineage}))
    attempt=uuid.UUID(lineage["attempt"]);cells=[]
    for v in itertools.product(*(x[1] for x in P.FACTORS)):
        for security in (("leader","all") if v[0]=="ii" and n==10000 else ("leader",)):
            cell="-".join(v)+("-all" if security=="all" else "")
            factors=dict(zip((x[0] for x in P.FACTORS),v));factors["groupSecurity"]=security
            trajectories=[]
            for schedule,rounds in P.SCHEDULES:
                ns=uuid.uuid5(attempt,cell+"/"+schedule);st=[];previous=None
                for k,wave in enumerate(rounds,1):
                    sid=str(uuid.uuid5(ns,str(k)));st.append(dict(round=k,throughWave=wave,stateId=sid,previousStateId=previous));previous=sid
                trajectories.append(dict(schedule=schedule,trajectoryId=str(ns),rounds=st,fixtureBinding=binding,publication=False))
            cells.append(dict(cell=cell,factors=factors,officialBaseline=v==P.BASELINE,trajectories=trajectories))
    require(len(cells)==(48 if n==1000 else 72),"SCALE_PLAN_COUNT")
    write(root/"phase4-matrix-bound-plan.json",dict(status="S4B_VERIFIED_MATRIX_PLANNED_ONLY",attempt=str(attempt),
          fixtureBinding=binding,cells=cells,firstContrast=dict(status="MEASURED",receiptSha256=P.sha((root/"s4b-receipt.json").read_bytes())),
          population=n,scaleProfile=label,scientificTest=False,publication=False))
def cp_upper(n,k,alpha):
    require(n>=2 and 0<=k<=n and 0<alpha<1,"SCALE_CP")
    if k==n:return 1.
    if k==0:return -math.expm1(math.log(alpha)/n)
    def logcdf(p):
        a=n*math.log1p(-p);terms=[a];ratio=math.log(p)-math.log1p(-p)
        for i in range(1,k+1):
            a+=math.log(n-i+1)-math.log(i)+ratio;terms.append(a)
        m=max(terms);return m+math.log(sum(math.exp(x-m) for x in terms))
    lo,hi=0.,1.
    for _ in range(60):
        mid=(lo+hi)/2
        if logcdf(mid)>math.log(alpha):lo=mid
        else:hi=mid
    return hi
def bound(n,errors,accepted,false_accepts,comparisons,cap=CAP):
    a=.05/(2*comparisons)
    cp=cp_upper(n,errors,a);den=accepted/n-cap*math.sqrt(math.log(1/a)/(2*n))
    upper=min(1.,cap*cp/den) if den>0 else None
    return dict(groups=n,errorGroups=errors,acceptedSources=accepted,falseAcceptedSources=false_accepts,
                cap=cap,comparisons=comparisons,alpha=.05,cpUpper=cp,acceptedMeanLower=max(0,den),
                conditionalFdrUpper=upper,criterion=LIMIT,referenceOnly=True,meetsReference=upper is not None and upper<=LIMIT,
                assumptionIidIndependentlyVerified=False,certified=False)
def quality(state,truth,comparisons):
    pred=state["pred"];actual=state["actual"];true=state["true"]
    dest_bases={p:{truth[k]["base"] for k in ids} for p,ids in pred.items()}
    multi={p for p,ids in pred.items() if len({truth[k]["origin"] for k in ids})>1 or len(dest_bases[p])>1}
    # Anchor is one VERSION, not every observation of its founding origin.
    # Choose by recorded authority, then first wave / stable observation ID;
    # never use truth or purity to choose it. If provenance is absent, label
    # deterministic fallback explicitly; this is a sensitivity, not a proven founder.
    anchors={};fallback=0
    for dest in multi:
        ids=pred[dest]
        cpf=[k for k in ids if truth[k]["cpf"]]
        founders=[k for k in ids if state["byid"][k]["Method"] in
                  ("SINGLETON_FOUNDER","S4B_HYPOTHETICAL_FOUNDER")]
        candidates=cpf or founders or ids
        if not cpf and not founders:fallback+=1
        anchors[dest]=min(candidates,key=lambda k:(truth[k]["wave"],k))
    accepted=Counter();wrong=Counter();nonanchor=Counter();nonanchor_wrong=Counter()
    integral=partial=unassigned=0
    for base,ids in true.items():
        assigned={actual[k] for k in ids if actual[k] is not None}
        if len(assigned)==1 and all(actual[k] is not None for k in ids) and len(dest_bases[next(iter(assigned))])==1:integral+=1
        elif assigned:partial+=1
        else:unassigned+=1
        for k in ids:
            p=actual[k]
            if not truth[k]["cpf"] and p in multi:
                accepted[base]+=1
                if dest_bases[p]!={base}:wrong[base]+=1
                if k!=anchors[p]:
                    nonanchor[base]+=1
                    if dest_bases[p]!={base}:nonanchor_wrong[base]+=1
    require(integral+partial+unassigned==len(true) and all(v<=CAP for v in accepted.values()),"SCALE_INTEGRAL_CENSUS")
    a=sum(accepted.values());f=sum(wrong.values());e=len(wrong)
    na=sum(nonanchor.values());nf=sum(nonanchor_wrong.values());ne=len(nonanchor_wrong)
    removed=sum(not truth[k]["cpf"] for k in anchors.values())
    require(a-na==removed and nf<=f and ne<=e,"SCALE_ANCHOR_RECONCILIATION")
    counts=Counter(len(ids) for ids in true.values())
    require(max(counts)<=CAP,"SCALE_VERSION_CAP")
    pairs=sum(len(ids)*(len(ids)-1)//2 for ids in pred.values())
    return dict(fullyIntactTruePeople=integral,assignedButNotIntactTruePeople=partial,unassignedTruePeople=unassigned,
        acceptedPairs=pairs,acceptedSources=a,errorSourcePeople=e,contaminatedDestinations=sum(len(b)>1 for b in dest_bases.values()),
        sourceAnchorPolicy="CPF_THEN_FOUNDER_THEN_FIRST_WAVE_ID_FALLBACK_ONE_VERSION_PER_DESTINATION",
        eligibleAnchorsRemoved=removed,anchorFallbackDestinations=fallback,
        nonAnchorAcceptedSources=na,nonAnchorFalseAcceptedSources=nf,nonAnchorErrorSourcePeople=ne,
        nonAnchorSourceFdr=nf/na if na else None,
        nonAnchorSourceFdr95Conditional=bound(len(true),ne,na,nf,1),
        nonAnchorSourceFdr95SimultaneousConditional=bound(len(true),ne,na,nf,comparisons),
        versionsPerPersonHistogram=dict(sorted(counts.items())),
        sourceCapSensitivity=[dict(cap=cap,diagnosticOnly=cap!=CAP,nominal=bound(len(true),e,a,f,1,cap),
            simultaneous=bound(len(true),e,a,f,comparisons,cap)) for cap in range(max(counts),CAP+1)],
        sourceFdr=f/a if a else None,sourceFdr95Conditional=bound(len(true),e,a,f,1),
        sourceFdr95FamilyConditional=bound(len(true),e,a,f,comparisons))
def audit_cases(state,truth,prior,ordinals):
    # State and prior only. All emitted references are local ordinals, not raw truth IDs or UUIDs.
    pred=state["pred"];actual=state["actual"];rows=state["byid"]
    targets={p:min(ordinals["obs"][k] for k in ids) for p,ids in pred.items()}
    prior_targets={} if prior is None else {p:min(ordinals["obs"][k] for k in ids) for p,ids in prior["pred"].items()}
    def trace(k):
        x=rows[k];base=truth[k]["base"];target=actual[k]
        prev=None if prior is None else prior["actual"].get(k)
        if prior:
            refs={p for p,ids in prior["pred"].items() if any(truth[j]["base"]==base for j in ids)}
        else:refs=set()
        # Candidate ordinals may refer to prior/current state; never emit the UUID.
        lookup={**prior_targets,**targets}
        return dict(observation=ordinals["obs"][k],person=ordinals["base"][base],origin=ordinals["origin"][truth[k]["origin"]],
            manager=truth[k]["manager"],wave=truth[k]["wave"],cpf=truth[k]["cpf"],
            destination=targets.get(target),previousDestination=prior_targets.get(prev),leader=ordinals["obs"].get(x["LeaderId"]),
            method=x["Method"],raw=x["RawReason"],effective=x["EffectiveReason"],status=x["RawStatus"],
            score=x["Score"],associationScore=x["AssociationScore"],groupSize=x["GroupSize"],minPosterior=x["GroupMinPosterior"],
            secureOrigin=x["SecureOrigin"],candidateCount=x["Candidates"],candidateDestinations=[lookup.get(p) for p in x["CandidateIds"] if p in lookup],
            trueReferencePreviouslyAvailable=bool(refs),trueReferenceInCandidates=bool(refs.intersection(x["CandidateIds"])),
            bestDestination=lookup.get(x["Best"]))
    fusions=[];splits=[]
    for p,ids in pred.items():
        bases={truth[k]["base"] for k in ids}
        if len(bases)>1:
            fusions.append(dict(destination=targets[p],affectedPeople=sorted(ordinals["base"][x] for x in bases),
                                explanation="IMPURE_DESTINATION: inspect original association/founder, group minimum and prior links",members=[trace(k) for k in ids]))
    for base,ids in state["true"].items():
        ps={actual[k] for k in ids if actual[k] is not None}
        if len(ps)>1:
            old=set() if prior is None else {prior["actual"].get(k) for k in ids}-{None}
            splits.append(dict(person=ordinals["base"][base],destinations=sorted(targets[p] for p in ps),
                explanation="ESTABLISHED_MULTIPLE_REFERENCES" if len(old)>1 else
                "NEW_DISTINCT_REFERENCE_WITH_PRIOR_REFERENCE_PRESERVED" if old else "MULTIPLE_REFERENCES_CREATED_IN_SAME_ROUND",
                versions=[trace(k) for k in ids]))
    return dict(fusions=fusions,splits=splits)
def measure(root):
    label,(n,_,_)=profile();I.MAX_BYTES=128*1024*1024
    require(label not in DIAGNOSTIC_PROFILES,"SCALE_DIAGNOSTIC_ONLY")
    census,identity,truth=truth_frame(root,n)
    require(identity==read(root/"identity-receipt.json"),"SCALE_BASELINE_RECHECK")
    p=read(root/"phase4-matrix-bound-plan.json");s4b=read(root/"s4b-receipt.json")
    require(p["firstContrast"]["receiptSha256"]==P.sha((root/"s4b-receipt.json").read_bytes()),"SCALE_CONTRAST_SEAL")
    exp={s["stateId"]:(c,t,s) for c in p["cells"] for t in c["trajectories"] for s in t["rounds"]}
    ords={"obs":{k:i+1 for i,k in enumerate(sorted(truth))},"base":{k:i+1 for i,k in enumerate(sorted({x["base"] for x in truth.values()}))},
          "origin":{k:i+1 for i,k in enumerate(sorted({x["origin"] for x in truth.values()}))}}
    results={};offsets={};bykey={};prior=None;prior_id=None
    file=root/"matrix-private.jsonl"
    require(file.is_file() and not file.is_symlink() and 0<file.stat().st_size<8*1024**3,"SCALE_PRIVATE_SIZE")
    with file.open("rb") as stream,(root/"case-audit.jsonl").open("x",encoding="utf-8") as cases:
        while True:
            offset=stream.tell();line=stream.readline(128*1024*1024+1)
            if not line:break
            require(len(line)<=128*1024*1024,"SCALE_LINE")
            x=I.load(line);sid=x["stateId"]
            require(sid in exp and sid not in results,"SCALE_STATE_DUPLICATE")
            c,t,s=exp[sid]
            require((x["cell"],x["schedule"],x["round"],x["throughWave"],x["previousStateId"])==
                    (c["cell"],t["schedule"],s["round"],s["throughWave"],s["previousStateId"]),"SCALE_STATE_BIND")
            if s["previousStateId"] is None:prior=None
            else:require(prior_id==s["previousStateId"],"SCALE_PREDECESSOR")
            require({r["Id"] for r in x["rows"]}=={k for k,v in truth.items() if v["wave"]<=s["throughWave"]},"SCALE_WAVE")
            metrics,state=M.assess(x["rows"],truth,prior)
            metrics.update(quality(state,truth,len(exp)))
            audit=audit_cases(state,truth,prior,ords)
            require(len(audit["fusions"])==metrics["falseFusedIdentities"] and len(audit["splits"])==metrics["splitTruePeople"],"SCALE_CASE_CENSUS")
            cases.write(json.dumps(dict(cell=c["cell"],schedule=t["schedule"],round=s["round"],**audit),sort_keys=True)+"\n")
            res=x["resources"]
            require(res["variantSqlExecuted"] is False and res["fsCompared"]==sum(r["Fs"] for r in x["rows"]),"SCALE_RESOURCES")
            res["secureRecordsPerPerson"]=M.distribution(res["secureRecordsPerPerson"])
            res["keysPerPerson"]=M.distribution(res["keysPerPerson"])
            results[sid]=dict(cell=c["cell"],factors=c["factors"],schedule=t["schedule"],round=s["round"],throughWave=s["throughWave"],
                metrics=metrics,resources=res,baselineExactParity=x["baselineExactParity"])
            offsets[sid]=offset;bykey[(c["cell"],t["schedule"],s["round"])]=sid;prior=state;prior_id=sid
        require(set(results)==set(exp),"SCALE_MATRIX_COMPLETE")
        def load_state(sid):
            stream.seek(offsets[sid]);x=I.load(stream.readline(128*1024*1024+1))
            return M.assess(x["rows"],truth)[1]
        # On-demand two-state reads, not retaining millions of row dictionaries.
        fields=("RawStatus","RawReason","EffectiveReason","Method","Score","AssociationScore","Fs","Candidates","SecureCandidates","OnlyViaNonsecure")
        contrasts=[]
        for sid,x in results.items():
            f=x["factors"];cell=x["cell"];schedule=x["schedule"];rnd=x["round"]
            if f["prior"]=="people":
                a,b=load_state(bykey[(cell.replace("-people-","-records-"),schedule,rnd)]),load_state(sid)
                require(M.partition(a)==M.partition(b) and all(all(a["byid"][k][z]==b["byid"][k][z] for z in fields) for k in a["byid"]),"SCALE_PRIOR_PARITY")
            if f["evidence"]=="b2":
                a,b=load_state(bykey[(cell.replace("-b2-","-b1-"),schedule,rnd)]),load_state(sid)
                x["b2ExclusiveAssociationsByScore"]=M.compare_exclusive(a,b,truth)
            if f["blocking"]=="secure":
                a,b=load_state(bykey[(cell.replace("-secure","-broad"),schedule,rnd)]),load_state(sid)
                x["broadIndefiniteSecureNew"]=sum(a["actual"][k] is None and b["actual"][k] is not None and b["byid"][k]["Method"] in ("SINGLETON_FOUNDER","S4B_HYPOTHETICAL_FOUNDER","S4B_GROUP","S4B_HYPOTHETICAL_SECURE_MEMBER") for k in b["actual"])
            if f["s4b"]=="ii":
                a=results[bykey[("i-"+cell[3:].removesuffix("-all"),schedule,rnd)]]["metrics"]
                contrasts.append(dict(cell=cell,schedule=schedule,round=rnd,**{k:x["metrics"][k]-a[k] for k in ("tp","fp","fn","falseFusedIdentities","splitTruePeople","fullyIntactTruePeople")}))
        baseline=results[bykey[("i-b1-records-r1-broad","single",1)]]
        require(baseline["baselineExactParity"] and all(baseline["metrics"][k]==identity["pairMetrics"][k] for k in ("tp","fp","fn","truePairs")),"SCALE_BASELINE_PARITY")
        alt=results[bykey[("ii-b1-records-r1-broad","single",1)]]
        cf=s4b["variants"]["one_new_per_group_origin_continuity"]
        parity=all(alt["metrics"][k]==cf[k] for k in ("tp","fp","fn","truePairs"))
        require(parity,"SCALE_S4B_PARITY")
        if label=="audit1k":
            require(results[bykey[("ii-b1-records-r1-broad","per_wave",2)]]["metrics"]["splitTruePeople"]==6,"SCALE_1K_SIX_SPLITS")
    write(root/"scale-receipt.json",dict(schema="V9_PHASE4_SCALE_V1",status="MEASURED",profile=label,population=n,
        commit=os.environ["JORNADA_E2E_EXACT_SHA"],winnerSha256=P.WINNER,fixtureBinding=p["fixtureBinding"],
        measuredCells=len(p["cells"]),measuredStates=len(results),measuredTrajectories=len(p["cells"])*2,
        physicalResources=read(root/"matrix-physical-resource.json"),baselineExactParity=True,s4bParity=parity,
        states=list(results.values()),contrasts=contrasts,caseAuditSha256=P.sha((root/"case-audit.jsonl").read_bytes()),
        comparableZeroFusions=all(x["metrics"]["falseFusedIdentities"]==0 for x in results.values()) if label=="comparable" else None,
        officialPolicyUnchanged=True,alternativePublication=False,scientificTest=False,activation=False))
def diagnostic_receipt(root):
    label,(n,_,_)=profile()
    require(label in DIAGNOSTIC_PROFILES,"SCALE_DIAGNOSTIC_ONLY")
    I.MAX_BYTES=128*1024*1024
    census,identity,truth=truth_frame(root,n)
    member=read(root/"membership-receipt.json");runner=read(root/"runner-receipt.json")
    full=read(root/"matrix-full-physical-resource.json")
    require(member["status"]=="PASS" and runner["status"]=="PUBLICADO"
            and runner["eligible"]==runner["evaluated"] and full["fullElapsedMs"]>0,"SCALE_DIAGNOSTIC_GATE")
    # Compare independent runs without depending on randomly allocated SQL
    # UUIDs or observation IDs. Canonical destination = exact set of source
    # version keys attached to that UUID; preserve every publication state.
    # The generator uses a fresh HMAC pseudonymization key for each attempt.
    # Align independent synthetic runs by the sealed truth's stable base,
    # manager and wave, only AFTER the independent physical checks.
    def version_key(x):
        t=truth[x["observationId"]]
        return (t["base"],t["manager"],t["wave"])
    require(len({version_key(x) for x in census})==len(census),"SCALE_PARITY_KEY")
    groups=defaultdict(list)
    for row in census:
        if row["currentUuid"]:groups[row["currentUuid"]].append(version_key(row))
    canon={u:sorted(keys) for u,keys in groups.items()}
    def target(u):
        if u is None:return None
        require(u in canon,"SCALE_PARITY_UNKNOWN_DESTINATION")
        return canon[u]
    normalized=[]
    for row in census:
        normalized.append(dict(key=version_key(row),cpf=row["hasCpf"],
            current=target(row["currentUuid"]),published=target(row["publishedUuid"]),
            status=row["currentStatus"],method=row["currentMethod"],goldState=row["goldState"],
            result=row["runResult"],reason=row["publicationReason"]))
    normalized.sort(key=lambda x:x["key"])
    parity_digest=hashlib.sha256(json.dumps(normalized,sort_keys=True,separators=(",",":")).encode()).hexdigest()
    byid={x["observationId"]:x for x in census}
    key_byid={k:version_key(x) for k,x in byid.items()}
    ranks=read(root/"scale-rank-private.json")
    graphs=read(root/"fn-private.json")
    # Group IDs are arbitrary; compare exact member sets by stable keys.
    group_members=defaultdict(list)
    for x in graphs:group_members[x["groupRoot"]].append(key_byid[x["observationId"]])
    graph_rows=[dict(key=key_byid[x["observationId"]],group=sorted(group_members[x["groupRoot"]]),
        candidates=sorted(target(u.lower()) for u in x["candidates"]),
        neighbors=sorted(key_byid[k] for k in x["silverNeighbors"]),minimum=x["groupMinPosterior"],
        reason=x["rankReason"]) for x in graphs]
    rank_rows=[]
    for x in ranks:
        row=dict(x)
        row["Id"]=key_byid[x["Id"]]
        for field in ("Best","Second","Resolved"):
            row[field]=target(x[field].lower()) if x[field] else None
        rank_rows.append(row)
    require(len(ranks)==len(graphs)==runner["evaluated"],"SCALE_PARITY_UNIVERSE")
    decision_digest=hashlib.sha256(json.dumps(dict(
        graph=sorted(graph_rows,key=lambda x:x["key"]),ranks=sorted(rank_rows,key=lambda x:x["Id"])),
        sort_keys=True,separators=(",",":")).encode()).hexdigest()
    write(root/"diagnostic-receipt.json",dict(schema="V9_DEV_FULL_SCALE_DIAGNOSTIC_V1",
        profile=label,population=n,versions=len(census),eligible=runner["eligible"],
        commit=os.environ["JORNADA_E2E_EXACT_SHA"],winnerSha256=P.WINNER,
        fullCompleted=True,independentMembershipVerified=True,identityCensusStatus=identity["status"],
        pairMetrics=identity["pairMetrics"],physicalFull=full,normalizedPublicationSha256=parity_digest,normalizedDecisionGraphSha256=decision_digest,
        matrixExecuted=False,scientificTest=False,activation=False))
def main():
    p=argparse.ArgumentParser();p.add_argument("mode",choices=("preflight","ingest","plan","measure","diagnostic"));p.add_argument("--root",type=Path,required=True);a=p.parse_args()
    try:
        label,(n,_,_)=profile()
        if a.mode=="preflight":preflight(a.root)
        elif a.mode=="ingest":H.ingest(a.root,uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"]),population=n)
        elif a.mode=="plan":plan(a.root)
        elif a.mode=="diagnostic":diagnostic_receipt(a.root)
        else:measure(a.root)
    except (ValueError,OSError,KeyError,TypeError,AttributeError,ArithmeticError) as e:
        # Fixed internal code only; never arbitrary input or stack.
        import re
        code=str(e) if re.fullmatch(r"V9_E2E_ORACLE_[A-Z0-9_]+",str(e)) else "UNCLASSIFIED"
        raise SystemExit("V9_PHASE4_SCALE_REJECTED_"+code) from None
    print("V9_PHASE4_SCALE_"+a.mode.upper()+"_PASS")
if __name__=="__main__":main()

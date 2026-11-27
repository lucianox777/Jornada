#!/usr/bin/env python3
"""PO-authorized independent DEV scale experiment. No truth is given to the runner."""
import argparse, hashlib, importlib.util, json, math, os, shutil, uuid
from collections import Counter, defaultdict
from decimal import Decimal, InvalidOperation, localcontext
from pathlib import Path

def module(name,file):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(file))
    m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

M=module("matrix","v9-phase4-matrix-oracle.py")
I=M.I;P=M.G.P
W=module("wave","v9-e2e-1k-wave-preflight.py")
H=module("http","v9-e2e-1k-http.py")
require=I.require
PROFILES={"diagnostic1k":(1000,355,"clean"),
          "diagnostic2k":(2000,355,"clean"),"diagnostic4k":(4000,355,"clean"),"optimized1k":(1000,355,"clean"),
          "optimized2k":(2000,355,"clean"),"optimized4k":(4000,355,"clean")}
CAP=8
def profile():
    label=os.environ["JORNADA_V9_PHASE4_SCALE"]
    require(label in PROFILES and os.environ.get("JORNADA_V9_FULL_DIAGNOSTIC")=="true" and os.environ.get("GITHUB_ACTIONS")=="true"
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
def decision_field_digests(graph_rows,rank_rows):
    # Aggregate hashes only, keyed by the same stable version keys as the
    # strict combined digest. Never export individual rows or identifiers.
    result={}
    for prefix,rows,key,fields in (
        ("graph",graph_rows,"key",("group","candidates","neighbors","minimum","reason")),
        ("rank",rank_rows,"Id",("Best","BestScore","Second","SecondScore","Margin","ScoreableRecords",
            "PeopleWithoutScoreableEvidence","CandidateCount","HasPass","Status","Resolved","Reason"))):
        require(all(set(row)==set(fields)|{key} for row in rows),"SCALE_PARITY_DIAGNOSTIC_SCHEMA")
        ordered=sorted(rows,key=lambda row:row[key])
        for field in fields:
            payload=[(row[key],row[field]) for row in ordered]
            result[prefix+"."+field]=hashlib.sha256(json.dumps(payload,sort_keys=True,
                separators=(",",":"),allow_nan=False).encode()).hexdigest()
    return result

def valid_uuid(value):
    try:
        return isinstance(value, str) and str(uuid.UUID(value)) == value and uuid.UUID(value).int != 0
    except (ValueError, TypeError, AttributeError):
        return False

def canonical_prefull_frame(frame, attempt):
    frame_fields={"Attempt","Rows","InitialRefs","SnapshotMs","VersionStoreStartBytes",
                  "VersionStoreEndBytes","ProcessMemoryBytes"}
    row_fields={"Id","Origin","Version","Wave","Manager","Observation","HasCpf","Initial",
                "Current","Canonical","Status","Method","State","AsOf","NameVerified",
                "BirthVerified","MotherVerified"}
    ref_fields={"Person","Name","Birth","Mother"}
    require(isinstance(frame,dict) and set(frame)==frame_fields
            and frame["Attempt"]==attempt,"SCALE_PAIRED_FRAME")
    rows,refs=frame["Rows"],frame["InitialRefs"]
    require(isinstance(rows,list) and rows and isinstance(refs,list) and refs
            and all(isinstance(r,dict) and set(r)==row_fields
                and type(r["Id"]) is int and r["Id"]>0
                and type(r["Origin"]) is int and r["Origin"]>0
                and valid_uuid(r["Initial"])
                and all(r[k] is None or valid_uuid(r[k]) for k in ("Current","Canonical"))
                for r in rows)
            and all(isinstance(r,dict) and set(r)==ref_fields and valid_uuid(r["Person"])
                    for r in refs),"SCALE_PAIRED_FRAME_ROWS")
    require(len({r["Id"] for r in rows})==len(rows)
            and len({r["Person"] for r in refs})==len(refs),"SCALE_PAIRED_FRAME_DUPLICATE")
    # Only list ordering and the attempt-specific measurement envelope
    # differ. No observation or reference field may be normalized away.
    return json.dumps(dict(rows=sorted(rows,key=lambda r:r["Id"]),
                           references=sorted(refs,key=lambda r:r["Person"])),
                      sort_keys=True,separators=(",",":"),allow_nan=False)

def verify_paired_prefull_frames(left, right, left_attempt, right_attempt):
    require(valid_uuid(left_attempt) and valid_uuid(right_attempt)
            and left_attempt != right_attempt, "SCALE_PAIRED_ATTEMPT")
    require(canonical_prefull_frame(left, left_attempt) == canonical_prefull_frame(right, right_attempt),
            "SCALE_PAIRED_PREFULL_INPUT_DIFFERENCE")

def verify_restored_prefull_frames(left, right, attempt, first_run, second_run):
    # A verified restore keeps C07, all input IDs and the actual HTTP lineage.
    # Distinct published Runner runs prevent comparing a receipt to itself.
    require(all(valid_uuid(x) for x in (attempt, first_run, second_run))
            and first_run != second_run, "SCALE_PAIRED_RUN")
    require(canonical_prefull_frame(left, attempt) == canonical_prefull_frame(right, attempt),
            "SCALE_PAIRED_PREFULL_INPUT_DIFFERENCE")
    return dict(observations=len(left["Rows"]), references=len(left["InitialRefs"]))

def canonical_v8_operational_receipt(receipt):
    def persisted_decimal(value):
        require(isinstance(value,str) and bool(value)
                and all(ch in "-0123456789." for ch in value),
                "SCALE_V8_OPERATIONAL_DECIMAL")
        try:
            number=Decimal(value)
        except InvalidOperation:
            require(False,"SCALE_V8_OPERATIONAL_DECIMAL")
        require(number.is_finite(),"SCALE_V8_OPERATIONAL_DECIMAL")
        return number
    top={"schema","schemaStage","entrypoint","publish","fixtureLifecycle","runId",
         "model","run","decisions","gold","currentLinks","progressiveOrigin"}
    require(isinstance(receipt,dict) and set(receipt)==top
            and receipt["schema"]=="V8_OPERATIONAL_REGRESSION_PRIVATE_V1"
            and receipt["schemaStage"] in ("BASELINE","20261126_V3")
            and receipt["entrypoint"]=="Jornada.Linkage.Runner.Program/ProbabilisticLinkageBatchRunner.RunAsync"
            and receipt["publish"] is True
            and receipt["fixtureLifecycle"]=="NO_TRAIN_NO_VALIDATION_NO_ACTIVATION"
            and valid_uuid(receipt["runId"]),"SCALE_V8_OPERATIONAL_RECEIPT")
    model=receipt["model"]
    require(isinstance(model,dict) and set(model)=={"modelId","version","status","algorithmVersion",
            "ruleSetVersion","ruleSetFingerprintSha256","projectionSchemaVersion",
            "projectionFingerprintSha256"}
            and model["modelId"]=="88000000-0000-4000-8000-000000008008"
            and model["version"]==8008 and model["status"]=="INATIVO"
            and model["algorithmVersion"]=="FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8"
            and model["ruleSetVersion"]=="V8_OPERATIONAL_REGRESSION_RULESET_V1"
            and model["ruleSetFingerprintSha256"]=="900a6ab052f078f8422d76a297a5a9fbf6fe0003f764edc86e5d01e802991504"
            and model["projectionSchemaVersion"]=="PERSON_RESOLUTION_PROJECTION_V2"
            and model["projectionFingerprintSha256"]=="d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4",
            "SCALE_V8_OPERATIONAL_MODEL")
    run=receipt["run"]
    run_fields={"mode","status","observationId","highWatermark","eligible","evaluated",
                "resolved","unresolved","conflicts","noCandidateInBlock","batchSize",
                "maxParallelism","requestedBy","reason"}
    require(isinstance(run,dict) and set(run)==run_fields and run["mode"]=="ON_DEMAND"
            and run["status"]=="PUBLICADO" and type(run["observationId"]) is int
            and 0<run["observationId"]<=run["highWatermark"]
            and run["eligible"]==run["evaluated"]==run["resolved"]==1
            and run["unresolved"]==run["conflicts"]==run["noCandidateInBlock"]==0
            and run["batchSize"]==100 and run["maxParallelism"]==1
            and run["requestedBy"]=="CI_V8_OPERATIONAL_REGRESSION"
            and run["reason"]=="governed-v8-operational-publication-regression",
            "SCALE_V8_OPERATIONAL_RUN")
    decisions=receipt["decisions"]
    decision_fields={"observationId","resolvedUuid","bestUuid","bestScore","secondUuid",
        "secondScore","margin","status","reason","publicationResult","publishedUuid",
        "publicationStatus","publicationReason","publishedOriginId","progressiveVersion",
        "publicationPolicy","universeReference"}
    require(isinstance(decisions,list) and len(decisions)==1
            and set(decisions[0])==decision_fields,"SCALE_V8_OPERATIONAL_DECISION")
    decision=decisions[0]
    expected_universe=(f"LINKAGE_RUN:{receipt['runId']};HW:{run['highWatermark']};"
                       f"ELIGIVEIS:{run['eligible']}")
    require(decision["observationId"]==run["observationId"]
            and decision["status"]=="RESOLVIDO"
            and decision["reason"] is None
            and valid_uuid(decision["resolvedUuid"])
            and decision["bestUuid"]==decision["resolvedUuid"]
            and 0<=persisted_decimal(decision["bestScore"])<=1
            and decision["secondUuid"] is None and decision["secondScore"] is None
            and decision["margin"] is None
            and decision["publicationResult"]=="ASSOCIACAO_EXISTENTE"
            and decision["publicationStatus"]=="RESOLVIDO"
            and decision["publishedUuid"]==decision["resolvedUuid"]
            and decision["publicationReason"]=="REFERENCIA_PROGRESSIVA_CONFIRMADA"
            and type(decision["publishedOriginId"]) is int and decision["publishedOriginId"]>0
            and type(decision["progressiveVersion"]) is int and decision["progressiveVersion"]>0
            and decision["publicationPolicy"]=="LINKAGE_PROGRESSIVE_PUBLICATION_V1"
            and decision["universeReference"]==expected_universe,
            "SCALE_V8_OPERATIONAL_DECISION")
    gold=receipt["gold"]
    gold_fields={"personUuid","cpf","cpfStatus","fullName","birthDate","motherName",
        "distinctSources","concordanceState","identityState","nucleusCompleteness",
        "normalizedPublicationName","publicationNameMethodVersion",
        "publicationNameNormalizationVersion"}
    require(isinstance(gold,list) and gold and all(isinstance(row,dict) and set(row)==gold_fields
            and valid_uuid(row["personUuid"]) for row in gold)
            and len({row["personUuid"] for row in gold})==len(gold)
            and decision["resolvedUuid"] in {row["personUuid"] for row in gold},
            "SCALE_V8_OPERATIONAL_GOLD")
    links=receipt["currentLinks"]
    link_fields={"observationId","personUuid","method","score","status","reason","modelId","exactRun"}
    require(isinstance(links,list) and len(links)==1 and set(links[0])==link_fields
            and links[0]["observationId"]==run["observationId"]
            and links[0]["personUuid"]==decision["resolvedUuid"]
            and links[0]["method"]=="LINKAGE_PROBABILISTICO"
            and links[0]["score"]==decision["bestScore"]
            and links[0]["status"]==decision["publicationStatus"]
            and links[0]["reason"]==decision["publicationReason"]
            and links[0]["exactRun"] is True
            and links[0]["modelId"]==model["modelId"],"SCALE_V8_OPERATIONAL_LINK")
    progressive=receipt["progressiveOrigin"]
    progressive_fields={"originId","initialUuid","canonicalUuid","legacyUuid","state","version",
                        "lastExternalTargetUuid"}
    require(isinstance(progressive,dict) and set(progressive)==progressive_fields
            and progressive["originId"]==decision["publishedOriginId"]
            and valid_uuid(progressive["initialUuid"])
            and progressive["canonicalUuid"]==decision["publishedUuid"]
            and progressive["state"]=="REFERENCIA"
            and progressive["version"]==decision["progressiveVersion"],
            "SCALE_V8_OPERATIONAL_PROGRESSIVE")
    canonical=json.loads(json.dumps(receipt,sort_keys=True,allow_nan=False))
    canonical.pop("schemaStage");canonical.pop("runId")
    canonical["decisions"][0]["universeReference"]=(
        f"HW:{run['highWatermark']};ELIGIVEIS:{run['eligible']}")
    return canonical

def verify_v8_operational_pair(left, right, expected_stages):
    a,b=(canonical_v8_operational_receipt(x) for x in (left,right))
    require(tuple(expected_stages) in (("BASELINE","20261126_V3"),("20261126_V3","BASELINE"))
            and (left["schemaStage"],right["schemaStage"])==tuple(expected_stages),
            "SCALE_V8_OPERATIONAL_STAGES")
    require(left["runId"]!=right["runId"] and a==b,"SCALE_V8_OPERATIONAL_DIFFERENCE")
    # Prove that this same strict comparator exposes a semantic Gold mutation.
    mutation=json.loads(json.dumps(b,sort_keys=True,allow_nan=False))
    mutation["gold"][0]["fullName"]=(mutation["gold"][0]["fullName"] or "")+" MUTATION"
    require(a!=mutation,"SCALE_V8_OPERATIONAL_MUTATION_NOT_DETECTED")
    raw=json.dumps(a,sort_keys=True,separators=(",",":"),allow_nan=False).encode()
    return dict(operationalV8RegressionVerified=True,
        operationalV8MutationDetected=True,
        operationalV8SemanticSha256=hashlib.sha256(raw).hexdigest(),
        operationalV8DecisionRows=len(a["decisions"]),
        operationalV8GoldRows=len(a["gold"]),
        operationalV8ModelId=a["model"]["modelId"],
        operationalV8RuleSetFingerprintSha256=a["model"]["ruleSetFingerprintSha256"],
        operationalV8Stages=list(expected_stages))

def pair_receipt(root):
    left, right = root, root / "pair"
    attempt = os.environ["JORNADA_V9_DEV_ATTEMPT"]
    first, second = (read(x / "runner-receipt.json") for x in (left, right))
    require(first["status"] == second["status"] == "PUBLICADO", "SCALE_PAIRED_PUBLICATION")
    equal = verify_restored_prefull_frames(
        read(left / "matrix-frame-private.json"), read(right / "matrix-frame-private.json"),
        attempt, first["runId"], second["runId"])
    graphs = [read(x / "actual-graph-receipt.json") for x in (left, right)]
    require(all(graph["runId"] == runner["runId"] for graph, runner in zip(graphs, (first, second)))
            and all(graphs[0][key] == graphs[1][key] for key in (
                "edges", "edgesSha256", "directions", "directionSha256",
                "goldRecordScores", "goldRecordScoreSha256")),
            "SCALE_PAIRED_RAW_SCORE_DIFFERENCE")
    primary_profile=read(left/"diagnostic-receipt.json")["profile"]
    restored_profile=read(right/"diagnostic-receipt.json")["profile"]
    stage_by_direction={
        ("diagnostic1k","optimized1k"):("BASELINE","20261126_V3"),
        ("optimized1k","diagnostic1k"):("20261126_V3","BASELINE")}
    require((primary_profile,restored_profile) in stage_by_direction,
            "SCALE_V8_OPERATIONAL_STAGES")
    v8=verify_v8_operational_pair(
        read(left / "v8-operational-private.json"),
        read(right / "v8-operational-private.json"),
        stage_by_direction[(primary_profile,restored_profile)])
    write(left / "pair-receipt.json", dict(
        schema="V9_RESTORED_PREFULL_PAIR_V2", commit=os.environ["JORNADA_E2E_EXACT_SHA"],
        primaryProfile=primary_profile,
        restoredProfile=restored_profile,
        inputVerified=True, distinctPublishedRuns=True, rawEdgesAndScoresEqual=True,
        **equal, **v8))

def exact_decimal(value):
    require(type(value) in (int, float, Decimal), "SCALE_GOLD_SCORE_NUMBER")
    number = Decimal(str(value))
    require(number.is_finite(), "SCALE_GOLD_SCORE_NUMBER")
    return number

def decimal_text(value):
    if value is None:
        return None
    number = exact_decimal(value)
    if number == 0:
        return "0"
    text = format(number, "f")
    return text.rstrip("0").rstrip(".") if "." in text else text

def read_exact(path):
    return json.loads(I.read(path), object_pairs_hook=I.strict_pairs, parse_constant=I.nonfinite,
                      parse_float=Decimal)

def gold_rank_diagnostics(scores, ranks, graphs, key_byid, target, expected_count,
                          independent_labels=None):
    """Verify the actual UUID order and build normalized exact-score evidence."""
    require(len(scores)==expected_count,"SCALE_GOLD_SCORE_CENSUS")
    bysource={r["Id"]:r for r in ranks}
    candidates={g["observationId"]:{u.lower() for u in g["candidates"]} for g in graphs}
    require(len(bysource)==len(ranks)==len(candidates)==len(graphs)
            and set(bysource)==set(candidates),"SCALE_GOLD_SCORE_UNIVERSE")
    evidence=defaultdict(list); normalized=[]; seen=set()
    for s in scores:
        require(set(s)=={"Source","Record","Person","Posterior","LogOdds"},"SCALE_GOLD_SCORE_SCHEMA")
        src,rec,u=s["Source"],s["Record"],s["Person"].lower()
        require(src in bysource and rec in key_byid and src in key_byid and u in candidates[src],
                "SCALE_GOLD_SCORE_REFERENCE")
        k=(src,rec,u)
        require(k not in seen,"SCALE_GOLD_SCORE_DUPLICATE");seen.add(k)
        p,l=s["Posterior"],s["LogOdds"]
        require((p is None)==(l is None),"SCALE_GOLD_SCORE_ABSTENTION")
        if p is not None:
            p, l = exact_decimal(p), exact_decimal(l)
            require(0<=p<=1,"SCALE_GOLD_SCORE_POSTERIOR")
        evidence[src].append((u,rec,p,l))
        normalized.append((key_byid[src],target(u),key_byid[rec],decimal_text(p),decimal_text(l)))
    bands=[]; counts=dict(rows=len(ranks),scoreRecords=len(scores),
        tiedBestRows=0,tiedSecondRows=0,tiedBestOrSecondRows=0,tiedResolvedRows=0)
    for src,r in bysource.items():
        ev=evidence[src]
        require(len(candidates[src])==r["CandidateCount"],"SCALE_GOLD_CANDIDATE_CENSUS")
        valid=[(u,rec,p,l) for u,rec,p,l in ev if l is not None]
        require(len(valid)==r["ScoreableRecords"],"SCALE_GOLD_SCOREABLE_CENSUS")
        best={}
        for u,rec,p,l in valid:
            # Independent oracle uses maximum LogOdds then source observation ID.
            if u not in best or (l.copy_negate(),rec)<(best[u][2].copy_negate(),best[u][0]):
                best[u]=(rec,p,l)
        require(len(candidates[src])-len(best)==r["PeopleWithoutScoreableEvidence"],
                "SCALE_GOLD_ABSTAINED_CENSUS")
        # Guid.CompareTo orders unsigned fields, equivalently canonical hex strings.
        ranked=sorted(best,key=lambda u:(best[u][2].copy_negate(),u))
        raw_best=r["Best"].lower() if r["Best"] else None
        raw_second=r["Second"].lower() if r["Second"] else None
        require(raw_best==(ranked[0] if ranked else None) and
                raw_second==(ranked[1] if len(ranked)>1 else None),"SCALE_GOLD_RAW_RANK_MISMATCH")
        require(decimal_text(r["BestScore"]) == decimal_text(best[ranked[0]][1] if ranked else 0)
                and decimal_text(r["SecondScore"]) == decimal_text(best[ranked[1]][1] if len(ranked)>1 else None),
                "SCALE_GOLD_RAW_POSTERIOR_MISMATCH")
        with localcontext() as context:
            context.prec = 64
            margin = best[ranked[0]][2] - best[ranked[1]][2] if len(ranked)>1 else None
        require(decimal_text(r["Margin"]) == decimal_text(margin), "SCALE_GOLD_RAW_MARGIN_MISMATCH")
        # Compare all candidates within score tiers, including ties for second
        # below a unique best. Preserve scores, membership and abstentions.
        tiers=defaultdict(list)
        for u in ranked:tiers[best[u][2]].append((target(u),decimal_text(best[u][1])))
        ordered=[(decimal_text(l),sorted(tiers[l])) for l in sorted(tiers,reverse=True)]
        tied_best=bool(ranked and len(tiers[best[ranked[0]][2]])>1)
        tied_second=bool(len(ranked)>1 and len(tiers[best[ranked[1]][2]])>1)
        counts["tiedBestRows"]+=int(tied_best)
        counts["tiedSecondRows"]+=int(tied_second)
        counts["tiedBestOrSecondRows"]+=int(tied_best or tied_second)
        counts["tiedResolvedRows"]+=int(tied_best and r["Resolved"] is not None)
        bands.append((key_byid[src],ordered))
        if independent_labels is not None:
            labels = {}
            for index, field in enumerate(("Best", "Second")):
                if index >= len(ranked):
                    labels[field] = None
                    continue
                selected = ranked[index]
                log_odds = best[selected][2]
                tier = tiers[log_odds]
                labels[field] = target(selected) if len(tier) == 1 else dict(
                    logOdds=decimal_text(log_odds), destinations=sorted(dest for dest, _ in tier))
            independent_labels[key_byid[src]] = labels
    def digest(rows):
        return hashlib.sha256(json.dumps(sorted(rows),sort_keys=True,separators=(",",":"),
            allow_nan=False).encode()).hexdigest()
    return dict(normalizedGoldRecordScoreSha256=digest(normalized),
        normalizedGoldScoreTiersSha256=digest(bands),counts=counts,rawUuidRankVerified=True)

def sql_probe_receipt(root):
    stats = read(root / "sql-probe-finish.json")
    counters = {"completedRpc", "completedBatches", "completedSqlStatements",
                "completedProcedureStatements", "topLevelSqlCpuMicroseconds", "eventFiles",
                "serverCompletedRpc", "serverCompletedBatches"}
    require(set(stats) == counters and all(type(v) is int and v >= 0 for v in stats.values())
            and stats["completedRpc"] + stats["completedBatches"] > 0
            and stats["completedSqlStatements"] + stats["completedProcedureStatements"] > 0
            and 1 <= stats["eventFiles"] <= 4
            and stats["serverCompletedRpc"] == stats["completedRpc"]
            and stats["serverCompletedBatches"] == stats["completedBatches"], "SCALE_FULL_SQL_PROBE_RECEIPT")
    write(root / "full-sql-probe-receipt.json", dict(
        schema="V9_DEV_FULL_SQL_OPERATIONS_V2", scope="TAGGED_RUNNER_AND_SCORER_SQL",
        droppedEvents=0, rawEventsExported=False, **stats))

def diagnostic_receipt(root):
    label,(n,_,_)=profile()
    I.MAX_BYTES=128*1024*1024
    census,identity,truth=truth_frame(root,n)
    member=read(root/"membership-receipt.json");runner=read(root/"runner-receipt.json")
    full=read(root/"matrix-full-physical-resource.json")
    instrumentation = read(root / "full-sql-probe-receipt.json")
    census_timing = read(Path(os.environ["RUNNER_TEMP"]) / "v9-census-diagnostic.json")
    ingest_ms = float(I.read(root / "ingest-elapsed.txt")) * 1000
    require(math.isfinite(ingest_ms) and ingest_ms > 0
            and census_timing["status"] == "QUERY_COMPLETED"
            and type(census_timing["elapsedMs"]) is int and census_timing["elapsedMs"] >= 0,
            "SCALE_PIPELINE_TIMING")
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
    ranks=read_exact(root/"scale-rank-private.json")
    graphs=read_exact(root/"fn-private.json")
    # Group IDs are arbitrary; compare exact member sets by stable keys.
    group_members=defaultdict(list)
    for x in graphs:group_members[x["groupRoot"]].append(key_byid[x["observationId"]])
    graph_rows=[dict(key=key_byid[x["observationId"]],group=sorted(group_members[x["groupRoot"]]),
        candidates=sorted(target(u.lower()) for u in x["candidates"]),
        neighbors=sorted(key_byid[k] for k in x["silverNeighbors"]),minimum=decimal_text(x["groupMinPosterior"]),
        reason=x["rankReason"]) for x in graphs]
    rank_rows=[]
    for x in ranks:
        row=dict(x)
        row["Id"]=key_byid[x["Id"]]
        for field in ("Best","Second","Resolved"):
            row[field]=target(x[field].lower()) if x[field] else None
        for field in ("BestScore", "SecondScore", "Margin"):
            row[field] = decimal_text(x[field])
        rank_rows.append(row)
    require(len(ranks)==len(graphs)==runner["evaluated"],"SCALE_PARITY_UNIVERSE")
    actual_graph=read(root/"actual-graph-receipt.json")
    require(actual_graph["actualGoldRecordScoresVerified"] is True,"SCALE_GOLD_SCORE_ORACLE_GATE")
    independent_labels = {}
    gold=gold_rank_diagnostics(read_exact(root/"scale-gold-scores-private.json"),ranks,graphs,
        key_byid,target,actual_graph["goldRecordScores"],independent_labels)
    independent_rows = [dict(row, **independent_labels[row["Id"]]) for row in rank_rows]

    def graph_digest(rows):
        return hashlib.sha256(json.dumps(dict(
            graph=sorted(graph_rows,key=lambda x:x["key"]), ranks=sorted(rows,key=lambda x:x["Id"])),
            sort_keys=True,separators=(",",":"),allow_nan=False).encode()).hexdigest()

    write(root/"diagnostic-receipt.json",dict(schema="V9_DEV_FULL_SCALE_DIAGNOSTIC_V1",
        profile=label,population=n,versions=len(census),eligible=runner["eligible"],
        commit=os.environ["JORNADA_E2E_EXACT_SHA"],winnerSha256=P.WINNER,
        fullCompleted=True,independentMembershipVerified=True,identityCensusStatus=identity["status"],
        pairMetrics=identity["pairMetrics"],physicalFull=full,normalizedPublicationSha256=parity_digest,normalizedDecisionGraphSha256=graph_digest(rank_rows),
        normalizedIndependentDecisionGraphSha256=graph_digest(independent_rows),
        independentDecisionFieldSha256=decision_field_digests(graph_rows,independent_rows),
        decisionFieldSha256=decision_field_digests(graph_rows,rank_rows),goldRankDiagnostics=gold,
        ingestElapsedMs=ingest_ms,censusElapsedMs=census_timing["elapsedMs"],runnerSql=instrumentation,
        matrixExecuted=False,scientificTest=False,activation=False))
# Explicit fixed diagnostic vocabulary. Never export exceptions or SQL text.
INGEST_FAILURE_CODES=frozenset((
    "VOLUME_ADMISSION","WAVE_PREFLIGHT_REQUIRED","DIRTY_SILVER_ATTEMPT","DIRTY_RUN_ATTEMPT",
    "PACKAGE_RECEIPT_INVALID","PACKAGE_SIZE_DRIFT","PACKAGE_BYTES_DRIFT","HTTP_INGESTION_NOT_ACCEPTED",
    "HTTP_STATUS_UNAVAILABLE","PROCESSOR_REJECTED","PROCESSOR_TIMEOUT","SILVER_SOURCE_SET_DIVERGENCE",
    "BRONZE_LINEAGE_DIVERGENCE","HTTP_IDEMPOTENCY_DIVERGENCE","SILVER_COMPLETE_CENSUS_DIVERGENCE",
    "HTTP_DELIVERY_IDEMPOTENCY_CENSUS","PRIVATE_JSON_INVALID","MANAGER_CREDENTIAL_REQUIRED",
    "MANAGER_INVALID","HTTP_RESPONSE_BOUND","CI_CONTAINER_INVALID","SQL_LINEAGE_QUERY_FAILED",
    "SQL_LINEAGE_SCALAR_INVALID"))
def safe_failure_code(mode,error):
    import re
    value=str(error)
    if mode=="ingest" and value in INGEST_FAILURE_CODES:return value
    if mode == "pair" and value in (
            "SCALE_PAIRED_RUN", "SCALE_PAIRED_PUBLICATION", "SCALE_PAIRED_FRAME",
            "SCALE_PAIRED_FRAME_ROWS", "SCALE_PAIRED_FRAME_DUPLICATE",
            "SCALE_PAIRED_PREFULL_INPUT_DIFFERENCE", "SCALE_PAIRED_RAW_SCORE_DIFFERENCE",
            "SCALE_V8_OPERATIONAL_RECEIPT", "SCALE_V8_OPERATIONAL_MODEL",
            "SCALE_V8_OPERATIONAL_RUN", "SCALE_V8_OPERATIONAL_DECISION",
            "SCALE_V8_OPERATIONAL_GOLD", "SCALE_V8_OPERATIONAL_LINK",
            "SCALE_V8_OPERATIONAL_PROGRESSIVE", "SCALE_V8_OPERATIONAL_STAGES",
            "SCALE_V8_OPERATIONAL_DIFFERENCE", "SCALE_V8_OPERATIONAL_MUTATION_NOT_DETECTED",
            "SCALE_V8_OPERATIONAL_DECIMAL"):
        return value
    if re.fullmatch(r"V9_E2E_ORACLE_[A-Z0-9_]{1,96}",value):return value
    return "UNCLASSIFIED"

def sql_failure_summary(lines):
    import re
    stages={"BASELINE","MIGRATED_V1","MIGRATED_V2","ROLLBACK_V2","ROLLBACK_V1","REAPPLY_V1","REAPPLY_V2","MIGRATED_V3","ROLLBACK_V3","REAPPLY_V3"}
    fixtures={"NEGATIVE_GUARDS","CURRENT_MULTISET","CANONICAL_NAME","RECOMPOSITION"}
    result=[];errors=0
    for line in lines:
        line=line.strip()
        prefix,_,value=line.partition("=")
        if ((prefix=="V9_SQL_REGRESSION_STAGE" and value in stages) or
            (prefix=="V9_SQL_FIXTURE_STAGE" and value in fixtures)):
            result.append(prefix+"="+value)
        match=re.match(r"^Msg ([0-9]{1,6}), Level ([0-9]{1,2}), State ([0-9]{1,3})(?:,|$)",line)
        if match:
            result.append("V9_SQL_ERROR="+"_".join(match.groups()));errors+=1
        if len(result)>=64:break
    if not errors:result.append("V9_SQL_ERROR=UNCLASSIFIED")
    return result

def main():
    p=argparse.ArgumentParser();p.add_argument("mode",choices=("preflight","ingest","diagnostic","pair","sql-probe","sql-failure"));p.add_argument("--root",type=Path,required=True);a=p.parse_args()
    try:
        label,(n,_,_)=profile()
        if a.mode=="preflight":preflight(a.root)
        elif a.mode=="ingest":H.ingest(a.root,uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"]),population=n)
        elif a.mode=="diagnostic":diagnostic_receipt(a.root)
        elif a.mode=="pair":pair_receipt(a.root)
        elif a.mode=="sql-probe":sql_probe_receipt(a.root)
        elif a.mode=="sql-failure":
            path=a.root/"publication-regression.log"
            require(path.is_file() and not path.is_symlink() and path.stat().st_size<=16*1024*1024,"SCALE_SQL_LOG_BOUND")
            with path.open(encoding="utf-8",errors="replace") as f:
                for line in sql_failure_summary(f):print(line)
            return
    except (ValueError,OSError,KeyError,TypeError,AttributeError,ArithmeticError) as e:
        # Fixed internal code only; never arbitrary input or stack.
        code=safe_failure_code(a.mode,e)
        raise SystemExit("V9_PHASE4_SCALE_REJECTED_"+code) from None
    print("V9_PHASE4_SCALE_"+a.mode.upper()+"_PASS")
if __name__=="__main__":main()

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
          "optimized2k":(2000,355,"clean"),"optimized4k":(4000,355,"clean"),
          "optimized10k":(10000,355,"clean")}
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
    original = None
    if n in (4000, 10000):
        corpus = root / "fixture" / "corpus"
        W.BRIDGE.PEOPLE = n
        W.BRIDGE.MAX_SOURCE_CSV_BYTES = 128 * 1024 * 1024
        original = W.BRIDGE.source_observation_truth(corpus, read(corpus / "generation-manifest.json"))
    for i in (1,2):
        d=root/"fixture"/"ingestion"/f"wave-{i:02}"/"delivery"
        raw=I.read(d/"bridge-truth.jsonl")
        waves.append((read(d/"bridge-manifest.json"),raw))
        for line in raw.splitlines():
            x=I.load(line);k=I.version_key(x["GestorCodigo"],x["PackageFileName"],x["OpaquePersonId"])
            if original is not None:
                source, sep, wave = x["ObservationId"].rpartition("-W")
                require(sep and wave == str(i) and original.get(source) ==
                        (x["BasePersonId"], x["Partition"], x["SyntheticGestor"])
                        and W.ROUTES[x["SyntheticGestor"]] == x["GestorCodigo"], "SCALE_TRACE_SOURCE_JOIN")
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

V8_DECISION_FAILURE_FIELDS=(
    "decisionCollection","decisionRowCount","decisionFieldSet",
    "scenarioCoverage",
    "observationId","status","reason","resolvedUuid","bestUuid","bestScore",
    "secondUuid","secondScore","margin","publicationResult","publicationStatus",
    "publishedUuid","publicationReason","publishedOriginId","progressiveVersion",
    "publicationPolicy","universeReference")
V8_DECISION_FAILURE_FIELD_SET=frozenset(V8_DECISION_FAILURE_FIELDS)
V8_DECISION_ERROR_CODE="V9_E2E_ORACLE_SCALE_V8_OPERATIONAL_DECISION"
V8_DECIMAL_ERROR_CODE="V9_E2E_ORACLE_SCALE_V8_OPERATIONAL_DECIMAL"
V8_LINK_FAILURE_FIELDS=(
    "linkCollection","linkRowCount","linkFieldSet","observationCoverage",
    "method","status","reason","exactRun","modelId","personUuid","score")
V8_LINK_FAILURE_FIELD_SET=frozenset(V8_LINK_FAILURE_FIELDS)
V8_LINK_ERROR_CODE="V9_E2E_ORACLE_SCALE_V8_OPERATIONAL_LINK"

class V8DecisionPredicateError(ValueError):
    """Carry only fixed field labels; never receipt values or private identifiers."""
    def __init__(self,fields,error_code=V8_DECISION_ERROR_CODE):
        fields=tuple(fields)
        if (not fields or len(fields)!=len(set(fields))
                or any(field not in V8_DECISION_FAILURE_FIELD_SET for field in fields)
                or tuple(sorted(fields,key=V8_DECISION_FAILURE_FIELDS.index))!=fields
                or error_code not in (V8_DECISION_ERROR_CODE,V8_DECIMAL_ERROR_CODE)):
            raise ValueError(V8_DECISION_ERROR_CODE)
        self.fields=fields
        super().__init__(error_code)

class V8LinkPredicateError(ValueError):
    """Carry only fixed link predicate labels; never receipt values."""
    def __init__(self,fields):
        fields=tuple(fields)
        if (not fields or len(fields)!=len(set(fields))
                or any(field not in V8_LINK_FAILURE_FIELD_SET for field in fields)
                or tuple(sorted(fields,key=V8_LINK_FAILURE_FIELDS.index))!=fields):
            raise ValueError(V8_LINK_ERROR_CODE)
        self.fields=fields
        super().__init__(V8_LINK_ERROR_CODE)

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
         "model","run","decisions","gold","currentLinks","progressiveOrigins"}
    require(isinstance(receipt,dict) and set(receipt)==top
            and receipt["schema"]=="V8_OPERATIONAL_REGRESSION_PRIVATE_V2"
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
            and model["ruleSetFingerprintSha256"]=="017a20967264e5de2351de3c4b1ebe0ef98f0fbe0d16d92da392ee63176a82fd"
            and model["projectionSchemaVersion"]=="PERSON_RESOLUTION_PROJECTION_V2"
            and model["projectionFingerprintSha256"]=="d186f28c51e18802f7c2df5b8b192b6278d28874833c8d824d483608aa3abbb4",
            "SCALE_V8_OPERATIONAL_MODEL")
    run=receipt["run"]
    run_fields={"mode","status","observationId","highWatermark","eligible","evaluated",
                "resolved","unresolved","conflicts","noCandidateInBlock","batchSize",
                "maxParallelism","requestedBy","reason"}
    require(isinstance(run,dict) and set(run)==run_fields and run["mode"]=="ON_DEMAND"
            and run["status"]=="PUBLICADO" and run["observationId"] is None
            and type(run["highWatermark"]) is int and run["highWatermark"]>0
            and run["eligible"]==run["evaluated"]==4
            and run["resolved"]==1 and run["unresolved"]==2
            and run["conflicts"]==1 and run["noCandidateInBlock"]==1
            and run["batchSize"]==100 and run["maxParallelism"]==1
            and run["requestedBy"]=="CI_V8_OPERATIONAL_REGRESSION"
            and run["reason"]=="governed-v8-operational-publication-regression",
            "SCALE_V8_OPERATIONAL_RUN")
    decisions=receipt["decisions"]
    decision_fields={"scenario","observationId","resolvedUuid","bestUuid","bestScore","secondUuid",
        "secondScore","margin","status","reason","publicationResult","publishedUuid",
        "publicationStatus","publicationReason","publishedOriginId","progressiveVersion",
        "publicationPolicy","universeReference"}
    if not isinstance(decisions,list):
        raise V8DecisionPredicateError(("decisionCollection",))
    if len(decisions)!=4:
        raise V8DecisionPredicateError(("decisionRowCount",))
    if any(not isinstance(row,dict) or set(row)!=decision_fields for row in decisions):
        raise V8DecisionPredicateError(("decisionFieldSet",))
    scenarios=("RESOLVED","CONFLICT","BELOW_THRESHOLD","NO_CANDIDATE")
    if ({row["scenario"] for row in decisions}!=set(scenarios)
            or len({row["scenario"] for row in decisions})!=4
            or len({row["observationId"] for row in decisions})!=4):
        raise V8DecisionPredicateError(("scenarioCoverage",))
    by_scenario={row["scenario"]:row for row in decisions}
    # The product persists uniqueidentifier text in SQL Server's canonical
    # uppercase representation. Keep the observed value untouched and compare
    # it textually and exactly against that physical representation.
    expected_universe=(f"LINKAGE_RUN:{receipt['runId'].upper()};HW:{run['highWatermark']};"
                       f"ELIGIVEIS:{run['eligible']}")
    decimal_malformed=False
    def decimal_value(value):
        nonlocal decimal_malformed
        try:return persisted_decimal(value)
        except (ValueError,TypeError,ArithmeticError):
            decimal_malformed=True
            return None
    scores={row["scenario"]:decimal_value(row["bestScore"]) for row in decisions}
    second_scores={row["scenario"]:(None if row["secondScore"] is None
        else decimal_value(row["secondScore"])) for row in decisions}
    margins={row["scenario"]:(None if row["margin"] is None
        else decimal_value(row["margin"])) for row in decisions}
    resolved=by_scenario["RESOLVED"]
    conflict=by_scenario["CONFLICT"]
    below=by_scenario["BELOW_THRESHOLD"]
    none=by_scenario["NO_CANDIDATE"]
    checks=(
        ("observationId",all(type(row["observationId"]) is int
            and 0<row["observationId"]<=run["highWatermark"] for row in decisions)),
        ("status",resolved["status"]=="RESOLVIDO" and conflict["status"]=="CONFLITO"
            and below["status"]==none["status"]=="NAO_RESOLVIDO"),
        ("reason",resolved["reason"] is None
            and conflict["reason"]=="MARGEM_ENTRE_CANDIDATOS_INSUFICIENTE"
            and below["reason"]=="ABAIXO_T_LINKAGE"
            and none["reason"]=="SEM_CANDIDATO_NO_RULESET_BLOCKING"),
        ("resolvedUuid",valid_uuid(resolved["resolvedUuid"])
            and all(row["resolvedUuid"] is None for row in (conflict,below,none))),
        ("bestUuid",resolved["bestUuid"]==resolved["resolvedUuid"]
            and valid_uuid(conflict["bestUuid"]) and valid_uuid(below["bestUuid"])
            and none["bestUuid"] is None),
        ("bestScore",not decimal_malformed
            and scores["RESOLVED"] is not None and scores["RESOLVED"]>=Decimal("0.95")
            and scores["CONFLICT"] is not None and scores["CONFLICT"]>=Decimal("0.95")
            and scores["BELOW_THRESHOLD"] is not None
            and Decimal(0)<=scores["BELOW_THRESHOLD"]<Decimal("0.95")
            and scores["NO_CANDIDATE"]==0),
        ("secondUuid",valid_uuid(conflict["secondUuid"])
            and all(row["secondUuid"] is None for row in (resolved,below,none))),
        ("secondScore",second_scores["CONFLICT"]==scores["CONFLICT"]
            and all(second_scores[key] is None for key in ("RESOLVED","BELOW_THRESHOLD","NO_CANDIDATE"))),
        ("margin",margins["CONFLICT"]==0
            and all(margins[key] is None for key in ("RESOLVED","BELOW_THRESHOLD","NO_CANDIDATE"))),
        ("publicationResult",resolved["publicationResult"]=="ASSOCIACAO_EXISTENTE"
            and conflict["publicationResult"]==below["publicationResult"]=="INDEFINIDA"
            and none["publicationResult"]=="NOVA_IDENTIDADE"),
        ("publicationStatus",resolved["publicationStatus"]==none["publicationStatus"]=="RESOLVIDO"
            and conflict["publicationStatus"]=="CONFLITO"
            and below["publicationStatus"]=="NAO_RESOLVIDO"),
        ("publishedUuid",resolved["publishedUuid"]==resolved["resolvedUuid"]
            and valid_uuid(none["publishedUuid"])
            and conflict["publishedUuid"] is None and below["publishedUuid"] is None),
        ("publicationReason",resolved["publicationReason"]=="ASSOCIACAO_EXISTENTE_LINKAGE"
            and conflict["publicationReason"]==conflict["reason"]
            and below["publicationReason"]==below["reason"]
            and none["publicationReason"]=="NOVA_IDENTIDADE_APOS_BUSCA_COMPLETA"),
        ("publishedOriginId",all(type(row["publishedOriginId"]) is int
            and row["publishedOriginId"]>0 for row in decisions)
            and len({row["publishedOriginId"] for row in decisions})==4),
        ("progressiveVersion",all(type(row["progressiveVersion"]) is int
            and row["progressiveVersion"]==1 for row in decisions)),
        ("publicationPolicy",all(row["publicationPolicy"]=="LINKAGE_PROGRESSIVE_PUBLICATION_V1"
            for row in decisions)),
        ("universeReference",all(row["universeReference"]==expected_universe for row in decisions)))
    failures=tuple(field for field,passed in checks if not passed)
    if failures:
        # Preserve the original short-circuit code: malformed decimal won only
        # when every preceding decision predicate passed. Diagnostics still
        # list every failed fixed field without exposing receipt values.
        error_code=(V8_DECIMAL_ERROR_CODE
            if decimal_malformed and failures[0] in ("bestScore","secondScore","margin")
            else V8_DECISION_ERROR_CODE)
        raise V8DecisionPredicateError(failures,error_code)
    gold=receipt["gold"]
    gold_fields={"personUuid","cpf","cpfStatus","fullName","birthDate","motherName",
        "distinctSources","concordanceState","identityState","nucleusCompleteness",
        "normalizedPublicationName","publicationNameMethodVersion",
        "publicationNameNormalizationVersion"}
    require(isinstance(gold,list) and gold and all(isinstance(row,dict) and set(row)==gold_fields
            and valid_uuid(row["personUuid"]) for row in gold)
            and len({row["personUuid"] for row in gold})==len(gold)
            and all(value in {row["personUuid"] for row in gold}
                for value in (resolved["resolvedUuid"],conflict["bestUuid"],conflict["secondUuid"],
                              below["bestUuid"],none["publishedUuid"])),
            "SCALE_V8_OPERATIONAL_GOLD")
    links=receipt["currentLinks"]
    link_fields={"observationId","personUuid","method","score","status","reason","modelId","exactRun"}
    decision_by_observation={row["observationId"]:row for row in decisions}
    if not isinstance(links,list):
        raise V8LinkPredicateError(("linkCollection",))
    if len(links)!=4:
        raise V8LinkPredicateError(("linkRowCount",))
    if any(not isinstance(link,dict) or set(link)!=link_fields for link in links):
        raise V8LinkPredicateError(("linkFieldSet",))
    if ({link["observationId"] for link in links}!=set(decision_by_observation)
            or len({link["observationId"] for link in links})!=4):
        raise V8LinkPredicateError(("observationCoverage",))
    def exact_link_score(link,decision):
        expected=(decision["bestScore"]
            if decision["publicationResult"]=="ASSOCIACAO_EXISTENTE"
                and decision["status"]=="RESOLVIDO"
                and decision["resolvedUuid"]==decision["publishedUuid"]
            else None)
        if expected is None:
            return link["score"] is None
        try:
            return persisted_decimal(link["score"])==persisted_decimal(expected)
        except (ValueError,TypeError,ArithmeticError):
            return False
    link_checks=(
        ("method",all(link["method"]=="LINKAGE_PROBABILISTICO" for link in links)),
        ("status",all(link["status"]==
            decision_by_observation[link["observationId"]]["publicationStatus"]
            for link in links)),
        ("reason",all(link["reason"]==
            decision_by_observation[link["observationId"]]["publicationReason"]
            for link in links)),
        ("exactRun",all(link["exactRun"] is True for link in links)),
        ("modelId",all(link["modelId"]==model["modelId"] for link in links)),
        ("personUuid",all(link["personUuid"]==
            decision_by_observation[link["observationId"]]["publishedUuid"] for link in links)),
        ("score",all(exact_link_score(link,decision_by_observation[link["observationId"]])
            for link in links)))
    link_failures=tuple(field for field,passed in link_checks if not passed)
    if link_failures:
        raise V8LinkPredicateError(link_failures)
    progressive=receipt["progressiveOrigins"]
    progressive_fields={"originId","initialUuid","canonicalUuid","legacyUuid","state","version",
                        "lastExternalTargetUuid"}
    require(isinstance(progressive,list) and len(progressive)==4
            and all(isinstance(row,dict) and set(row)==progressive_fields
                and type(row["originId"]) is int and row["originId"]>0
                and valid_uuid(row["initialUuid"]) for row in progressive),
            "SCALE_V8_OPERATIONAL_PROGRESSIVE")
    progressive_by_origin={row["originId"]:row for row in progressive}
    require(len(progressive_by_origin)==4
            and all(row["publishedOriginId"] in progressive_by_origin for row in decisions)
            and progressive_by_origin[resolved["publishedOriginId"]]["canonicalUuid"]==resolved["publishedUuid"]
            and progressive_by_origin[resolved["publishedOriginId"]]["state"]=="REFERENCIA"
            and progressive_by_origin[none["publishedOriginId"]]["canonicalUuid"]==none["publishedUuid"]
            and progressive_by_origin[none["publishedOriginId"]]["initialUuid"]==none["publishedUuid"]
            and progressive_by_origin[none["publishedOriginId"]]["state"]=="REFERENCIA"
            and all(progressive_by_origin[row["publishedOriginId"]]["canonicalUuid"] is None
                and progressive_by_origin[row["publishedOriginId"]]["state"]=="INDEFINIDA"
                for row in (conflict,below))
            and all(progressive_by_origin[row["publishedOriginId"]]["version"]==row["progressiveVersion"]
                for row in decisions),
            "SCALE_V8_OPERATIONAL_PROGRESSIVE")
    canonical=json.loads(json.dumps(receipt,sort_keys=True,allow_nan=False))
    canonical.pop("schemaStage");canonical.pop("runId")
    for row in canonical["decisions"]:
        row["universeReference"]=f"HW:{run['highWatermark']};ELIGIVEIS:{run['eligible']}"
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
        operationalV8Scenarios=sorted(row["scenario"] for row in a["decisions"]),
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
        schema="V9_RESTORED_PREFULL_PAIR_V3", commit=os.environ["JORNADA_E2E_EXACT_SHA"],
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

def comparison_labels(left, right, fields, *, directional=False):
    """Compare privately. Empty/missing values do not constitute agreement."""
    labels = []
    for field in fields:
        a, b = left[field], right[field]
        if a in (None, "") or b in (None, ""):
            if a in (None, "") and b in (None, ""):
                label = "bothMissing"
            elif directional:
                label = "sourceMissing" if a in (None, "") else "evidenceMissing"
            else:
                label = "oneMissing"
        else:
            label = "equal" if a == b else "different"
        labels.append(label)
    return tuple(labels)


def fusion_inputs(root, selected_bases):
    # truth_frame has verified the generator CSV hashes against its manifest.
    import csv
    corpus = root / "fixture" / "corpus" / "pessoas_verdade.csv"
    with corpus.open(encoding="utf-8-sig", newline="") as source:
        people = {row["base_person_id"]: row for row in csv.DictReader(source)
                  if row["base_person_id"] in selected_bases}
    require(people.keys() == selected_bases, "SCALE_FUSION_BASE_CENSUS")
    raw = I.read(root / "selection" / (P.WINNER + ".operational.json"))
    require(hashlib.sha256(raw).hexdigest() == P.WINNER, "SCALE_FUSION_WINNER")
    pinned = json.loads(raw, parse_float=Decimal, object_pairs_hook=I.strict_pairs)
    return people, pinned["Threshold"], pinned["ConflictMargin"]


def fusion_profiles(root, groups, truth, prior, ranked, strongest, bases_by_ref):
    """Histogram of anonymous fusion patterns. No version, identity or attribute values leave the attempt."""
    from itertools import combinations
    truth_fields = ("nome", "nome_mae", "data_nascimento", "sexo", "cpf", "cns")
    score_fields = ("NomeCompleto", "NomeMae", "DataNascimento")
    mixed = {dest: ids for dest, ids in groups.items() if len({truth[i]["base"] for i in ids}) > 1}
    bases = {truth[i]["base"] for ids in mixed.values() for i in ids}
    people, threshold, cutoff = fusion_inputs(root, bases)
    patterns = Counter()
    for dest, ids in mixed.items():
        sizes = Counter(truth[i]["base"] for i in ids)
        truth_types = Counter(comparison_labels(people[a], people[b], truth_fields)
                              for a, b in combinations(sizes, 2))
        decision_types = Counter()
        for i in ids:
            rank = ranked.get(i)
            if (not rank or rank["Status"] != "RESOLVIDO" or rank["Resolved"] != dest
                    or not bases_by_ref.get(dest) or truth[i]["base"] in bases_by_ref[dest]):
                continue
            best_log, negative_record = strongest[i, dest]
            record = -negative_record
            second = rank["Second"]
            second_log = strongest[i, second][0] if second is not None else None
            margin = rank["Margin"]
            require(rank["Best"] == dest and rank["BestScore"] >= threshold
                    and rank["PeopleWithoutScoreableEvidence"] == 0
                    and ((second is None and margin is None)
                         or (second_log is not None and margin == best_log - second_log and margin >= cutoff)),
                    "SCALE_FUSION_RESOLVED_RANK")
            observed = comparison_labels(prior[i]["Observation"], prior[record]["Observation"], score_fields, directional=True)
            decision_types[(observed, decimal_text(best_log), decimal_text(rank["BestScore"]),
                            decimal_text(second_log), decimal_text(margin), rank["CandidateCount"])] += 1
        # Sort serialized aggregate profiles, independent of UUID and SQL observation order.
        pattern = dict(versions=len(ids), bases=len(sizes),
                       falsePairs=sum(a*b for a,b in combinations(sizes.values(), 2)),
                       truthComparisons=[dict(labels=list(labels), basePairs=n)
                                         for labels,n in sorted(truth_types.items())],
                       foreignDecisions=[dict(observed=list(labels), bestLogOdds=best, bestPosterior=p,
                                              secondLogOdds=second, margin=gap, candidateCount=count,
                                              versions=n)
                                         for (labels,best,p,second,gap,count),n in decision_types.items()])
        pattern["foreignDecisions"].sort(key=lambda row: json.dumps(row, sort_keys=True))
        patterns[json.dumps(pattern, sort_keys=True)] += 1
    return dict(truthFields=list(truth_fields), observedFields=list(score_fields),
                threshold=decimal_text(threshold), conflictMargin=decimal_text(cutoff),
                patterns=[dict(json.loads(pattern), goldCount=count) for pattern,count in sorted(patterns.items())])


def quality_trace(census, truth, before, ranks, graphs, identity, candidate_count, scores, fusion_root=None):
    """Post-FULL only. All ID-indexed collections stay inside the disposable attempt."""
    by_id = {row["observationId"]: row for row in census}
    prior = {row["Id"]: row for row in before}
    ranked = {row["Id"]: row for row in ranks}
    eligible = {i for i, t in truth.items() if not t["cpf"]}
    require(len(prior) == len(before) == len(by_id) == len(census)
            and prior.keys() == by_id.keys() == truth.keys()
            and len(ranked) == len(ranks) and ranked.keys() == eligible
            and {row["observationId"] for row in graphs} == eligible
            and len(graphs) == len(eligible), "SCALE_TRACE_UNIVERSE")

    bases_by_ref = defaultdict(set)
    refs_by_base = defaultdict(set)
    current_groups = defaultdict(list)
    for i, row in by_id.items():
        prev, t = prior[i], truth[i]
        require((prev["Version"], prev["Manager"], prev["Wave"], prev["Origin"], prev["HasCpf"])
                == (row["version"], t["manager"], t["wave"], t["origin"], t["cpf"]),
                "SCALE_TRACE_VERSION_JOIN")
        if prev["Status"] == "RESOLVIDO":
            ref = prev["Current"]
            require(ref is not None, "SCALE_TRACE_PRIOR")
            bases_by_ref[ref].add(t["base"])
            refs_by_base[t["base"]].add(ref)
        if row["currentStatus"] == "RESOLVIDO":
            current_groups[row["currentUuid"]].append(i)

    count_with = 0
    extra_candidates = 0
    incidences = Counter()
    candidate_sets = {}
    for graph in graphs:
        i = graph["observationId"]
        candidates = set(graph["candidates"])
        require(len(candidates) == len(graph["candidates"]), "SCALE_TRACE_DUPLICATE_CANDIDATE")
        candidate_sets[i] = candidates
        if candidates:
            count_with += 1
            extra_candidates += len(candidates) - 1
        for ref in candidates:
            bases = bases_by_ref.get(ref)
            if not bases:
                kind = "unmappedReference"
            elif truth[i]["base"] not in bases:
                kind = "foreignBase"
            elif len(bases) > 1:
                kind = "mixedReference"
            else:
                kind = "ownBase"
            incidences[kind] += 1
    require(sum(incidences.values()) == candidate_count == count_with + extra_candidates,
            "SCALE_TRACE_CANDIDATE_TOTAL")

    def cross_base_pairs(ids):
        sizes = Counter(truth[i]["base"] for i in ids)
        return math.comb(sum(sizes.values()), 2) - sum(math.comb(n, 2) for n in sizes.values())

    # Same (descending LogOdds, ascending record ID) order already checked
    # against the scalar oracle. Only count field agreement for foreign winners.
    strongest = {}
    for score in scores:
        if score["LogOdds"] is None:
            continue
        key = (score["Source"], score["Person"])
        order = (score["LogOdds"], -score["Record"])
        if key not in strongest or order > strongest[key]:
            strongest[key] = order
    agreements = Counter()
    stage = dict(preFull=0, rawDecision=0, publication=0)
    affected = set()
    fused = 0
    affected_versions = 0
    paths = Counter()
    wrong_with_own = wrong_without_candidate = wrong_without_ref = 0
    for dest, ids in current_groups.items():
        pairs = cross_base_pairs(ids)
        if not pairs:
            continue
        fused += 1
        affected_versions += len(ids)
        affected.update(truth[i]["base"] for i in ids)
        pre_members = []
        ledger_members = []
        for i in ids:
            if prior[i]["Current"] == dest and prior[i]["Status"] == "RESOLVIDO":
                pre_members.append(i)
                ledger_members.append(i)
                paths["preFull"] += 1
            elif i in ranked and ranked[i]["Resolved"] == dest and ranked[i]["Status"] == "RESOLVIDO":
                ledger_members.append(i)
                paths["rawResolvedPublished"] += 1
                base = truth[i]["base"]
                require(ranked[i]["Best"] == dest and dest in candidate_sets[i],
                        "SCALE_TRACE_RAW_RANK")
                if dest in bases_by_ref and base not in bases_by_ref[dest]:
                    record_id = -strongest[i, dest][1]
                    source = prior[i]["Observation"]
                    evidence = prior[record_id]["Observation"]
                    for field in ("NomeCompleto", "NomeMae", "DataNascimento"):
                        if source[field] is not None and source[field] == evidence[field]:
                            agreements[field] += 1
                    if refs_by_base[base] & candidate_sets[i]:
                        wrong_with_own += 1
                    elif refs_by_base[base]:
                        wrong_without_candidate += 1
                    else:
                        wrong_without_ref += 1
            else:
                row = by_id[i]
                if row["runResult"] == "NOVA_IDENTIDADE" and row["publishedUuid"] == prior[i]["Initial"]:
                    paths["publishedOriginFounder"] += 1
                elif row["runResult"] == "ASSOCIACAO_EXISTENTE" and row["publishedUuid"] == dest:
                    paths["publishedAssociationWithoutMatchingRawResolution"] += 1
                else:
                    paths["otherCurrentResolution"] += 1
        previous = cross_base_pairs(pre_members)
        ledger = cross_base_pairs(ledger_members)
        stage["preFull"] += previous
        stage["rawDecision"] += ledger - previous
        stage["publication"] += pairs - ledger
    require(sum(stage.values()) == identity["pairMetrics"]["fp"]
            and fused == identity["falseFusedGoldIdentities"], "SCALE_TRACE_PAIR_TOTAL")
    details = {}
    if fusion_root is not None:
        details["fusionProfiles"] = fusion_profiles(fusion_root, current_groups, truth, prior, ranked, strongest, bases_by_ref)
        measured = sum(p["goldCount"] * sum(row["versions"] for row in p["foreignDecisions"])
                       for p in details["fusionProfiles"]["patterns"])
        require(measured == wrong_with_own + wrong_without_candidate + wrong_without_ref, "SCALE_FUSION_COVERAGE")
    return dict(**details, fusedGold=fused, affectedBases=len(affected), affectedVersions=affected_versions,
                pairsByStage=stage, affectedVersionsByPath=dict(paths),
                selectedForeignWithOwnCandidate=wrong_with_own,
                selectedForeignWithoutOwnCandidate=wrong_without_candidate,
                selectedForeignWithoutOwnReference=wrong_without_ref,
                foreignWinnerExactFieldAgreement=dict(agreements),
                preFullReferencesWithResolvedVersions=len(bases_by_ref),
                preFullMixedReferences=sum(len(bases) > 1 for bases in bases_by_ref.values()),
                observationsWithCandidates=count_with, additionalCandidateIncidences=extra_candidates,
                candidateIncidences=dict(incidences))


def pre_full_cpf_pairs(root, census, truth, before):
    """Compare delivered CPF values privately only for pre-existing mixed references."""
    from io import BytesIO
    from zipfile import ZipFile
    prior = defaultdict(list)
    for row in before:
        if row["Status"] == "RESOLVIDO":
            prior[row["Current"]].append(row["Id"])
    mixed = {dest: ids for dest, ids in prior.items() if len({truth[i]["base"] for i in ids}) > 1}
    by_id = {row["observationId"]: row for row in census}
    wanted = {}
    for dest, ids in mixed.items():
        for i in ids:
            row = by_id[i]
            wanted[(row["manager"], row["package"], row["opaque"])] = (dest, truth[i]["base"])
    counts = Counter()
    totals = Counter()
    seen = set()
    for wave in read(root / "wave-preflight-receipt.json")["packagesByWave"]:
        folder = root / "fixture" / "ingestion" / f"wave-{wave['wave']:02}" / "delivery"
        for item in wave["packages"]:
            name = item["fileName"]
            require(Path(name).name == name, "SCALE_TRACE_PACKAGE")
            raw = I.read(folder / name)
            require(hashlib.sha256(raw).hexdigest().upper() == item["zipSha256"], "SCALE_TRACE_PACKAGE")
            with ZipFile(BytesIO(raw)) as archive:
                for line in archive.read("pessoas.jsonl").splitlines():
                    person = I.load(line)
                    key = (item["manager"], name, person["codigoPessoaOrigem"])
                    if key in wanted:
                        dest, base = wanted[key]
                        cpf = person.get("cpf")
                        require(key not in seen and isinstance(cpf, str) and cpf, "SCALE_TRACE_CPF")
                        seen.add(key)
                        counts[dest, cpf, base] += 1
                        totals[dest, cpf] += 1
    require(seen == wanted.keys(), "SCALE_TRACE_CPF_COVERAGE")
    equal = sum(math.comb(n, 2) for n in totals.values()) - sum(math.comb(n, 2) for n in counts.values())
    all_pairs = sum(math.comb(len(ids), 2) - sum(math.comb(n, 2) for n in
                    Counter(truth[i]["base"] for i in ids).values()) for ids in mixed.values())
    return dict(equalDeliveredCpf=equal, differentDeliveredCpf=all_pairs-equal)


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
    scores = read_exact(root/"scale-gold-scores-private.json")
    gold=gold_rank_diagnostics(scores,ranks,graphs,
        key_byid,target,actual_graph["goldRecordScores"],independent_labels)
    independent_rows = [dict(row, **independent_labels[row["Id"]]) for row in rank_rows]

    def graph_digest(rows):
        return hashlib.sha256(json.dumps(dict(
            graph=sorted(graph_rows,key=lambda x:x["key"]), ranks=sorted(rows,key=lambda x:x["Id"])),
            sort_keys=True,separators=(",",":"),allow_nan=False).encode()).hexdigest()

    trace = {}
    if label in {"optimized4k", "optimized10k"}:
        frame = read(root / "matrix-frame-private.json")
        require(frame["Attempt"] == os.environ["JORNADA_V9_DEV_ATTEMPT"], "SCALE_TRACE_ATTEMPT")
        trace["qualityTrace"] = quality_trace(census, truth, frame["Rows"], ranks, graphs,
                                              identity, member["candidatePeople"], scores,
                                              fusion_root=root if label == "optimized10k" else None)
        if label == "optimized10k":
            experiment = module("abstention", "v9-phase4-abstention.py")
            audit_inputs = ("runner-receipt.json", "sql-census.json", "matrix-frame-private.json",
                            "scale-rank-private.json", "fn-private.json", "scale-gold-scores-private.json",
                            "abstention-pairs-private.json", "membership-receipt.json", "actual-graph-receipt.json")
            def input_hashes():
                return {name: hashlib.sha256(I.read(root / name)).hexdigest() for name in audit_inputs}
            sealed = input_hashes()
            pairs = read_exact(root / "abstention-pairs-private.json")
            require(pairs["attempt"] == frame["Attempt"] and pairs["runId"] == runner["runId"]
                    and actual_graph["runId"] == runner["runId"], "ABSTENTION_PROVENANCE")
            replay = experiment.replay(census, frame["Rows"], ranks, graphs, scores, pairs["pairs"],
                                       member, I.PROTECTED)
            trace["abstentionExperiment"] = experiment.evaluate(replay, truth, frame["Rows"], identity)
            require(input_hashes() == sealed, "ABSTENTION_INPUT_MUTATION")
            trace["abstentionExperiment"]["privateInputSha256"] = sealed
        if trace["qualityTrace"]["preFullMixedReferences"]:
            trace["qualityTrace"]["preFullCrossBasePairs"] = pre_full_cpf_pairs(root, census, truth, frame["Rows"])
    write(root/"diagnostic-receipt.json",dict(schema="V9_DEV_FULL_SCALE_DIAGNOSTIC_V1",
        profile=label,population=n,versions=len(census),eligible=runner["eligible"],
        commit=os.environ["JORNADA_E2E_EXACT_SHA"],winnerSha256=P.WINNER,
        fullCompleted=True,independentMembershipVerified=True,identityCensusStatus=identity["status"],
        pairMetrics=identity["pairMetrics"],physicalFull=full,normalizedPublicationSha256=parity_digest,normalizedDecisionGraphSha256=graph_digest(rank_rows),
        normalizedIndependentDecisionGraphSha256=graph_digest(independent_rows),
        independentDecisionFieldSha256=decision_field_digests(graph_rows,independent_rows),
        decisionFieldSha256=decision_field_digests(graph_rows,rank_rows),goldRankDiagnostics=gold,
        ingestElapsedMs=ingest_ms,censusElapsedMs=census_timing["elapsedMs"],runnerSql=instrumentation,
        matrixExecuted=False,scientificTest=False,activation=False, **trace))
# Explicit fixed diagnostic vocabulary. Never export exceptions or SQL text.
INGEST_FAILURE_CODES=frozenset((
    "VOLUME_ADMISSION","WAVE_PREFLIGHT_REQUIRED","DIRTY_SILVER_ATTEMPT","DIRTY_RUN_ATTEMPT",
    "PACKAGE_RECEIPT_INVALID","PACKAGE_SIZE_DRIFT","PACKAGE_BYTES_DRIFT","HTTP_INGESTION_NOT_ACCEPTED",
    "HTTP_STATUS_UNAVAILABLE","PROCESSOR_REJECTED","PROCESSOR_TIMEOUT","SILVER_SOURCE_SET_DIVERGENCE",
    "BRONZE_LINEAGE_DIVERGENCE","HTTP_IDEMPOTENCY_DIVERGENCE","SILVER_COMPLETE_CENSUS_DIVERGENCE",
    "HTTP_DELIVERY_IDEMPOTENCY_CENSUS","PRIVATE_JSON_INVALID","MANAGER_CREDENTIAL_REQUIRED",
    "MANAGER_INVALID","HTTP_RESPONSE_BOUND","CI_CONTAINER_INVALID","SQL_LINEAGE_QUERY_FAILED",
    "SQL_LINEAGE_SCALAR_INVALID"))
V8_RUNNER_FAILURE_CODES=frozenset((
    "RUNNER_V9_SELECTION_VERSION_MISMATCH","RUNNER_V9_SELECTION_MISSING",
    "RUNNER_MODEL_CONFIGURATION_MISSING","RUNNER_MODEL_CONFIGURATION_MISMATCH",
    "RUNNER_MODEL_CONTRACT_INCOMPATIBLE","RUNNER_MODEL_ALREADY_PINNED",
    "RUNNER_MODEL_NOT_PREFLIGHTED","RUNNER_MODEL_PINNED_VERSION_MISMATCH",
    "RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED"))
V8_RUNNER_FAILURE_MARKERS=(
    ("ConnectionStrings:Jornada não configurada.", "CONNECTION_CONFIGURATION_MISSING"),
    ("Format of the initialization string does not conform", "CONNECTION_CONFIGURATION_INVALID"),
    ("The ConnectionString property has not been initialized.", "CONNECTION_CONFIGURATION_INVALID"),
    ("Keyword not supported:", "CONNECTION_CONFIGURATION_INVALID"),
    ("Microsoft.Data.SqlClient.SqlException", "SQL_CLIENT_FAILURE"),
    ("System.Data.SqlClient.SqlException", "SQL_CLIENT_FAILURE"),
    ("ExecuteReader requires an open and available Connection", "SQL_CLIENT_FAILURE"),
    ("Modelo probabilístico v", "MODEL_VERSION_NOT_CONSUMABLE"),
    ("Modelo incompleto. Parâmetros ausentes:", "MODEL_PARAMETERS_INCOMPLETE"),
    ("Modelo de decisão/evidência incompleto.", "MODEL_PARAMETERS_INCOMPLETE"),
    ("V8 com TF exige ", "MODEL_V8_TF_CONTRACT_INVALID"),
    ("V8 com TF habilitado exige ", "MODEL_V8_TF_CONTRACT_INVALID"),
    ("TF exige ", "MODEL_V8_TF_CONTRACT_INVALID"),
    ("Piso TF não pode superar", "MODEL_V8_TF_CONTRACT_INVALID"),
    ("Novo contrato V8 com TF não admite", "MODEL_V8_TF_CONTRACT_INVALID"),
    ("DUAL_THRESHOLD_CONFLICT_FLOOR_V2 sem", "MODEL_V8_FLOOR_CONTRACT_INVALID"),
    ("DUAL_THRESHOLD_CONFLICT_FLOOR deve estar em [0,1]", "MODEL_V8_FLOOR_CONTRACT_INVALID"),
    ("V8 exige ", "MODEL_V8_CONTRACT_INVALID"),
    ("V8 não admite probabilidades M/U", "MODEL_V8_CONTRACT_INVALID"),
    ("Modelo semântico incompleto.", "MODEL_SEMANTIC_BIRTH_INCOMPLETE"),
    ("Modelo V5/V6/V7 incompleto.", "MODEL_SEMANTIC_BIRTH_INCOMPLETE"),
    ("sem ruleset persistido", "RULESET_MISSING"),
    ("não está em estado consumível", "RULESET_STATE_INVALID"),
    ("Algoritmo do ruleset diverge", "RULESET_ALGORITHM_MISMATCH"),
    ("do ruleset/modelo não é suportada", "RULESET_NORMALIZATION_UNSUPPORTED"),
    ("Identidade da projeção deve informar", "RULESET_PROJECTION_INVALID"),
    ("Fingerprint da projeção deve ser", "RULESET_PROJECTION_INVALID"),
    ("Projeção física não suportada", "RULESET_PROJECTION_INVALID"),
    ("usa atributo dinâmico sem identidade de projeção física", "RULESET_PROJECTION_IDENTITY_MISSING"),
    ("possui passe inconsistente", "RULESET_PASS_INCONSISTENT"),
    ("não possui passes/campos completos", "RULESET_PASSES_INVALID"),
    ("Fingerprint do ruleset", "RULESET_FINGERPRINT_MISMATCH"))

def v8_runner_failure_code(lines):
    """Classify a private Runner log without returning any of its text."""
    import re
    text="".join(lines)
    for token in re.findall(r"RUNNER_[A-Z0-9_]{1,96}",text):
        if token in V8_RUNNER_FAILURE_CODES:return token
    for marker,code in V8_RUNNER_FAILURE_MARKERS:
        if marker in text:return code
    return "UNCLASSIFIED"

def v8_runner_failure_from_path(path):
    try:
        if (not path.is_file() or path.is_symlink()
                or path.stat().st_size>4*1024*1024):
            return "UNCLASSIFIED"
        with path.open(encoding="utf-8",errors="replace") as source:
            return v8_runner_failure_code(source)
    except OSError:
        return "UNCLASSIFIED"

V8_RECEIPT_SQL_FAILURE_CODES={
    52974:"SCHEMA_STAGE_INVALID",
    52975:"RUN_CARDINALITY",
    52976:"RUN_NOT_PUBLISHED",
    52977:"MODEL_NOT_FROZEN",
    52978:"FIXTURE_LIFECYCLE_FORBIDDEN",
    52979:"UNIVERSE_INVALID",
    52980:"RECEIPT_SIZE_INVALID",
    52983:"SCENARIO_COVERAGE_INVALID",
}

def v8_receipt_sql_failure_code(lines):
    """Classify only governed Receipt THROW numbers; never return SQL text."""
    import re
    for line in lines:
        match=re.match(r"^Msg ([0-9]{1,6}), Level ([0-9]{1,2}), State ([0-9]{1,3})(?:,|$)",
                       line.strip())
        if match:
            code=V8_RECEIPT_SQL_FAILURE_CODES.get(int(match.group(1)))
            if code is not None:return code
    return "UNCLASSIFIED"

def v8_receipt_sql_failure_from_path(path):
    try:
        if (not path.is_file() or path.is_symlink()
                or path.stat().st_size>1024*1024):
            return "UNCLASSIFIED"
        with path.open(encoding="utf-8",errors="replace") as source:
            return v8_receipt_sql_failure_code(source)
    except OSError:
        return "UNCLASSIFIED"

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
    p=argparse.ArgumentParser();p.add_argument("mode",choices=("preflight","ingest","diagnostic","pair","sql-probe","sql-failure","v8-runner-failure","v8-receipt-sql-failure"));p.add_argument("--root",type=Path,required=True);a=p.parse_args()
    try:
        # Private-log classification must remain available before the scale
        # environment can be admitted or inspected.
        if a.mode=="v8-runner-failure":
            print("V8_OPERATIONAL_RUNNER_ERROR="+
                  v8_runner_failure_from_path(a.root/"v8-operational-runner.log"))
            return
        if a.mode=="v8-receipt-sql-failure":
            print("V8_OPERATIONAL_RECEIPT_SQL_ERROR="+
                  v8_receipt_sql_failure_from_path(a.root/"v8-operational-receipt.log"))
            return
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
        if isinstance(e,V8DecisionPredicateError):
            for field in e.fields:
                print("V8_OPERATIONAL_DECISION_FIELD="+field)
        elif isinstance(e,V8LinkPredicateError):
            for field in e.fields:
                print("V8_OPERATIONAL_LINK_FIELD="+field)
        code=safe_failure_code(a.mode,e)
        raise SystemExit("V9_PHASE4_SCALE_REJECTED_"+code) from None
    print("V9_PHASE4_SCALE_"+a.mode.upper()+"_PASS")
if __name__=="__main__":main()

#!/usr/bin/env python3
"""Independent three-seed validation of physical SQL prefix3 experiments.

Reads only aggregate reports. No synthetic truth labels or TEST pairs are
selected by this gate. A green CI does not imply calibrated FS/TF or promotion.
"""
import json
from decimal import Decimal
from pathlib import Path

PASSES={"MOTHER_PREFIX3_PLUS_EXACT_BIRTH","PERSON_PREFIX3_PLUS_MOTHER_PREFIX3"}
summary=[]
for seed in (42,43,44):
    path=Path(f".local/v9-sql-physical-seed-{seed}.json")
    data=json.loads(path.read_text(encoding="utf-8"))
    assert data["Seed"]==seed and data["CorpusPeople"]==30000
    previous=data["Prefix3ExactBirthTrainValidation"]
    report=data["OtherPrefix3PairsTrainValidation"]
    assert report["Version"]=="V9_SQL_TWO_MORE_PREFIX3_FAMILIES_TRAIN_VALIDATION_V1"
    assert report["Status"]=="DIAGNOSTIC_ONLY_TRAIN_VALIDATION_NO_TEST"
    assert report["MayPromotePolicy"] is False
    ps=report["Passes"]
    splits=report["Splits"]
    assert len(ps)==4 and len(splits)==2
    assert {(x["Name"],x["Partition"]) for x in ps}=={
        (name,part) for name in PASSES for part in ("TRAIN","VALIDATION")}
    assert {x["Partition"] for x in splits}=={"TRAIN","VALIDATION"}
    for row in ps:
        assert row["TruthMNotRetrieved"]==0
        assert row["M"]>=row["NewMVsTwelve"]>=0
        assert row["U"]>=row["NewUVsTwelve"]>=0
        assert row["TruthMWithPredicate"]>=row["M"]
    for row in splits:
        original=next(x for x in previous["Splits"]
                      if x["Partition"]==row["Partition"])
        assert row["EligibleM"]==original["EligibleM"]
        assert row["EligibleObservations"]==original["EligibleObservations"]
        assert row["TwelveM"]==original["TwelveM"]
        assert row["TwelveU"]==original["TwelveU"]
        assert row["FourteenM"]==row["TwelveM"]+row["AddedM"]
        assert row["FourteenU"]==row["TwelveU"]+row["AddedU"]
        assert 0<=row["FourteenM"]<=row["EligibleM"]
        assert row["AddedM"]>=0 and row["AddedU"]>=0
        assert row["FourteenM"]+row["FourteenU"]<=data["CandidatePairHardCap"]
        assert row["MoreMNeededFor95Percent"]==max(
            0,(19*row["EligibleM"]+19)//20-row["FourteenM"])
        assert abs(Decimal(str(row["Recall"]))
                   -Decimal(row["FourteenM"])/Decimal(row["EligibleM"]))<Decimal("1e-12")
        assert 0<=row["ZeroCandidateObservations"]<=row["EligibleObservations"]
        assert 0<=row["P95Degree"]<=row["P99Degree"]<=row["MaxDegree"]
        assert 0<=row["P95UDegree"]<=row["P99UDegree"]<=row["MaxUDegree"]
        assert row["P99UDegree"]<=row["P99Degree"]
    summary.append({"seed":seed,"splits":splits,"passes":ps})
out={"version":"V9_SQL_THREE_PREFIX3_PASSES_14_UNION_V1",
     "scope":"TRAIN_VALIDATION_ONLY_TEST_BLIND",
     "source":"SQL_SERVER_JORNADA_HARNESS_IBGE_DERIVED_SYNTHETIC",
     "isMunicipalRealWorldValidation":False,
     "mayPromotePolicy":False,"bySeed":summary}
target=Path(".local/v9-sql-two-prefix3-summary.json")
target.write_text(json.dumps(out,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
print("PREFIX3 PHYSICAL SQL 14-PASS DIAGNOSTIC",json.dumps(out,ensure_ascii=False))

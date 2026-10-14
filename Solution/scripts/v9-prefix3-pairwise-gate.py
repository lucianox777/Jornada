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
    cells_report=report["NameRequiredSqlCells"]
    assert cells_report["Version"]=="V9_NAME_REQUIRED_SQL_CELLS_DIAGNOSTIC_V1"
    assert cells_report["PolicyVersion"]=="V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1"
    assert cells_report["Scope"]=="TRAIN_VALIDATION_SQL_PHYSICAL_NO_TEST"
    assert cells_report["MayPromotePolicy"] is False
    assert cells_report["FdrMeasured"] is False
    assert cells_report["ProjectionVersion"]=="BLOCKING_PROJECTION_KEY_PROJECTOR_V4"
    assert cells_report["PhoneticVersion"]=="PERSON_NAME_METAPHONE_BR_V1"
    assert cells_report["SurnameSemantics"]=="IBGE_ANY_SURNAME_PRESENCE_PROXY_LAST_SIGNIFICANT_FIRST_NOT_POSITIONAL_TF"
    ids=("PERSON_PHONETIC_MOTHER_LAST","PERSON_PHONETIC_BIRTH_YEAR",
         "PERSON_LAST_MOTHER_PHONETIC","PERSON_PHONETIC_MOTHER_ANY")
    cells=cells_report["Cells"]
    assert len(cells)==8
    assert [(x["Name"],x["Partition"]) for x in cells]==[
        (name,part) for name in ids for part in ("TRAIN","VALIDATION")]
    assert all(0<=x["ExclusiveNewM"]<=x["NewMvs14"]<=x["M"]
               and 0<=x["NewUvs14"]<=x["U"]
               and 0<=x["JaroWinklerHighM"]<=x["M"]
               and 0<=x["JaroWinklerHighU"]<=x["U"] for x in cells)
    cell_splits=cells_report["Splits"]
    assert len(cell_splits)==2
    assert {x["Partition"] for x in cell_splits}=={"TRAIN","VALIDATION"}
    for row in cell_splits:
        old=next(x for x in report["TwoFieldSqlCoverage"]["NameRequiredSplits"]
                 if x["Partition"]==row["Partition"])
        assert row["EligibleTruthM"]==old["EligibleTruthM"]
        assert row["LegacyM"]==old["SqlEligibleM"]
        assert row["LegacyU"]==old["SqlEligibleU"]
        assert row["MissingBirthTruthM"]==old["EligibleTruthMWithoutSqlBirth"]
        assert row["UnionM"]==row["LegacyM"]+row["NewM"]
        assert row["UnionU"]==row["LegacyU"]+row["NewU"]
        assert 0<=row["UnionM"]<=row["EligibleTruthM"]
        assert 0<=row["LegacyM"]<=row["UnionM"]
        assert 0<=row["LegacyU"]<=row["UnionU"]
        assert row["MoreMNeededFor95"]==max(
            0,(19*row["EligibleTruthM"]+19)//20-row["UnionM"])
        for key,numerator in (("LegacyRecall",row["LegacyM"]),
                              ("UnionRecall",row["UnionM"])):
            if row["EligibleTruthM"]==0:
                assert row[key] is None
            else:
                assert abs(Decimal(str(row[key]))-
                           Decimal(numerator)/Decimal(row["EligibleTruthM"]))<Decimal("1e-12")

    coverage=report["TwoFieldSqlCoverage"]
    assert coverage["Version"]=="V9_TWO_FIELD_SQL_FOURTEEN_PASS_COVERAGE_V1"
    assert coverage["Status"]=="DIAGNOSTIC_LEGACY_SQL_DOB_FRAME_NO_TEST_NO_PROMOTION"
    assert coverage["Scope"]=="TRAIN_VALIDATION_NO_CPF_ALL_DOB_STATES"
    assert coverage["MayPromoteModelOrBlockingPolicy"] is False
    assert coverage["FdrMeasured"] is False
    availability=coverage["AvailabilityByCombination"]
    assert len(availability)==16
    assert {(x["Partition"],x["Combination"]) for x in availability}=={
        (part,c) for part in ("TRAIN","VALIDATION")
        for c in ("NONE","P","M","D","PM","PD","MD","PMD")}
    assert all(x["ProbabilisticAbstention"]==
               (x["Combination"] in ("NONE","P","M","D"))
               and x["Observations"]>=0 for x in availability)
    two_field_splits=coverage["Splits"]
    assert {x["Partition"] for x in two_field_splits}=={"TRAIN","VALIDATION"}
    assert len(two_field_splits)==2
    assert coverage["NameRequiredPolicyVersion"]==(
        "V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1")
    required_splits=coverage["NameRequiredSplits"]
    assert len(required_splits)==2
    assert {x["Partition"] for x in required_splits}=={"TRAIN","VALIDATION"}
    for name in required_splits:
        part=name["Partition"]
        two=next(x for x in two_field_splits if x["Partition"]==part)
        cells=[x for x in availability if x["Partition"]==part]
        allowed={"PM","PD","PMD"}
        assert name["EligibleObservations"]==sum(
            x["Observations"] for x in cells if x["Combination"] in allowed)
        assert name["AbstainingObservations"]==sum(
            x["Observations"] for x in cells if x["Combination"] not in allowed)
        assert name["EligibleTruthM"]+name["ExcludedTruthM"]==two["AllNoCpfTruthM"]
        assert 0<=name["EligibleTruthM"]<=two["EligibleTwoFieldTruthM"]
        assert 0<=name["EligibleTruthMWithoutSqlBirth"]<=(
            name["EligibleTruthM"]-name["SqlEligibleM"])
        assert name["SqlEligibleM"]+name["SqlExcludedM"]==two["LegacySqlM"]
        assert name["SqlEligibleU"]+name["SqlExcludedU"]==two["LegacySqlU"]
        assert 0<=name["SqlEligibleM"]<=two["LegacySqlEligibleM"]
        assert 0<=name["SqlEligibleU"]<=two["LegacySqlEligibleU"]
        assert name["UnrecoveredEligibleM"]==(
            name["EligibleTruthM"]-name["SqlEligibleM"])
        for key,num,den in (
            ("RecallAgainstAllNoCpfTruthM",name["SqlEligibleM"],two["AllNoCpfTruthM"]),
            ("RecallAgainstNameEligibleTruthM",name["SqlEligibleM"],name["EligibleTruthM"]),
            ("EligibilityCeilingAgainstAllTruthM",name["EligibleTruthM"],two["AllNoCpfTruthM"])):
            if den==0:
                assert name[key] is None
            else:
                assert abs(Decimal(str(name[key]))-Decimal(num)/Decimal(den))<Decimal("1e-12")
        assert name["MoreMNeededFor95"]==max(
            0,(19*name["EligibleTruthM"]+19)//20-name["SqlEligibleM"])
    for two in two_field_splits:
        part=two["Partition"]
        legacy=next(x for x in splits if x["Partition"]==part)
        cells=[x for x in availability if x["Partition"]==part]
        assert two["NoCpfObservations"]==sum(x["Observations"] for x in cells)
        assert two["EligibleObservations"]==sum(
            x["Observations"] for x in cells if not x["ProbabilisticAbstention"])
        assert two["AbstainingObservations"]==sum(
            x["Observations"] for x in cells if x["ProbabilisticAbstention"])
        assert two["LegacySqlM"]==legacy["FourteenM"]
        assert two["LegacySqlU"]==legacy["FourteenU"]
        assert two["AllNoCpfTruthM"]==(
            two["EligibleTwoFieldTruthM"]+two["IneligibleTruthM"])
        assert two["LegacySqlM"]==(
            two["LegacySqlEligibleM"]+two["LegacySqlIneligibleM"])
        assert two["LegacySqlU"]==(
            two["LegacySqlEligibleU"]+two["LegacySqlIneligibleU"])
        assert 0<=two["LegacySqlEligibleM"]<=two["EligibleTwoFieldTruthM"]
        assert two["UnrecoveredEligibleM"]==(
            two["EligibleTwoFieldTruthM"]-two["LegacySqlEligibleM"])
        assert 0<=two["EligibleTruthMWithMissingBirth"]<=two["UnrecoveredEligibleM"]
        all_m=two["AllNoCpfTruthM"]
        eligible_m=two["EligibleTwoFieldTruthM"]
        for key,num,den in (
            ("RecallAgainstAllNoCpfTruthM",two["LegacySqlEligibleM"],all_m),
            ("RecallAgainstTwoFieldEligibleM",two["LegacySqlEligibleM"],eligible_m),
            ("TwoFieldEligibilityCeilingAgainstAllTruthM",eligible_m,all_m)):
            if den==0:
                assert two[key] is None
            else:
                assert abs(Decimal(str(two[key]))
                           -Decimal(num)/Decimal(den))<Decimal("1e-12")
        assert two["AdditionalEligibleMNeededFor95"]==max(
            0,(19*eligible_m+19)//20-two["LegacySqlEligibleM"])
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
    summary.append({"seed":seed,"splits":splits,"passes":ps,
                    "twoFieldSqlCoverage":coverage,"nameRequiredSqlCells":cells_report})
out={"version":"V9_SQL_THREE_PREFIX3_PASSES_14_UNION_V1",
     "scope":"TRAIN_VALIDATION_ONLY_TEST_BLIND",
     "source":"SQL_SERVER_JORNADA_HARNESS_IBGE_DERIVED_SYNTHETIC",
     "isMunicipalRealWorldValidation":False,
     "mayPromotePolicy":False,"bySeed":summary}
target=Path(".local/v9-sql-two-prefix3-summary.json")
target.write_text(json.dumps(out,indent=2,ensure_ascii=False)+"\n",encoding="utf-8")
print("PREFIX3 PHYSICAL SQL 14-PASS DIAGNOSTIC",json.dumps(out,ensure_ascii=False))

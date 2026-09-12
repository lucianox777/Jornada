#!/usr/bin/env python3
from __future__ import annotations

import argparse
import copy
import json
import re
import sys
from datetime import datetime
from pathlib import Path

SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
SCOPE = "LINKAGE_V2_STATISTICAL_HML_READINESS_ONLY"
REQUIREMENTS = {
    "representativeCorpus": (None, False),
    "referenceTruthQuality": (None, False),
    "selectionNonResponseMethodology": (None, True),
    "independentEvaluation": ("LINKAGE_INDEPENDENT_RESOLUTION_EVALUATION_V1", False),
    "surveyUncertainty": ("LINKAGE_INDEPENDENT_RESOLUTION_SURVEY_V1", True),
    "weightProvenance": ("LINKAGE_INDEPENDENT_RESOLUTION_GOVERNED_SURVEY_V1", True),
    "evidenceDependency": ("CANDIDATE_EVIDENCE_DEPENDENCY_DIAGNOSTIC_V1", False),
    "referenceAgreement": ("CANDIDATE_REFERENCE_AGREEMENT_DIAGNOSTIC_V1", True),
    "scaleValidation": ("LINKAGE_SCALE_EVIDENCE_V1", False),
}
ROW_STATUSES = {"PENDENTE", "APRESENTADA", "NAO_APLICAVEL"}
TOP_STATUSES = {"PENDENTE", "APROVADO"}


class GateError(ValueError):
    pass


def fail(message: str) -> None:
    raise GateError(message)


def load_json(path: Path) -> dict:
    if not path.is_file():
        fail(f"arquivo ausente: {path}")
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, OSError) as exc:
        fail(f"JSON inválido {path}: {exc}")
    if not isinstance(data, dict):
        fail(f"JSON raiz deve ser objeto: {path}")
    return data


def valid_utc(value: object) -> bool:
    if not isinstance(value, str) or not value.endswith("Z"):
        return False
    try:
        datetime.fromisoformat(value[:-1] + "+00:00")
        return True
    except ValueError:
        return False


def valid_sha(value: object) -> bool:
    return isinstance(value, str) and SHA256_RE.fullmatch(value.lower()) is not None


def validate_evidence(value: object, prefix: str) -> None:
    if not isinstance(value, dict):
        fail(f"{prefix}.evidence deve ser objeto com artifact+sha256")
    artifact = value.get("artifact")
    if not isinstance(artifact, str) or not artifact.strip():
        fail(f"{prefix}.evidence.artifact ausente")
    if not valid_sha(value.get("sha256")):
        fail(f"{prefix}.evidence.sha256 inválido")


def validate_report_identity(row: dict, prefix: str, expected_version: str | None) -> None:
    version = row.get("reportVersion")
    fingerprint = row.get("reportFingerprintSha256")
    if expected_version is not None:
        if version != expected_version:
            fail(f"{prefix}.reportVersion deve ser {expected_version}")
        if not valid_sha(fingerprint):
            fail(f"{prefix}.reportFingerprintSha256 inválido")
        return
    if version is None and fingerprint is None:
        return
    if not isinstance(version, str) or not version.strip():
        fail(f"{prefix}.reportVersion inválido")
    if not valid_sha(fingerprint):
        fail(f"{prefix}.reportFingerprintSha256 inválido")


def validate_requirement(row: dict, requirement_id: str, expected_version: str | None, allow_not_applicable: bool) -> str:
    prefix = f"requirements[{requirement_id}]"
    status = row.get("status")
    if status not in ROW_STATUSES:
        fail(f"{prefix}.status inválido: {status!r}")

    if status == "PENDENTE":
        if row.get("evidence") is not None or row.get("reportVersion") is not None or row.get("reportFingerprintSha256") is not None or row.get("justification") is not None:
            fail(f"{prefix} PENDENTE não pode carregar evidência/aprovação implícita")
        return status

    validate_evidence(row.get("evidence"), prefix)
    if status == "APRESENTADA":
        validate_report_identity(row, prefix, expected_version)
        if row.get("justification") is not None:
            fail(f"{prefix}.justification deve ser nula quando a evidência foi APRESENTADA")
        return status

    if not allow_not_applicable:
        fail(f"{prefix} não admite NAO_APLICAVEL")
    justification = row.get("justification")
    if not isinstance(justification, str) or not justification.strip():
        fail(f"{prefix}.justification é obrigatória quando NAO_APLICAVEL")
    if row.get("reportVersion") is not None or row.get("reportFingerprintSha256") is not None:
        fail(f"{prefix} NAO_APLICAVEL não pode declarar identidade de relatório inexistente")
    return status


def validate_approval(value: object) -> None:
    if not isinstance(value, dict):
        fail("approval ausente")
    if value.get("scope") != SCOPE:
        fail(f"approval.scope deve ser {SCOPE}")
    if not valid_utc(value.get("approvedAtUtc")):
        fail("approval.approvedAtUtc deve ser ISO-8601 UTC terminado em Z")
    if not isinstance(value.get("approvedBy"), str) or not value.get("approvedBy", "").strip():
        fail("approval.approvedBy ausente")
    validate_evidence(value.get("evidence"), "approval")


def validate(data: dict, require_approved: bool) -> dict:
    if data.get("schemaVersion") != 1:
        fail("schemaVersion deve ser 1")
    if data.get("issue") != 31:
        fail("issue deve ser 31")
    if data.get("scope") != SCOPE:
        fail(f"scope deve ser {SCOPE}")
    if data.get("productionActivationAuthorized") is not False:
        fail("productionActivationAuthorized deve permanecer false neste contrato HML")

    top_status = data.get("status")
    if top_status not in TOP_STATUSES:
        fail(f"status inválido: {top_status!r}")
    rows = data.get("requirements")
    if not isinstance(rows, list):
        fail("requirements deve ser array")

    by_id: dict[str, dict] = {}
    statuses: dict[str, str] = {}
    for row in rows:
        if not isinstance(row, dict):
            fail("requirements contém item não objeto")
        requirement_id = row.get("id")
        if not isinstance(requirement_id, str) or requirement_id not in REQUIREMENTS:
            fail(f"requirement id inválido: {requirement_id!r}")
        if requirement_id in by_id:
            fail(f"requirement duplicado: {requirement_id}")
        by_id[requirement_id] = row

    missing = sorted(set(REQUIREMENTS) - set(by_id))
    extra = sorted(set(by_id) - set(REQUIREMENTS))
    if missing or extra:
        fail(f"inventário de requirements divergente missing={missing} extra={extra}")

    for requirement_id, (expected_version, allow_not_applicable) in REQUIREMENTS.items():
        statuses[requirement_id] = validate_requirement(
            by_id[requirement_id], requirement_id, expected_version, allow_not_applicable)

    resolved = all(status != "PENDENTE" for status in statuses.values())
    if top_status == "PENDENTE":
        if data.get("approval") is not None:
            fail("status PENDENTE não pode carregar approval")
    else:
        if not resolved:
            fail("status APROVADO exige todos os requirements resolvidos")
        validate_approval(data.get("approval"))

    approved = top_status == "APROVADO" and resolved
    if require_approved and not approved:
        fail("readiness estatístico da issue #31 ainda não está APROVADO")

    return {
        "schemaVersion": 1,
        "status": "PASS",
        "approved": approved,
        "requireApproved": require_approved,
        "scope": SCOPE,
        "issue": 31,
        "requirements": statuses,
        "productionActivationAuthorized": False,
        "note": "Este gate verifica completude/proveniência de evidências; não define thresholds estatísticos nem autoriza produção.",
    }


def synthetic_approved(data: dict) -> dict:
    result = copy.deepcopy(data)
    result["status"] = "APROVADO"
    fake_sha = "a" * 64
    for row in result["requirements"]:
        expected_version, _ = REQUIREMENTS[row["id"]]
        row["status"] = "APRESENTADA"
        row["evidence"] = {"artifact": f"evidence/{row['id']}.json", "sha256": fake_sha}
        row["justification"] = None
        if expected_version is not None:
            row["reportVersion"] = expected_version
            row["reportFingerprintSha256"] = fake_sha
        else:
            row["reportVersion"] = None
            row["reportFingerprintSha256"] = None
    result["approval"] = {
        "scope": SCOPE,
        "approvedAtUtc": "2026-09-11T00:00:00Z",
        "approvedBy": "SELF_TEST",
        "evidence": {"artifact": "evidence/approval.json", "sha256": fake_sha},
    }
    return result


def run_self_test(policy_path: Path) -> int:
    distributed = load_json(policy_path)
    validate(distributed, False)
    try:
        validate(distributed, True)
    except GateError:
        pass
    else:
        fail("self-test esperava rejeição do contrato distribuído PENDENTE em modo estrito")

    approved = synthetic_approved(distributed)
    validate(approved, True)

    invalid = copy.deepcopy(approved)
    invalid["requirements"] = invalid["requirements"][:-1]
    try:
        validate(invalid, True)
    except GateError:
        pass
    else:
        fail("self-test esperava rejeição de requirement ausente")

    print("LINKAGE STATISTICAL READINESS GATE SELF-TEST: OK")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Valida o contrato de evidências estatísticas da issue #31 sem fabricar critérios institucionais.")
    ap.add_argument("--root", default=".", help="raiz da Solution")
    ap.add_argument("--policy", help="caminho alternativo para o contrato de readiness")
    ap.add_argument("--require-approved", action="store_true", help="falha enquanto a evidência institucional da #31 não estiver aprovada")
    ap.add_argument("--summary")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    root = Path(args.root).resolve()
    policy = Path(args.policy).resolve() if args.policy else root / "config/hml/linkage-statistical-readiness.json"
    try:
        if args.self_test:
            return run_self_test(policy)
        result = validate(load_json(policy), args.require_approved)
    except GateError as exc:
        print(f"LINKAGE STATISTICAL READINESS GATE: FAIL: {exc}", file=sys.stderr)
        return 2

    if args.summary:
        out = Path(args.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"LINKAGE STATISTICAL READINESS GATE: OK (approved={result['approved']}; strict={args.require_approved})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

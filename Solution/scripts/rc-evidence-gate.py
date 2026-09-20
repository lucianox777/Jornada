#!/usr/bin/env python3
"""Consolidate hash-addressed evidence for a non-normative technical RC."""
from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import sys
import tempfile
from pathlib import Path

REQUIRED_ARTIFACTS = (
    "nuget-lockfiles",
    "unit-test-evidence",
    "openapi-runtime-evidence",
    "integration-test-evidence",
    "fault-injection-evidence",
    "sbom-cyclonedx",
    "linkage-evaluation-smoke",
    "ddl-upgrade-evidence",
    "local-e2e-evidence",
    "bronze-restore-drill",
    "scale-harness-smoke",
    "deterministic-build-evidence",
    "security-analysis-evidence",
    "rc-source-provenance",
    "rc-structural-provenance",
)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path}: JSON raiz deve ser objeto")
    return value


def inventory(root: Path, errors: list[str]) -> list[dict]:
    rows: list[dict] = []
    for name in REQUIRED_ARTIFACTS:
        base = root / name
        if not base.is_dir():
            errors.append(f"artefato obrigatório ausente: {name}")
            continue
        files = sorted(p for p in base.rglob("*") if p.is_file())
        if not files:
            errors.append(f"artefato obrigatório vazio: {name}")
            continue
        for path in files:
            rows.append({
                "artifact": name,
                "path": path.relative_to(root).as_posix(),
                "sha256": sha256(path),
                "bytes": path.stat().st_size,
            })
    return rows


def execute(input_root: Path, candidate_path: Path, output: Path) -> tuple[int, dict]:
    root = input_root.resolve()
    candidate_file = candidate_path.resolve()
    candidate = load_json(candidate_file)
    errors: list[str] = []

    technical_rc = (candidate.get("candidate") or {}).get("technical_rc") or {}
    if technical_rc.get("status") != "CHECKPOINT_CONTENT":
        errors.append("technical_rc.status deve ser CHECKPOINT_CONTENT")
    if technical_rc.get("release_effect") != "NONE":
        errors.append("technical_rc.release_effect deve ser NONE")
    if (candidate.get("candidate") or {}).get("release_status") != "NOT_RELEASED":
        errors.append("candidate.release_status deve permanecer NOT_RELEASED")

    expected_gates = {
        "HML_REPRESENTATIVE_VOLUMETRY",
        "GTPR_PURPOSE_LEGAL_BASIS_DECISION",
        "LINKAGE_REPRESENTATIVE_STATISTICAL_VALIDATION",
    }
    gates = set(candidate.get("external_gates") or [])
    if not expected_gates.issubset(gates):
        errors.append("gates externos obrigatórios não estão explicitamente pendentes")

    source_prov_path = root / "rc-source-provenance" / "RC_SOURCE_PROVENANCE.json"
    structural_path = root / "rc-structural-provenance" / "RC_SCHEMA_PROVENANCE.json"
    source_prov = load_json(source_prov_path) if source_prov_path.is_file() else {}
    structural = load_json(structural_path) if structural_path.is_file() else {}

    if source_prov.get("nature") != "TECHNICAL_RC_SOURCE_PROVENANCE":
        errors.append("RC_SOURCE_PROVENANCE inválida")
    if source_prov.get("releaseEffect") != "NONE":
        errors.append("source provenance não preserva releaseEffect=NONE")

    declared_fp = ((candidate.get("candidate") or {}).get("schema_provenance") or {}).get("structural_fingerprint_sha256")
    if structural.get("status") != "PASS":
        errors.append("RC_SCHEMA_PROVENANCE não está PASS")
    if structural.get("declaredFingerprintSha256") != declared_fp:
        errors.append("fingerprint declarado no RC_SCHEMA_PROVENANCE diverge do candidato")
    if structural.get("actualFingerprintSha256") != declared_fp:
        errors.append("fingerprint executado no RC diverge do candidato")

    rows = inventory(root, errors)
    rows.sort(key=lambda row: row["path"])
    evidence_digest = hashlib.sha256(
        "\n".join(f"{r['sha256']}  {r['path']}" for r in rows).encode("utf-8")
    ).hexdigest()
    candidate_sha = sha256(candidate_file)
    envelope = hashlib.sha256(
        f"candidate {candidate_sha}\nartifacts {evidence_digest}\n".encode("utf-8")
    ).hexdigest()

    current = source_prov.get("current") or {}
    result = {
        "schemaVersion": 1,
        "nature": "TECHNICAL_RC_EVIDENCE",
        "status": "FAIL" if errors else "PASS",
        "releaseEffect": "NONE",
        "tag": technical_rc.get("identifier"),
        "semver": technical_rc.get("semver"),
        "sourceCommit": current.get("commit"),
        "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "candidateInfoSha256": candidate_sha,
        "structuralFingerprintSha256": declared_fp,
        "externalGates": sorted(gates),
        "requiredArtifactNames": list(REQUIRED_ARTIFACTS),
        "artifactFileCount": len(rows),
        "evidenceCombinedSha256": evidence_digest,
        "rcEvidenceEnvelopeSha256": envelope,
        "evidence": rows,
        "errors": errors,
        "note": (
            "Checkpoint técnico sem efeito de release normativa. "
            "Os gates externos permanecem pendentes e esta evidência não autoriza Produção "
            "nem ativação probabilística."
        ),
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return (2 if errors else 0), result


def selftest() -> int:
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        inp = root / "input"
        for name in REQUIRED_ARTIFACTS:
            (inp / name).mkdir(parents=True)
            (inp / name / "evidence.txt").write_text("ok\n", encoding="utf-8")
        source = {
            "nature": "TECHNICAL_RC_SOURCE_PROVENANCE",
            "releaseEffect": "NONE",
            "current": {"commit": "a" * 40},
        }
        (inp / "rc-source-provenance" / "RC_SOURCE_PROVENANCE.json").write_text(
            json.dumps(source), encoding="utf-8"
        )
        fp = "b" * 64
        structural = {
            "status": "PASS",
            "declaredFingerprintSha256": fp,
            "actualFingerprintSha256": fp,
        }
        (inp / "rc-structural-provenance" / "RC_SCHEMA_PROVENANCE.json").write_text(
            json.dumps(structural), encoding="utf-8"
        )
        candidate = {
            "candidate": {
                "release_status": "NOT_RELEASED",
                "technical_rc": {
                    "identifier": "v5.00-rc.1",
                    "semver": "5.0.0-rc.1",
                    "status": "CHECKPOINT_CONTENT",
                    "release_effect": "NONE",
                },
                "schema_provenance": {"structural_fingerprint_sha256": fp},
            },
            "external_gates": [
                "HML_REPRESENTATIVE_VOLUMETRY",
                "GTPR_PURPOSE_LEGAL_BASIS_DECISION",
                "LINKAGE_REPRESENTATIVE_STATISTICAL_VALIDATION",
            ],
        }
        candidate_path = root / "CANDIDATE_INFO.json"
        candidate_path.write_text(json.dumps(candidate), encoding="utf-8")
        code, result = execute(inp, candidate_path, root / "out.json")
        if code != 0 or result["status"] != "PASS":
            raise AssertionError("cenário positivo falhou")
        structural["actualFingerprintSha256"] = "c" * 64
        (inp / "rc-structural-provenance" / "RC_SCHEMA_PROVENANCE.json").write_text(
            json.dumps(structural), encoding="utf-8"
        )
        code, result = execute(inp, candidate_path, root / "out2.json")
        if code == 0 or result["status"] != "FAIL":
            raise AssertionError("fingerprint divergente foi aceito")
    print("TECHNICAL RC EVIDENCE GATE SELFTEST: OK")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Consolida evidência completa de RC técnico.")
    parser.add_argument("--input-root")
    parser.add_argument("--candidate-info")
    parser.add_argument("--output")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return selftest()
    if not all([args.input_root, args.candidate_info, args.output]):
        print("ERRO: --input-root, --candidate-info e --output são obrigatórios", file=sys.stderr)
        return 2
    code, result = execute(Path(args.input_root), Path(args.candidate_info), Path(args.output))
    if code:
        for error in result["errors"]:
            print("ERRO:", error, file=sys.stderr)
        return code
    print(
        f"TECHNICAL RC EVIDENCE GATE: OK (files={result['artifactFileCount']}; "
        f"envelopeSha256={result['rcEvidenceEnvelopeSha256']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

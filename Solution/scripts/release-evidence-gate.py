#!/usr/bin/env python3
from __future__ import annotations
import argparse, datetime, hashlib, json, sys, tempfile
from pathlib import Path


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path):
    obj = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(obj, dict):
        raise ValueError(f"{path}: JSON raiz deve ser objeto")
    return obj


def get_path(obj: dict, dotted: str):
    cur = obj
    for part in dotted.split("."):
        if not isinstance(cur, dict) or part not in cur:
            raise KeyError(dotted)
        cur = cur[part]
    return cur


def validate_check(base: Path, check: dict, errors: list[str]):
    pattern = check.get("glob")
    if not pattern:
        errors.append("check sem glob")
        return
    matches = sorted(p for p in base.glob(pattern) if p.is_file())
    if not matches:
        errors.append(f"{base.name}: nenhum arquivo para {pattern}")
        return
    for path in matches:
        try:
            kind = check.get("kind")
            if kind == "json":
                obj = load_json(path)
                for field in check.get("requiredFields", []):
                    get_path(obj, field)
                for field, expected in (check.get("equals") or {}).items():
                    actual = get_path(obj, field)
                    if actual != expected:
                        errors.append(f"{path}: {field}={actual!r}, esperado {expected!r}")
                for field, minimum in (check.get("minimums") or {}).items():
                    actual = get_path(obj, field)
                    if not isinstance(actual, (int, float)) or isinstance(actual, bool) or actual < minimum:
                        errors.append(f"{path}: {field} abaixo de {minimum}")
            elif kind == "text":
                text = path.read_text(encoding="utf-8")
                for token in check.get("contains", []):
                    if token not in text:
                        errors.append(f"{path}: trecho ausente {token!r}")
            elif kind == "file":
                if path.stat().st_size <= 0:
                    errors.append(f"{path}: arquivo vazio")
            else:
                errors.append(f"{path}: kind inválido {kind!r}")
        except (ValueError, KeyError, json.JSONDecodeError, OSError) as exc:
            errors.append(f"{path}: {exc}")


def parse_release_info(path: Path) -> dict[str, str]:
    result = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if "=" in line:
            k, v = line.split("=", 1)
            result[k.strip()] = v.strip()
    return result


def inventory_artifact(base: Path, input_root: Path, artifact_name: str, errors: list[str]) -> list[dict]:
    rows = []
    if not base.is_dir():
        errors.append(f"artefato obrigatório ausente: {artifact_name}")
        return rows
    files = sorted(p for p in base.rglob("*") if p.is_file())
    if not files:
        errors.append(f"artefato obrigatório vazio: {artifact_name}")
        return rows
    for path in files:
        rows.append({
            "artifact": artifact_name,
            "path": str(path.relative_to(input_root)).replace("\\", "/"),
            "sha256": sha(path),
            "bytes": path.stat().st_size,
        })
    return rows


def run(input_root: Path, policy_path: Path, release_info: Path, output: Path) -> tuple[int, dict]:
    inp = input_root.resolve()
    policy_file = policy_path.resolve()
    release_file = release_info.resolve()
    policy = load_json(policy_file)
    info = parse_release_info(release_file)
    errors: list[str] = []
    rows: list[dict] = []
    if policy.get("schemaVersion") != 1:
        errors.append("policy.schemaVersion deve ser 1")
    required = policy.get("requiredArtifacts")
    if not isinstance(required, list) or not required:
        errors.append("policy.requiredArtifacts deve ser array não vazio")
        required = []

    names = []
    for artifact in required:
        name = artifact.get("name") if isinstance(artifact, dict) else None
        if not name:
            errors.append("artifact sem name")
            continue
        if name in names:
            errors.append(f"artifact duplicado na policy: {name}")
            continue
        names.append(name)
        base = inp / name
        rows.extend(inventory_artifact(base, inp, name, errors))
        if base.is_dir():
            for check in artifact.get("checks") or []:
                validate_check(base, check, errors)

    rows.sort(key=lambda r: r["path"])
    release_sha = sha(release_file)
    policy_sha = sha(policy_file)
    evidence_digest = hashlib.sha256(
        "\n".join(f"{r['sha256']}  {r['path']}" for r in rows).encode()
    ).hexdigest()
    envelope_digest = hashlib.sha256(
        (f"release-info {release_sha}\npolicy {policy_sha}\nartifacts {evidence_digest}\n").encode()
    ).hexdigest()
    out = {
        "schemaVersion": 1,
        "status": "FAIL" if errors else "PASS",
        "release": info.get("release"),
        "baseNormativa": info.get("base_normativa"),
        "solutionEngenharia": info.get("solution_engenharia"),
        "sourceGitTag": info.get("source_git_tag"),
        "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "policyVersion": policy.get("policyVersion"),
        "policySha256": policy_sha,
        "releaseInfoSha256": release_sha,
        "requiredArtifactNames": names,
        "artifactFileCount": len(rows),
        "evidenceCombinedSha256": evidence_digest,
        "releaseEvidenceEnvelopeSha256": envelope_digest,
        "evidence": rows,
        "errors": errors,
        "note": "Inventário hash-addressed de TODOS os arquivos baixados dos artefatos obrigatórios; os checks especializados validam a semântica e o envelope também ancora policy + RELEASE_INFO.",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return (2 if errors else 0), out


def selftest() -> int:
    with tempfile.TemporaryDirectory() as td:
        d = Path(td)
        inp = d / "in"
        (inp / "unit").mkdir(parents=True)
        summary = inp / "unit" / "summary.json"
        trx = inp / "unit" / "unit.trx"
        summary.write_text('{"status":"PASS"}\n', encoding="utf-8")
        trx.write_text("<TestRun/>\n", encoding="utf-8")
        policy = d / "policy.json"
        policy.write_text(json.dumps({
            "schemaVersion": 1,
            "policyVersion": "TEST",
            "requiredArtifacts": [{"name": "unit", "checks": [{"glob": "**/summary.json", "kind": "json", "equals": {"status": "PASS"}}]}],
        }), encoding="utf-8")
        ri = d / "RELEASE_INFO.txt"
        ri.write_text("release=TEST\nbase_normativa=v0\nsolution_engenharia=v0\nsource_git_tag=test\n", encoding="utf-8")
        code, out = run(inp, policy, ri, d / "out.json")
        if code or out["status"] != "PASS" or out["artifactFileCount"] != 2:
            raise AssertionError("cenário positivo/inventário completo falhou")
        before = out["releaseEvidenceEnvelopeSha256"]
        trx.write_text("<TestRun id='tampered'/>\n", encoding="utf-8")
        code, out = run(inp, policy, ri, d / "out-tampered.json")
        if code or out["releaseEvidenceEnvelopeSha256"] == before:
            raise AssertionError("alteração em arquivo não-semântico não mudou o hash do envelope")
        summary.write_text('{"status":"FAIL"}\n', encoding="utf-8")
        code, out = run(inp, policy, ri, d / "out2.json")
        if code == 0 or out["status"] != "FAIL":
            raise AssertionError("cenário semântico negativo foi aceito")
    print("RELEASE EVIDENCE GATE SELFTEST: OK")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description="Consolida o conjunto completo de evidências de promoção em RELEASE_EVIDENCE.json.")
    ap.add_argument("--input-root")
    ap.add_argument("--policy")
    ap.add_argument("--release-info")
    ap.add_argument("--output")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        return selftest()
    if not all([a.input_root, a.policy, a.release_info, a.output]):
        print("ERRO: --input-root, --policy, --release-info e --output são obrigatórios", file=sys.stderr)
        return 2
    code, out = run(Path(a.input_root), Path(a.policy), Path(a.release_info), Path(a.output))
    if code:
        for e in out["errors"]:
            print("ERRO:", e, file=sys.stderr)
        return code
    print(
        f"RELEASE EVIDENCE GATE: OK (files={out['artifactFileCount']}; "
        f"evidenceSha256={out['evidenceCombinedSha256']}; envelopeSha256={out['releaseEvidenceEnvelopeSha256']})"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

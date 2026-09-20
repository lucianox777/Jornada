#!/usr/bin/env python3
"""Fail-closed gate for a non-normative technical RC tag."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


def fail(message: str) -> None:
    raise SystemExit(f"TECHNICAL RC GATE: FAIL: {message}")


def run(cmd: list[str], cwd: Path | None = None) -> str:
    try:
        result = subprocess.run(
            cmd, cwd=cwd, check=True, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE
        )
    except FileNotFoundError:
        fail(f"comando ausente: {cmd[0]}")
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or "").strip()
        fail(f"comando falhou ({' '.join(cmd)}): {detail}")
    return result.stdout.strip()


def load_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        fail(f"JSON raiz deve ser objeto: {path}")
    return value


def normalized_text_sha256(raw: bytes) -> str:
    text = raw.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def parse_release_info(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        if "=" not in raw or raw.lstrip().startswith("#"):
            continue
        key, value = raw.split("=", 1)
        result[key.strip()] = value.strip()
    return result


def verify(
    repo: Path,
    candidate_path: Path,
    release_info_path: Path,
    expected_tag: str,
    require_clean: bool,
) -> None:
    candidate = load_json(candidate_path)
    if candidate.get("manifest_version") != 3 or candidate.get("nature") != "ENGINEERING_CANDIDATE":
        fail("CANDIDATE_INFO não é o manifesto de candidata esperado")

    state = candidate.get("candidate") or {}
    technical_rc = state.get("technical_rc") or {}
    provenance = state.get("schema_provenance") or {}
    sealed = candidate.get("sealed_release") or {}

    identifier = technical_rc.get("identifier")
    if identifier != expected_tag:
        fail(f"tag esperada={expected_tag}, technical_rc.identifier={identifier}")
    if not re.fullmatch(r"v\d+\.\d+-rc\.\d+", expected_tag):
        fail(f"tag não segue o padrão de RC técnico: {expected_tag}")
    if technical_rc.get("status") != "CHECKPOINT_CONTENT":
        fail("technical_rc.status deve ser CHECKPOINT_CONTENT")
    if technical_rc.get("release_effect") != "NONE":
        fail("RC técnico não pode ter efeito de release normativa")
    if state.get("release_status") != "NOT_RELEASED":
        fail("candidata deve permanecer NOT_RELEASED no RC técnico")
    if provenance.get("status") != "BOUND_FOR_TECHNICAL_RC":
        fail("schema_provenance.status deve ser BOUND_FOR_TECHNICAL_RC")

    release_info = parse_release_info(release_info_path)
    sealed_tag = sealed.get("source_git_tag")
    if not sealed_tag or release_info.get("source_git_tag") != sealed_tag:
        fail("RELEASE_INFO e sealed_release devem continuar apontando a última release selada")
    if sealed_tag == expected_tag:
        fail("RC técnico não pode sobrescrever a tag da release selada")

    if run(["git", "rev-parse", "--is-inside-work-tree"], repo) != "true":
        fail(f"não é checkout Git: {repo}")
    head = run(["git", "rev-parse", "HEAD"], repo)
    tagged = run(["git", "rev-parse", f"refs/tags/{expected_tag}^{{commit}}"], repo)
    if head != tagged:
        fail(f"HEAD {head} não é o commit da tag {expected_tag} ({tagged})")

    predecessor = run(["git", "rev-parse", f"refs/tags/{sealed_tag}^{{commit}}"], repo)
    try:
        subprocess.run(
            ["git", "merge-base", "--is-ancestor", predecessor, tagged],
            cwd=repo, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True
        )
    except subprocess.CalledProcessError:
        fail(f"release selada {sealed_tag} não é ancestral do RC {expected_tag}")

    source_commit = str(provenance.get("source_commit") or "")
    if not re.fullmatch(r"[0-9a-f]{40}", source_commit):
        fail("schema_provenance.source_commit inválido")
    try:
        subprocess.run(
            ["git", "merge-base", "--is-ancestor", source_commit, tagged],
            cwd=repo, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True
        )
    except subprocess.CalledProcessError:
        fail("checkpoint estrutural não é ancestral do RC")

    manifest_path = str(provenance.get("migration_manifest") or "")
    declared_manifest = str(provenance.get("migration_manifest_sha256") or "")
    if not manifest_path or not re.fullmatch(r"[0-9a-f]{64}", declared_manifest):
        fail("proveniência do manifesto inválida")
    source_manifest = subprocess.check_output(
        ["git", "show", f"{source_commit}:{manifest_path}"], cwd=repo
    )
    if normalized_text_sha256(source_manifest) != declared_manifest:
        fail("hash do manifesto no checkpoint estrutural diverge do declarado")
    if normalized_text_sha256((repo / manifest_path).read_bytes()) != declared_manifest:
        fail("manifesto do RC diverge do checkpoint estrutural declarado")

    if require_clean and run(["git", "status", "--porcelain=v1", "--untracked-files=all"], repo):
        fail("worktree não está limpa")

    print(
        f"TECHNICAL RC GATE: OK (tag={expected_tag} commit={tagged[:12]} "
        f"sealed={sealed_tag}:{predecessor[:12]} schemaCheckpoint={source_commit[:12]})"
    )


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Valida tag de RC técnico sem promover RELEASE_INFO.")
    parser.add_argument("--repo", type=Path, default=root)
    parser.add_argument("--candidate-info", type=Path, default=root / "CANDIDATE_INFO.json")
    parser.add_argument("--release-info", type=Path, default=root / "RELEASE_INFO.txt")
    parser.add_argument("--expected-tag", required=True)
    parser.add_argument("--require-clean", action="store_true")
    args = parser.parse_args()
    verify(
        args.repo.resolve(),
        args.candidate_info.resolve(),
        args.release_info.resolve(),
        args.expected_tag,
        args.require_clean,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

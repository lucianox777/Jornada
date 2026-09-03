#!/usr/bin/env python3
"""Fail-closed Git lineage/provenance gate for release tags and embedded source bundles."""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
import tarfile
from pathlib import Path


def fail(message: str) -> None:
    raise SystemExit(f"RELEASE SOURCE GATE: FAIL: {message}")


def run(cmd: list[str], cwd: Path | None = None) -> str:
    try:
        proc = subprocess.run(cmd, cwd=cwd, check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except FileNotFoundError:
        fail(f"comando ausente: {cmd[0]}")
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or "").strip()
        fail(f"comando falhou ({' '.join(cmd)}): {detail}")
    return proc.stdout.strip()


def read_info(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        result[key.strip()] = value.strip()
    return result


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def tag_commit(repo: Path, tag: str) -> str:
    return run(["git", "rev-parse", f"refs/tags/{tag}^{{commit}}"], repo)


def commit_tree(repo: Path, commit: str) -> str:
    return run(["git", "rev-parse", f"{commit}^{{tree}}"], repo)


def tracked_count(repo: Path, commit: str) -> int:
    out = run(["git", "ls-tree", "-r", "--name-only", commit], repo)
    return len([line for line in out.splitlines() if line.strip()])


def verify_ancestry(repo: Path, predecessor_tag: str, current_tag: str) -> tuple[str, str]:
    pred = tag_commit(repo, predecessor_tag)
    current = tag_commit(repo, current_tag)
    if pred == current:
        fail("tag predecessora e tag corrente apontam para o mesmo commit")
    try:
        subprocess.run(["git", "merge-base", "--is-ancestor", pred, current], cwd=repo, check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    except subprocess.CalledProcessError:
        fail(f"{predecessor_tag} não é ancestral de {current_tag}")
    return pred, current


def verify_repo(repo: Path, info: dict[str, str], expected_tag: str | None, require_clean: bool) -> None:
    if run(["git", "rev-parse", "--is-inside-work-tree"], repo) != "true":
        fail(f"não é checkout Git: {repo}")
    current_tag = info.get("source_git_tag", "")
    predecessor_tag = info.get("source_git_predecessor_tag", "")
    if not current_tag or not predecessor_tag:
        fail("RELEASE_INFO deve declarar source_git_tag e source_git_predecessor_tag")
    if expected_tag and current_tag != expected_tag:
        fail(f"tag esperada pelo workflow={expected_tag}, RELEASE_INFO={current_tag}")
    head = run(["git", "rev-parse", "HEAD"], repo)
    tagged = tag_commit(repo, current_tag)
    if head != tagged:
        fail(f"HEAD {head} não é o commit da tag {current_tag} ({tagged})")
    pred, current = verify_ancestry(repo, predecessor_tag, current_tag)
    if require_clean:
        dirty = run(["git", "status", "--porcelain=v1", "--untracked-files=all"], repo)
        if dirty:
            fail("worktree não está limpo na promoção de release")
    print(
        f"RELEASE SOURCE GATE: OK (repo tag={current_tag} commit={current[:12]} "
        f"predecessor={predecessor_tag}:{pred[:12]} tree={commit_tree(repo, current)[:12]})"
    )


def in_source_scope(root: Path, path: Path) -> bool:
    rel = path.relative_to(root).as_posix()
    if rel.startswith(".git/"):
        return False
    if rel in {"MANIFESTO_ARQUIVOS.txt", "SHA256SUMS.txt", "RELATORIO_VALIDACAO_ENGENHARIA.txt", "SOURCE_PROVENANCE.json"}:
        return False
    if rel.startswith("Solution/supply-chain/source/") and rel.endswith(".bundle"):
        return False
    return True


def compare_snapshot_to_distribution(repo: Path, commit: str, distribution_root: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="jornada-source-compare-") as tmp:
        tmpdir = Path(tmp)
        archive = tmpdir / "source.tar"
        snapshot = tmpdir / "snapshot"
        snapshot.mkdir()
        run(["git", "archive", "--format=tar", "-o", str(archive), commit], repo)
        with tarfile.open(archive, "r") as tf:
            tf.extractall(snapshot, filter="data")
        dist_files = {p.relative_to(distribution_root).as_posix(): p for p in distribution_root.rglob("*") if p.is_file() and in_source_scope(distribution_root, p)}
        snap_files = {p.relative_to(snapshot).as_posix(): p for p in snapshot.rglob("*") if p.is_file()}
        if set(dist_files) != set(snap_files):
            missing = sorted(set(snap_files) - set(dist_files))[:5]
            extra = sorted(set(dist_files) - set(snap_files))[:5]
            fail(f"escopo fonte do pacote diverge do snapshot Git; ausentes={missing} extras={extra}")
        for rel in sorted(dist_files):
            if dist_files[rel].read_bytes() != snap_files[rel].read_bytes():
                fail(f"conteúdo distribuído diverge do snapshot Git: {rel}")


def verify_bundle(bundle: Path, provenance: Path, compare_root: Path | None = None) -> None:
    if shutil.which("git") is None:
        fail("git não encontrado")
    if not bundle.is_file():
        fail(f"bundle ausente: {bundle}")
    if not provenance.is_file():
        fail(f"proveniência ausente: {provenance}")
    data = json.loads(provenance.read_text(encoding="utf-8"))
    expected_sha = str(data.get("bundleSha256", "")).lower()
    actual_sha = sha256(bundle)
    if expected_sha != actual_sha:
        fail(f"SHA do bundle divergente: esperado={expected_sha} obtido={actual_sha}")
    current_tag = str(data.get("current", {}).get("tag", ""))
    predecessor_tag = str(data.get("predecessor", {}).get("tag", ""))
    if not current_tag or not predecessor_tag:
        fail("SOURCE_PROVENANCE sem tags corrente/predecessora")
    with tempfile.TemporaryDirectory(prefix="jornada-source-gate-") as tmp:
        repo = Path(tmp) / "repo"
        run(["git", "clone", "--quiet", str(bundle), str(repo)])
        pred, current = verify_ancestry(repo, predecessor_tag, current_tag)
        if current != data["current"].get("commit"):
            fail("commit corrente do bundle diverge da proveniência")
        if pred != data["predecessor"].get("commit"):
            fail("commit predecessor do bundle diverge da proveniência")
        current_tree = commit_tree(repo, current)
        pred_tree = commit_tree(repo, pred)
        if current_tree != data["current"].get("tree") or pred_tree != data["predecessor"].get("tree"):
            fail("tree Git diverge da proveniência")
        if tracked_count(repo, current) != int(data["current"].get("trackedFileCount", -1)):
            fail("contagem de arquivos do snapshot corrente diverge")
        if tracked_count(repo, pred) != int(data["predecessor"].get("trackedFileCount", -1)):
            fail("contagem de arquivos do snapshot predecessor diverge")
        if compare_root is not None:
            compare_snapshot_to_distribution(repo, current, compare_root.resolve())
        print(
            f"RELEASE SOURCE GATE: OK (bundle sha256={actual_sha}; "
            f"{predecessor_tag}:{pred[:12]} -> {current_tag}:{current[:12]})"
        )


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    ap = argparse.ArgumentParser(description="Valida cadeia Git de release em checkout real ou bundle distribuído.")
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--repo", type=Path, help="Checkout Git a validar.")
    mode.add_argument("--bundle", type=Path, help="Git bundle distribuído a validar.")
    ap.add_argument("--release-info", type=Path, default=root / "RELEASE_INFO.txt")
    ap.add_argument("--provenance", type=Path, default=root / "SOURCE_PROVENANCE.json")
    ap.add_argument("--expected-tag", help="Tag recebida do workflow (ex.: GITHUB_REF_NAME).")
    ap.add_argument("--compare-root", type=Path, help="Em modo bundle, compara o snapshot corrente ao escopo fonte deste diretório distribuído.")
    ap.add_argument("--require-clean", action="store_true", help="Exige worktree limpo em modo --repo.")
    args = ap.parse_args()
    if args.repo:
        verify_repo(args.repo.resolve(), read_info(args.release_info.resolve()), args.expected_tag, args.require_clean)
    else:
        verify_bundle(args.bundle.resolve(), args.provenance.resolve(), args.compare_root)


if __name__ == "__main__":
    main()

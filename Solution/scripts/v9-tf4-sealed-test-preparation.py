#!/usr/bin/env python3
"""V9 TF4: preregister, generate separately, and CMS-seal synthetic TEST.

This tool does not run, observe, tune, validate, select or certify a model.
The TEST raw corpus and truth exist only in the isolated preparation runner.
The immutable GitHub handoff contains AES-256-GCM CMS ciphertext and a
content-addressed receipt, never plaintext observations or truth.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tarfile

SCHEMA = "V9_TF4_100K_SEALED_TEST_PREPARATION_RECEIPT_V1"
LOCK_SCHEMA = "V9_TF4_100K_TEST_DATA_PREPARATION_LOCK_V1"
NAMES = ("generation-manifest.json", "pessoas_verdade.csv",
         "observacoes.csv", "gabarito.json")
SHA40 = re.compile(r"^[a-f0-9]{40}$")
SHA64 = re.compile(r"^[a-f0-9]{64}$")
LOCK_DEFAULT_SHA = "4d071fe9cb350f26bd6705caf3275a22ba3fc91831c4dab401d81b67c462af6e"


def require(ok, message):
    if not ok:
        raise ValueError("V9_SEALED_TEST_FAIL_CLOSED: " + message)


def sha_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def regular(path):
    p = Path(path)
    require(p.is_file() and not p.is_symlink(), "regular file missing")
    return p


def unique_pairs(pairs):
    out = {}
    for k, v in pairs:
        require(k not in out, "duplicate JSON property")
        out[k] = v
    return out


def metadata(path):
    p = regular(path)
    require(p.stat().st_size < 16 * 1024 * 1024, "metadata too large")
    return json.loads(p.read_text(encoding="utf-8"),
                      object_pairs_hook=unique_pairs)


def inspect_lock(path, expected):
    require(isinstance(expected, str) and SHA64.fullmatch(expected),
            "LOCK expected sha invalid")
    require(sha_file(regular(path)) == expected, "LOCK SHA diverges")
    lock = metadata(path)
    require(lock.get("schema") == LOCK_SCHEMA and lock.get("status") == "LOCKED"
            and lock.get("scope") == "PRE_GENERATE_SEAL_ONLY_NOT_SCIENTIFIC_TEST_EXECUTION"
            and lock.get("people") == 100000
            and type(lock.get("trainSeed")) is int and lock["trainSeed"] == 42
            and type(lock.get("validationSeed")) is int and lock["validationSeed"] == 20261112001
            and type(lock.get("testSeed")) is int and lock["testSeed"] == 20261113001
            and len({lock["trainSeed"], lock["validationSeed"], lock["testSeed"]}) == 3
            and lock.get("errorProfile") == "clean"
            and lock.get("populationProfile") == "demographic-primary"
            and lock.get("referenceRoot") == "data/reference/ibge-nomes-2022"
            and lock.get("birthDailySource") ==
            "data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json"
            and lock.get("artifactAllowedFiles") == list(NAMES)
            and lock.get("sealing") == "OPENSSL_CMS_AES_256_GCM_X509_RSA"
            and lock.get("observationalStatus") == "SEALED_DATA_ONLY_NO_RESULTS_NO_CERTIFICATION"
            and lock.get("declaredIndependence") ==
            "DISTINCT_RNG_SEEDS_ONLY;IDENTITY_DISJOINTNESS_NOT_ATTESTED",
            "TEST preparation lock inconsistent or incomplete")
    return lock


def verify_raw_files(root, lock):
    root = Path(root)
    require(root.is_dir() and not root.is_symlink(), "TEST corpus missing")
    require({p.name for p in root.iterdir()} == set(NAMES),
            "unexpected synthetic TEST file")
    manifest = metadata(root / "generation-manifest.json")
    require(manifest.get("schema_version") == 1
            and manifest.get("seed") == lock["testSeed"]
            and manifest.get("population_profile") == "DEMOGRAPHIC_PRIMARY_V1"
            and manifest.get("error_profile") == lock["errorProfile"]
            and manifest.get("reference_code") == "CENSO2022_NOMES_BRASIL_V1",
            "generator manifest does not match predeclared TEST")
    outputs = manifest.get("outputs")
    require(isinstance(outputs, list) and len(outputs) == 3, "missing output SHA ledger")
    by_name = {o.get("path"): o.get("sha256") for o in outputs if isinstance(o, dict)}
    require(set(by_name) == set(NAMES) - {"generation-manifest.json"},
            "unexpected generated output names")
    hashes = {}
    for name in NAMES:
        f = regular(root / name)
        h = sha_file(f)
        if name != "generation-manifest.json":
            require(isinstance(by_name[name], str) and by_name[name].lower() == h,
                    "generated TEST file SHA diverges from manifest")
        hashes[name] = {"sha256": h, "bytes": f.stat().st_size}
    return hashes


def verify_identity(code_sha, run_id):
    require(isinstance(code_sha, str) and SHA40.fullmatch(code_sha), "invalid code SHA")
    require(type(run_id) is int and run_id > 0, "invalid workflow run ID")


def seal(lock_path, expected_lock_sha, corpus, certificate,
         expected_cert_sha, destination, code_sha, run_id):
    verify_identity(code_sha, run_id)
    lock = inspect_lock(lock_path, expected_lock_sha)
    cert = regular(certificate)
    require(isinstance(expected_cert_sha, str) and SHA64.fullmatch(expected_cert_sha),
            "certificate expected SHA invalid")
    require(sha_file(cert) == expected_cert_sha, "encryption certificate mismatch")
    subprocess.run(["openssl", "x509", "-in", str(cert), "-noout"], check=True,
                   capture_output=True)
    files = verify_raw_files(corpus, lock)
    destination = Path(destination)
    require(not destination.exists(), "sealed output must be new")
    destination.mkdir(parents=True)
    cipher = destination / "test-corpus.cms"
    process = subprocess.Popen(
        ["openssl", "cms", "-encrypt", "-binary", "-aes-256-gcm",
         "-outform", "DER", "-recip", str(cert), "-out", str(cipher)],
        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    try:
        with tarfile.open(fileobj=process.stdin, mode="w|") as tar:
            for name in NAMES:
                tar.add(str(Path(corpus) / name), arcname=name, recursive=False)
    finally:
        if process.stdin is not None and not process.stdin.closed:
            process.stdin.close()
    error = process.stderr.read()
    require(process.wait() == 0, "CMS encryption failed: " + error.decode(errors="replace")[:120])
    receipt = {
        "schema": SCHEMA, "runId": run_id, "codeSha": code_sha,
        "preparationLockSha256": expected_lock_sha,
        "certificateSha256": expected_cert_sha,
        "testSeed": lock["testSeed"], "testPopulation": lock["people"],
        "ciphertextSha256": sha_file(cipher), "ciphertextBytes": cipher.stat().st_size,
        "sealedFiles": files, "labelsExposed": False, "tested": False,
        "certified": False, "activated": False,
    }
    with (destination / "test-seal-receipt.json").open("x", encoding="utf-8") as out:
        out.write(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    verify_sealed(destination, lock_path, expected_lock_sha, code_sha, run_id)
    return receipt


def verify_sealed(destination, lock_path, expected_lock_sha, code_sha, run_id):
    verify_identity(code_sha, run_id)
    lock = inspect_lock(lock_path, expected_lock_sha)
    dest = Path(destination)
    require(dest.is_dir() and not dest.is_symlink(), "sealed directory missing")
    require({p.name for p in dest.iterdir()} == {"test-corpus.cms", "test-seal-receipt.json"},
            "unapproved or missing sealed artifact member")
    r = metadata(dest / "test-seal-receipt.json")
    cipher = regular(dest / "test-corpus.cms")
    require(r.get("schema") == SCHEMA and r.get("runId") == run_id
            and r.get("codeSha") == code_sha and r.get("testSeed") == lock["testSeed"]
            and r.get("testPopulation") == 100000
            and r.get("preparationLockSha256") == expected_lock_sha
            and isinstance(r.get("certificateSha256"), str)
            and SHA64.fullmatch(r["certificateSha256"])
            and r.get("ciphertextSha256") == sha_file(cipher)
            and r.get("ciphertextBytes") == cipher.stat().st_size
            and r.get("labelsExposed") is False and r.get("tested") is False
            and r.get("certified") is False and r.get("activated") is False
            and set(r.get("sealedFiles", {})) == set(NAMES)
            and all(isinstance(v, dict)
                    and isinstance(v.get("sha256"), str) and SHA64.fullmatch(v["sha256"])
                    and type(v.get("bytes")) is int and v["bytes"] >= 0
                    for v in r["sealedFiles"].values()),
            "sealed TEST manifest or ciphertext invalid")
    return r


def unseal(destination, lock_path, expected_lock_sha, code_sha, run_id,
           certificate, key, output):
    # Call ONLY in the isolated TEST job after independent scientific LOCK,
    # immutable winner selection, and persistent one-use reservation gates.
    r = verify_sealed(destination, lock_path, expected_lock_sha, code_sha, run_id)
    require(sha_file(regular(certificate)) == r["certificateSha256"],
            "TEST private-key certificate does not match sealed recipient")
    require(not Path(output).exists(), "TEST output must be new")
    Path(output).mkdir(parents=True)
    archive = Path(output) / "_temp-plaintext.tar"
    try:
        subprocess.run(["openssl", "cms", "-decrypt", "-binary",
                        "-inform", "DER", "-in", str(Path(destination)/"test-corpus.cms"),
                        "-recip", str(certificate), "-inkey", str(regular(key)),
                        "-out", str(archive)], check=True, capture_output=True)
        with tarfile.open(archive, "r:") as tar:
            members = tar.getmembers()
            require(len(members) == len(NAMES)
                    and {x.name for x in members} == set(NAMES)
                    and all(x.isfile() and not x.issym() and not x.islnk() for x in members),
                    "TEST plaintext archive includes unapproved members")
            for member in members:
                source = tar.extractfile(member)
                require(source is not None, "missing plaintext member")
                with (Path(output)/member.name).open("xb") as dest:
                    while True:
                        chunk = source.read(4*1024*1024)
                        if not chunk:
                            break
                        dest.write(chunk)
        archive.unlink()  # No extra plaintext file may survive the extraction check.
        lock = inspect_lock(lock_path, expected_lock_sha)
        hashes = verify_raw_files(output, lock)
        require(hashes == r["sealedFiles"], "decrypted TEST bytes differ from sealed receipt")
    finally:
        if archive.exists():
            archive.unlink()
    return {"status": "TEST_CORPUS_OPENED_AFTER_EXTERNAL_LOCK_NOT_TESTED",
            "runId": run_id, "testSeed": r["testSeed"], "certified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["verify-lock", "seal", "verify", "unseal"])
    parser.add_argument("--lock", type=Path, required=True)
    parser.add_argument("--expected-lock-sha", default=LOCK_DEFAULT_SHA)
    parser.add_argument("--code-sha")
    parser.add_argument("--run-id", type=int)
    parser.add_argument("--raw", type=Path)
    parser.add_argument("--certificate", type=Path)
    parser.add_argument("--expected-cert-sha")
    parser.add_argument("--sealed", type=Path)
    parser.add_argument("--key", type=Path)
    parser.add_argument("--out", type=Path)
    a = parser.parse_args()
    if a.operation == "verify-lock":
        lock = inspect_lock(a.lock, a.expected_lock_sha)
        print(json.dumps({"status": "TEST_DATA_PREPARATION_LOCK_VERIFIED",
                          "seed": lock["testSeed"], "testExecuted": False}))
        return
    require(a.code_sha and a.run_id and a.sealed, "run identity and sealed directory required")
    if a.operation == "seal":
        require(a.raw and a.certificate and a.expected_cert_sha,
                "raw corpus, public cert and precommitted cert SHA required")
        result = seal(a.lock, a.expected_lock_sha, a.raw, a.certificate,
                      a.expected_cert_sha, a.sealed, a.code_sha, a.run_id)
        print(json.dumps({"status": "SEALED_TEST_DATA_ONLY_NOT_TESTED", "runId": a.run_id,
                          "cipherSha256": result["ciphertextSha256"]}))
    elif a.operation == "verify":
        r = verify_sealed(a.sealed, a.lock, a.expected_lock_sha, a.code_sha, a.run_id)
        print(json.dumps({"status": "ENCRYPTED_TEST_VERIFIED_NOT_OPENED",
                          "cipherSha256": r["ciphertextSha256"]}))
    else:
        require(a.certificate and a.key and a.out, "sealed TEST requires key and output")
        print(json.dumps(unseal(a.sealed, a.lock, a.expected_lock_sha, a.code_sha,
                                a.run_id, a.certificate, a.key, a.out)))


if __name__ == "__main__":
    main()

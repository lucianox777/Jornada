#!/usr/bin/env python3
"""Adversarial offline contract for the already-implemented C# IBGE-to-ZIP bridge."""
import hashlib
import csv
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "v9_ibge_e2e_preflight", ROOT / "scripts/v9-e2e-ibge-bridge-preflight.py")
PREFLIGHT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PREFLIGHT)


def dump(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False) + "\n", encoding="utf-8")


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def fixture(directory):
    root = Path(directory)
    corpus = root / "corpus"
    ingestion = root / "ingestion"
    corpus.mkdir()
    ingestion.mkdir()
    reference = "A" * 64
    dump(corpus / "generation-manifest.json", {
        "generator_version": PREFLIGHT.GENERATOR, "population_profile": PREFLIGHT.PROFILE,
        "seed": PREFLIGHT.SEED, "reference_code": "IBGE_CENSO2022_NOMES",
        "input_fingerprint_sha256": reference})
    dump(corpus / "gabarito.json", {
        "generator_version": PREFLIGHT.GENERATOR, "population_profile": PREFLIGHT.PROFILE,
        "seed": PREFLIGHT.SEED, "people": PREFLIGHT.PEOPLE,
        "observations": PREFLIGHT.PEOPLE, "input_fingerprint_sha256": reference})
    ground = ingestion / "bridge-truth.jsonl"
    truth_rows = []
    packages = []
    synthetic_by_gestor = {"SEHAB": "G0", "SMADS": "G1", "SMDET": "G2", "SMS": "G3"}
    for gestor in PREFLIGHT.EXPECTED_GESTORES:
        name = f"SYNTH-{gestor}.zip"
        path = ingestion / name
        rows = b"".join(
            (json.dumps({"idPessoaEntrega": f"SYNTH-{gestor}-{i}",
                         "codigoPessoaOrigem": f"SYNTH-{gestor}-{i}"}) + "\n").encode()
            for i in range(250))
        for i in range(250):
            truth_rows.append({
                "Status": "MATERIALIZADA",
                "ObservationId": f"OBS-{gestor}-{i}",
                "BasePersonId": f"DEV_TRUTH_ONLY-{gestor}-{i}",
                "Partition": "TRAIN" if i % 3 == 0 else ("VALIDATION" if i % 3 == 1 else "TEST"),
                "SyntheticGestor": synthetic_by_gestor[gestor],
                "GestorCodigo": gestor,
                "OpaquePersonId": f"SYNTH-{gestor}-{i}",
                "PackageFileName": name,
                "ExclusionReason": None,
            })
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("manifest.json", "{}")
            archive.writestr("pessoas.jsonl", rows)
            archive.writestr("registros.jsonl", b"")
        packages.append({
            "GestorCodigo": gestor, "FileName": name,
            "PeopleCount": 250, "sha256": digest(path)})
    ground.write_text("".join(json.dumps(row) + "\n" for row in truth_rows),
                      encoding="utf-8")
    people_path = corpus / "pessoas_verdade.csv"
    observations_path = corpus / "observacoes.csv"
    with people_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("base_person_id", "particao"))
        writer.writerows((row["BasePersonId"], row["Partition"]) for row in truth_rows)
    with observations_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("observacao_id", "base_person_id", "particao", "gestor"))
        writer.writerows((row["ObservationId"], row["BasePersonId"], row["Partition"],
                          row["SyntheticGestor"]) for row in truth_rows)
    source_manifest = corpus / "generation-manifest.json"
    metadata = json.loads(source_manifest.read_text(encoding="utf-8"))
    metadata["outputs"] = [
        {"path": name, "sha256": digest(corpus / name)}
        for name in ("gabarito.json", "observacoes.csv", "pessoas_verdade.csv")
    ]
    dump(source_manifest, metadata)
    dump(ingestion / "bridge-manifest.json", {
        "generatorVersion": PREFLIGHT.GENERATOR,
        "bridgeVersion": "SYNTHETIC_INGESTION_BRIDGE_V1",
        "corpusInputFingerprintSha256": reference,
        "sourceObservationCount": 1000, "materializedObservationCount": 1000,
        "excludedObservationCount": 0,
        "truthSidecar": {"path": "bridge-truth.jsonl",
                         "sha256": digest(ground),
                         "containsSyntheticGroundTruth": True,
                         "allowedForScoring": False},
        "packages": packages})
    return corpus, ingestion


class IbgeGeneratorBridgePreflightTests(unittest.TestCase):
    def test_valid_four_manager_person_only_bridge_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture(directory)
            summary = PREFLIGHT.verify(Path(directory))
            self.assertEqual(summary["baseIdentities"], 1000)
            self.assertEqual(summary["materializedObservations"], 1000)
            self.assertEqual(summary["packages"], 4)
            self.assertEqual(summary["sourceIdentitySetDigestMethod"],
                             "SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1")
            expected = "".join(identity + "\n" for identity in sorted(
                f"SYNTH-SEHAB-{i}" for i in range(250)
            )).encode("utf-16le")
            self.assertEqual(summary["sourceIdentitySetSha256Utf16le"]["SEHAB"],
                             hashlib.sha256(expected).hexdigest().upper())
            self.assertFalse(summary["scientificValidation"])
            self.assertNotIn("basePersonId", json.dumps(summary))
            self.assertNotIn("DEV_TRUTH_ONLY", json.dumps(summary))


    def test_nonfinite_manifest_field_is_rejected_even_when_not_used_for_scoring(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus, _ = fixture(directory)
            path = corpus / "generation-manifest.json"
            manifest = json.loads(path.read_text(encoding="utf-8"))
            manifest["unusedDiagnostic"] = float("nan")
            dump(path, manifest)
            with self.assertRaisesRegex(ValueError, "Non-finite"):
                PREFLIGHT.verify(Path(directory))

    def test_symlinked_manifest_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus, _ = fixture(directory)
            original = corpus / "generation-manifest.json"
            alternate = corpus / "manifest-real.json"
            original.rename(alternate)
            original.symlink_to(alternate)
            with self.assertRaisesRegex(ValueError, "symlinked"):
                PREFLIGHT.verify(Path(directory))

    def test_nonfinite_truth_row_is_rejected_even_when_hash_is_recomputed(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            path = ingestion / "bridge-truth.jsonl"
            lines = path.read_text(encoding="utf-8").splitlines()
            tampered = json.loads(lines[0])
            tampered["unusedStatistic"] = float("inf")
            lines[0] = json.dumps(tampered)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            bridge_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(bridge_path.read_text(encoding="utf-8"))
            manifest["truthSidecar"]["sha256"] = digest(path)
            dump(bridge_path, manifest)
            with self.assertRaisesRegex(ValueError, "Non-finite"):
                PREFLIGHT.verify(Path(directory))

    def test_oversized_zip_member_fails_before_decompression_even_with_updated_sha(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            path = ingestion / "SYNTH-SEHAB.zip"
            with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                archive.writestr("manifest.json", "{}")
                archive.writestr("pessoas.jsonl", b" " * (PREFLIGHT.MAX_PERSON_MEMBER_BYTES + 1))
                archive.writestr("registros.jsonl", b"")
            bridge_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(bridge_path.read_text(encoding="utf-8"))
            manifest["packages"][0]["sha256"] = digest(path)
            dump(bridge_path, manifest)
            with self.assertRaisesRegex(ValueError, "exceeds its approved bound"):
                PREFLIGHT.verify(Path(directory))


    def test_generator_source_csv_hash_drift_is_rejected_before_bridge(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus, _ = fixture(directory)
            path = corpus / "observacoes.csv"
            with path.open("a", encoding="utf-8") as stream:
                stream.write("UNDECLARED,UNKNOWN,TEST,G0\n")
            with self.assertRaisesRegex(ValueError, "output bytes"):
                PREFLIGHT.verify(Path(directory))

    def test_source_csv_vs_sidecar_mismatch_fails_even_with_rehashed_generator(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus, _ = fixture(directory)
            path = corpus / "observacoes.csv"
            with path.open("r", encoding="utf-8", newline="") as stream:
                reader = csv.DictReader(stream)
                records = list(reader)
                headers = reader.fieldnames
            records[0]["observacao_id"] = "OBS-TAMPERED"
            with path.open("w", encoding="utf-8", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=headers)
                writer.writeheader()
                writer.writerows(records)
            manifest_path = corpus / "generation-manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            for entry in manifest["outputs"]:
                if entry["path"] == "observacoes.csv":
                    entry["sha256"] = digest(path)
            dump(manifest_path, manifest)
            with self.assertRaisesRegex(ValueError, "Source CSV and bridge truth"):
                PREFLIGHT.verify(Path(directory))

    def test_sidecar_cross_format_identity_swap_fails_even_if_sidecar_rehashed(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            path = ingestion / "bridge-truth.jsonl"
            lines = path.read_text(encoding="utf-8").splitlines()
            first = json.loads(lines[0])
            second = json.loads(lines[1])
            first["BasePersonId"], second["BasePersonId"] = (
                second["BasePersonId"], first["BasePersonId"])
            lines[0] = json.dumps(first)
            lines[1] = json.dumps(second)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            manifest_path = ingestion / "bridge-manifest.json"
            bridge = json.loads(manifest_path.read_text(encoding="utf-8"))
            bridge["truthSidecar"]["sha256"] = digest(path)
            dump(manifest_path, bridge)
            with self.assertRaisesRegex(ValueError, "Source CSV and bridge truth"):
                PREFLIGHT.verify(Path(directory))

    def test_modified_zip_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            with (ingestion / "SYNTH-SEHAB.zip").open("ab") as file:
                file.write(b"tampering")
            with self.assertRaises(ValueError):
                PREFLIGHT.verify(Path(directory))

    def test_generator_provenance_drift_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            corpus, _ = fixture(directory)
            manifest = json.loads((corpus / "generation-manifest.json").read_text())
            manifest["population_profile"] = "LEGACY_ENGINEERING_FIXTURE"
            dump(corpus / "generation-manifest.json", manifest)
            with self.assertRaises(ValueError):
                PREFLIGHT.verify(Path(directory))

    def test_ground_truth_placed_inside_zip_fails_closed_even_with_updated_digest(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            zip_path = ingestion / "SYNTH-SEHAB.zip"
            with zipfile.ZipFile(zip_path, "a") as archive:
                archive.writestr("bridge-truth.jsonl", b"private truth")
            manifest_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(manifest_path.read_text())
            manifest["packages"][0]["sha256"] = digest(zip_path)
            dump(manifest_path, manifest)
            with self.assertRaises(ValueError):
                PREFLIGHT.verify(Path(directory))

    def test_modified_truth_mapping_fails_closed_even_if_receipt_hash_is_updated(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            path = ingestion / "bridge-truth.jsonl"
            lines = path.read_text().splitlines()
            tampered = json.loads(lines[0])
            tampered["PackageFileName"] = "ENTREGA_WRONG.zip"
            lines[0] = json.dumps(tampered)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            bridge_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(bridge_path.read_text())
            manifest["truthSidecar"]["sha256"] = digest(path)
            dump(bridge_path, manifest)
            with self.assertRaisesRegex(ValueError, "ZIP does not match"):
                PREFLIGHT.verify(Path(directory))

    def test_duplicate_base_identity_fails_closed_even_with_valid_sha(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            path = ingestion / "bridge-truth.jsonl"
            lines = path.read_text().splitlines()
            first, second = (json.loads(lines[0]), json.loads(lines[1]))
            second["BasePersonId"] = first["BasePersonId"]
            lines[1] = json.dumps(second)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            bridge_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(bridge_path.read_text())
            manifest["truthSidecar"]["sha256"] = digest(path)
            dump(bridge_path, manifest)
            with self.assertRaisesRegex(ValueError, "base identities"):
                PREFLIGHT.verify(Path(directory))

    def test_wrong_zip_source_identity_fails_closed_even_with_valid_sha(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            zip_path = ingestion / "SYNTH-SEHAB.zip"
            with zipfile.ZipFile(zip_path) as archive:
                contents = {name: archive.read(name) for name in archive.namelist()}
            lines = contents["pessoas.jsonl"].splitlines()
            row = json.loads(lines[0])
            row["codigoPessoaOrigem"] = "SYNTH-WRONG-IDENTITY"
            lines[0] = json.dumps(row).encode()
            contents["pessoas.jsonl"] = b"\n".join(lines) + b"\n"
            with zipfile.ZipFile(zip_path, "w") as archive:
                for name, value in contents.items():
                    archive.writestr(name, value)
            bridge_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(bridge_path.read_text())
            manifest["packages"][0]["sha256"] = digest(zip_path)
            dump(bridge_path, manifest)
            with self.assertRaisesRegex(ValueError, "ZIP does not match"):
                PREFLIGHT.verify(Path(directory))

    def test_invalid_or_missing_sidecar_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            (ingestion / "bridge-truth.jsonl").unlink()
            with self.assertRaises(ValueError):
                PREFLIGHT.verify(Path(directory))

    def test_unsafe_zip_path_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            _, ingestion = fixture(directory)
            manifest_path = ingestion / "bridge-manifest.json"
            manifest = json.loads(manifest_path.read_text())
            manifest["packages"][0]["FileName"] = "../SYNTH-SEHAB.zip"
            dump(manifest_path, manifest)
            with self.assertRaises(ValueError):
                PREFLIGHT.verify(Path(directory))


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Adversarial offline contract for the already-implemented C# IBGE-to-ZIP bridge."""
import hashlib
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
    ground.write_bytes(b'{"basePersonId":"DEV_TRUTH_ONLY"}\n' * 1000)
    packages = []
    for gestor in PREFLIGHT.EXPECTED_GESTORES:
        name = f"SYNTH-{gestor}.zip"
        path = ingestion / name
        rows = b"".join(
            (json.dumps({"idPessoaEntrega": f"SYNTH-{gestor}-{i}"}) + "\n").encode()
            for i in range(250))
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("manifest.json", "{}")
            archive.writestr("pessoas.jsonl", rows)
            archive.writestr("registros.jsonl", b"")
        packages.append({
            "GestorCodigo": gestor, "FileName": name,
            "PeopleCount": 250, "sha256": digest(path)})
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
            self.assertFalse(summary["scientificValidation"])
            self.assertNotIn("basePersonId", json.dumps(summary))
            self.assertNotIn("DEV_TRUTH_ONLY", json.dumps(summary))

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

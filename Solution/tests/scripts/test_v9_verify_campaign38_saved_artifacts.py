"""Offline fail-closed tests for SHA/CRC of stored #38 ZIPs, no network."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"
SPEC = importlib.util.spec_from_file_location(
    "v9_campaign38_local_verify", SCRIPTS / "v9-verify-campaign38-saved-artifacts.py")
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)


class SavedCampaign38VerificationTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.archive = self.root / "archive"
        self.archive.mkdir(mode=0o700)
        self.original_pins = CHECKER._pins.ARTIFACTS
        self.original_winner = CHECKER._pins.WINNER_SHA
        self.addCleanup(setattr, CHECKER._pins, "ARTIFACTS", self.original_pins)
        self.addCleanup(setattr, CHECKER._pins, "WINNER_SHA", self.original_winner)
        winner = b"offline-test-winner"
        CHECKER._pins.WINNER_SHA = hashlib.sha256(winner).hexdigest()
        artifacts = {}
        for index in range(14):
            id = 14742452561 if index == 6 else 10000000000 + index
            role = "winner-operational" if id == 14742452561 else f"fixture-{index}"
            file = self.archive / f"{id}-{role}.zip"
            with zipfile.ZipFile(file, "w") as z:
                name = (CHECKER._pins.WINNER_SHA + ".operational.json"
                        if id == 14742452561 else f"safe-{index}.txt")
                z.writestr(name, winner if id == 14742452561 else b"safe")
            file.chmod(0o600)
            artifacts[id] = role, hashlib.sha256(file.read_bytes()).hexdigest()
        CHECKER._pins.ARTIFACTS = artifacts

    def test_14_archived_bytes_and_crc_are_verified_without_durability_claim(self):
        out = CHECKER.inspect_directory(self.archive)
        self.assertEqual(out["verifiedArtifacts"], 14)
        self.assertEqual(out["status"],
                         "14_OF_14_DESTINATION_BYTES_VERIFIED_NOT_STORAGE_CERTIFIED")
        self.assertTrue(out["zipSha256AndCrcVerified"])
        self.assertFalse(out["storageDurabilityIndependentlyVerified"])
        self.assertFalse(out["storageRecoveryTested"])
        self.assertFalse(out["nasCertified"])
        self.assertFalse(out["E2E_NUCLEO_PASS"])
        self.assertFalse(out["E2E_INTEGRAL_PASS"])
        self.assertEqual(len(out["artifacts"]), 14)
        self.assertNotIn(str(self.archive), json.dumps(out))
        self.assertNotIn("ownerReportedDurableStorage", out)

    def test_mismatched_sha_and_missing_or_extra_artifacts_fail(self):
        id, (role, _) = next(iter(CHECKER._pins.ARTIFACTS.items()))
        file = self.archive / f"{id}-{role}.zip"
        file.write_bytes(file.read_bytes() + b"tampered")
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            CHECKER.inspect_directory(self.archive)
        file.unlink()
        with self.assertRaisesRegex(ValueError, "incomplete"):
            CHECKER.inspect_directory(self.archive)

    def test_broken_zip_crc_even_when_sha_is_updated_fails(self):
        id, (role, _) = next(iter(CHECKER._pins.ARTIFACTS.items()))
        file = self.archive / f"{id}-{role}.zip"
        file.write_bytes(b"invalid-zip")
        CHECKER._pins.ARTIFACTS[id] = role, hashlib.sha256(file.read_bytes()).hexdigest()
        with self.assertRaises(zipfile.BadZipFile):
            CHECKER.inspect_directory(self.archive)

    def test_pinned_operational_winner_inner_sha_must_match(self):
        id = 14742452561
        role = CHECKER._pins.ARTIFACTS[id][0]
        file = self.archive / f"{id}-{role}.zip"
        with zipfile.ZipFile(file, "w") as z:
            z.writestr(CHECKER._pins.WINNER_SHA + ".operational.json",
                       b"modified-inner-bytes")
        CHECKER._pins.ARTIFACTS[id] = role, hashlib.sha256(file.read_bytes()).hexdigest()
        with self.assertRaisesRegex(ValueError, "internal winner bytes mismatch"):
            CHECKER.inspect_directory(self.archive)

    def test_reject_directory_symlink_and_unrestricted_permissions(self):
        alias = self.root / "alias"
        alias.symlink_to(self.archive, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "symlink"):
            CHECKER.inspect_directory(alias)
        self.archive.chmod(0o755)
        with self.assertRaisesRegex(ValueError, "0700"):
            CHECKER.inspect_directory(self.archive)

    def test_reject_zip_symlink_and_public_permissions(self):
        id, (role, _) = next(iter(CHECKER._pins.ARTIFACTS.items()))
        file = self.archive / f"{id}-{role}.zip"
        real = self.archive / "moved.bin"
        file.rename(real)
        file.symlink_to(real)
        with self.assertRaisesRegex(ValueError, "linked ZIP"):
            CHECKER.inspect_directory(self.archive)
        file.unlink()
        real.rename(file)
        file.chmod(0o644)
        with self.assertRaisesRegex(ValueError, "ZIP permissions"):
            CHECKER.inspect_directory(self.archive)

    def test_receipt_is_private_and_never_overwritten(self):
        receipt = self.root / "receipts"
        receipt.mkdir(mode=0o700)
        dest = receipt / "verify.json"
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(CHECKER.main([
                "--directory", str(self.archive), "--receipt", str(dest)]), 0)
        data = json.loads(dest.read_text())
        self.assertEqual(data["verifiedArtifacts"], 14)
        self.assertFalse(data["nasCertified"])
        with self.assertRaisesRegex(ValueError, "receipt target exists"):
            CHECKER.main(["--directory", str(self.archive), "--receipt", str(dest)])


if __name__ == "__main__":
    unittest.main()

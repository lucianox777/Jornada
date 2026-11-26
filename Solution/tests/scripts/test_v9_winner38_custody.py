import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SOURCE=Path(__file__).resolve().parents[2]/"scripts/v9-winner38-custody.py"
SPEC=importlib.util.spec_from_file_location("custody",SOURCE)
C=importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(C)

class CustodyTests(unittest.TestCase):
    def test_exact_original_recovery_and_both_scientific_hashes(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            model=b'{"fixture":true}'
            sha=hashlib.sha256(model).hexdigest()
            original=root/"original.zip"
            with zipfile.ZipFile(original,"w") as z:z.writestr(sha+".operational.json",model)
            raw=original.read_bytes()
            outer=root/"outer.zip"
            def envelope(name,data,extra=False):
                with zipfile.ZipFile(outer,"w") as z:
                    z.writestr(name,data)
                    if extra:z.writestr("extra",b"x")
            with patch.multiple(C.pin,ZIP_SHA=hashlib.sha256(raw).hexdigest(),ZIP_BYTES=len(raw),
                                WINNER_SHA=sha,WINNER_BYTES=len(model)):
                envelope(C.ORIGINAL,raw)
                recovered=root/"recovered.zip"
                C.unpack_verified_envelope(outer,recovered)
                self.assertEqual(recovered.read_bytes(),raw)
                self.assertEqual(C.pin.verify(recovered,root/"selection")["winnerSha256"],sha)
                for name,data,extra in [(C.ORIGINAL,raw[:-1]+bytes([raw[-1]^1]),False),
                    ("../"+C.ORIGINAL,raw,False),(C.ORIGINAL,raw,True)]:
                    envelope(name,data,extra)
                    with self.assertRaises(ValueError):C.unpack_verified_envelope(outer,root/"bad.zip")
                with patch.object(C.pin,"WINNER_SHA","0"*64):
                    with self.assertRaises(ValueError):C.pin.verify(recovered,root/"bad-selection")
    def test_runner_requires_provenance_and_recovers_original_before_selection(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            model=b'{"fixture":true}'
            sha=hashlib.sha256(model).hexdigest()
            original=root/"original.zip"
            with zipfile.ZipFile(original,"w") as z:z.writestr(sha+".operational.json",model)
            raw=original.read_bytes()
            run=dict(id=C.CUSTODY_RUN,run_attempt=C.CUSTODY_ATTEMPT,head_sha=C.CUSTODY_CODE,
                head_branch=C.CUSTODY_BRANCH,path=C.WORKFLOW,event="pull_request",status="completed",conclusion="success")
            artifact=dict(id=C.CUSTODY_ID,expired=False,workflow_run=dict(id=C.CUSTODY_RUN,head_sha=C.CUSTODY_CODE),
                name=f"v9-winner38-original-zip-custody-{C.CUSTODY_RUN}-{C.CUSTODY_ATTEMPT}",
                digest="sha256:"+C.CUSTODY_ENVELOPE_SHA,expires_at=C.CUSTODY_EXPIRES,size_in_bytes=len(raw)+200)
            def metadata(suffix,token):
                return run if suffix.startswith("runs/") else artifact
            def download(artifact_id,expected,destination,token):
                self.assertEqual((artifact_id,expected),(C.CUSTODY_ID,C.CUSTODY_ENVELOPE_SHA))
                outer=destination/"mock-envelope.zip"
                with zipfile.ZipFile(outer,"w") as z:z.writestr(C.ORIGINAL,raw)
                return str(outer),[C.ORIGINAL]
            with patch.multiple(C.pin,ZIP_SHA=hashlib.sha256(raw).hexdigest(),ZIP_BYTES=len(raw),
                                WINNER_SHA=sha,WINNER_BYTES=len(model)),patch.object(C,"api",side_effect=metadata),patch.object(C.p,"download_verified",side_effect=download) as dl:
                for record,key,bad in [(run,"conclusion","failure"),(run,"head_sha","0"*40),
                    (run,"run_attempt",2),(artifact,"expired",True),(artifact,"digest","sha256:"+"0"*64),
                    (artifact,"expires_at","2099-01-01T00:00:00Z"),(artifact,"id",C.pin.ARTIFACT_ID)]:
                    value=record[key];record[key]=bad
                    try:
                        with self.assertRaises(ValueError):C.fetch_for_runner(root,"test-token")
                        dl.assert_not_called()
                        self.assertFalse((root/"selection").exists())
                    finally:record[key]=value
                C.fetch_for_runner(root,"test-token")
                dl.assert_called_once()
                import json
                receipt=json.loads((root/"selection"/"winner-pin-receipt.json").read_text())
                self.assertEqual(receipt["artifactId"],C.pin.ARTIFACT_ID)
                self.assertEqual(receipt["custodyArtifactId"],C.CUSTODY_ID)
                self.assertEqual(receipt["zipSha256"],C.pin.ZIP_SHA)
                self.assertEqual(receipt["winnerSha256"],C.pin.WINNER_SHA)
                self.assertEqual((root/"selection"/(sha+".operational.json")).read_bytes(),model)
                self.assertFalse((root/"mock-envelope.zip").exists())

    def test_scientific_and_custody_pins_are_distinct(self):
        self.assertEqual(C.pin.ARTIFACT_ID,14742452561)
        self.assertEqual(C.CUSTODY_ID,15076996165)
        self.assertNotEqual(C.CUSTODY_ENVELOPE_SHA,C.pin.ZIP_SHA)
        self.assertEqual(C.pin.ZIP_SHA,"24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7")
        self.assertEqual(C.pin.WINNER_SHA,"6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee")

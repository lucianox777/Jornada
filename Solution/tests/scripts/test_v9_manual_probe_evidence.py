import importlib.util
from pathlib import Path
import unittest

source = Path(__file__).resolve().parents[2] / "scripts" / "v9_manual_probe_evidence.py"
spec = importlib.util.spec_from_file_location("v9_manual_probe_evidence", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SHA = "a" * 40


class ManualEvidenceTests(unittest.TestCase):
    def test_requires_matching_sha_completed_success_dispatch(self):
        run = {"event": "workflow_dispatch", "head_sha": SHA, "status": "completed", "conclusion": "success", "id": 7}
        self.assertTrue(module.valid_run(run, SHA))
        for change in ({"head_sha": "b" * 40}, {"event": "pull_request"}, {"status": "in_progress"}, {"conclusion": "failure"}):
            self.assertFalse(module.valid_run({**run, **change}, SHA))

    def test_requires_real_physical_job(self):
        job = {"name": module.PHYSICAL_JOB, "status": "completed", "conclusion": "success"}
        self.assertTrue(module.valid_physical_job([job]))
        self.assertFalse(module.valid_physical_job([{**job, "name": "SQL físico 30k clean — FS blind Rank-Resolve TF0"}]))
        self.assertFalse(module.valid_physical_job([{**job, "conclusion": "skipped"}]))

    def test_end_to_end_positive_negative_and_api_failure(self):
        run = {"event": "workflow_dispatch", "head_sha": SHA, "status": "completed", "conclusion": "success", "id": 7}
        job = {"name": module.PHYSICAL_JOB, "status": "completed", "conclusion": "success"}
        def fetch(url, token):
            return {"jobs": [job]} if "/runs/7/jobs" in url else {"workflow_runs": [run]}
        self.assertTrue(module.check("owner/repo", SHA, "token", fetch))
        self.assertFalse(module.check("owner/repo", "b" * 40, "token", fetch))
        def failed(url, token):
            raise OSError("API unavailable")
        with self.assertRaises(OSError):
            module.check("owner/repo", SHA, "token", failed)


if __name__ == "__main__":
    unittest.main()

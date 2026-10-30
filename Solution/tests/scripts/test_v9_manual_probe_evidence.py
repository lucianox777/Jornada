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

    def test_missing_and_incomplete_manual_evidence(self):
        good = {"event": "workflow_dispatch", "head_sha": SHA, "status": "completed", "conclusion": "success", "id": 7}
        physical = {"name": module.PHYSICAL_JOB, "status": "completed", "conclusion": "success"}
        for altered in (
            {"event": "pull_request"},
            {"head_sha": "b" * 40},
            {"status": "in_progress"},
            {"conclusion": "failure"},
        ):
            def fetch(url, token, altered=altered):
                if "/runs/7/jobs" in url:
                    return {"jobs": [physical]}
                return {"workflow_runs": [{**good, **altered}]}
            self.assertFalse(module.check("owner/repo", SHA, "token", fetch))
        for jobs in ([], [{**physical, "conclusion": "skipped"}], [{"name": "unrelated", "status": "completed", "conclusion": "success"}]):
            def fetch(url, token, jobs=jobs):
                return {"jobs": jobs} if "/runs/7/jobs" in url else {"workflow_runs": [good]}
            self.assertFalse(module.check("owner/repo", SHA, "token", fetch))

    def test_pagination_finds_evidence_beyond_first_page(self):
        wrong = {"event": "workflow_dispatch", "head_sha": "b" * 40, "status": "completed", "conclusion": "success", "id": 3}
        good = {**wrong, "head_sha": SHA, "id": 7}
        physical = {"name": module.PHYSICAL_JOB, "status": "completed", "conclusion": "success"}
        seen = []
        def fetch(url, token):
            seen.append(url)
            if "/runs/7/jobs" in url:
                return {"jobs": [physical]}
            if "page=2" in url:
                return {"workflow_runs": [good]}
            return {"workflow_runs": [wrong] * 100}
        self.assertTrue(module.check("owner/repo", SHA, "token", fetch))
        self.assertTrue(any("page=2" in url for url in seen))

    def test_invalid_sha_fails_closed(self):
        with self.assertRaises(ValueError):
            module.check("owner/repo", "not-a-sha", "token", lambda url, token: {})

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

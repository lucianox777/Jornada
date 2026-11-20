"""Offline regression tests for fail-closed cumulative GitHub Actions inventory."""
import datetime as dt
import importlib.util
import pathlib
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "ci-cumulative-inventory.py"
spec = importlib.util.spec_from_file_location("ci_cumulative_inventory", SCRIPT)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

START = dt.datetime(2026, 11, 19, tzinfo=dt.timezone.utc)
END = dt.datetime(2026, 11, 20, tzinfo=dt.timezone.utc)


def run(run_id, status="completed", conclusion="success"):
    return {"id": run_id, "head_sha": "a" * 40, "created_at": "2026-11-19T12:00:00Z",
            "status": status, "conclusion": conclusion, "name": "ci", "run_attempt": 1}


class InventoryTests(unittest.TestCase):
    def test_empty_window_is_complete_and_non_utc_rejected(self):
        result = mod.enumerate_runs(
            lambda _: {"total_count": 0, "workflow_runs": []}, START, END)
        self.assertEqual(result, {})
        local = dt.datetime(2026, 11, 19, tzinfo=dt.timezone(dt.timedelta(hours=-3)))
        with self.assertRaisesRegex(ValueError, "UTC endpoints"):
            mod.enumerate_runs(lambda _: {"total_count": 0, "workflow_runs": []}, local, END)
        naive = dt.datetime(2026, 11, 19)
        with self.assertRaisesRegex(ValueError, "timezone-aware"):
            mod.enumerate_runs(lambda _: {"total_count": 0, "workflow_runs": []}, naive, END)

    def test_categories_are_mutually_exclusive(self):
        cases = [
            (run(1), "approved"),
            (run(2, "in_progress", None), "running"),
            (run(3, "queued", None), "queued"),
            (run(4, "completed", "failure"), "failed"),
            (run(5, "completed", "skipped"), "other"),
            (run(6, "completed", "cancelled"), "other"),
            (run(7, "completed", None), "other"),
        ]
        for item, expected in cases:
            self.assertEqual(mod.category(item), expected)

    def test_complete_snapshot_counts_and_job_failures(self):
        data = [run(1), run(2, "in_progress", None), run(3, "completed", "failure")]
        def get(path):
            if "/jobs?" in path:
                return {"total_count": 1, "jobs": [
                    {"id": 99, "name": "Memory Batch", "conclusion": "failure"}]}
            return {"total_count": 3, "workflow_runs": data}
        result = mod.snapshot(get, START, END)
        self.assertEqual(result["total_unique_runs"], 3)
        self.assertEqual(result["counts"], {"approved": 1, "running": 1,
                                            "queued": 0, "failed": 1, "other": 0})
        self.assertEqual(len(result["failed_jobs"]), 2)

    def test_pagination_and_run_dedup_fail_closed(self):
        data = [run(i) for i in range(1, 102)]
        def get(path):
            return {"total_count": 101,
                    "workflow_runs": data[:100] if "page=1&" in path else data[100:]}
        self.assertEqual(len(mod.enumerate_runs(get, START, END)), 101)
        def duplicated(path):
            return {"total_count": 101,
                    "workflow_runs": data[:100] if "page=1&" in path else [data[0]]}
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            mod.enumerate_runs(duplicated, START, END)

    def test_1000_cap_and_incomplete_pages_rejected(self):
        with self.assertRaisesRegex(ValueError, "1000-run"):
            mod.enumerate_runs(lambda _: {"total_count": 1001, "workflow_runs": [run(1)]},
                               START, END)
        with self.assertRaisesRegex(ValueError, "Pagination incomplete"):
            mod.enumerate_runs(lambda _: {"total_count": 2, "workflow_runs": []},
                               START, END)

    def test_missing_sha_and_out_of_window_rejected(self):
        with self.assertRaisesRegex(ValueError, "ID/SHA"):
            mod.enumerate_runs(lambda _: {"total_count": 1,
                                          "workflow_runs": [{"id": 1, "created_at": "2026-11-19T12:00:00Z"}]},
                               START, END)
        outside = run(1)
        outside["created_at"] = "2026-11-18T12:00:00Z"
        with self.assertRaisesRegex(ValueError, "outside"):
            mod.enumerate_runs(lambda _: {"total_count": 1, "workflow_runs": [outside]},
                               START, END)

    def test_snapshot_delta_transitions_and_new_runs(self):
        def entry(run_id, state, attempt=1):
            return {"run_id": run_id, "sha": "a" * 40,
                    "category": state, "attempt": attempt}
        base = {"repository": mod.REPO, "since": START.isoformat(),
                "until": END.isoformat(), "coverage": "complete_for_created_window"}
        old = dict(base, runs=[entry(1, "queued"), entry(2, "failed")],
                   total_unique_runs=2)
        new = dict(base, runs=[entry(1, "approved"), entry(2, "running", 2),
                               entry(3, "queued")], total_unique_runs=3)
        delta = mod.compare_snapshots(old, new)
        self.assertEqual(delta["new_run_ids"], [3])
        self.assertEqual(len(delta["transitions"]), 2)
        self.assertEqual(delta["transitions"][1]["attempt_after"], 2)

    def test_snapshot_delta_rejects_mismatched_windows_and_missing_runs(self):
        base = {"repository": mod.REPO, "since": START.isoformat(),
                "until": END.isoformat(), "coverage": "complete_for_created_window"}
        old = dict(base, runs=[{"run_id": 1, "sha": "a" * 40,
                                "category": "queued", "attempt": 1}],
                   total_unique_runs=1)
        new = dict(base, runs=[], total_unique_runs=0)
        with self.assertRaisesRegex(ValueError, "disappeared"):
            mod.compare_snapshots(old, new)
        changed_window = dict(new, since="2026-11-18T00:00:00+00:00")
        with self.assertRaisesRegex(ValueError, "windows differ"):
            mod.compare_snapshots(old, changed_window)

    def test_jobs_paginate_over_100_and_detect_duplicate(self):
        jobs = [{"id": i, "name": f"job-{i}", "conclusion": "failure" if i == 101 else "success"}
                for i in range(1, 102)]
        def get(path):
            return {"total_count": 101, "jobs": jobs[:100] if "page=1" in path else jobs[100:]}
        self.assertEqual(mod.failing_jobs(get, 77), [
            {"id": 101, "name": "job-101", "conclusion": "failure"}])
        def duplicate(path):
            return {"total_count": 101, "jobs": jobs[:100] if "page=1" in path else [jobs[0]]}
        with self.assertRaisesRegex(ValueError, "duplicate job ID"):
            mod.failing_jobs(duplicate, 77)

    def test_job_pagination_missing_id_and_changed_count_fail_closed(self):
        def missing_id(_):
            return {"total_count": 1, "jobs": [{"name": "unidentified", "conclusion": "failure"}]}
        with self.assertRaisesRegex(ValueError, "Missing or duplicate job ID"):
            mod.failing_jobs(missing_id, 78)

        jobs = [{"id": i, "name": "job", "conclusion": "success"} for i in range(1, 102)]
        def changed_total(path):
            if "page=1" in path:
                return {"total_count": 101, "jobs": jobs[:100]}
            return {"total_count": 102, "jobs": jobs[100:]}
        with self.assertRaisesRegex(ValueError, "Jobs changed"):
            mod.failing_jobs(changed_total, 79)

    def test_run_identity_and_attempt_validation(self):
        invalid = [
            dict(run(1), run_attempt=None),
            dict(run(1), run_attempt=0),
            dict(run(1), run_attempt=True),
            dict(run(1), head_sha="not-a-commit"),
            dict(run(1), id=0),
        ]
        for item in invalid:
            with self.subTest(item=item):
                with self.assertRaisesRegex(ValueError, "valid ID/SHA|valid attempt"):
                    mod.enumerate_runs(
                        lambda _: {"total_count": 1, "workflow_runs": [item]},
                        START, END)

    def test_delta_rejects_attempt_regression_and_identity_drift(self):
        base = {"repository": mod.REPO, "since": START.isoformat(),
                "until": END.isoformat(), "coverage": "complete_for_created_window",
                "total_unique_runs": 1}
        entry = {"run_id": 11, "sha": "a" * 40, "category": "approved",
                 "attempt": 2, "event": "pull_request",
                 "created_at": "2026-11-19T12:00:00Z"}
        old = dict(base, runs=[entry])
        regressed = dict(entry, attempt=1)
        with self.assertRaisesRegex(ValueError, "attempt invalid or regressed"):
            mod.compare_snapshots(old, dict(base, runs=[regressed]))
        altered = dict(entry, event="push")
        with self.assertRaisesRegex(ValueError, "immutable metadata"):
            mod.compare_snapshots(old, dict(base, runs=[altered]))

    def test_inconsistent_count_rejected(self):
        data = [run(i) for i in range(1, 102)]
        def get(path):
            if "page=1&" in path:
                return {"total_count": 101, "workflow_runs": data[:100]}
            return {"total_count": 102, "workflow_runs": data[100:]}
        with self.assertRaisesRegex(ValueError, "changed"):
            mod.enumerate_runs(get, START, END)


if __name__ == "__main__":
    unittest.main()

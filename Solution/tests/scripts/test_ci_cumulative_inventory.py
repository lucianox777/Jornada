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

#!/usr/bin/env python3
"""Isolated safety tests: no GitHub, external secrets, SQL or Docker needed."""
import datetime as dt
import importlib.util
import pathlib
import unittest

PATH = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "ci-watchdog.py"
SPEC = importlib.util.spec_from_file_location("ci_watchdog", PATH)
watch = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(watch)

NOW = dt.datetime(2026, 10, 12, 12, 0, tzinfo=dt.timezone.utc)
SHA = "a" * 40


def run(**changes):
    base = dict(
        name="jornada-ci", status="completed", conclusion="failure",
        run_attempt=1, event="push", head_branch="master",
        head_sha=SHA, head_repository={"full_name": watch.REPO},
        created_at="2026-10-12T11:00:00Z", id=123,
        pull_requests=[],
    )
    base.update(changes)
    return base


def jobs(*conclusions):
    return {
        "total_count": len(conclusions),
        "jobs": [
            {"id": i + 5, "status": "completed", "conclusion": value}
            for i, value in enumerate(conclusions)
        ],
    }


class FakeApi:
    def __init__(self, runs=None, job_results=None, logs=None, pr=None):
        self.runs = runs if runs is not None else [run()]
        self.job_results = job_results or jobs("failure", "success")
        self.logs = logs or {5: "The runner has received a shutdown signal"}
        self.pr = pr or {}
        self.posts = []

    def request(self, path, *, method="GET"):
        if method == "POST":
            self.posts.append(path)
            return {}
        if path == "/branches/master":
            return {"commit": {"sha": SHA}}
        if path.startswith("/pulls/"):
            return self.pr
        if path.startswith("/actions/workflows/"):
            return {"workflow_runs": self.runs}
        if path.startswith("/actions/runs/"):
            return self.job_results
        raise AssertionError(path)

    def job_logs(self, job_id):
        return self.logs.get(job_id, "")


class CiWatchdogTests(unittest.TestCase):
    def test_current_head_and_first_attempt_only(self):
        api = FakeApi()
        self.assertTrue(watch.is_current_run(run(), api, SHA, NOW))
        self.assertFalse(watch.is_current_run(run(head_sha="b" * 40), api, SHA, NOW))
        self.assertFalse(watch.is_current_run(run(run_attempt=2), api, SHA, NOW))
        self.assertFalse(watch.is_current_run(run(status="in_progress"), api, SHA, NOW))
        self.assertFalse(watch.is_current_run(run(conclusion="cancelled"), api, SHA, NOW))
        self.assertFalse(watch.is_current_run(run(created_at="2026-10-10T10:00:00Z"), api, SHA, NOW))

    def test_pull_request_head_must_match_open_same_repository(self):
        pr = {
            "state": "open", "base": {"ref": "master"},
            "head": {"sha": SHA, "repo": {"full_name": watch.REPO}},
        }
        candidate = run(event="pull_request", head_branch="feature",
                        pull_requests=[{"number": 10}])
        self.assertTrue(watch.is_current_run(candidate, FakeApi(pr=pr), SHA, NOW))
        self.assertFalse(watch.is_current_run(
            candidate, FakeApi(pr={**pr, "state": "closed"}), SHA, NOW))
        self.assertFalse(watch.is_current_run(
            candidate, FakeApi(pr={**pr, "head": {"sha": "b"*40}}), SHA, NOW))
        self.assertFalse(watch.is_current_run(
            {**candidate, "head_repository": {"full_name": "fork/repo"}},
            FakeApi(pr=pr), SHA, NOW))

    def test_ambiguous_error_does_not_retry(self):
        result, why = watch.retryable_jobs(
            jobs("failure", "success"), lambda _: "AssertionError: mismatch")
        self.assertFalse(result)
        self.assertIn("evidence", why)
        self.assertFalse(watch.retryable_jobs(
            jobs("timed_out"), lambda _: "runner has received a shutdown signal")[0])
        self.assertFalse(watch.retryable_jobs(
            jobs("failure", "cancelled"),
            lambda _: "the runner has received a shutdown signal")[0])

    def test_incomplete_jobs_or_any_unclassified_failure_fail_closed(self):
        self.assertFalse(watch.retryable_jobs(
            {"total_count": 120, "jobs": jobs("failure")["jobs"]},
            lambda _: watch.INFRA_LOG_MARKERS[0])[0])
        result, _ = watch.retryable_jobs(
            jobs("failure", "failure"),
            lambda job_id: watch.INFRA_LOG_MARKERS[0] if job_id == 5 else "")
        self.assertFalse(result)

    def test_verified_infra_retries_only_once_on_apply(self):
        api = FakeApi()
        summary = watch.supervise(api, True, NOW)
        self.assertEqual(api.posts, ["/actions/runs/123/rerun-failed-jobs"])
        self.assertIn("retried failed jobs once", "\n".join(summary))
        dry = FakeApi()
        watch.supervise(dry, False, NOW)
        self.assertEqual(dry.posts, [])

    def test_newer_success_prevents_replay_of_older_failed_run(self):
        succeeded = run(id=124, conclusion="success")
        api = FakeApi(runs=[succeeded, run()])
        watch.supervise(api, True, NOW)
        self.assertEqual(api.posts, [])

    def test_no_retry_of_program_failure(self):
        api = FakeApi(logs={5: "Test failed: expected 2 got 3"})
        watch.supervise(api, True, NOW)
        self.assertEqual(api.posts, [])


    def test_checkpoint_counts_preserve_successful_jobs(self):
        checkpoint = watch.summarize_checkpoint(jobs("success", "failure", "skipped"))
        self.assertIn("1 succeeded", checkpoint)
        self.assertIn("1 failed/blocked", checkpoint)
        self.assertIn("1 skipped", checkpoint)
        self.assertEqual(watch.summarize_checkpoint({
            "total_count": 4, "jobs": jobs("success")["jobs"]
        }), "checkpoint incomplete")

    def test_workflow_resumes_on_ci_failure_and_keeps_hourly_fallback(self):
        root = pathlib.Path(__file__).resolve().parents[3]
        workflow = (root / ".github" / "workflows" / "ci-watchdog.yml").read_text(encoding="utf-8")
        self.assertIn("workflow_run:\\n    workflows: [jornada-ci]", workflow)
        self.assertIn("types: [completed]", workflow)
        self.assertIn("cron: '17 * * * *'", workflow)
        self.assertIn("ref: master", workflow)
        self.assertIn("github.event.workflow_run.conclusion == 'failure'", workflow)
        self.assertIn("actions: write", workflow)
        self.assertIn("cancel-in-progress: false", workflow)
        self.assertNotIn("contents: write", workflow)

    def test_partial_resume_does_not_restart_all_successful_jobs(self):
        api = FakeApi(job_results=jobs("success", "failure", "skipped"),
                      logs={6: "The runner has received a shutdown signal"})
        report = "\\n".join(watch.supervise(api, True, NOW))
        self.assertEqual(api.posts, ["/actions/runs/123/rerun-failed-jobs"])
        self.assertIn("checkpoint: 1 succeeded", report)
        self.assertIn("retried failed jobs once", report)


if __name__ == "__main__":
    unittest.main()

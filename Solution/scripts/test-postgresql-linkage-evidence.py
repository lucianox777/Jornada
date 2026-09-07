#!/usr/bin/env python3
"""Synthetic contract tests for scoped evidence summaries; never substitutes a CI gate."""
import json
import os
import pathlib
import subprocess
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).with_name('postgresql-linkage-evidence.sh')
ALL = 'environment restore build ddl unit policy integration'.split()
SCOPED = 'environment restore build unit policy'.split()


class EvidenceSummaryTests(unittest.TestCase):
    def run_summary(self, stages, required=None, marker=True):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            if marker:
                (root / 'runner-started.txt').write_text('started\n', encoding='utf-8')
            (root / 'results.jsonl').write_text(''.join(json.dumps({
                'stage': name, 'status': status, 'exit_code': 0 if status == 'passed' else 1
            }) + '\n' for name, status in stages), encoding='utf-8')
            env = os.environ.copy()
            env['JORNADA_LINKAGE_EVIDENCE_DIR'] = str(root)
            env.pop('JORNADA_LINKAGE_EVIDENCE_REQUIRED_STAGES', None)
            if required is not None:
                env['JORNADA_LINKAGE_EVIDENCE_REQUIRED_STAGES'] = required
            result = subprocess.run(['bash', str(SCRIPT), 'summary'], env=env,
                                    capture_output=True, text=True, check=False)
            summary = json.loads((root / 'summary.json').read_text(encoding='utf-8')) if (root / 'summary.json').exists() else None
            return result, summary

    def test_default_requires_every_stage(self):
        _, summary = self.run_summary([(s, 'passed') for s in SCOPED])
        self.assertEqual(summary['outcome'], 'incomplete_or_failed')
        self.assertEqual(summary['required_stages'], ALL)

    def test_scoped_success_never_claims_unexecuted_sql(self):
        _, summary = self.run_summary([(s, 'passed') for s in SCOPED], ' '.join(SCOPED))
        self.assertEqual(summary['outcome'], 'passed')
        self.assertEqual([c['status'] for c in summary['checks'] if c['stage'] in ('ddl', 'integration')],
                         ['not_applicable', 'not_applicable'])

    def test_missing_required_stage_is_not_success(self):
        _, summary = self.run_summary([(s, 'passed') for s in SCOPED[:-1]], ' '.join(SCOPED))
        self.assertEqual(summary['outcome'], 'incomplete_or_failed')

    def test_failed_optional_stage_is_not_hidden(self):
        _, summary = self.run_summary([(s, 'passed') for s in SCOPED] + [('integration', 'failed')], ' '.join(SCOPED))
        self.assertEqual(summary['outcome'], 'incomplete_or_failed')

    def test_missing_runner_marker_is_not_success(self):
        _, summary = self.run_summary([(s, 'passed') for s in SCOPED], ' '.join(SCOPED), marker=False)
        self.assertEqual(summary['outcome'], 'incomplete_or_failed')

    def test_default_full_success(self):
        _, summary = self.run_summary([(s, 'passed') for s in ALL])
        self.assertEqual(summary['outcome'], 'passed')

    def test_invalid_scope_is_rejected(self):
        for scope in ('', 'environment restore build', 'environment restore build unit unit',
                      'environment restore build unit fake', 'unit policy'):
            with self.subTest(scope=scope):
                result, summary = self.run_summary([(s, 'passed') for s in ALL], scope)
                self.assertNotEqual(result.returncode, 0)
                self.assertIsNone(summary)

    def test_custom_failed_stage_is_not_ignored(self):
        _, summary = self.run_summary([(s, 'passed') for s in ALL] + [('safety', 'failed')])
        self.assertEqual(summary['outcome'], 'incomplete_or_failed')
        self.assertEqual(summary['checks'][-1]['stage'], 'safety')


if __name__ == '__main__':
    unittest.main()

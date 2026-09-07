"""Offline regression tests for the complete NuGet project inventory and refresh gates."""
from __future__ import annotations
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'nuget-lock-refresh.py'
spec = importlib.util.spec_from_file_location('nuget_lock_refresh', SCRIPT)
refresh = importlib.util.module_from_spec(spec)
spec.loader.exec_module(refresh)

PROJECTS = [f'src/Jornada.Project{i:02d}' for i in range(15)] + [
    'clients/Jornada.Integrador.CSharp',
    'src/Jornada.Resultado.Api',
    'tests/Jornada.Integrador.Tests',
]
OPERATIONAL = 'src/Jornada.Operational.Sql'
PROJECTS[0] = OPERATIONAL


def lock_data(extra=None, package_version='1.0.0'):
    return (json.dumps({'version': 1, 'dependencies': {'net8.0': {
        'Example.Package': {'type': 'Direct', 'requested': f'[{package_version}, )',
                            'resolved': package_version, 'contentHash': 'fixed-content-hash'},
        **(extra or {})}}}, sort_keys=True) + '\n').encode()


class RefreshTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'Solution'
        self.root.mkdir()
        self.before = {}
        for directory in PROJECTS:
            folder = self.root / directory
            folder.mkdir(parents=True)
            (folder / (directory.rsplit('/', 1)[-1] + '.csproj')).write_text('<Project/>')
            path = directory + '/packages.lock.json'
            data = lock_data({'jornada.contracts': {'type': 'Project', 'requested': '[1.0.0, )'}}) if directory == OPERATIONAL else lock_data()
            (self.root / path).write_bytes(data)
            self.before[path] = data
        manifest = {'locks': [{'path': k, 'sha256': hashlib.sha256(v).hexdigest(),
                                'sourceSha256': hashlib.sha256(v).hexdigest()}
                              for k, v in sorted(self.before.items())],
                    'lockGraphGeneration': {}}
        p = self.root / 'config/release/nuget-lock-provenance.json'
        p.parent.mkdir(parents=True)
        p.write_text(json.dumps(manifest))
        (self.root / 'Jornada.sln').write_text('Microsoft Visual Studio Solution File')
        self.root_patch = patch.object(refresh, 'ROOT', self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)
        self.manifest_patch = patch.object(refresh, 'MANIFEST', p)
        self.manifest_patch.start()
        self.addCleanup(self.manifest_patch.stop)

    def test_all_18_projects_including_standalone_are_restored(self):
        targets = refresh.project_targets(self.before)
        calls = []
        with patch.object(refresh, 'run', side_effect=lambda *args: calls.append(args) or ''):
            refresh.restore_all(targets, '--locked-mode')
        self.assertEqual(len(calls), 19)
        self.assertEqual(calls[0], ('dotnet', 'restore', 'Jornada.sln', '--locked-mode'))
        self.assertEqual({c[2] for c in calls[1:]}, {
            d + '/' + d.rsplit('/', 1)[-1] + '.csproj' for d in PROJECTS})
        self.assertTrue(all(c[-1] == '--locked-mode' for c in calls))

    def test_missing_standalone_project_is_rejected(self):
        (self.root / 'src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj').unlink()
        with self.assertRaisesRegex(RuntimeError, 'inventory mismatch'):
            refresh.project_targets(self.before)

    def test_unversioned_project_is_rejected(self):
        p = self.root / 'src/Jornada.Untracked/Jornada.Untracked.csproj'
        p.parent.mkdir(parents=True)
        p.write_text('<Project/>')
        with self.assertRaisesRegex(RuntimeError, 'inventory mismatch'):
            refresh.project_targets(self.before)

    def test_duplicate_project_in_one_directory_is_rejected(self):
        (self.root / OPERATIONAL / 'Duplicate.csproj').write_text('<Project/>')
        with self.assertRaisesRegex(RuntimeError, 'projects=19'):
            refresh.project_targets(self.before)

    def test_bin_obj_and_local_projects_are_ignored(self):
        for d in ('bin', 'obj', '.local'):
            p = self.root / d / 'Generated.csproj'
            p.parent.mkdir(parents=True)
            p.write_text('<Project/>')
        self.assertEqual(len(refresh.project_targets(self.before)), 18)

    def simulate(self, drift=False, extra_change=False, second_drift=False, dirty=False):
        # The fake SDK changes only ProjectReference metadata in Resultado.Api.
        updated = lock_data({'jornada.operational.sql': {'type': 'Project',
            'requested': '[1.0.0, )', 'dependencies': {'Jornada.Contracts': '[1.0.0, )'}}})
        if drift:
            updated = lock_data(package_version='2.0.0')
        calls = []
        phase = 0
        original_manifest = json.loads(refresh.MANIFEST.read_text())
        def fake_run(*args):
            nonlocal phase
            calls.append(args)
            if args == ('dotnet', '--version'):
                return refresh.SDK + '\n'
            if args == ('git', 'status', '--porcelain'):
                return ' M surprise\n' if dirty else ''
            if args[:2] == ('dotnet', 'restore'):
                if args[2] == 'Jornada.sln':
                    phase += 1
                if args[2] == 'src/Jornada.Resultado.Api/Jornada.Resultado.Api.csproj':
                    p = self.root / 'src/Jornada.Resultado.Api/packages.lock.json'
                    p.write_bytes(lock_data(package_version='3.0.0') if second_drift and phase == 2 else updated)
                return ''
            if args[:2] == ('python3', 'scripts/nuget-lock-gate.py'):
                path = Path(args[args.index('--summary') + 1])
                path.write_text(json.dumps({'projectCount': 18, 'combinedSha256': 'mock-hash'}))
                return ''
            if args[:2] == ('python3', 'scripts/nuget-lock-provenance-gate.py'):
                return ''
            if args == ('git', 'rev-parse', 'HEAD'):
                return 'test-commit\n'
            if args == ('git', 'diff', '--name-only', '--relative'):
                result = 'config/release/nuget-lock-provenance.json\nsrc/Jornada.Resultado.Api/packages.lock.json\n'
                return result + ('src/Unexpected.cs\n' if extra_change else '')
            raise AssertionError(args)
        with patch.object(refresh, 'run', side_effect=fake_run):
            if dirty:
                with self.assertRaisesRegex(RuntimeError, 'clean checkout'):
                    refresh.main()
                self.assertEqual(original_manifest, json.loads(refresh.MANIFEST.read_text()))
                self.assertFalse(any(c[:2] == ('dotnet', 'restore') for c in calls))
                return
            if drift or extra_change or second_drift:
                with self.assertRaises(RuntimeError):
                    refresh.main()
            else:
                self.assertEqual(refresh.main(), 0)
                evidence = json.loads((self.root / '.local/nuget-lock-refresh/refresh-summary.json').read_text())
                self.assertEqual(evidence['restoreTargetCount'], 18)
                self.assertIn('src/Jornada.Resultado.Api/packages.lock.json', evidence['changedLocks'])
                self.assertEqual(evidence['lockedRestore'], 'PASS')
                self.assertEqual(len([c for c in calls if c[:2] == ('dotnet', 'restore')]), 57)
            if drift:
                self.assertEqual(original_manifest, json.loads(refresh.MANIFEST.read_text()))

    def test_sdk_refreshes_standalone_lock_and_preserves_package_graph(self):
        self.simulate()

    def test_package_drift_fails_before_provenance_write(self):
        self.simulate(drift=True)

    def test_second_evaluation_drift_is_rejected(self):
        self.simulate(second_drift=True)

    def test_unexpected_worktree_change_is_rejected(self):
        self.simulate(extra_change=True)

    def test_dirty_checkout_is_rejected_before_restore(self):
        self.simulate(dirty=True)


if __name__ == '__main__':
    unittest.main()

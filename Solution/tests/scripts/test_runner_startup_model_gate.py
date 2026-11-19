"""Static regression: operator-selected model is checked before the hosted worker starts."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROGRAM = ROOT / "src/Jornada.Linkage.Runner/Program.cs"
OPTIONS = ROOT / "src/Jornada.Linkage.Runner/LinkageRunOptions.cs"


class RunnerStartupModelGateTests(unittest.TestCase):
    def test_preflight_precedes_worker_and_runtime_loop(self):
        source = PROGRAM.read_text(encoding="utf-8")
        self.assertIn('Linkage:SelectedModelVersion', source)
        self.assertIn('GetModelByVersionAsync(configuredVersion.Value', source)
        self.assertIn('RUNNER_MODEL_CONFIGURATION_MISMATCH', source)
        self.assertIn('RUNNER_MODEL_CONFIGURATION_MISSING', source)
        self.assertIn('RUNNER_MODEL_CONTRACT_INCOMPATIBLE', source)
        self.assertIn('AddInMemoryCollection(builder.Configuration.AsEnumerable()', source)
        self.assertIn('builder.Services.AddSingleton<IConfiguration>(frozenConfiguration)', source)
        self.assertIn('builder.Services.AddSingleton<IProbabilisticIdentityLinkage>(pinnedLinkage)', source)
        self.assertIn('SHA256.HashData(fingerprintInput)', source)
        self.assertIn('pinnedLinkage.PinStartupModel(selectedModel)', source)
        linkage = (ROOT / 'src/Jornada.Linkage.Runner/SqlProbabilisticIdentityLinkage.cs').read_text(encoding='utf-8')
        self.assertIn('RUNNER_MODEL_PINNED_VERSION_MISMATCH', linkage)
        self.assertIn('return pinned;', linkage)
        self.assertIn('runtimeCache.ContainsKey(selected.ModelId)', linkage)
        self.assertNotIn('reloadOnChange: true', source)
        self.assertLess(source.index('GetModelByVersionAsync(configuredVersion.Value'),
                        source.index('AddHostedService<LinkageRunnerWorker>'))
        self.assertLess(source.index('GetModelByVersionAsync(configuredVersion.Value'),
                        source.index('await builder.Build().RunAsync()'))
        self.assertIn('options = options with { ModelVersion = configuredVersion.Value };', source)

    def test_local_cluster_pins_calibrated_model_only_for_one_shot(self):
        wrapper = (ROOT / "scripts/local-cluster.sh").read_text(encoding="utf-8")
        start = wrapper.index("run_linkage() {")
        end = wrapper.index("\ndiagnose_linkage() {", start)
        linkage = wrapper[start:end]
        self.assertIn('active="$(sql_scalar', linkage)
        self.assertIn('version="$(sql_scalar', linkage)
        self.assertIn('model_id="$(sql_scalar', linkage)
        self.assertIn('compose exec -T -e "Linkage__SelectedModelVersion=$version"', linkage)
        self.assertIn('--model-version "$version"', linkage)
        self.assertNotIn('invocation.\\n', linkage)
        self.assertIn('\n  compose exec -T -e "Linkage__SelectedModelVersion=$version"', linkage)
        self.assertNotIn('compose up', linkage)
        self.assertNotIn('docker compose down', linkage)
        # Check shell parsing as well as static command placement.
        import subprocess
        subprocess.run(['bash', '-n', str(ROOT / 'scripts/local-cluster.sh')], check=True)


    def test_scale_harness_pins_generated_model_for_both_runner_invocations(self):
        wrapper = (ROOT / "scripts/local-scale.sh").read_text(encoding="utf-8")
        self.assertIn('MODEL_VERSION="$(scalar', wrapper)
        self.assertIn('[[ "$MODEL_VERSION" =~ ^[0-9]+$ ]]', wrapper)
        self.assertEqual(wrapper.count('Linkage__SelectedModelVersion="$MODEL_VERSION"'), 2)
        self.assertIn(
            'Linkage__SelectedModelVersion="$MODEL_VERSION" \\\n'
            '  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \\\n'
            '  --blocking-pass-audit-labels', wrapper)
        self.assertIn(
            'Linkage__SelectedModelVersion="$MODEL_VERSION" \\\n'
            '  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \\\n'
            '  --mode MODEL_VALIDATION --model-version "$MODEL_VERSION"', wrapper)
        import subprocess
        subprocess.run(['bash', '-n', str(ROOT / 'scripts/local-scale.sh')], check=True)

    def test_replay_preserves_historical_model_binding(self):
        source = PROGRAM.read_text(encoding="utf-8")
        self.assertIn('options.Mode != LinkageRunType.REPLAY', source)
        options = OPTIONS.read_text(encoding="utf-8")
        self.assertIn('REPLAY exige --replay-source-run-id', options)


if __name__ == "__main__":
    unittest.main()

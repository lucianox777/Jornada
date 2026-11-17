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
        self.assertNotIn('reloadOnChange: true', source)
        self.assertLess(source.index('GetModelByVersionAsync(configuredVersion.Value'),
                        source.index('AddHostedService<LinkageRunnerWorker>'))
        self.assertLess(source.index('GetModelByVersionAsync(configuredVersion.Value'),
                        source.index('await builder.Build().RunAsync()'))
        self.assertIn('options = options with { ModelVersion = configuredVersion.Value };', source)

    def test_replay_preserves_historical_model_binding(self):
        source = PROGRAM.read_text(encoding="utf-8")
        self.assertIn('options.Mode != LinkageRunType.REPLAY', source)
        options = OPTIONS.read_text(encoding="utf-8")
        self.assertIn('REPLAY exige --replay-source-run-id', options)


if __name__ == "__main__":
    unittest.main()

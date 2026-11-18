"""Contrato de paridade funcional Windows/Linux sem exigir host Windows/RHEL."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
WINDOWS = ROOT / "Install-JornadaLocal.ps1"
LINUX = ROOT / "Install-JornadaHml-RHEL10.sh"
COMPOSE = ROOT / "docker-compose.yml"
OVERRIDE = ROOT / "install/linux-hml/compose.single-node.yml"

# Cada linha representa uma capacidade compartilhada, não igualdade textual.
# Diferenças intencionais (NODE2, perfil, comandos de administração) ficam na decisão.
CAPABILITIES = {
    "docker_engine": ("Assert-DockerEngine", "docker info"),
    "compose": ("docker','compose','version", "docker compose version"),
    "dotnet_sdk": ("dotnet' '.NET SDK'", "dotnet --version"),
    "env_bootstrap": ("local_env_bootstrap.py", "local_env_bootstrap.py"),
    "locked_restore": ("restore','Jornada.sln','--locked-mode", "dotnet restore Jornada.sln --locked-mode"),
    "release_build": ("build','Jornada.sln','--configuration','Release','--no-restore','-warnaserror", "dotnet build Jornada.sln --configuration Release --no-restore -warnaserror"),
    "node_image": ("build','jornada-node1','jornada-nas", "compose build jornada-node1 jornada-nas"),
    "database": ("$ClusterScript up -NoBuild", 'local-db.sh" up'),
    "ready": ("/health/ready", "/health/ready"),
    "monitor": ("/monitor", "/monitor"),
}

class InstallerParity(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.windows = WINDOWS.read_text(encoding="utf-8-sig")
        cls.linux = LINUX.read_text(encoding="utf-8")

    def test_shared_capabilities(self):
        for name, (windows_token, linux_token) in CAPABILITIES.items():
            with self.subTest(capability=name, platform="windows"):
                self.assertIn(windows_token, self.windows)
            with self.subTest(capability=name, platform="linux"):
                self.assertIn(linux_token, self.linux)

    def test_same_compose_baseline(self):
        self.assertTrue(COMPOSE.is_file())
        self.assertIn("docker-compose.yml", self.linux)
        self.assertIn("Invoke-Compose", self.windows)
        self.assertIn("compose.single-node.yml", self.linux)
        self.assertIn('profiles: ["two-node-only"]', OVERRIDE.read_text())

    def test_documented_topology_divergence(self):
        self.assertIn("5180/health/ready", self.windows)
        self.assertIn("5180/monitor", self.windows)
        self.assertIn('grep -qx \'jornada-node2\'', self.linux)
        self.assertIn("Jornada.Cluster.Hml.SingleNode.json", self.linux)

if __name__ == "__main__":
    unittest.main()

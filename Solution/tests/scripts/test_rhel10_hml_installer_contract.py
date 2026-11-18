"""Contratos estáticos do instalador HML RHEL 10 single-node (sem Docker físico)."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "Install-JornadaHml-RHEL10.sh"
OVERRIDE = ROOT / "install/linux-hml/compose.single-node.yml"
README = ROOT / "install/linux-hml/README.md"

class Rhel10HmlInstallerContract(unittest.TestCase):
    def test_platform_and_no_destructive_reset(self):
        s = SCRIPT.read_text()
        self.assertIn('uname -m', s)
        self.assertIn('VERSION_ID%%.*', s)
        self.assertIn('dotnet restore Jornada.sln --locked-mode', s)
        self.assertIn('local-db.sh" up', s)
        self.assertNotIn('local-db.sh" reset', s)
        self.assertNotIn('compose down -v', s)
        self.assertNotIn('ACTIVATE', s)
        self.assertNotIn('GENERATE_DRAFT', s)

    def test_single_node_compose(self):
        c = OVERRIDE.read_text()
        self.assertIn('jornada-node2:', c)
        self.assertIn('profiles: ["two-node-only"]', c)
        self.assertIn('jornada-node1:', c)
        self.assertIn('Jornada.Cluster.Hml.SingleNode.json', c)
        self.assertNotIn('jornada-nas:\n    profiles:', c)

    def test_health_and_operational_limits(self):
        s = SCRIPT.read_text()
        self.assertIn('/health/ready', s)
        self.assertIn('/monitor', s)
        self.assertIn('jornada-reference-bootstrap', s)
        self.assertIn('jornada-nas', s)
        self.assertIn('jornada-node2', s)
        self.assertIn('RHEL', README.read_text())

if __name__ == "__main__":
    unittest.main()

"""Product lock validation must not consume local tool or sibling checkout graphs."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


GATE = Path(__file__).resolve().parents[2] / "scripts" / "nuget-lock-gate.py"


class ProductInventoryTests(unittest.TestCase):
    def test_local_tools_and_nested_checkout_do_not_replace_product_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Directory.Build.props").write_text(
                "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                "</PropertyGroup></Project>", encoding="utf-8")
            product = root / "src" / "App"
            product.mkdir(parents=True)
            (product / "App.csproj").write_text("<Project/>", encoding="utf-8")
            (product / "packages.lock.json").write_text(
                json.dumps({"version": 1, "dependencies": {"net10.0": {}}}), encoding="utf-8")
            for relative in (".local/tool", "work-copy/Solution/src/App"):
                local = root / relative
                local.mkdir(parents=True)
                (local / "App.csproj").write_text("not product XML", encoding="utf-8")
                (local / "packages.lock.json").write_text("not product JSON", encoding="utf-8")
            summary = root / "summary.json"
            result = subprocess.run([sys.executable, str(GATE), "--root", str(root),
                "--summary", str(summary)], capture_output=True, text=True, check=False)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            report = json.loads(summary.read_text(encoding="utf-8"))
            self.assertEqual(report["projectCount"], 1)
            self.assertEqual(report["projects"][0]["project"], "src/App/App.csproj")

            # A missing lock in the actual product must STILL block the gate.
            (product / "packages.lock.json").unlink()
            result = subprocess.run([sys.executable, str(GATE), "--root", str(root)],
                capture_output=True, text=True, check=False)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("lock file ausente", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()

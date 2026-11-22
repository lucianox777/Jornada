"""Real CLI subprocess smoke: deterministic synthetic ZIP, no network."""
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class ApoioCliPhysicalZipSmoke(unittest.TestCase):
    def test_cli_produces_real_zip_without_transmission(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            origin, config, output = base / "origem", base / "config", base / "saida"
            origin.mkdir()
            config.mkdir()
            shutil.copy2(ROOT / "exemplos/entrada/pessoas.csv", origin / "pessoas.csv")
            shutil.copy2(ROOT / "exemplos/esquemas/pessoa.sintetica.schema.json", config / "pessoas.schema.json")
            profile = json.loads((ROOT / "exemplos/perfil.sintetico.json").read_text(encoding="utf-8"))
            profile["contratos"]["pessoas"]["arquivo"] = "pessoas.schema.json"
            profile_path = config / "perfil.json"
            profile_path.write_text(json.dumps(profile), encoding="utf-8")

            cmd = [sys.executable, "executar_cli.py", "--entrada", str(origin),
                   "--perfil", str(profile_path), "--saida", str(output), "--nao-enviar"]
            first = subprocess.run(cmd, cwd=ROOT, text=True, capture_output=True, timeout=45, check=False)
            self.assertEqual(first.returncode, 0, first.stderr + first.stdout)
            result = json.loads(first.stdout)
            self.assertEqual(result["estado"], "GERADO_NAO_ENVIADO")
            files = list(output.glob("*.zip"))
            self.assertEqual(len(files), 1)
            with zipfile.ZipFile(files[0]) as archive:
                self.assertEqual(archive.namelist(), ["manifest.json", "pessoas.jsonl", "registros.jsonl"])
                self.assertEqual(len(archive.read("pessoas.jsonl").splitlines()), 2)
                self.assertEqual(archive.read("registros.jsonl"), b"")
            original_bytes = files[0].read_bytes()
            second = subprocess.run(cmd, cwd=ROOT, text=True, capture_output=True, timeout=45, check=False)
            self.assertEqual(second.returncode, 0, second.stderr + second.stdout)
            self.assertEqual(json.loads(second.stdout)["estado"], "GERADO_NAO_ENVIADO")
            self.assertEqual(files[0].read_bytes(), original_bytes)
            self.assertEqual(len(list(output.glob("*.zip"))), 1)


if __name__ == "__main__":
    unittest.main()

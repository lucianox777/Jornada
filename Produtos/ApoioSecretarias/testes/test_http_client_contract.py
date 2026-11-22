"""Exercise the real HTTP client with an intercepted HTTPS transport (no network)."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from apoio import core


class FakeResponse:
    status = 202

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def read(self, _limit):
        return b'{"entregaId":"00000000-0000-0000-0000-000000000001","status":"RECEBIDO"}'


class FakeOpener:
    def __init__(self):
        self.request = None

    def open(self, request, timeout):
        self.request = request
        assert timeout == 40
        return FakeResponse()


class HttpClientContractTest(unittest.TestCase):
    def test_post_zip_exact_bytes_and_headers(self):
        with tempfile.TemporaryDirectory() as directory:
            payload = b"synthetic zip bytes only"
            digest = hashlib.sha256(payload).hexdigest()
            name = f"ENTREGA_SEHAB_SEHAB_v2_{digest}.zip"
            zip_path = Path(directory) / name
            zip_path.write_bytes(payload)
            opener = FakeOpener()
            profile = {"gestor": "SEHAB", "sistemaOrigem": "SEHAB",
                       "endpoint": "https://dev.example.invalid/api/v1/ingestao/entregas"}
            with patch.object(core, "_no_redirect", return_value=opener):
                result = core._enviar(zip_path, profile, "SYNTHETIC_KEY")
            req = opener.request
            self.assertEqual(req.get_method(), "POST")
            self.assertEqual(req.full_url, profile["endpoint"])
            self.assertEqual(req.data, payload)
            headers = {k.lower(): v for k, v in req.header_items()}
            self.assertEqual(headers["content-type"], "application/zip")
            self.assertEqual(headers["content-disposition"], f'attachment; filename="{name}"')
            self.assertEqual(headers["x-jornada-gestor"], "SEHAB")
            self.assertEqual(headers["x-jornada-access-key"], "SYNTHETIC_KEY")
            self.assertEqual(headers["idempotency-key"], f"sha256:{digest}")
            self.assertEqual(result["http"], 202)
            self.assertEqual(result["recibo"]["status"], "RECEBIDO")

    def test_reject_non_https_before_transport(self):
        with tempfile.TemporaryDirectory() as directory:
            zip_path = Path(directory) / "any.zip"
            zip_path.write_bytes(b"x")
            with patch.object(core, "_no_redirect") as opener:
                with self.assertRaises(core.Falha) as ctx:
                    core._enviar(zip_path, {"endpoint": "http://localhost:8000"}, "SYNTHETIC_KEY")
                self.assertEqual(ctx.exception.codigo, "ENDPOINT_INVALIDO")
                opener.assert_not_called()


    def test_reject_changed_zip_before_transport(self):
        with tempfile.TemporaryDirectory() as directory:
            digest = hashlib.sha256(b"original").hexdigest()
            zip_path = Path(directory) / f"ENTREGA_SEHAB_SEHAB_v2_{digest}.zip"
            zip_path.write_bytes(b"changed after naming")
            profile = {"gestor": "SEHAB", "sistemaOrigem": "SEHAB",
                       "endpoint": "https://dev.example.invalid/api/v1/ingestao/entregas"}
            with patch.object(core, "_no_redirect") as opener:
                with self.assertRaises(core.Falha) as ctx:
                    core._enviar(zip_path, profile, "SYNTHETIC_KEY")
                self.assertEqual(ctx.exception.codigo, "ZIP_DIVERGENTE")
                opener.assert_not_called()

    def test_reject_missing_credentials_before_transport(self):
        with tempfile.TemporaryDirectory() as directory:
            zip_path = Path(directory) / "any.zip"
            zip_path.write_bytes(b"x")
            profile = {"endpoint": "https://dev.example.invalid/api/v1/ingestao/entregas"}
            with patch.object(core, "_no_redirect") as opener:
                with self.assertRaises(core.Falha) as ctx:
                    core._enviar(zip_path, profile, "")
                self.assertEqual(ctx.exception.codigo, "CREDENCIAL_AUSENTE")
                opener.assert_not_called()

if __name__ == "__main__":
    unittest.main()

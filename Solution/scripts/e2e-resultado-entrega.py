"""Prova HTTP da Resultado.Api sobre as duas entregas sintéticas do local-e2e."""
import http.client
import json
from pathlib import Path
import subprocess
import sys
import time
from urllib.parse import quote, urlsplit

ROOT = Path(__file__).resolve().parents[1]


def check_detail(detail, filename, latest_id):
    delivery = detail["entrega"]
    if (delivery["entregaId"] != latest_id or delivery["gestor"] != "SEHAB"
            or delivery["nomeArquivo"] != filename or delivery["status"] != "PROCESSADA"
            or detail["entregasEncontradasParaOMesmoNome"] != 2 or detail["finalizado"] is not True):
        raise ValueError("Detalhe diverge dos dois recibos HTTP da fixture SEHAB.")
    counts = detail["processamento"]["resumoItens"]
    totals = [row["totalObservado"] for row in counts
              if (row["classeItem"], row["resultado"]) == ("REGISTRO", "RETRANSMITIDO")]
    if totals != [1]:
        raise ValueError("Resumo diverge do único fato retransmitido no SQL.")


def run(base_url, filename, latest_id, receipt):
    origin = urlsplit(base_url)
    if origin.scheme != "http" or origin.hostname != "127.0.0.1" or not origin.port:
        raise ValueError("Resultado.Api requer porta efêmera de loopback.")
    keys = json.loads((ROOT / "config/security/test-access-keys.json").read_text())["credentials"]
    gestores = {c["gestorCodigo"]: c["accessKey"] for c in keys if c["type"] == "GESTOR"}
    path = "/api/v1/ingestao/resultados/"
    statuses = {}
    start = time.monotonic()

    def request(case, name, expected, gestor=None):
        headers = {} if gestor is None else {
            "X-Jornada-Gestor": gestor, "X-Jornada-Access-Key": gestores[gestor]}
        connection = http.client.HTTPConnection(origin.hostname, origin.port, timeout=15)
        try:
            connection.request("GET", path + quote(name, safe=""), headers=headers)
            response = connection.getresponse()
            body = response.read()
            if response.status != expected:
                raise ValueError(f"{case}: HTTP {response.status}, esperado {expected}.")
            statuses[case] = response.status
            if expected == 200:
                return json.loads(body)
            if latest_id.encode() in body or filename.encode() in body:
                raise ValueError(f"{case}: detalhe da entrega presente em negativa.")
        finally:
            connection.close()

    request("semCredencial", filename, 401)
    request("nomeInvalido", "invalido.txt", 400, "SEHAB")
    request("outroGestor", filename, 404, "SMADS")
    first = request("consulta", filename, 200, "SEHAB")
    check_detail(first, filename, latest_id)
    if first != request("releitura", filename, 200, "SEHAB"):
        raise ValueError("Releitura da entrega terminal devolveu outro resultado.")
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    receipt.write_text(json.dumps({
        "sha": sha, "entregaId": latest_id, "http": statuses,
        "registrosRetransmitidos": 1, "entregasMesmoNome": 2,
        "duracaoSegundos": round(time.monotonic() - start, 3)
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    run(*sys.argv[1:4], Path(sys.argv[4]))

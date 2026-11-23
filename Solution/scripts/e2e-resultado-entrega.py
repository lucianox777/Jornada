"""Prova HTTP da Resultado.Api sobre as duas entregas sintéticas do local-e2e."""
import http.client
import json
from pathlib import Path
import subprocess
import sys
import time
from urllib.parse import quote, urlsplit

ROOT = Path(__file__).resolve().parents[1]


def check_detail(detail, filename, latest_id, payload_sha256):
    """Bind Resultado.Api to the exact submitted ZIP and reconciled processing."""
    def count(x):
        return type(x) is int and x >= 0

    if type(payload_sha256) is not str or len(payload_sha256) != 64 or any(
            c not in "0123456789abcdefABCDEF" for c in payload_sha256):
        raise ValueError("SHA-256 local da entrega inválido.")
    if (not isinstance(detail, dict) or detail.get("nomeArquivoConsultado") != filename
            or detail.get("entregasEncontradasParaOMesmoNome") != 2
            or detail.get("finalizado") is not True):
        raise ValueError("Resultado.Api não corresponde às entregas HTTP.")
    entrega = detail.get("entrega")
    if (not isinstance(entrega, dict) or entrega.get("entregaId") != latest_id
            or entrega.get("gestor") != "SEHAB"
            or entrega.get("nomeArquivo") != filename
            or entrega.get("status") != "PROCESSADA"
            or not count(entrega.get("bytesRecebidos"))
            or entrega["bytesRecebidos"] == 0
            or type(entrega.get("payloadSha256")) is not str
            or entrega["payloadSha256"].lower() != payload_sha256.lower()):
        raise ValueError("Bytes ou escopo da Entrega divergem do ZIP enviado.")

    processing = detail.get("processamento")
    if not isinstance(processing, dict):
        raise ValueError("Resumo de processamento ausente.")
    lots = processing.get("lotes")
    if not isinstance(lots, list) or not lots:
        raise ValueError("Sem lote processado verificável.")
    ids = set()
    people = records = 0
    for lot in lots:
        if (not isinstance(lot, dict) or type(lot.get("loteId")) is not str
                or not lot["loteId"] or lot["loteId"] in ids
                or lot.get("status") != "PROCESSADO"
                or not count(lot.get("qtdPessoas")) or not count(lot.get("qtdRegistros"))):
            raise ValueError("Lote duplicado, incompleto ou inválido.")
        ids.add(lot["loteId"])
        people += lot["qtdPessoas"]
        records += lot["qtdRegistros"]
    if (people, records) != (1, 1):
        raise ValueError("Lotes divergem da fixture 1 Pessoa + 1 Registro.")

    rows = processing.get("resumoItens")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Resumo de itens ausente.")
    keys = set()
    detailed = consolidated = 0
    retransmitted = []
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Linha de resumo inválida.")
        key = (row.get("classeItem"), row.get("resultado"))
        a, b, total = (row.get("quantidadeDetalhada"),
                       row.get("quantidadeConsolidada"), row.get("totalObservado"))
        if (not all(type(x) is str and x for x in key) or key in keys
                or not all(count(x) for x in (a, b, total))
                or a + b != total):
            raise ValueError("Resumo por classe incoerente ou duplicado.")
        keys.add(key)
        detailed += a
        consolidated += b
        if key == ("REGISTRO", "RETRANSMITIDO"):
            retransmitted.append(total)
    if retransmitted != [1]:
        raise ValueError("Resumo não registra o único fato retransmitido.")
    if (not count(processing.get("itensDetalhadosDisponiveis"))
            or not count(processing.get("itensConsolidados"))
            or processing["itensDetalhadosDisponiveis"] != detailed
            or processing["itensConsolidados"] != consolidated
            or processing.get("detalheItemAItemIntegral") is not (consolidated == 0)):
        raise ValueError("Totais detalhados/consolidados ou retenção inconsistentes.")
    return {"payloadSha256Verified": True, "lotesProcessados": len(lots),
            "resumoItensConsistente": True}


def run(base_url, filename, latest_id, receipt, payload_sha256):
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
    verified = check_detail(first, filename, latest_id, payload_sha256)
    if first != request("releitura", filename, 200, "SEHAB"):
        raise ValueError("Releitura da entrega terminal devolveu outro resultado.")
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    receipt.write_text(json.dumps({
        "sha": sha, "entregaId": latest_id, "http": statuses,
        "registrosRetransmitidos": 1, "entregasMesmoNome": 2,
        **verified,
        "duracaoSegundos": round(time.monotonic() - start, 3)
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    run(*sys.argv[1:4], Path(sys.argv[4]), sys.argv[5])

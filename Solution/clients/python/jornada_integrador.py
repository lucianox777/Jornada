#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

SHA_RE = re.compile(r"^[0-9a-f]{64}$")


def load_config(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        cfg = json.load(f)
    if not cfg.get("gestor"):
        raise ValueError("gestor é obrigatório no arquivo de configuração")
    if not cfg.get("accessKey") or cfg.get("accessKey") == "CHANGE_ME":
        raise ValueError("accessKey deve ser configurada")
    endpoints = cfg.get("endpoints") or {}
    if not endpoints.get("envio"):
        raise ValueError("endpoints.envio é obrigatório")
    if "{identificador}" not in str(endpoints.get("resultado", "")):
        raise ValueError("endpoints.resultado deve conter {identificador}")
    polling = cfg.setdefault("polling", {})
    polling.setdefault("intervalSeconds", 5)
    polling.setdefault("timeoutSeconds", 3600)
    cfg.setdefault("diretorioSaida", "resultados")
    return cfg


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def read_manifest(path: Path) -> tuple[int, str]:
    with zipfile.ZipFile(path, "r") as zf:
        try:
            raw = zf.read("manifest.json")
        except KeyError as exc:
            raise ValueError("ZIP não contém manifest.json") from exc
    data = json.loads(raw.decode("utf-8-sig"))
    return int(data["formatoVersao"]), str(data["codigoSistemaOrigem"])


def common_headers(cfg: dict) -> dict[str, str]:
    return {
        "X-Jornada-Gestor": str(cfg["gestor"]),
        "X-Jornada-Access-Key": str(cfg["accessKey"]),
    }


def http_request(request: urllib.request.Request) -> tuple[int, bytes]:
    try:
        with urllib.request.urlopen(request) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read()


def send_file(cfg: dict, raw_path: str) -> int:
    path = Path(raw_path).expanduser().resolve()
    if not path.is_file():
        raise FileNotFoundError(f"ZIP para envio não encontrado: {path}")

    sha = sha256_file(path)
    fmt, source = read_manifest(path)
    canonical = f"ENTREGA_{str(cfg['gestor']).upper()}_{source}_v{fmt}_{sha}.zip"
    body = path.read_bytes()
    headers = common_headers(cfg)
    headers.update({
        "Content-Type": "application/zip",
        "Content-Disposition": f'attachment; filename="{canonical}"',
        "Idempotency-Key": f"sha256:{sha}",
    })
    request = urllib.request.Request(str(cfg["endpoints"]["envio"]), data=body, headers=headers, method="POST")
    status, response_body = http_request(request)
    text = response_body.decode("utf-8", errors="replace")
    if status < 200 or status >= 300:
        raise RuntimeError(f"Envio rejeitado ({status}): {text}")
    print(text)
    print(f"SHA-256: {sha}")
    print(f"Nome enviado: {canonical}")
    return 0


def resolve_identifier(raw: str) -> str:
    candidate = Path(raw).expanduser()
    if candidate.is_file():
        return sha256_file(candidate.resolve())
    value = raw.strip()
    if SHA_RE.fullmatch(value):
        return value
    if len(value) <= 260 and value.lower().endswith(".zip") and Path(value).name == value:
        return value
    raise ValueError("--resultado exige SHA-256, nome exato do ZIP enviado ou caminho de um ZIP local")


def safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9._-]", "_", value)


def query_result(cfg: dict, raw_identifier: str, output: str | None) -> int:
    identifier = resolve_identifier(raw_identifier)
    endpoint = str(cfg["endpoints"]["resultado"]).replace(
        "{identificador}", urllib.parse.quote(identifier, safe=""))
    interval = int(cfg["polling"]["intervalSeconds"])
    timeout = int(cfg["polling"]["timeoutSeconds"])
    deadline = time.monotonic() + timeout

    while True:
        request = urllib.request.Request(endpoint, headers=common_headers(cfg), method="GET")
        status, response_body = http_request(request)
        text = response_body.decode("utf-8", errors="replace")
        if status < 200 or status >= 300:
            raise RuntimeError(f"Consulta rejeitada ({status}): {text}")
        payload = json.loads(text)
        if bool(payload.get("finalizado")):
            destination = Path(output) if output else Path(str(cfg.get("diretorioSaida", "resultados"))) / f"resultado_{safe_name(identifier)}.json"
            destination = destination.expanduser().resolve()
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            print(destination)
            return 0
        if time.monotonic() >= deadline:
            raise TimeoutError("O processamento não chegou a estado final dentro do tempo configurado")
        time.sleep(interval)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Integrador padrão da Jornada do Cidadão")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--enviar", metavar="ARQUIVO_ZIP")
    mode.add_argument("--resultado", metavar="IDENTIFICADOR")
    parser.add_argument("--config", default="integrador.config.json")
    parser.add_argument("--saida")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    cfg = load_config(Path(args.config).expanduser().resolve())
    if args.enviar:
        return send_file(cfg, args.enviar)
    return query_result(cfg, args.resultado, args.saida)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        raise SystemExit(1)

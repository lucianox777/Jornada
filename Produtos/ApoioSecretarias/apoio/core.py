"""Adaptador externo Jornada v2: entrada explícita, validação completa e pacote determinístico."""
from __future__ import annotations

import csv
import fnmatch
import hashlib
import io
import json
import os
import re
import tempfile
import uuid
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib import error, request

from jsonschema import Draft202012Validator, FormatChecker

MAX_FILE = 100 * 1024 * 1024
MAX_TOTAL = 250 * 1024 * 1024
MAX_ROWS = 100_000
FILE_NAME = re.compile(r"[A-Z0-9_-]{2,60}\Z")
ENVELOPE = ("manifest.json", "pessoas.jsonl", "registros.jsonl")


class Falha(Exception):
    def __init__(self, codigo: str, detalhe: str, arquivo: str = "", linha: int | None = None, campo: str = ""):
        super().__init__(detalhe)
        self.codigo, self.detalhe, self.arquivo, self.linha, self.campo = codigo, detalhe, arquivo, linha, campo

    def registro(self) -> dict:
        return {"codigo": self.codigo, "mensagem": self.detalhe, "arquivo": self.arquivo,
                "linha": self.linha, "campo": self.campo}


def json_estrito(blob: bytes, origem: str) -> dict:
    def pares(items):
        result = {}
        for key, value in items:
            if key in result:
                raise Falha("JSON_DUPLICADO", "Propriedade JSON duplicada", origem, campo=key)
            result[key] = value
        return result
    try:
        data = json.loads(blob.decode("utf-8-sig"), object_pairs_hook=pares,
                          parse_constant=lambda _: (_ for _ in ()).throw(ValueError("constante inválida")))
    except Falha:
        raise
    except (ValueError, UnicodeError, json.JSONDecodeError) as exc:
        raise Falha("JSON_INVALIDO", "JSON/UTF-8 inválido", origem) from exc
    if not isinstance(data, dict):
        raise Falha("JSON_INVALIDO", "Esperado objeto JSON", origem)
    return data


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def under(path: Path, folder: Path) -> bool:
    try:
        path.resolve().relative_to(folder.resolve())
        return True
    except ValueError:
        return False


def _sem_links(path: Path, root: Path) -> None:
    if path.is_symlink():
        raise Falha("LINK_NAO_PERMITIDO", "Links simbólicos não são aceitos", str(path))
    if root.is_dir() and not under(path, root):
        raise Falha("CAMINHO_INVALIDO", "Arquivo fora da origem", str(path))


def _arquivos(entrada: Path, saida: Path) -> list[Path]:
    if not entrada.exists():
        raise Falha("ORIGEM_INEXISTENTE", "Arquivo ou pasta de origem não encontrado", str(entrada))
    if entrada.is_symlink():
        raise Falha("LINK_NAO_PERMITIDO", "Origem simbólica não permitida", str(entrada))
    if entrada.is_file():
        if saida.resolve() == entrada.resolve():
            raise Falha("SAIDA_INVALIDA", "Saída não pode sobrescrever arquivo de origem", str(entrada))
        return [entrada]
    if not entrada.is_dir():
        raise Falha("ORIGEM_INVALIDA", "Origem não é arquivo nem pasta", str(entrada))
    if under(saida, entrada) or under(entrada, saida):
        raise Falha("SAIDA_INVALIDA", "A pasta de saída deve ficar fora da pasta de origem", str(saida))
    output = []
    for path in sorted(entrada.rglob("*")):
        _sem_links(path, entrada)
        if path.is_file():
            output.append(path)
    if not output:
        raise Falha("ORIGEM_VAZIA", "Nenhum arquivo encontrado na pasta", str(entrada))
    return output


def _perfil(path: Path) -> tuple[dict, dict[str, tuple[dict, str]]]:
    if not path.is_file() or path.is_symlink() or path.stat().st_size > 128 * 1024:
        raise Falha("PERFIL_INVALIDO", "Perfil não encontrado ou excede limite", str(path))
    p = json_estrito(path.read_bytes(), str(path))
    campos = {"versao", "gestor", "sistemaOrigem", "pessoaSchemaVersao", "dataReferencia",
              "natureza", "codigoTipo", "tipoVersao", "arquivos", "contratos", "enviar", "endpoint"}
    if set(p) - campos or not {"versao", "gestor", "sistemaOrigem", "pessoaSchemaVersao",
                             "dataReferencia", "arquivos", "contratos"}.issubset(p):
        raise Falha("PERFIL_INVALIDO", "Chaves do perfil inválidas, desconhecidas ou ausentes", str(path))
    if p["versao"] != 1 or type(p["pessoaSchemaVersao"]) is not int or p["pessoaSchemaVersao"] < 1:
        raise Falha("PERFIL_INVALIDO", "Versão do perfil/schema inválida", str(path))
    if not isinstance(p["gestor"], str) or not FILE_NAME.fullmatch(p["gestor"]):
        raise Falha("PERFIL_INVALIDO", "Código do Gestor inválido", str(path))
    if not isinstance(p["sistemaOrigem"], str) or not FILE_NAME.fullmatch(p["sistemaOrigem"]):
        raise Falha("PERFIL_INVALIDO", "Código do sistema de origem inválido", str(path))
    try:
        d = datetime.fromisoformat(p["dataReferencia"])
        if d.tzinfo is None or d.utcoffset() is None:
            raise ValueError("fuso ausente")
    except (ValueError, TypeError):
        raise Falha("PERFIL_INVALIDO", "dataReferencia exige ISO 8601 com fuso", str(path))
    facts = (p.get("natureza"), p.get("codigoTipo"), p.get("tipoVersao"))
    if any(x is not None for x in facts) and (not all(x is not None for x in facts)
        or p["natureza"] not in ("BENEFICIO", "SERVICO")
        or not isinstance(p["codigoTipo"], str)
        or type(p["tipoVersao"]) is not int or p["tipoVersao"] < 1):
        raise Falha("PERFIL_INVALIDO", "Contexto factual deve ser completo", str(path))
    if type(p.get("enviar", False)) is not bool:
        raise Falha("PERFIL_INVALIDO", "enviar deve ser booleano", str(path))
    rules = p["arquivos"]
    if not isinstance(rules, list) or not rules:
        raise Falha("PERFIL_INVALIDO", "arquivos exige regras explícitas", str(path))
    seen = set()
    for rule in rules:
        if not isinstance(rule, dict) or (set(rule) - {"padrao", "papel", "formato", "colunas", "delimitador", "obrigatorio"}):
            raise Falha("PERFIL_INVALIDO", "Regra de arquivo inválida", str(path))
        if not isinstance(rule.get("padrao"), str) or not rule["padrao"].strip() or rule["padrao"].startswith(("/", "\\")) or ".." in Path(rule["padrao"]).parts:
            raise Falha("PERFIL_INVALIDO", "Padrão de arquivo inválido", str(path))
        if rule["padrao"] in seen:
            raise Falha("PERFIL_INVALIDO", "Padrão de arquivo duplicado", str(path))
        seen.add(rule["padrao"])
        if rule.get("papel") not in ("pessoas", "registros") or rule.get("formato") not in ("csv", "jsonl"):
            raise Falha("PERFIL_INVALIDO", "Papel/formato não suportado", str(path))
        if type(rule.get("obrigatorio", True)) is not bool:
            raise Falha("PERFIL_INVALIDO", "obrigatorio deve ser booleano", str(path))
        if rule["formato"] == "csv":
            cols = rule.get("colunas")
            if not isinstance(cols, dict) or not cols or not all(isinstance(k, str) and isinstance(v, str) and v for k, v in cols.items()) or len(set(cols.values())) != len(cols):
                raise Falha("PERFIL_INVALIDO", "Colunas CSV inválidas", str(path))
            if not isinstance(rule.get("delimitador", ";"), str) or len(rule.get("delimitador", ";")) != 1:
                raise Falha("PERFIL_INVALIDO", "Delimitador inválido", str(path))
        elif "colunas" in rule or "delimitador" in rule:
            raise Falha("PERFIL_INVALIDO", "JSONL não admite colunas/delimitador", str(path))
    contracts = p["contratos"]
    if not isinstance(contracts, dict) or set(contracts) - {"pessoas", "registros"} or "pessoas" not in contracts:
        raise Falha("PERFIL_INVALIDO", "Contratos insuficientes", str(path))
    result = {}
    for role, item in contracts.items():
        if not isinstance(item, dict) or set(item) != {"arquivo", "sha256", "versao"} or type(item["versao"]) is not int or item["versao"] < 1:
            raise Falha("PERFIL_INVALIDO", "Contrato deve ter arquivo, sha256 e versao", str(path))
        if role == "pessoas" and item["versao"] != p["pessoaSchemaVersao"]:
            raise Falha("CONTRATO_VERSAO", "Versão de Pessoa diverge do perfil", str(path))
        if role == "registros" and item["versao"] != p.get("tipoVersao"):
            raise Falha("CONTRATO_VERSAO", "Versão de Registro diverge do perfil", str(path))
        schema_path = path.parent / item["arquivo"]
        if schema_path.is_symlink() or not schema_path.is_file() or not under(schema_path, path.parent):
            raise Falha("CONTRATO_AUSENTE", "Contrato inválido ou fora do diretório do perfil", str(schema_path))
        if schema_path.stat().st_size > 2 * 1024 * 1024 or not re.fullmatch(r"[0-9a-f]{64}", str(item["sha256"])):
            raise Falha("CONTRATO_INVALIDO", "Tamanho/hash do contrato inválido", str(schema_path))
        digest = sha256(schema_path)
        if digest != item["sha256"]:
            raise Falha("CONTRATO_DIVERGENTE", "SHA-256 do contrato não confere", str(schema_path))
        schema = json_estrito(schema_path.read_bytes(), str(schema_path))
        try:
            Draft202012Validator.check_schema(schema)
        except Exception as exc:
            raise Falha("CONTRATO_INVALIDO", "JSON Schema inválido", str(schema_path)) from exc
        result[role] = (schema, digest)
    return p, result


def _validar(item: dict, schema: dict, arquivo: str, linha: int):
    validator = Draft202012Validator(schema, format_checker=FormatChecker())
    err = next(iter(validator.iter_errors(item)), None)
    if err is not None:
        campo = ".".join(map(str, err.absolute_path))
        if not campo and err.validator == "required":
            campo = "(obrigatório)"
        raise Falha("FORA_CONTRATO", f"Violação do contrato: {err.validator}", arquivo, linha, campo)


def _checar_identidade(obj: dict, arquivo: str, linha: int, *, csv_input: bool = False) -> None:
    cpf = obj.get("cpf")
    if cpf is not None and (not isinstance(cpf, str) or not re.fullmatch(r"[0-9]{11}", cpf)):
        raise Falha("CPF_INVALIDO", "CPF exige 11 dígitos; não há normalização implícita", arquivo, linha, "cpf")
    if cpf and obj.get("cpfAusenteMotivo"):
        raise Falha("CPF_CONTRADITORIO", "CPF informado e motivo de ausência contraditórios", arquivo, linha, "cpfAusenteMotivo")
    if not cpf:
        if not obj.get("cpfAusenteMotivo"):
            raise Falha("CPF_SEM_MOTIVO", "Ausência de CPF requer motivo declarado pela origem", arquivo, linha, "cpfAusenteMotivo")
        if csv_input:
            obj["cpf"] = None  # JSONL recebido é preservado sem inventar presença da chave.


def _ler_arquivo(path: Path, rule: dict, schema: dict) -> list[dict]:
    if path.stat().st_size > MAX_FILE:
        raise Falha("LIMITE_EXCEDIDO", "Arquivo maior que 100 MiB", str(path))
    output = []
    try:
        with path.open("r", encoding="utf-8-sig", newline="") as f:
            if rule["formato"] == "csv":
                rd = csv.DictReader(f, delimiter=rule.get("delimitador", ";"), strict=True)
                header = rd.fieldnames or []
                if len(header) != len(set(header)) or set(header) != set(rule["colunas"].values()):
                    raise Falha("COLUNAS_INVALIDAS", "Cabeçalho não corresponde exatamente ao mapeamento", str(path), 1)
                def lines_csv():
                    for line in rd:
                        if None in line or any(val is None for val in line.values()):
                            raise Falha("COLUNAS_INVALIDAS", "Linha CSV tem colunas extras ou ausentes", str(path), rd.line_num)
                        mapped = {destino: line[origem].strip() for destino, origem in rule["colunas"].items() if line[origem].strip()}
                        yield rd.line_num, mapped
                iterator = lines_csv()
            else:
                def lines_jsonl():
                    for line_no, line in enumerate(f, 1):
                        if not line.strip():
                            raise Falha("JSONL_LINHA_VAZIA", "Linha vazia não é permitida em JSONL com registros", str(path), line_no)
                        yield line_no, json_estrito(line.encode("utf-8"), str(path))
                iterator = lines_jsonl()
            for line_no, obj in iterator:
                if line_no > MAX_ROWS + 1 or len(output) >= MAX_ROWS:
                    raise Falha("LIMITE_EXCEDIDO", "Número máximo de linhas excedido", str(path), line_no)
                if not isinstance(obj, dict):
                    raise Falha("FORMATO_INVALIDO", "Registro não é objeto", str(path), line_no)
                if rule["papel"] == "pessoas":
                    _checar_identidade(obj, str(path), line_no, csv_input=rule["formato"] == "csv")
                _validar(obj, schema, str(path), line_no)
                output.append(obj)
    except Falha:
        raise
    except (OSError, UnicodeError, csv.Error, TypeError) as exc:
        raise Falha("LEITURA_INVALIDA", "Erro ao ler conteúdo, UTF-8 ou CSV", str(path)) from exc
    return output


def _bytes_jsonl(records: list[dict]) -> bytes:
    return ("\n".join(json.dumps(item, ensure_ascii=False, sort_keys=True, separators=(",", ":")) for item in records) + ("\n" if records else "")).encode("utf-8")


def _zip_canonico(entries: dict[str, bytes], target: Path) -> None:
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in ENVELOPE:
            meta = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            meta.compress_type = zipfile.ZIP_DEFLATED
            meta.external_attr = 0o600 << 16
            z.writestr(meta, entries[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def _inventario(entrada: Path, saida: Path, profile: dict, schemas: dict) -> tuple[dict[str, bytes], list[dict]]:
    candidates = _arquivos(entrada, saida)
    if sum(x.stat().st_size for x in candidates) > MAX_TOTAL:
        raise Falha("LIMITE_EXCEDIDO", "Total da seleção excede 250 MiB")
    groups: dict[str, list[dict]] = {"pessoas": [], "registros": []}
    observations = []
    matched = {i: 0 for i in range(len(profile["arquivos"]))}
    for path in candidates:
        relative = path.name if entrada.is_file() else path.relative_to(entrada).as_posix()
        rules = [(i, r) for i, r in enumerate(profile["arquivos"]) if fnmatch.fnmatchcase(relative, r["padrao"])]
        if len(rules) != 1:
            raise Falha("ARQUIVO_FORA_PERFIL", "Arquivo desconhecido ou ambíguo para o perfil", relative)
        i, rule = rules[0]
        matched[i] += 1
        papel = rule["papel"]
        if papel not in schemas:
            raise Falha("CONTRATO_AUSENTE", "Contrato obrigatório para a origem", relative)
        # A prova de origem deve corresponder exatamente aos bytes que foram
        # lidos para montar a entrega. Mudança concorrente implica bloqueio.
        tamanho_inicial = path.stat().st_size
        hash_inicial = sha256(path)
        rows = _ler_arquivo(path, rule, schemas[papel][0])
        if tamanho_inicial != path.stat().st_size or hash_inicial != sha256(path):
            raise Falha("ORIGEM_ALTERADA", "Arquivo foi alterado durante a leitura", relative)
        if not rows and papel == "pessoas":
            raise Falha("ARQUIVO_VAZIO", "Arquivo de Pessoas sem registros", relative)
        groups[papel].extend(rows)
        observations.append({"arquivo": relative, "tamanho": tamanho_inicial, "sha256": hash_inicial, "papel": papel, "linhas": len(rows)})
    if any(not matched[i] and r.get("obrigatorio", True) for i, r in enumerate(profile["arquivos"])):
        raise Falha("ARQUIVO_AUSENTE", "Arquivo obrigatório do perfil não foi encontrado")
    if not groups["pessoas"]:
        raise Falha("PESSOAS_AUSENTES", "Remessa sem Pessoas")
    if len(groups["pessoas"]) > MAX_ROWS or len(groups["registros"]) > MAX_ROWS:
        raise Falha("LIMITE_EXCEDIDO", "Máximo de registros do lote ultrapassado")
    ids = set()
    for person in groups["pessoas"]:
        key = person.get("idPessoaEntrega")
        if not isinstance(key, str) or not key or key in ids:
            raise Falha("ID_DUPLICADO", "idPessoaEntrega ausente ou duplicado", campo="idPessoaEntrega")
        ids.add(key)
    for record in groups["registros"]:
        if record.get("idPessoaEntrega") not in ids:
            raise Falha("REFERENCIA_INVALIDA", "Registro aponta para Pessoa ausente na mesma remessa", campo="idPessoaEntrega")
    if groups["registros"] and not all(profile.get(k) is not None for k in ("natureza", "codigoTipo", "tipoVersao")):
        raise Falha("CONTEXTO_AUSENTE", "Registros exigem natureza/codigoTipo/tipoVersao")
    manifest = {"formatoVersao": 2, "pessoaSchemaVersao": profile["pessoaSchemaVersao"],
                "codigoSistemaOrigem": profile["sistemaOrigem"],
                "natureza": profile.get("natureza"), "codigoTipo": profile.get("codigoTipo"),
                "tipoVersao": profile.get("tipoVersao"), "dataReferencia": profile["dataReferencia"]}
    # A origem pode ganhar novos arquivos entre o inventário inicial e a
    # publicação: a seleção inteira precisa continuar exatamente a mesma.
    after = _arquivos(entrada, saida)
    if [str(p.resolve()) for p in after] != [str(p.resolve()) for p in candidates]:
        raise Falha("ORIGEM_ALTERADA", "Seleção de arquivos alterada durante a preparação")
    for row, path in zip(observations, candidates):
        if path.stat().st_size != row["tamanho"] or sha256(path) != row["sha256"]:
            raise Falha("ORIGEM_ALTERADA", "Arquivo mudou após a validação", row["arquivo"])
    entries = {"manifest.json": (json.dumps(manifest, ensure_ascii=False, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8"),
               "pessoas.jsonl": _bytes_jsonl(groups["pessoas"]),
               "registros.jsonl": _bytes_jsonl(groups["registros"])}
    return entries, observations


def _no_redirect():
    class NoRedirect(request.HTTPRedirectHandler):
        def redirect_request(self, *_args, **_kwargs):
            return None
    return request.build_opener(NoRedirect())


def _enviar(zip_path: Path, profile: dict, access_key: str) -> dict:
    from urllib.parse import urlparse
    endpoint = profile.get("endpoint")
    u = urlparse(endpoint) if isinstance(endpoint, str) else None
    if not u or u.scheme != "https" or not u.netloc or u.username or u.password or u.fragment:
        raise Falha("ENDPOINT_INVALIDO", "Envio exige URL HTTPS válida sem credencial embutida")
    if not access_key:
        raise Falha("CREDENCIAL_AUSENTE", "Defina JORNADA_ACCESS_KEY no ambiente")
    # O hash e o corpo enviado vêm do MESMO snapshot de bytes.
    # Não calcular digest sobre o arquivo e depois reabri-lo para enviar,
    # pois o arquivo poderia mudar entre as duas operações.
    payload = zip_path.read_bytes()
    digest = hashlib.sha256(payload).hexdigest()
    canonical = f"ENTREGA_{profile['gestor']}_{profile['sistemaOrigem']}_v2_{digest}.zip"
    if zip_path.name != canonical:
        raise Falha("ZIP_DIVERGENTE", "Nome do ZIP não corresponde ao conteúdo", zip_path.name)
    req = request.Request(endpoint, data=payload, method="POST", headers={
        "Content-Type": "application/zip", "Content-Disposition": f'attachment; filename="{canonical}"',
        "X-Jornada-Gestor": profile["gestor"], "X-Jornada-Access-Key": access_key,
        "Idempotency-Key": f"sha256:{digest}"})
    try:
        with _no_redirect().open(req, timeout=40) as resp:
            status = resp.status
            value = resp.read(8192)
    except error.HTTPError as exc:
        raise Falha("ENVIO_HTTP", f"API recusou envio, HTTP {exc.code}") from exc
    except (error.URLError, TimeoutError, OSError) as exc:
        raise Falha("ENVIO_REDE", "Falha de rede/TLS durante envio") from exc
    if not (200 <= status < 300):
        raise Falha("ENVIO_HTTP", f"Status HTTP {status}")
    receipt = {}
    try:
        raw = json.loads(value.decode("utf-8"))
        if isinstance(raw, dict):
            for k in ("entregaId", "correlationId", "status"):
                if k in raw and isinstance(raw[k], (str, int)):
                    receipt[k] = raw[k]
    except (ValueError, UnicodeError):
        pass
    return {"http": status, "recibo": receipt}


def _diretorio_relatorio(seguro_saida: Path, origem: Path) -> Path:
    """Relatório nunca pode tocar a seleção de entrada, mesmo em erro de caminho."""
    if origem.is_dir() and (under(seguro_saida, origem) or under(origem, seguro_saida)):
        return Path(tempfile.gettempdir()) / "JornadaApoioRelatorios"
    if origem.is_file() and seguro_saida == origem:
        return Path(tempfile.gettempdir()) / "JornadaApoioRelatorios"
    return seguro_saida


def _relatorio(saida: Path, report: dict) -> tuple[Path, Path]:
    import html
    report_dir = saida / "relatorios"
    report_dir.mkdir(parents=True, exist_ok=True)
    base = "execucao_" + report["execucaoId"]
    js = report_dir / (base + ".json")
    ht = report_dir / (base + ".html")
    js.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    lines = ["<!DOCTYPE html><html lang='pt-BR'><head><meta charset='utf-8'><title>Relatório Jornada</title><style>body{font:16px system-ui;margin:2em;max-width:900px}pre{white-space:pre-wrap;background:#f3f3f3;padding:1em}</style></head><body><h1>Solução de Apoio — Relatório</h1><pre>", html.escape(json.dumps(report, indent=2, ensure_ascii=False)), "</pre></body></html>"]
    ht.write_text("".join(lines), encoding="utf-8")
    return js, ht


def executar(entrada: str | Path, perfil: str | Path, saida: str | Path, *, enviar: bool | None = None,
             access_key: str | None = None) -> dict:
    """Uma seleção gera UM envelope. Falha em qualquer entrada impede publicação/envio."""
    entrada, perfil, saida = Path(entrada).absolute(), Path(perfil).absolute(), Path(saida).absolute()
    report = {"execucaoId": uuid.uuid4().hex, "instanteUtc": datetime.now(timezone.utc).isoformat(),
              "estado": "INICIADA", "entrada": str(entrada), "perfil": str(perfil),
              "arquivos": [], "pacotes": [], "erros": [], "transmissao": None}
    tmp = None
    try:
        cfg, schemas = _perfil(perfil)
        report.update({"gestor": cfg["gestor"], "sistemaOrigem": cfg["sistemaOrigem"],
                       "versaoPerfil": cfg["versao"], "sha256Perfil": sha256(perfil),
                       "contratos": {k: v[1] for k, v in schemas.items()}})
        do_send = cfg.get("enviar", False) if enviar is None else enviar
        # Fail-closed: credencial, URL e destino antes de fazer ZIP.
        if do_send:
            from urllib.parse import urlparse
            url = cfg.get("endpoint", "")
            parsed = urlparse(url) if isinstance(url, str) else None
            if not parsed or parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password or parsed.fragment:
                raise Falha("ENDPOINT_INVALIDO", "Endpoint deve ser URL HTTPS sem segredo embutido")
            if not (access_key or os.getenv("JORNADA_ACCESS_KEY")):
                raise Falha("CREDENCIAL_AUSENTE", "JORNADA_ACCESS_KEY não configurada")
        entries, origins = _inventario(entrada, saida, cfg, schemas)
        report["arquivos"] = origins
        # A política e as referências também são parte do snapshot validado.
        if sha256(perfil) != report["sha256Perfil"]:
            raise Falha("PERFIL_ALTERADO", "O mapeamento mudou durante o processamento")
        for role, item in cfg["contratos"].items():
            contract = perfil.parent / item["arquivo"]
            if contract.is_symlink() or sha256(contract) != schemas[role][1]:
                raise Falha("CONTRATO_ALTERADO", "O schema mudou durante o processamento", str(contract))
        saida.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=saida, prefix=".jornada-", suffix=".tmp", delete=False) as t:
            tmp = Path(t.name)
        _zip_canonico(entries, tmp)
        digest = sha256(tmp)
        name = f"ENTREGA_{cfg['gestor']}_{cfg['sistemaOrigem']}_v2_{digest}.zip"
        final = saida / name
        # Never overwrite any divergent or existing package.
        if final.exists():
            if sha256(final) != digest:
                raise Falha("ZIP_DIVERGENTE", "Arquivo de destino existe e não confere com o SHA-256", name)
        else:
            os.replace(tmp, final)
            tmp = None
        report["pacotes"] = [{"arquivo": name, "sha256": digest, "bytes": final.stat().st_size}]
        report["estado"] = "GERADO_NAO_ENVIADO"
        if do_send:
            try:
                report["transmissao"] = _enviar(final, cfg, access_key or os.getenv("JORNADA_ACCESS_KEY", ""))
                report["estado"] = "RECEBIDO"  # HTTP receipt is not processing completion
            except Falha as exc:
                report["estado"] = "ENVIO_FALHOU"
                report["erros"].append(exc.registro())
    except Falha as exc:
        report["estado"] = "BLOQUEADO"
        report["erros"].append(exc.registro())
    except Exception:
        report["estado"] = "BLOQUEADO"
        report["erros"].append(Falha("FALHA_INTERNA", "Erro inesperado; verifique permissões/configuração").registro())
    finally:
        if tmp is not None:
            tmp.unlink(missing_ok=True)
        try:
            js, html = _relatorio(_diretorio_relatorio(saida, entrada), report)
            report["relatorioJson"] = str(js)
            report["relatorioHtml"] = str(html)
        except OSError:
            report["erros"].append(Falha("RELATORIO_NAO_GRAVADO", "Não foi possível gravar o relatório na saída").registro())
            report["estado"] = "BLOQUEADO"
    return report


def reenviar_zip_existente(arquivo: str | Path, perfil: str | Path, saida: str | Path,
                           *, access_key: str | None = None) -> dict:
    """Retentativa: valida pacote arquivado, preserva bytes e chave idempotente; nunca recompacta."""
    arquivo, perfil, saida = Path(arquivo).absolute(), Path(perfil).absolute(), Path(saida).absolute()
    report = {"execucaoId": uuid.uuid4().hex, "instanteUtc": datetime.now(timezone.utc).isoformat(),
              "estado": "INICIADA", "entrada": str(arquivo), "perfil": str(perfil),
              "arquivos": [], "pacotes": [], "erros": [], "transmissao": None, "operacao": "REENVIAR_ZIP"}
    try:
        cfg, schemas = _perfil(perfil)
        report.update({"gestor": cfg["gestor"], "sistemaOrigem": cfg["sistemaOrigem"],
                       "versaoPerfil": cfg["versao"], "sha256Perfil": sha256(perfil),
                       "contratos": {k: v[1] for k, v in schemas.items()}})
        if not arquivo.is_file() or arquivo.is_symlink() or arquivo.stat().st_size > MAX_TOTAL:
            raise Falha("ZIP_INVALIDO", "Arquivo para retransmissão ausente ou excede limite", str(arquivo))
        digest = sha256(arquivo)
        expected_name = f"ENTREGA_{cfg['gestor']}_{cfg['sistemaOrigem']}_v2_{digest}.zip"
        if arquivo.name != expected_name:
            raise Falha("ZIP_DIVERGENTE", "Nome/identidade SHA-256 não corresponde aos bytes", arquivo.name)
        try:
            with zipfile.ZipFile(arquivo) as z:
                infos = z.infolist()
                if len(infos) != 3 or [i.filename for i in infos] != list(ENVELOPE):
                    raise Falha("ZIP_INVALIDO", "ZIP não tem as três entradas canônicas", arquivo.name)
                if any(i.file_size > MAX_FILE or i.flag_bits & 1 for i in infos) or sum(i.file_size for i in infos) > MAX_TOTAL or infos[0].file_size > 65536:
                    raise Falha("ZIP_INVALIDO", "ZIP excede limites ou está criptografado", arquivo.name)
                manifest = json_estrito(z.read("manifest.json"), "manifest.json")
                expected = {"formatoVersao": 2, "pessoaSchemaVersao": cfg["pessoaSchemaVersao"],
                            "codigoSistemaOrigem": cfg["sistemaOrigem"], "natureza": cfg.get("natureza"),
                            "codigoTipo": cfg.get("codigoTipo"), "tipoVersao": cfg.get("tipoVersao"),
                            "dataReferencia": cfg["dataReferencia"]}
                if manifest != expected:
                    raise Falha("ZIP_DIVERGENTE", "Manifesto diverge do perfil vigente")
                payload = {}
                for papel in ("pessoas", "registros"):
                    raw = z.read(papel + ".jsonl")
                    payload[papel] = []
                    for line_no, line in enumerate(raw.splitlines(), 1):
                        if line_no > MAX_ROWS:
                            raise Falha("ZIP_INVALIDO", "JSONL excede limite", papel + ".jsonl", line_no)
                        item = json_estrito(line, papel + ".jsonl")
                        if papel not in schemas:
                            raise Falha("CONTRATO_AUSENTE", "Schema factual não configurado", papel + ".jsonl")
                        if papel == "pessoas":
                            _checar_identidade(item, papel + ".jsonl", line_no)
                        _validar(item, schemas[papel][0], papel + ".jsonl", line_no)
                        payload[papel].append(item)
                ids = [p.get("idPessoaEntrega") for p in payload["pessoas"]]
                if not ids or any(not isinstance(x, str) or not x for x in ids) or len(ids) != len(set(ids)):
                    raise Falha("ZIP_INVALIDO", "IDs de Pessoa ausentes/duplicados")
                if any(r.get("idPessoaEntrega") not in set(ids) for r in payload["registros"]):
                    raise Falha("REFERENCIA_INVALIDA", "Registro sem Pessoa no mesmo ZIP")
        except (OSError, zipfile.BadZipFile, RuntimeError, UnicodeError) as exc:
            raise Falha("ZIP_INVALIDO", "ZIP ilegível ou corrompido", arquivo.name) from exc
        report["pacotes"] = [{"arquivo": arquivo.name, "sha256": digest, "bytes": arquivo.stat().st_size}]
        # O pacote foi validado integralmente e não é reempacotado nem sobrescrito.
        try:
            report["transmissao"] = _enviar(arquivo, cfg, access_key or os.getenv("JORNADA_ACCESS_KEY", ""))
            report["estado"] = "RECEBIDO"
        except Falha as exc:
            report["estado"] = "ENVIO_FALHOU"
            report["erros"].append(exc.registro())
    except Falha as exc:
        report["estado"] = "BLOQUEADO"
        report["erros"].append(exc.registro())
    except Exception:
        report["estado"] = "BLOQUEADO"
        report["erros"].append(Falha("FALHA_INTERNA", "Erro inesperado na validação do ZIP").registro())
    finally:
        try:
            js, html = _relatorio(_diretorio_relatorio(saida, arquivo), report)
            report["relatorioJson"] = str(js)
            report["relatorioHtml"] = str(html)
        except OSError:
            report["estado"] = "BLOQUEADO"
            report["erros"].append(Falha("RELATORIO_NAO_GRAVADO", "Falha ao gerar relatório").registro())
    return report

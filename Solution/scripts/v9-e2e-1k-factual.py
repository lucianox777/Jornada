#!/usr/bin/env python3
"""Private factual fixture and exhaustive verification of the declared facts.

Keep original bridge manifests and truth unchanged. Seal the new delivery mapping
and factual expectations outside the transmitted ZIP. No fitting or policy change.
"""
import argparse
import hashlib
import importlib.util
import io
import json
import os
from datetime import datetime
from pathlib import Path
import re
import time
import uuid
import zipfile

SPEC = importlib.util.spec_from_file_location("http_driver", Path(__file__).with_name("v9-e2e-1k-http.py"))
HTTP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HTTP)
require = HTTP.require
KINDS = {"SEHAB": ("BENEFICIO", "AA01"), "SMADS": ("SERVICO", "CRA1")}


def encoded(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def sha(raw):
    return hashlib.sha256(raw).hexdigest().upper()


def write_new(path, raw):
    with path.open("xb") as stream:
        stream.write(raw)


def select_facts(people, manager, wave):
    nature, code = KINDS[manager]
    # Two deterministic versions of each CPF class per package, without
    # selecting by score, ground-truth outcome or eventual Runner decision.
    rows = sorted(people, key=lambda row: row["codigoPessoaOrigem"])
    selected = [row for row in rows if row.get("cpf")][:2]
    selected += [row for row in rows if not row.get("cpf")][:2]
    require(len(selected) == 4, "FACT_FIXTURE_CPF_CLASSES_MISSING")
    result = []
    for i, row in enumerate(selected, 1):
        fact = {"idPessoaEntrega": row["idPessoaEntrega"],
                "codigoRegistroOrigem": f"E2E-V9-{code}-W{wave:02}-{i:03}",
                "operacao": "INCLUSAO"}
        if nature == "BENEFICIO":
            fact.update(dataInicioConcessao="2026-01-01",
                        dataEventoConcessao=f"2026-08-{10+i:02}",
                        situacaoVigencia="VIGENTE", valorConcedido=600+i*13+wave)
        else:
            fact.update(dataHoraServico=f"2026-08-{10+i:02}T10:30:00-03:00",
                        unidadeServico=f"Unidade sintetica {wave}-{i}", situacao="REALIZADO")
        result.append({"manager": manager, "nature": nature, "type": code,
                       "source": row["codigoPessoaOrigem"], "hasCpf": bool(row.get("cpf")),
                       "payload": fact})
    return result


def prepare(root):
    prepath = root / "wave-preflight-receipt.json"
    original = HTTP.get_json(prepath)
    require(original["status"] == "PASS" and original["waveCount"] == 2
            and original["baseIdentities"] == 1000, "FACT_FIXTURE_PREFLIGHT_REQUIRED")
    delivery_pre = json.loads(encoded(original))
    facts = []
    for group in delivery_pre["packagesByWave"]:
        wave = group["wave"]
        directory = root / "ingestion" / f"wave-{wave:02}"
        truth = directory / "bridge-truth.jsonl"
        manifest = HTTP.get_json(directory / "bridge-manifest.json")
        raw_truth = truth.read_bytes()
        require(sha(raw_truth) == manifest["truthSidecar"]["sha256"], "FACT_IDENTITY_SEAL_DRIFT")
        mapped = {}
        for item in group["packages"]:
            manager, old = item["manager"], item["fileName"]
            if manager not in KINDS:
                continue
            raw = (directory / old).read_bytes()
            require(sha(raw) == item["zipSha256"] and len(raw) == item["zipBytes"], "FACT_SOURCE_ZIP_DRIFT")
            with zipfile.ZipFile(io.BytesIO(raw)) as archive:
                header = json.loads(archive.read("manifest.json"))
                people_bytes = archive.read("pessoas.jsonl")
                require(not archive.read("registros.jsonl"), "FACT_SOURCE_NOT_PERSON_ONLY")
            rows = [json.loads(line) for line in people_bytes.splitlines()]
            batch = select_facts(rows, manager, wave)
            header.update(natureza=KINDS[manager][0], codigoTipo=KINDS[manager][1], tipoVersao=1)
            # Expected records exist before serialization of the transmitted bytes.
            payload = b"".join(encoded(x["payload"]) + b"\n" for x in batch)
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                for name, data in (("manifest.json", encoded(header)),
                                   ("pessoas.jsonl", people_bytes), ("registros.jsonl", payload)):
                    info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    archive.writestr(info, data)
            data = stream.getvalue()
            digest = sha(data)
            require(re.fullmatch(r"[A-Z0-9_]+", header["codigoSistemaOrigem"]), "FACT_SYSTEM_INVALID")
            new = f"ENTREGA_{manager}_{header['codigoSistemaOrigem']}_v2_{digest.lower()}.zip"
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                require(archive.read("pessoas.jsonl") == people_bytes
                        and archive.read("registros.jsonl") == payload, "FACT_ZIP_SERIALIZATION_DRIFT")
            write_new(directory / new, data)
            mapped[old] = new
            item.update(fileName=new, zipSha256=digest, zipBytes=len(data))
            for fact in batch:
                fact.update(package=new, wave=wave, zipSha256=digest)
                facts.append(fact)
        # Reseal only the delivery filenames, without changing any identity label.
        rows = [json.loads(line) for line in raw_truth.splitlines()]
        for row in rows:
            row["PackageFileName"] = mapped.get(row["PackageFileName"], row["PackageFileName"])
        delivered = b"".join(encoded(row) + b"\n" for row in rows)
        manifest["truthSidecar"]["sha256"] = sha(delivered)
        # The existing census expects this canonical name in the logical descriptor.
        # The file itself has a distinct delivery-specific name.
        write_new(directory / "delivery-truth.jsonl", delivered)
        write_new(directory / "delivery-manifest.json", encoded(manifest))
    require(len(facts) == 16 and len({x["payload"]["codigoRegistroOrigem"] for x in facts}) == 16,
            "FACT_CENSUS_INVALID")
    sealed = encoded(facts)
    write_new(root / "factual-truth.json", sealed)
    delivery_pre["factualTruthSha256"] = sha(sealed)
    delivery_pre["originalPreflightSha256"] = sha(prepath.read_bytes())
    write_new(root / "delivery-preflight-receipt.json", encoded(delivery_pre))
    print("V9_E2E_FACTUAL_FIXTURE_PREPARED facts=16")


def checked_facts(root):
    pre = HTTP.get_json(root / "delivery-preflight-receipt.json")
    raw = (root / "factual-truth.json").read_bytes()
    require(pre["factualTruthSha256"] == sha(raw)
            and pre["originalPreflightSha256"] == sha((root / "wave-preflight-receipt.json").read_bytes()),
            "FACT_TRUTH_SEAL_DRIFT")
    facts = json.loads(raw)
    require(len(facts) == 16 and len({x["payload"]["codigoRegistroOrigem"] for x in facts}) == 16,
            "FACT_TRUTH_CENSUS_INVALID")
    by_package = {}
    for fact in facts:
        by_package.setdefault((fact["wave"], fact["package"]), []).append(fact)
    for (wave, package), rows in by_package.items():
        data = (root / "ingestion" / f"wave-{wave:02}" / package).read_bytes()
        require(all(x["zipSha256"] == sha(data) for x in rows), "FACT_DELIVERED_ZIP_DRIFT")
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            require(archive.namelist() == ["manifest.json", "pessoas.jsonl", "registros.jsonl"],
                    "FACT_TRUTH_ZIP_BOUNDARY")
            actual = [json.loads(line) for line in archive.read("registros.jsonl").splitlines()]
            require(actual == [row["payload"] for row in rows], "FACT_PAYLOAD_TRUTH_DRIFT")
    return facts


def eq_fields(actual, expected):
    require(isinstance(actual, dict) and all(k in actual and actual[k] == v for k, v in expected.items()),
            "FACT_API_PAYLOAD_DIVERGENCE")


def date_value(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def verify(root, attempt):
    facts = checked_facts(root)
    census = HTTP.get_json(root / "sql-census.json")
    require(HTTP.get_json(root / "identity-receipt.json")["status"] == "PASS", "FACT_IDENTITY_CENSUS_REQUIRED")
    by_version = {(x["manager"], x["package"], x["opaque"]): x for x in census}
    require(len(by_version) == len(census), "FACT_DUPLICATE_IDENTITY_VERSION")
    sql = lambda query: HTTP.sql_scalar(query, attempt)
    for table, count in (("silver.registro_observacao", 16), ("gold.beneficio_concedido", 8),
                         ("gold.servico_prestado", 8), ("serving.registro_integrado", 16)):
        require(sql(f"SELECT COUNT_BIG(*) FROM {table};") == str(count), "FACT_LAYER_CENSUS_DIVERGENCE")
    keys = HTTP.credentials()
    attributed = pending = 0
    groups = {}
    for fact in facts:
        p = fact["payload"]
        code, manager, nature = fact["type"], fact["manager"], fact["nature"]
        ident = by_version[(manager, fact["package"], fact["source"])]
        require(ident["hasCpf"] == fact["hasCpf"], "FACT_IDENTITY_CPF_DRIFT")
        record = p["codigoRegistroOrigem"]
        require(re.fullmatch(r"E2E-V9-(AA01|CRA1)-W0[12]-00[1-4]", record)
                and KINDS[manager] == (nature, code), "FACT_CODE_INVALID")
        source = "|".join((manager, fact["package"], fact["source"], p["idPessoaEntrega"]))
        expected_sha = sha(source.encode("utf-16le"))
        # All data identifiers in query text are bounded fixture constants.
        # Identity details are returned only as a private hash, never logged.
        base = f""" FROM silver.registro_observacao ro
          JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=ro.pessoa_observacao_id
          JOIN ref.gestor g ON g.gestor_id=ro.gestor_id
          JOIN ref.tipo_registro tr ON tr.tipo_registro_id=ro.tipo_registro_id
          JOIN ingestao.lote l ON l.lote_id=ro.lote_id
          JOIN bronze.entrega_arquivo b ON b.entrega_id=l.entrega_id
          JOIN identidade.v_vinculo_corrente vc ON vc.pessoa_observacao_id=po.pessoa_observacao_id """
        where = f" WHERE ro.codigo_registro_origem=N'{record}'"
        require(sql("SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(g.codigo,N'|',b.nome_arquivo,N'|',po.codigo_pessoa_origem,N'|',po.id_pessoa_entrega)),2)"
                    + base + where) == expected_sha, "FACT_SILVER_SUBJECT_DIVERGENCE")
        if nature == "BENEFICIO":
            value = p["valorConcedido"]
            require(type(value) is int and 600 < value < 700, "FACT_VALUE_INVALID")
            fields = {"data_inicio_concessao": p["dataInicioConcessao"],
                      "data_evento_concessao": p["dataEventoConcessao"],
                      "situacao_vigencia": "VIGENTE", "valor_concedido": value}
            gold = "gold.beneficio_concedido"
            idcol = "beneficio_concedido_id"
            view = "serving.v_beneficios_concedidos_pessoa"
        else:
            fields = {"data_hora_servico": p["dataHoraServico"],
                      "unidade_servico": p["unidadeServico"], "situacao": "REALIZADO"}
            gold = "gold.servico_prestado"
            idcol = "servico_prestado_id"
            view = "serving.v_servicos_prestados_pessoa"
        def literal(v):
            require(type(v) is int or (isinstance(v, str) and re.fullmatch(r"[A-Za-z0-9 :+-]+", v)),
                    "FACT_LITERAL_INVALID")
            return str(v) if type(v) is int else "N'" + v + "'"
        checks = []
        for field, value in fields.items():
            for alias in ("ro", "go", "ri"):
                checks.append(f"{alias}.{field}={literal(value)}")
        query = ("SELECT COUNT_BIG(*)" + base +
                 f" JOIN {gold} go ON go.registro_observacao_id=ro.registro_observacao_id"
                 " JOIN serving.registro_integrado ri ON ri.registro_observacao_id=ro.registro_observacao_id"
                 + where + f" AND g.codigo=N'{manager}' AND tr.codigo=N'{code}' AND ro.natureza=N'{nature}'"
                 " AND ro.versao_interna=1 AND ro.operacao=N'INCLUSAO'"
                 " AND go.versao_interna=1 AND ri.versao_interna=1"
                 " AND go.operacao=ro.operacao AND ri.operacao=ro.operacao"
                 " AND go.status_analitico=N'VIGENTE' AND ri.status_analitico=N'VIGENTE'"
                 " AND go.codigo_registro_origem=ro.codigo_registro_origem AND ri.codigo_registro_origem=ro.codigo_registro_origem"
                 " AND go.registro_origem_id=ro.registro_origem_id AND ri.registro_origem_id=ro.registro_origem_id"
                 " AND go.pessoa_origem_id=po.pessoa_origem_id AND ri.pessoa_origem_id=po.pessoa_origem_id"
                 " AND go.codigo_pessoa_origem=po.codigo_pessoa_origem AND ri.codigo_pessoa_origem=po.codigo_pessoa_origem"
                 " AND go.entrega_id=l.entrega_id AND ri.entrega_id=l.entrega_id AND ri.entrega_completa=1"
                 " AND go.source_as_of=ro.source_as_of AND ri.source_as_of=ro.source_as_of"
                 " AND go.gestor_id=ro.gestor_id AND ri.gestor_id=ro.gestor_id"
                 " AND go.tipo_registro_id=ro.tipo_registro_id AND ri.tipo_registro_id=ro.tipo_registro_id"
                 " AND go.tipo_registro_versao_id=ro.tipo_registro_versao_id AND ri.tipo_registro_versao_id=ro.tipo_registro_versao_id"
                 " AND (go.cpf_declarado=po.cpf OR (go.cpf_declarado IS NULL AND po.cpf IS NULL))"
                 " AND (ri.cpf_declarado=po.cpf OR (ri.cpf_declarado IS NULL AND po.cpf IS NULL))"
                 " AND " + " AND ".join(checks))
        if ident["currentStatus"] == "RESOLVIDO":
            destination = str(uuid.UUID(ident["currentUuid"]))
            query += (f" AND go.pessoa_uuid='{destination}' AND ri.pessoa_uuid='{destination}'"
                      " AND go.estado_atribuicao_identidade=N'ATRIBUIDA' AND ri.estado_atribuicao_identidade=N'ATRIBUIDA'")
            attributed += 1
        else:
            state = "CONFLITO_IDENTIDADE" if ident["currentStatus"] == "CONFLITO" else "PENDENTE_IDENTIDADE"
            query += (f" AND go.pessoa_uuid IS NULL AND ri.pessoa_uuid IS NULL"
                      f" AND go.estado_atribuicao_identidade=N'{state}' AND ri.estado_atribuicao_identidade=N'{state}'")
            pending += 1
        require(sql(query + ";") == "1", "FACT_LAYER_PAYLOAD_DIVERGENCE")
        view_count = sql(f"SELECT COUNT_BIG(*) FROM {view} WHERE codigo_registro_origem=N'{record}';")
        if ident["currentStatus"] != "RESOLVIDO":
            require(view_count == "0", "FACT_UNATTRIBUTED_VISIBLE")
            continue
        require(view_count == "1", "FACT_ASSIGNED_VIEW_MISSING")
        row_id = sql(f"SELECT CONVERT(varchar(32),{idcol}) FROM {gold} WHERE codigo_registro_origem=N'{record}';")
        require(row_id.isdecimal(), "FACT_GOLD_ID_INVALID")
        groups.setdefault((manager, code, destination), []).append((fact, int(row_id)))
    require(attributed > 0 and pending > 0 and attributed + pending == len(facts),
            "FACT_ATTRIBUTION_CLASSES_MISSING")
    def get(route, manager):
        for _ in range(30):
            status, value = HTTP.http(route, manager, keys[manager])
            if status != 429:
                require(status == 200 and isinstance(value, list), "FACT_API_RESPONSE_INVALID")
                return value
            time.sleep(2)
        raise ValueError("FACT_API_RATE_LIMIT")
    checked = 0
    for (manager, code, destination), expected in groups.items():
        nature = KINDS[manager][0]
        route = HTTP.ROUTES + "/pessoas/" + destination
        suffix = "/beneficios-concedidos" if nature == "BENEFICIO" else "/servicos-prestados"
        generic = get(route + "/registros?codigo=" + code, manager)
        specific = get(route + suffix + "?codigo=" + code, manager)
        # Filter by declared type; demand complete equality, not existence-only.
        prefix = "B:" if nature == "BENEFICIO" else "S:"
        require(len(generic) == len(expected) == len(specific), "FACT_API_CENSUS_DIVERGENCE")
        by_id = {x["registroId"]: x for x in generic}
        by_record = {x["codigoRegistroOrigem"]: x for x in specific}
        require(len(by_id) == len(expected) == len(by_record), "FACT_API_DUPLICATE")
        for fact, row_id in expected:
            p = fact["payload"]
            item = by_id[prefix + str(row_id)]
            detail = by_record[p["codigoRegistroOrigem"]]
            eq_fields(item, {"pessoaUuid": destination, "natureza": nature,
                             "codigo": code, "gestor": manager})
            eq_fields(detail, {"pessoaUuid": destination, "gestor": manager,
                              "versaoTipo": 1, "versaoInternaRegistro": 1,
                              "operacao": "INCLUSAO", "statusAnalitico": "VIGENTE"})
            if nature == "BENEFICIO":
                eq_fields(detail, {"beneficioConcedidoId": row_id, "codigoBeneficio": code,
                                  "dataInicioConcessao": p["dataInicioConcessao"],
                                  "dataEventoConcessao": p["dataEventoConcessao"],
                                  "situacaoVigencia": p["situacaoVigencia"], "valorConcedido": p["valorConcedido"]})
                require(item["situacao"] == p["situacaoVigencia"]
                        and item["ocorridoEm"][:10] == p["dataEventoConcessao"], "FACT_API_EVENT_DRIFT")
            else:
                eq_fields(detail, {"servicoPrestadoId": row_id, "codigoServico": code,
                                  "unidadeServico": p["unidadeServico"], "situacao": p["situacao"]})
                require(date_value(detail["dataHoraServico"]) == date_value(p["dataHoraServico"])
                        == date_value(item["ocorridoEm"]) and item["situacao"] == p["situacao"],
                        "FACT_API_EVENT_DRIFT")
            checked += 1
    require(checked == attributed, "FACT_API_COVERAGE")
    write_new(root / "factual-receipt.json", encoded({"schema": "V9_E2E_FACTUAL_V1", "status": "PASS",
              "facts": len(facts), "benefits": 8, "services": 8, "attributed": attributed,
              "unattributed": pending, "apiFacts": checked, "truthOutsideZip": True,
              "scope": "DECLARED_FACTS_SILVER_GOLD_SERVING_GENERIC_AND_SPECIALIZED_API",
              "e2eSystemCertified": False}))
    print("V9_E2E_FACTUAL_PAYLOAD_PASS_NOT_INTEGRAL")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "verify"))
    parser.add_argument("--root", required=True, type=Path)
    a = parser.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("JORNADA_E2E_V9_DEV_FULL") == "true"
                and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
                and a.root.is_dir() and not a.root.is_symlink(), "FACT_DISPOSABLE_CI_REQUIRED")
        if a.mode == "prepare":
            prepare(a.root)
        else:
            verify(a.root, uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"]))
    except (OSError, ValueError, KeyError, TypeError, AttributeError, zipfile.BadZipFile):
        raise SystemExit("V9_E2E_FACTUAL_FAILED") from None


if __name__ == "__main__":
    main()

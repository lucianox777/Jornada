#!/usr/bin/env python3
"""Census of published references through batch Person API, with private SQL digests.

No real data, no change to the presentation/recomposition contract. Verify Gold
versus Serving for every physical person, then compare all resolved UUIDs
reachable from each manager's proven observations against the API payload.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import time
import uuid
from datetime import datetime

SPEC = importlib.util.spec_from_file_location("http_driver", Path(__file__).with_name("v9-e2e-1k-http.py"))
HTTP = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HTTP)
require = HTTP.require

DATA_FIELDS = ("codigoPessoaOrigem", "cpf", "cpfAusenteMotivo", "nomeCompleto", "dataNascimento", "nomeMae")
META_FIELDS = ("fontesDistintas", "estadoConcordancia", "estadoIdentidade", "completudeNucleo")
SQL_FIELDS = ("LOWER(CONVERT(nvarchar(36),gp.pessoa_uuid))", "src.codigo_pessoa_origem", "gp.cpf",
              "CASE WHEN gp.cpf IS NULL THEN COALESCE(src.cpf_ausente_motivo,gp.status_cpf) END",
              "gp.nome_completo", "CONVERT(nvarchar(10),gp.data_nascimento,23)", "gp.nome_mae",
              "gp.fontes_distintas", "gp.estado_concordancia", "gp.estado_identidade", "gp.completude_nucleo")


def field(value):
    if value is None:
        return "-"
    require(type(value) in (str, int), "PERSON_FIELD_TYPE")
    return str(value).encode("utf-16le").hex().upper()


def canonical_row(response, schema_ref):
    require(response["schemaRef"] == schema_ref, "PERSON_SCHEMA_DIVERGENCE")
    data, meta = response["dados"], response["metadados"]
    person = str(uuid.UUID(response["pessoaUuid"]))
    require(meta["pessoaUuidSolicitado"] == person and meta["redirecionadoPorFusao"] is False
            and meta["estadoIdentidade"] == "REFERENCIA"
            and type(meta["fontesDistintas"]) is int and meta["fontesDistintas"] > 0,
            "PERSON_METADATA_DIVERGENCE")
    require(datetime.fromisoformat(meta["atualizadoEm"].replace("Z","+00:00")).tzinfo is not None,
            "PERSON_TIMESTAMP_INVALID")
    require(set(data) == set(DATA_FIELDS) | {"atributosTransversais","conferenciasDocumentais"}
            and data["atributosTransversais"] == [] and data["conferenciasDocumentais"] == []
            and all(data[k] is None or isinstance(data[k],str) for k in DATA_FIELDS),
            "PERSON_PROJECTION_DIVERGENCE")
    require(all(isinstance(meta[k],str) for k in META_FIELDS[1:]), "PERSON_METADATA_TYPE")
    return person, "|".join(field(x) for x in
                            [person, *(data[k] for k in DATA_FIELDS), *(meta[k] for k in META_FIELDS)]) + "\n"


def digest_rows(responses, expected_ids, schema_ref):
    require(isinstance(responses, list), "PERSON_BATCH_INVALID")
    rows = dict()
    for response in responses:
        person, line = canonical_row(response, schema_ref)
        require(person not in rows, "PERSON_BATCH_DUPLICATE")
        rows[person] = line
    require(set(rows) == set(expected_ids), "PERSON_BATCH_CENSUS_DIVERGENCE")
    return hashlib.sha256("".join(rows[k] for k in sorted(rows)).encode("ascii")).hexdigest().upper()


def verify(root, attempt):
    census = HTTP.get_json(root / "sql-census.json")
    require(HTTP.get_json(root / "identity-receipt.json")["status"] == "PASS",
            "PERSON_IDENTITY_ORACLE_REQUIRED")
    refs = {x["currentUuid"] for x in census if x["currentStatus"] == "RESOLVIDO"}
    require(len(refs) > 0 and None not in refs, "PERSON_REFERENCE_CENSUS_EMPTY")
    sql = lambda q: HTTP.sql_scalar(q, attempt)
    gold_fields = "pessoa_uuid,cpf,status_cpf,nome_completo,data_nascimento,nome_mae,fontes_distintas,estado_concordancia,estado_identidade,completude_nucleo,atualizado_em"
    serving_fields = gold_fields.replace("nome_completo","nome_referencia")
    gold = f"SELECT {gold_fields} FROM gold.pessoa"
    serving = f"SELECT {serving_fields} FROM serving.v_pessoa"
    require(sql(f"SELECT CASE WHEN EXISTS({gold} EXCEPT {serving}) OR EXISTS({serving} EXCEPT {gold}) THEN 0 ELSE 1 END;") == "1",
            "PERSON_GOLD_SERVING_DIVERGENCE")
    require(sql("SELECT COUNT_BIG(*) FROM gold.pessoa WHERE estado_identidade=N'REFERENCIA';") == str(len(refs)),
            "PERSON_REFERENCE_GOLD_CENSUS")
    total_gold = int(sql("SELECT COUNT_BIG(*) FROM gold.pessoa;"))
    require(total_gold >= len(refs) and sql("SELECT COUNT_BIG(*) FROM serving.v_pessoa;") == str(total_gold),
            "PERSON_TOTAL_GOLD_SERVING_CENSUS")
    require(sql("SELECT COUNT_BIG(*) FROM silver.pessoa_atributo_observacao;") == "0",
            "PERSON_FIXTURE_ATTRIBUTES_UNEXPECTED")
    keys = HTTP.credentials()
    all_seen = set()
    views = 0
    for manager in sorted(keys):
        expected = sorted({x["currentUuid"] for x in census if x["manager"] == manager and x["currentStatus"] == "RESOLVIDO"})
        require(0 < len(expected) <= 1000, "PERSON_MANAGER_CENSUS_INVALID")
        schema = HTTP.get_json(Path("config/contracts/gestores") / manager / "pessoa/v1/pessoa.schema.json")
        require(set(DATA_FIELDS) <= set(schema["properties"]), "PERSON_SCHEMA_SCOPE")
        def hex_expr(expr):
            return "COALESCE(CONVERT(varchar(max),CONVERT(varbinary(max),CONVERT(nvarchar(max),"+expr+")),2),'-')"
        row = "CONCAT(" + ", '|', ".join(hex_expr(x) for x in SQL_FIELDS) + ",CHAR(10))"
        # Same canonical identity population as the validated external census,
        # but independent read from Gold and physical source history, not the
        # API's response or a repeated API call as expected data.
        q = f"""WITH latest AS (
              SELECT vc.pessoa_uuid,po.codigo_pessoa_origem,po.cpf_ausente_motivo,
                   ROW_NUMBER() OVER(PARTITION BY vc.pessoa_uuid ORDER BY po.source_as_of DESC,po.pessoa_observacao_id DESC) rn
              FROM identidade.v_vinculo_corrente vc JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=vc.pessoa_observacao_id
              JOIN ref.gestor g ON g.gestor_id=po.gestor_id WHERE g.codigo=N'{manager}'
            )
            SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',STRING_AGG(CONVERT(varchar(max),{row}),'')
              WITHIN GROUP (ORDER BY LOWER(CONVERT(varchar(36),gp.pessoa_uuid)) COLLATE Latin1_General_100_BIN2)),2)
            FROM gold.pessoa gp JOIN latest src ON src.pessoa_uuid=gp.pessoa_uuid AND src.rn=1
            WHERE gp.estado_identidade=N'REFERENCIA' AND EXISTS(
              SELECT 1 FROM identidade.v_vinculo_corrente vc JOIN silver.pessoa_observacao po
                ON po.pessoa_observacao_id=vc.pessoa_observacao_id
                JOIN ref.gestor g ON g.gestor_id=po.gestor_id
              WHERE vc.pessoa_uuid=gp.pessoa_uuid AND vc.status=N'RESOLVIDO' AND g.codigo=N'{manager}');"""
        expected_sha = sql(q)
        for _ in range(30):
            status, response = HTTP.http("/api/v1/pessoas/consulta", manager, keys[manager],
                json.dumps({"pessoaUuids":expected}).encode(), {"Content-Type":"application/json"})
            if status != 429:
                break
            time.sleep(2)
        require(status == 200, "PERSON_BATCH_HTTP_FAILED")
        require(digest_rows(response, expected, schema["$id"]) == expected_sha, "PERSON_BATCH_GOLD_PAYLOAD_DIVERGENCE")
        all_seen.update(expected)
        views += len(expected)
    require(all_seen == refs, "PERSON_REFERENCE_API_CENSUS_DIVERGENCE")
    sample = sorted(refs)[0]
    for request, expected in (([sample,sample],400), (["00000000-0000-0000-0000-000000000001"],404)):
        status, _ = HTTP.http("/api/v1/pessoas/consulta","SEHAB",keys["SEHAB"],
                              json.dumps({"pessoaUuids":request}).encode(), {"Content-Type":"application/json"})
        require(status == expected, "PERSON_BATCH_NEGATIVE_DIVERGENCE")
    with (root / "person-census-receipt.json").open("x", encoding="utf-8") as out:
        json.dump({"schema":"V9_E2E_PERSON_CENSUS_V1","status":"PASS","goldPeople":total_gold,
                   "servingPeople":total_gold,"resolvedReferencePeople":len(refs),
                   "managerPersonProjections":views,"managers":4,"negativeCases":2,
                   "scope":"ALL_GOLD_SERVING_CORE_ROWS_AND_RESOLVED_REFERENCE_API_BATCH_PROJECTIONS",
                   "e2eSystemCertified":False},out)
    print("V9_E2E_PERSON_CENSUS_PASS_NOT_INTEGRAL")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True,type=Path)
    a = parser.parse_args()
    try:
        require(os.environ.get("GITHUB_ACTIONS") == "true"
                and os.environ.get("JORNADA_E2E_V9_DEV_FULL") == "true"
                and os.environ.get("JORNADA_RUNTIME_MODE") == "DEV"
                and a.root.is_dir() and not a.root.is_symlink(), "PERSON_DISPOSABLE_REQUIRED")
        verify(a.root,uuid.UUID(os.environ["JORNADA_V9_DEV_ATTEMPT"]))
    except (OSError,ValueError,KeyError,TypeError,AttributeError) as exc:
        # Only fixed diagnostic tokens, never arbitrary payload or exception.
        code = str(exc) if type(exc) is ValueError and str(exc).startswith("PERSON_") and str(exc).isupper() and str(exc).replace("_","").isalnum() else type(exc).__name__
        raise SystemExit("V9_E2E_PERSON_CENSUS_FAILED " + code) from None


if __name__ == "__main__":
    main()

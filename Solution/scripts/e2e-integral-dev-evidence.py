#!/usr/bin/env python3
"""Emit only verified aggregate DEV observations and fail unproved milestones."""
import json
import math
import os
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / ".local/e2e"
COUNTS = {"personCount": 1000, "bronzeFiles": 1, "silverPersons": 1000,
          "silverRecords": 1000, "processedItems": 2000, "goldPersons": 1000,
          "servingRecords": 1000}


# Only small, local, unambiguous aggregate receipts can influence PASS rows.
MAX_RECEIPT_BYTES = 1024 * 1024


def reject_duplicate_keys(pairs):
    seen = {}
    for key, value in pairs:
        if key in seen:
            raise ValueError(f"Duplicate key in E2E receipt: {key}")
        seen[key] = value
    return seen


def reject_nonfinite(token):
    raise ValueError(f"Non-finite number in E2E receipt: {token}")


def read(path):
    if path.is_symlink():
        raise ValueError("Symlink E2E receipt forbidden")
    if not path.is_file():
        return {}
    if path.stat().st_size > MAX_RECEIPT_BYTES:
        raise ValueError("Oversized E2E receipt")
    value = json.loads(path.read_text(encoding="utf-8"),
                       object_pairs_hook=reject_duplicate_keys,
                       parse_constant=reject_nonfinite)
    if not isinstance(value, dict):
        raise ValueError("Expected a receipt object")
    return value


def validate_volume(receipt, sha):
    expected = dict(COUNTS, seed=355, status="PASS", commitSha=sha,
                    httpAccepted=True, processed=True, httpIdempotencyReplaySameDelivery=True,
                    scientificValidation=False, activated=False)
    for key, value in expected.items():
        if type(receipt.get(key)) is not type(value) or receipt[key] != value:
            return {}
    elapsed = receipt.get("e2eElapsedMs")
    if type(elapsed) is not int or elapsed <= 0:
        return {}
    return dict(COUNTS, elapsedMs=elapsed, personsPerSecond=round(1_000_000 / elapsed, 3))


def inspect_run(directory, sha):
    counts = validate_volume(read(directory / "http-volume-1k-summary.json"), sha)
    result = read(directory / "resultado-api-evidence.json")
    semiblind = read(directory / "semiblind-denials.json")
    ibge = read(directory / "ibge-http-1k-summary.json")
    codes = {"semCredencial": 401, "nomeInvalido": 400, "outroGestor": 404,
             "consulta": 200, "releitura": 200}
    result_ok = (result.get("sha") == sha and result.get("http") == codes
                 and result.get("registrosRetransmitidos") == 1 and result.get("entregasMesmoNome") == 2)
    cases = semiblind.get("cases", [])
    denials = [(c.get("case"), c.get("httpStatus"), c.get("responseBytes"), c.get("auditEvents"))
               for c in cases]
    denied = (semiblind.get("sha") == sha and semiblind.get("scope") == "SEMIBLIND_DEV_ACCESS_DENIALS"
              and denials == [("missing_auth", 401, 0, 1), ("missing_scope", 403, 0, 1), ("disabled", 503, 0, 1)])
    ibge_ok = (
        ibge.get("schema") == "V9_E2E_IBGE_HTTP_1K_V1"
        and ibge.get("status") == "PASS" and ibge.get("commitSha") == sha
        and ibge.get("generatorVersion") == "JORNADA_SYNTH_CORPUS_CSHARP_V1"
        and ibge.get("populationProfile") == "DEMOGRAPHIC_PRIMARY_V1"
        and ibge.get("seed") == 355 and ibge.get("baseIdentities") == 1000
        and ibge.get("sourceObservations") == ibge.get("materializedObservations")
        and type(ibge.get("silverPersons")) is int
        and 1000 <= ibge["silverPersons"] <= 4000
        and ibge["silverPersons"] == ibge.get("sourceObservations")
        and ibge.get("excludedObservations") == 0
        and ibge.get("deliveries") == ibge.get("bronzeFiles") == 4
        and ibge.get("scientificValidation") is False
        and ibge.get("activated") is False
        and ibge.get("v9RunnerVerified") is False
        and all(isinstance(ibge.get(key), str) and len(ibge[key]) == 64
                and all(char in "0123456789ABCDEF" for char in ibge[key])
                for key in ("generatorFingerprintSha256",
                            "bridgeManifestSha256", "groundTruthSidecarSha256"))
    )
    return {"counts": counts, "resultadoHttp": codes if result_ok else {},
            "semiblindHttpDenials": [401, 403, 503] if denied else [],
            "ibgeSyntheticHttp": {
                "baseIdentities": 1000, "silverPersons": ibge["silverPersons"],
                "deliveries": 4, "bridgeManifestSha256": ibge["bridgeManifestSha256"]}
            if ibge_ok else {}}


def main():
    temp = Path(os.environ["RUNNER_TEMP"])
    sha = os.environ["GITHUB_SHA"]
    step_names = ("ADMISSION", "CHECKOUT", "ISOLATION", "HTTP", "CONTRACTS", "APOIO", "RUNTIME", "CLEANUP")
    steps = {name: os.environ[name + "_STEP"] for name in step_names}
    failures = [name for name, outcome in steps.items() if outcome != "success"]
    try:
        measurements = inspect_run(OUT, sha)
        if not all(measurements.values()):
            failures.append("Recibo físico ausente ou incompatível. Conferir os componentes sem valores.")
    except (ValueError, OSError, TypeError, AttributeError):
        measurements = {}
        failures.append("Formato inválido de recibo físico.")
    resources = temp / "harness-resources"
    if resources.is_file():
        try:
            user_cpu, sys_cpu, rss = resources.read_text().split()
            cpu = float(user_cpu) + float(sys_cpu)
            if math.isfinite(cpu) and cpu >= 0 and int(rss) > 0:
                measurements["harnessProcessTree"] = {"cpuSeconds": round(cpu, 2), "maxRssKiB": int(rss)}
        except ValueError:
            failures.append("Métricas de recursos indisponíveis.")
    verified = [
        ("Despacho, ref protegida, SHA local e remoto", ("ADMISSION", "CHECKOUT"), None),
        ("Isolamento e limpeza da tentativa", ("ISOLATION", "CLEANUP"), None),
        ("HTTP 1k, Bronze, Processor, Silver, Gold de pessoas e Serving; replay HTTP",
         ("HTTP",), "counts"),
        ("Gerador IBGE C# (1k) → ponte ZIP por gestor → HTTP/Bronze/Silver; sem Runner",
         ("HTTP",), "ibgeSyntheticHttp"),
        ("Resultado.Api por entrega, releitura e negativas", ("HTTP",), "resultadoHttp"),
        ("Busca semicega desabilitada: negativas HTTP e auditoria SQL", ("HTTP",), "semiblindHttpDenials"),
        ("OpenAPI versionado e igualdade de operações mapeadas nos dois hosts",
         ("CONTRACTS", "RUNTIME"), None),
        ("ZIP sintético determinístico do Apoio sem envio", ("APOIO",), None),
    ]
    coverage = []
    for requirement, gates, measurement in verified:
        passed = all(steps[gate] == "success" for gate in gates)
        passed = passed and (measurement is None or bool(measurements.get(measurement)))
        coverage.append({
            "requirement": requirement, "sha": sha, "status": "PASS" if passed else "NAO_VALIDADO",
            "steps": {gate: steps[gate] for gate in gates}, "measurement": measurement,
            "reason": None if passed else "Etapa incompleta, negativa ou sem recibo físico compatível.",
        })

    blocked = {
        "nucleo": [
            ("Seleção DEV e proveniência do modelo vencedor", "#1488/#1584: RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED."),
            ("HTTP, Gold de fatos por tipo, Runner V9, linkage_resultado e recomposição de pessoas",
             "#1851: falta a cadeia física no mesmo banco descartável."),
            ("Estados PROVISORIA, REFERENCIA e INDEFINIDA, abstenção e APIs do núcleo",
             "#1851: falta verificar as decisões operacionais V9 e as respostas correspondentes."),
            ("Gabarito externo e oráculo de candidatos, scores, agrupamento e decisão",
             "#1584/#1851: falta a comparação por observação na cadeia física."),
            ("Reprocessamento, concorrência e rollback após falha intermediária",
             "#1851: faltam as provas do Runner na cadeia de ingestão."),
            ("10k em dez entregas de 1k e métricas de desempenho",
             "#1851: depende da prova funcional V9 1k."),
        ],
        "integral": [
            ("Busca semicega DEV habilitada: Top 5, Nenhum destes, busca completa/incompleta, "
             "passes seletivos, fan-out/timeout sem truncamento",
             "#1854/#612: faltam SQL/HTTP sintéticos com combinações opcionais, "
             "busca_completa/motivo_incompletude, seletividade e limites de custo; "
             "as negativas 401/403/503 não comprovam esses casos."),
            ("Busca semicega: projeção restrita, visibilidade por órgão/atributo e auditoria",
             "#1854/#539/#378: faltam política institucional e prova SQL/HTTP "
             "entre órgãos com pessoa restrita; HML/PROD permanecem bloqueados."),
            ("ZIP do Apoio até o receptor com autorização por tipo de fato",
             "#1855/#1194: autorização por recurso permanece pendente."),
            ("APIs de pessoa, identidade, registros, benefícios, serviços e monitor",
             "#1851: falta a cadeia integral com estados V9 e contratos de gestores/tipos de fato."),
        ],
    }
    for scope, entries in blocked.items():
        coverage.extend(
            {"requirement": requirement, "scope": scope, "sha": sha, "status": "BLOQUEADO", "reason": reason}
            for requirement, reason in entries
        )
    receipt = {
        "sha": sha, "runId": os.environ["GITHUB_RUN_ID"], "attempt": os.environ["GITHUB_RUN_ATTEMPT"],
        "job": os.environ["GITHUB_JOB"], "ref": os.environ["GITHUB_REF"], "event": os.environ["GITHUB_EVENT_NAME"],
        "runUrl": f'https://github.com/{os.environ["GITHUB_REPOSITORY"]}/actions/runs/{os.environ["GITHUB_RUN_ID"]}/attempts/{os.environ["GITHUB_RUN_ATTEMPT"]}',
        "steps": steps, "measurements": measurements, "failedOrMissing": failures, "coverage": coverage,
        "E2E_NUCLEO_PASS": False, "E2E_INTEGRAL_PASS": False,
    }
    (temp / "e2e-integral-dev.json").write_text(json.dumps(receipt, indent=2, ensure_ascii=False) + "\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        print("## E2E V9 DEV\n\n**Núcleo e sistema integral: NÃO VALIDADOS.**\n", file=summary)
        print("| Requisito | Resultado | Evidência ou bloqueio |", file=summary)
        print("| --- | --- | --- |", file=summary)
        for row in coverage:
            detail = row["reason"] or ", ".join(row["steps"])
            print(f'| {row["requirement"]} | {row["status"]} | {detail} |', file=summary)
        if failures:
            print("\nFalhas ou evidências ausentes: " + ", ".join(failures), file=summary)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

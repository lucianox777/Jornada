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


def read(path):
    if not path.is_file():
        return {}
    value = json.loads(path.read_text(encoding="utf-8"))
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
    codes = {"semCredencial": 401, "nomeInvalido": 400, "outroGestor": 404,
             "consulta": 200, "releitura": 200}
    result_ok = (result.get("sha") == sha and result.get("http") == codes
                 and result.get("registrosRetransmitidos") == 1 and result.get("entregasMesmoNome") == 2)
    cases = semiblind.get("cases", [])
    denials = [(c.get("case"), c.get("httpStatus"), c.get("responseBytes"), c.get("auditEvents"))
               for c in cases]
    denied = (semiblind.get("sha") == sha and semiblind.get("scope") == "SEMIBLIND_DEV_ACCESS_DENIALS"
              and denials == [("missing_auth", 401, 0, 1), ("missing_scope", 403, 0, 1), ("disabled", 503, 0, 1)])
    return {"counts": counts, "resultadoHttp": codes if result_ok else {},
            "semiblindHttpDenials": [401, 403, 503] if denied else []}


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
    # Each pending component requires its own physical verifier to be integrated
    # before either milestone can be admitted. No dispatch input can waive it.
    blocked = {
        "nucleo": [
            "#1488/#1584: seleção autorizada e proveniência do vencedor; RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED.",
            "#1851: RunAsync V9 após HTTP/Processor, linkage persistido, três estados Gold e APIs no mesmo banco.",
            "#1584/#1851: comparação por observação com gabarito externo e oráculo de candidatos, scores e decisões.",
            "#1851: recuperação após falha intermediária, concorrência e rollback do linkage.",
            "#1851: 10 entregas de 1k aguardam o aceite funcional V9 em 1k.",
        ],
        "integral": [
            "#1854: Top 5, Nenhum destes e visibilidade positiva da busca semicega.",
            "#1855/#1194: ZIP do Apoio até o receptor com autorização por tipo de fato.",
            "#1851: serviços, monitor e contratos de todos os gestores no fluxo integrado.",
        ],
    }
    receipt = {
        "sha": sha, "runId": os.environ["GITHUB_RUN_ID"], "attempt": os.environ["GITHUB_RUN_ATTEMPT"],
        "job": os.environ["GITHUB_JOB"], "ref": os.environ["GITHUB_REF"], "event": os.environ["GITHUB_EVENT_NAME"],
        "runUrl": f'https://github.com/{os.environ["GITHUB_REPOSITORY"]}/actions/runs/{os.environ["GITHUB_RUN_ID"]}/attempts/{os.environ["GITHUB_RUN_ATTEMPT"]}',
        "steps": steps, "measurements": measurements, "failedOrMissing": failures, "blocked": blocked,
        "E2E_NUCLEO_PASS": False, "E2E_INTEGRAL_PASS": False,
    }
    (temp / "e2e-integral-dev.json").write_text(json.dumps(receipt, indent=2, ensure_ascii=False) + "\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
        print("## E2E V9 DEV\n\n**Núcleo e sistema integral: NÃO VALIDADOS.**\n", file=summary)
        for scope, reasons in blocked.items():
            print(f"### {scope.capitalize()}\n", file=summary)
            for reason in reasons:
                print("-", reason, file=summary)
        if failures:
            print("\nFalhas ou evidências ausentes: " + ", ".join(failures), file=summary)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

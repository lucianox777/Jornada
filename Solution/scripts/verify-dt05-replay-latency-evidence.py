#!/usr/bin/env python3
"""DT-05 real Runner timing evidence acceptance. Synthetic GitHub CI only.

Records a reproducible wall-clock sample, NOT a production NAS performance SLA.
No SQL calls, Docker calls, remote access, or user dataset involved.
"""
from __future__ import annotations

import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / ".local/e2e/dt05-historical-replay-evidence.json"


def require(value: bool, reason: str) -> None:
    if not value:
        raise ValueError(reason)


def main() -> None:
    require(len(sys.argv) == 1
            and os.environ.get("GITHUB_ACTIONS") == "true"
            and os.environ.get("CI") == "true"
            and os.environ.get("GITHUB_REPOSITORY") == "lucianox777/Jornada"
            and bool(os.environ.get("GITHUB_RUN_ID"))
            and bool(os.environ.get("GITHUB_SHA")), "only isolated GitHub CI")
    require(EVIDENCE.is_file(), "real historical replay evidence missing")
    doc = json.loads(EVIDENCE.read_text(encoding="utf-8-sig"))
    require(isinstance(doc, dict), "invalid evidence schema")
    require(doc.get("gate") == "DT05_HISTORICAL_REPLAY_DETERMINISM_E2E"
            and doc.get("status") == "PASS", "historical replay not PASS")
    require(doc.get("database") == "JornadaSyntheticDev", "not synthetic DB")
    require(str(doc.get("githubRunId")) == os.environ["GITHUB_RUN_ID"]
            and doc.get("gitSha") == os.environ["GITHUB_SHA"],
            "evidence belongs to a different GitHub run/commit")
    require(doc.get("sourceUniverse") == doc.get("replayUniverse")
            and type(doc.get("replayUniverse")) is int
            and doc["replayUniverse"] > 0,
            "replay coverage must match source exactly")
    require(doc.get("exactUniverse") is True
            and doc.get("exactSemanticResult") is True
            and doc.get("replayPublishedOperationalEffects") is False,
            "semantic proof failed or published operational changes")
    require(doc.get("measurement") == "RUNNER_WALL_CLOCK_SYNTHETIC_CI_V1"
            and doc.get("measurementIterations") == 1,
            "real one-shot runner measurement missing")
    elapsed = doc.get("replayElapsedMilliseconds")
    rate = doc.get("replayRowsPerSecond")
    require(type(elapsed) is int and 0 < elapsed < 30 * 60 * 1000,
            "runtime stopwatch invalid or unbounded")
    require(type(rate) in (float, int)
            and math.isfinite(rate) and rate >= 0
            and abs(rate - round(doc["replayUniverse"] * 1000.0 / elapsed, 3))
            < 0.00051,
            "throughput is not derived from real runner time")
    require(doc.get("productionNasMeasured") is False
            and doc.get("productionSlaCertified") is False,
            "synthetic CI measurement falsely claims production acceptance")
    assert not any(key.lower() in ("cpf", "nome", "nomemae", "connectionstring",
                                  "sqlpassword") for key in doc), (
        "person/secret fields cannot be embedded in timing evidence"
    )
    print("DT-05 latency: PASS real historical replay wall-clock, "
          "exact source universe and no production claim")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, json.JSONDecodeError) as ex:
        # Do not expose raw evidence values or exception details in CI logs.
        print("DT-05 latency: REJECTED " + type(ex).__name__, file=sys.stderr)
        sys.exit(2)

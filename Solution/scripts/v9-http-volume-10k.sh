#!/usr/bin/env bash
# This script is intentionally SOURCED only after the normal local-e2e.sh
# tests initialized the disposable API, Processor and SQL. Never run standalone.
set -euo pipefail
[[ "${JORNADA_E2E_HTTP_VOLUME_COUNT:-}" == 10000 ]] || { echo 'V9 10k: explicit opt-in required' >&2; exit 50; }
[[ "${GITHUB_ACTIONS:-}" == true && "${CI:-}" == true &&
   "${GITHUB_REPOSITORY:-}" == lucianox777/Jornada &&
   "${GITHUB_REF:-}" == refs/heads/test/v9-e2e-http-10k-20261108 &&
   "${DB:-}" == JornadaE2E ]] || { echo 'V9 10k: non-disposable CI sandbox rejected' >&2; exit 50; }
[[ "$(git -C "$ROOT" rev-parse HEAD)" == "$GITHUB_SHA" ]] || { echo 'V9 10k: SHA drift' >&2; exit 50; }
volume_tsv="$OUT/http-volume-10k-batches.tsv"
printf 'batch\tpostMs\tprocessMs\telapsedMs\tbronze\tsilver\trecords\titems\tgold\tserving\tzipSha256\n' > "$volume_tsv"
volume_all_start="$(date +%s%3N)"
for batch in $(seq 0 9); do
  first="$((batch * 1000 + 1))"
  fixture_dir="$OUT/volume-10k-fixture-$batch"
  python3 "$ROOT/scripts/v9-http-volume-fixture.py" \
    --fixture "$ROOT/tests/fixtures/ingestao/AA01_v2" \
    --output-dir "$fixture_dir" --count 1000 --seed 355 --start-index "$first" >/dev/null
  package="$(python3 "$ROOT/scripts/build-ingestion-fixture.py" \
    --fixture "$fixture_dir" --gestor SEHAB --output-dir "$OUT/packages")"
  filename="$(basename "$package")"
  zip_sha="$(sha256sum "$package" | awk '{print $1}')"
  [[ "$filename" == *"_${zip_sha}.zip" ]] || { echo 'V9 10k: content SHA mismatch' >&2; exit 51; }
  key="v9-volume-10k-b${batch}-seed355"
  t0="$(date +%s%3N)"
  post_delivery "$key" "$OUT/volume-10k-receipt-$batch.json" "$OUT/volume-10k-post-$batch.code"
  t1="$(date +%s%3N)"
  [[ "$(cat "$OUT/volume-10k-post-$batch.code")" == 202 ]] || {
    echo "V9 10k: batch $batch POST refused" >&2; exit 52;
  }
  delivery="$(json_get "$OUT/volume-10k-receipt-$batch.json" entregaId)"
  [[ "$delivery" =~ ^[0-9a-fA-F-]{36}$ ]] || { echo 'V9 10k: receipt not a UUID' >&2; exit 52; }
  wait_processed "$delivery" "$OUT/volume-10k-status-$batch.json"
  t2="$(date +%s%3N)"
  lots="$(scalar "SELECT COUNT_BIG(*) FROM ingestao.lote WHERE entrega_id='$delivery';")"
  lot="$(scalar "SELECT CONVERT(varchar(36),lote_id) FROM ingestao.lote WHERE entrega_id='$delivery';")"
  [[ "$lots" == 1 && "$lot" =~ ^[0-9a-fA-F-]{36}$ ]] || { echo 'V9 10k: exactly 1 lot required' >&2; exit 53; }
  bronze="$(scalar "SELECT COUNT_BIG(*) FROM bronze.entrega_arquivo WHERE entrega_id='$delivery';")"
  silver="$(scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao WHERE lote_id='$lot';")"
  records="$(scalar "SELECT COUNT_BIG(*) FROM silver.registro_observacao WHERE lote_id='$lot';")"
  items="$(scalar "SELECT COUNT_BIG(*) FROM ingestao.item_processado WHERE lote_id='$lot';")"
  gold="$(scalar "SELECT COUNT_BIG(*) FROM gold.pessoa gp WHERE gp.cpf IN (SELECT po.cpf FROM silver.pessoa_observacao po WHERE po.lote_id='$lot');")"
  serving="$(scalar "SELECT COUNT_BIG(*) FROM serving.registro_integrado WHERE entrega_id='$delivery';")"
  [[ "$bronze" == 1 && "$silver" == 1000 && "$records" == 1000 &&
     "$items" == 2000 && "$gold" == 1000 && "$serving" == 1000 ]] || {
     echo "V9 10k: batch $batch cardinalities diverged: $bronze/$silver/$records/$items/$gold/$serving" >&2
     exit 54
  }
  # Idempotency confirmation at both ends, not ten needless replay requests.
  if [[ "$batch" == 0 || "$batch" == 9 ]]; then
    post_delivery "$key" "$OUT/volume-10k-replay-$batch.json" "$OUT/volume-10k-replay-$batch.code"
    [[ "$(cat "$OUT/volume-10k-replay-$batch.code")" == 202 &&
       "$(json_get "$OUT/volume-10k-replay-$batch.json" entregaId)" == "$delivery" &&
       "$(scalar "SELECT COUNT_BIG(*) FROM ingestao.entrega WHERE idempotency_key='$key';")" == 1 &&
       "$(scalar "SELECT COUNT_BIG(*) FROM serving.registro_integrado WHERE entrega_id='$delivery';")" == 1000 ]] || {
      echo "V9 10k: duplicate or data mutation on replay $batch" >&2; exit 55;
    }
  fi
  printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
    "$batch" "$((t1-t0))" "$((t2-t1))" "$((t2-t0))" "$bronze" "$silver" \
    "$records" "$items" "$gold" "$serving" "$zip_sha" >> "$volume_tsv"
  echo "V9 HTTP 10k: batch $((batch+1))/10 PASS (no identity records in log)"
done

# Cross-delivery invariant: 10 distinct idempotency keys, 10k unique CPFs.
total_deliveries="$(scalar "SELECT COUNT_BIG(*) FROM ingestao.entrega WHERE idempotency_key LIKE N'v9-volume-10k-b%-seed355';")"
unique_cpf="$(scalar "SELECT COUNT_BIG(DISTINCT po.cpf) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id JOIN ingestao.entrega e ON e.entrega_id=l.entrega_id WHERE e.idempotency_key LIKE N'v9-volume-10k-b%-seed355';")"
[[ "$total_deliveries" == 10 && "$unique_cpf" == 10000 ]] || {
  echo "V9 10k: global count mismatch (deliveries=$total_deliveries uniqueCpf=$unique_cpf)" >&2; exit 56;
}
total_ms="$(( $(date +%s%3N) - volume_all_start ))"
python3 - "$volume_tsv" "$OUT/http-volume-10k-summary.json" "$total_ms" <<'PY10K'
import csv, datetime, json, math, os, pathlib, sys
records=list(csv.DictReader(open(sys.argv[1],newline="",encoding="utf-8"),delimiter="\t"))
assert len(records)==10 and [int(r["batch"]) for r in records]==list(range(10))
numbers=("postMs","processMs","elapsedMs","bronze","silver","records","items","gold","serving")
for key in numbers:
    for record in records:
        assert record[key].isdigit() and int(record[key])>=0
assert all(int(r["postMs"])>0 and int(r["elapsedMs"])>0 for r in records)
assert sum(int(r["silver"]) for r in records)==10000
assert sum(int(r["serving"]) for r in records)==10000
assert sum(int(r["items"]) for r in records)==20000
assert len({r["zipSha256"] for r in records})==10
def percentile95(values):
    v=sorted(values)
    return v[math.ceil(.95*len(v))-1]
post=[int(r["postMs"]) for r in records]
end_to_end=[int(r["elapsedMs"]) for r in records]
total=int(sys.argv[3])
out={
  "status":"PASS","methodVersion":"JORNADA_E2E_HTTP_10K_TEN_DISPOSABLE_BATCHES_V1",
  "scope":"SYNTHETIC_DEV_HTTP_TO_SERVING_MULTI_DELIVERY",
  "commitSha":os.environ["GITHUB_SHA"],"seed":355,
  "batchCount":10,"personsPerBatch":1000,"personCount":10000,
  "deliveryCount":10,"uniqueCpfCount":10000,
  "bronzeFiles":sum(int(r["bronze"]) for r in records),
  "silverPersons":10000,"silverRecords":sum(int(r["records"]) for r in records),
  "processedItems":20000,"goldPersons":sum(int(r["gold"]) for r in records),
  "servingRecords":10000,
  "idempotentReplaysValidated":2,
  "httpPostP95MsTenBatches":percentile95(post),
  "httpPostMaxMsTenBatches":max(post),
  "batchE2EP95MsTenBatches":percentile95(end_to_end),
  "batchE2EMaxMsTenBatches":max(end_to_end),
  "totalWallClockMs":total,"throughputPersonsPerSecond":round(10_000_000/max(total,1),3),
  "perPersonHttpP95Ms":None,
  "perPersonHttpP95Status":"NOT_MEASURED_BULK_10_HTTP_REQUESTS",
  "scientificValidation":False,"activated":False,
  "generatedAtUtc":datetime.datetime.now(datetime.timezone.utc).isoformat(),
  "batches":[{"index":int(r["batch"]),"startIndex":int(r["batch"])*1000+1,
              "postMs":int(r["postMs"]),"processMs":int(r["processMs"]),
              "elapsedMs":int(r["elapsedMs"]),"zipSha256":r["zipSha256"]}
             for r in records]
}
pathlib.Path(sys.argv[2]).write_text(json.dumps(out,indent=2)+"\n",encoding="utf-8")
print("V9 HTTP 10k: PASS, ten physical HTTP deliveries, 10k SQL/Gold/Serving reconciled.")
PY10K

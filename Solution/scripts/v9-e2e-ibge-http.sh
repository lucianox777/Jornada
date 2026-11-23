#!/usr/bin/env bash
# Sourced only by local-e2e.sh after its original 1k HTTP acceptance.
# DEV-only: existing C# IBGE-derived generator -> bridge -> 4 real HTTP/SQL deliveries.
set -euo pipefail
umask 077
[[ "${JORNADA_E2E_HTTP_VOLUME_COUNT:-0}" == "1000" ]] || {
  echo 'ERRO: ponte IBGE E2E somente com fase 1k ativa.' >&2; exit 47;
}
[[ "${JORNADA_E2E_IBGE_1K:-false}" == true ]] || {
  echo 'ERRO: ponte IBGE exige opt-in explícito.' >&2; exit 47;
}
bridge_root="$OUT/ibge-1k"
[[ ! -e "$bridge_root" && ! -L "$bridge_root" ]] || {
  echo 'ERRO: saída IBGE preexistente; não sobrescrever nem reutilizar.' >&2; exit 47;
}
mkdir -m 700 -- "$bridge_root"

# Random, short-lived HMAC key: only synthetic opaque IDs, never persisted;
# corpus generator itself is deterministic for seed, versions and IBGE inputs.
JORNADA_SYNTH_PSEUDONYMIZATION_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')" \
  dotnet run --no-build --configuration Release \
    --project "$ROOT/src/Jornada.Linkage.SyntheticCorpus/Jornada.Linkage.SyntheticCorpus.csproj" -- \
    generate-ingestion \
    --reference-root "$ROOT/data/reference/ibge-nomes-2022" \
    --population-profile demographic-primary \
    --birth-daily-source "$ROOT/data/reference/synthetic-birth-sp/birth_daily_sp_projection2024_2026.json" \
    --people 1000 --seed 355 --error-profile clean --gestores 4 \
    --data-referencia 2026-11-22T00:00:00+00:00 \
    --out "$bridge_root" > /dev/null

preflight="$OUT/ibge-1k-preflight.json"
python3 "$ROOT/scripts/v9-e2e-ibge-bridge-preflight.py" \
  --root "$bridge_root" --receipt "$preflight"

ibge_deliveries=0
ibge_silver=0
ibge_bronze=0
# The preflight has authenticated exactly these manager-specific ZIP files.
# The shell only receives manager, basename, digest and counts; never truth data.
while IFS=$'\t' read -r gestor zip_name zip_digest expected; do
  [[ "$gestor" =~ ^(SEHAB|SMADS|SMDET|SMS)$ ]] || exit 48
  [[ "$expected" =~ ^[0-9]+$ && "$zip_digest" =~ ^[0-9A-F]{64}$ ]] || exit 48
  package="$bridge_root/ingestion/$zip_name"
  filename="$zip_name"
  manager_key="$(python3 - "$ROOT/config/security/test-access-keys.json" "$gestor" <<'PYKEY'
import json,sys
creds=json.load(open(sys.argv[1],encoding="utf-8"))["credentials"]
matches=[x for x in creds if x.get("type")=="GESTOR" and x.get("gestorCodigo")==sys.argv[2]]
assert len(matches)==1
print(matches[0]["accessKey"])
PYKEY
)"
  response="$OUT/ibge-http-$gestor-response.json"
  http="$(curl -sS -o "$response" -w '%{http_code}' -X POST \
    "$API_URL/api/v1/ingestao/entregas" \
    -H "X-Jornada-Gestor: $gestor" -H "X-Jornada-Access-Key: $manager_key" \
    -H "Idempotency-Key: e2e-ibge-v1-$gestor-${zip_digest:0:18}" \
    -H 'Content-Type: application/zip' \
    -H "Content-Disposition: attachment; filename=$filename" \
    --data-binary "@$package")"
  [[ "$http" == 202 ]] || {
    echo "ERRO: ponte IBGE $gestor HTTP=$http; sem divulgar payload." >&2; exit 49;
  }
  entrega="$(json_get "$response" entregaId)"
  [[ "$entrega" =~ ^[0-9a-fA-F-]{36}$ ]] || {
    echo 'ERRO: ponte IBGE retornou identificador de Entrega inválido.' >&2; exit 49;
  }
  wait_processed "$entrega" "$OUT/ibge-http-$gestor-status.json" "$gestor" "$manager_key"
  bronze="$(scalar "SELECT COUNT_BIG(*) FROM bronze.entrega_arquivo WHERE entrega_id='$entrega';")"
  silver="$(scalar "SELECT COUNT_BIG(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$entrega';")"
  [[ "$bronze" == 1 && "$silver" == "$expected" ]] || {
    echo "ERRO: ponte IBGE $gestor Bronze=$bronze Silver=$silver esperado=$expected." >&2; exit 50;
  }
  ibge_deliveries=$((ibge_deliveries + 1))
  ibge_silver=$((ibge_silver + silver))
  ibge_bronze=$((ibge_bronze + bronze))
done < <(python3 - "$bridge_root/ingestion/bridge-manifest.json" <<'PYPACKAGES'
import json,sys
manifest=json.load(open(sys.argv[1],encoding="utf-8"))
for item in sorted(manifest["packages"],key=lambda x:x["GestorCodigo"]):
    print(item["GestorCodigo"],item["FileName"],item["sha256"],item["PeopleCount"],sep="\t")
PYPACKAGES
)

python3 - "$preflight" "$OUT/ibge-http-1k-summary.json" "$ibge_deliveries" "$ibge_bronze" "$ibge_silver" <<'PYSUMMARY'
import json,os,pathlib,sys
pre=json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
deliveries,bronze,silver=map(int,sys.argv[3:])
assert deliveries==bronze==pre["packages"]==4
assert silver==pre["materializedObservations"]>=1000
assert pre["seed"]==355 and pre["baseIdentities"]==1000
summary={
    "schema":"V9_E2E_IBGE_HTTP_1K_V1","status":"PASS",
    "scope":"SYNTHETIC_DEV_PERSON_ONLY_NOT_V9_LINKAGE_CERTIFICATION",
    "commitSha":os.environ.get("GITHUB_SHA",""),
    "seed":355,"baseIdentities":1000,
    "generatorVersion":pre["generatorVersion"],
    "populationProfile":pre["populationProfile"],
    "generatorFingerprintSha256":pre["corpusInputFingerprintSha256"],
    "bridgeManifestSha256":pre["bridgeManifestSha256"],
    "groundTruthSidecarSha256":pre["truthSha256"],
    "sourceObservations":pre["sourceObservations"],
    "materializedObservations":silver,
    "excludedObservations":0,
    "deliveries":deliveries,"bronzeFiles":bronze,"silverPersons":silver,
    "scientificValidation":False,"activated":False,"v9RunnerVerified":False,
}
out=pathlib.Path(sys.argv[2])
assert not out.exists()
out.write_text(json.dumps(summary,indent=2,sort_keys=True)+"\n",encoding="utf-8")
print("IBGE-derived C# generator -> 4 manager ZIPs -> HTTP/Bronze/Silver: PASS (DEV person-only).")
PYSUMMARY

# Truth, synthetic identifiers, names, ZIPs and private HMAC provenance never enter Actions artifacts.
rm -rf -- "$bridge_root"

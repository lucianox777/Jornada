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
ibge_source_sets=0
http_lineage_proofs="$bridge_root/http-delivery-ledger-private.jsonl"
[[ ! -e "$http_lineage_proofs" && ! -L "$http_lineage_proofs" ]] || exit 47
: > "$http_lineage_proofs"
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
if len(matches)!=1:
    raise SystemExit("Expected exactly one DEV manager credential")
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
  # A count can hide a substituted source. Compare the complete ordered
  # multiset with the separately verified private source truth by SHA-256.
  # Only 64 hex characters cross this boundary; raw IDs never enter artifacts.
  expected_source_sha="$(python3 - "$preflight" "$gestor" <<'PYSOURCESHA'
import json,sys
pre=json.load(open(sys.argv[1],encoding="utf-8"))
if pre.get("sourceIdentitySetDigestMethod") != "SHA256_UTF16LE_LF_SORTED_OPAQUE_SOURCE_V1":
    raise SystemExit("Invalid source-set digest contract")
print(pre["sourceIdentitySetSha256Utf16le"][sys.argv[2]])
PYSOURCESHA
)"
  [[ "$expected_source_sha" =~ ^[0-9A-F]{64}$ ]] || exit 52
  actual_source_sha="$(scalar "SELECT UPPER(CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(NVARCHAR(MAX), po.codigo_pessoa_origem + NCHAR(10)), N'') WITHIN GROUP (ORDER BY po.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2)), 2)) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$entrega';")"
  [[ "$actual_source_sha" =~ ^[0-9A-F]{64}$ && "$actual_source_sha" == "$expected_source_sha" ]] || {
    echo "ERRO: ponte IBGE $gestor Silver diverge do conjunto de origens esperado; nenhum identificador divulgado." >&2; exit 52;
  }
  # Bind the received HTTP ZIP bytes to the exact SQL entrega and its
  # content-addressed Bronze object metadata, not only to Silver row counts.
  # Fail closed if metadata is absent, substituted, incomplete or nonterminal.
  zip_bytes="$(wc -c < "$package" | tr -d '[:space:]')"
  [[ "$zip_bytes" =~ ^[0-9]+$ && "$zip_bytes" -gt 0 ]] || exit 53
  ledger_sha="$(scalar "SELECT UPPER(e.payload_sha256)
    FROM ingestao.entrega e
    JOIN ref.gestor g ON g.gestor_id=e.gestor_id
    JOIN bronze.entrega_arquivo b ON b.entrega_id=e.entrega_id
    WHERE e.entrega_id='$entrega' AND g.codigo=N'$gestor'
      AND e.status=N'PROCESSADA'
      AND e.payload_sha256=b.payload_sha256
      AND e.bytes_recebidos=$zip_bytes
      AND b.tamanho_bytes=e.bytes_recebidos
      AND b.tamanho_bytes>0
      AND EXISTS (SELECT 1 FROM ingestao.lote l WHERE l.entrega_id=e.entrega_id
          GROUP BY l.entrega_id
          HAVING COUNT_BIG(*)=MAX(l.lote_total)
            AND MIN(l.lote_seq)=1 AND MAX(l.lote_seq)=MAX(l.lote_total)
            AND SUM(CASE WHEN l.status=N'PROCESSADO' THEN 1 ELSE 0 END)=COUNT_BIG(*));")"
  [[ "$ledger_sha" =~ ^[0-9A-F]{64}$ && "$ledger_sha" == "$zip_digest" ]] || {
    echo "ERRO: ZIP HTTP não coincide com Entrega/Bronze/lotes terminais ($gestor), sem conteúdo pessoal." >&2; exit 53;
  }
  # Only hashed delivery UUID, verified package/source digests and counts
  # enter the private transient proof. No raw delivery UUID or person ID.
  python3 - "$http_lineage_proofs" "$gestor" "$entrega" "$zip_digest" "$zip_bytes" "$silver" "$actual_source_sha" <<'PYLEDGER'
import hashlib,json,pathlib,sys,uuid
path=pathlib.Path(sys.argv[1])
if path.is_symlink() or not path.is_file():
    raise SystemExit("Invalid private HTTP lineage proof path")
manager,delivery,package,size,count,source=sys.argv[2:]
delivery_hash=hashlib.sha256(str(uuid.UUID(delivery)).encode("ascii")).hexdigest().upper()
proof={
    "gestor":manager,"deliveryIdSha256":delivery_hash,
    "packageSha256":package,"zipBytes":int(size),"silverPersons":int(count),
    "silverSourceSetSha256Utf16le":source,"httpStatus":202,
    "bronzePayloadAndBytesVerified":True,"lotesProcessadosVerified":True,
}
with path.open("a",encoding="utf-8") as stream:
    stream.write(json.dumps(proof,sort_keys=True)+"\n")
PYLEDGER
  ibge_source_sets=$((ibge_source_sets + 1))
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

# The receipt builder independently binds all four per-manager SHA/count
# claims to the already verified ZIP preflight, without any truth rows.
[[ "$ibge_deliveries" == 4 && "$ibge_bronze" == 4 && "$ibge_source_sets" == 4 ]] || exit 54
python3 "$ROOT/scripts/v9-e2e-ibge-http-lineage.py" \
  --preflight "$preflight" --proofs "$http_lineage_proofs" \
  --expected-sha "${GITHUB_SHA:-}" \
  --receipt "$OUT/ibge-http-1k-summary.json"

# Truth, synthetic identifiers, names, ZIPs and private HMAC provenance never enter Actions artifacts.
if [[ "\${JORNADA_E2E_V9_DEV_FULL:-false}" != true ]]; then
  rm -rf -- "$bridge_root"
fi

#!/usr/bin/env bash
# Source only after all HTTP 1k IBGE deliveries were processed, DEV only.
[[ "\${JORNADA_E2E_V9_DEV_FULL:-}" == true &&
   "\${JORNADA_E2E_HTTP_VOLUME_COUNT:-}" == 1000 &&
   "\${JORNADA_E2E_IBGE_1K:-}" == true &&
   "\${DOTNET_ENVIRONMENT:-}" == Development &&
   "\${DB:-}" == JornadaE2E ]] || {
  echo 'ERRO: V9 DEV E2E exige HTTP IBGE 1k, FULL e Development.' >&2; exit 55;
}
[[ -n "\${JORNADA_E2E_V9_SELECTION_FILE:-}" &&
   -f "$JORNADA_E2E_V9_SELECTION_FILE" &&
   ! -L "$JORNADA_E2E_V9_SELECTION_FILE" ]] || {
  echo 'ERRO: falta seleção pinada do vencedor para V9 DEV.' >&2; exit 55;
}
[[ -f "$OUT/ibge-1k/ingestion/bridge-truth.jsonl" &&
   ! -L "$OUT/ibge-1k/ingestion/bridge-truth.jsonl" &&
   -f "$OUT/ibge-http-1k-summary.json" ]] || {
  echo 'ERRO: gabarito externo e recibo HTTP 1k ausentes.' >&2; exit 56;
}
[[ ! -e "$OUT/v9-e2e-dev-1k-summary.json" ]] || exit 56
v9_e2e_attempt="$(python3 -c 'import uuid; print(uuid.uuid4())')"
[[ "$v9_e2e_attempt" =~ ^[0-9a-f-]{36}$ ]] || exit 56
[[ "$(scalar "SELECT DB_NAME();")" == JornadaE2E ]] || exit 56
existing="$(scalar "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0
 AND name IN (N'Jornada.EnvironmentProfile',N'Jornada.V9.DevAttempt',
 N'Jornada.V9.HttpE2eScope');")"
[[ "$existing" == 0 ]] || {
  echo 'ERRO: V9 E2E já possui perfil residente; não reutilizar.' >&2; exit 56;
}
compose_sql "EXEC sys.sp_addextendedproperty
 @name=N'Jornada.EnvironmentProfile',@value=N'Development';
 EXEC sys.sp_addextendedproperty
 @name=N'Jornada.V9.DevAttempt',@value=N'$v9_e2e_attempt';
 EXEC sys.sp_addextendedproperty
 @name=N'Jornada.V9.HttpE2eScope',@value=N'IBGE_1K_AFTER_PROCESSOR';" >/dev/null
JORNADA_E2E_V9_RUN_MODE=FULL \
JORNADA_V9_DEV_ATTEMPT_ID="$v9_e2e_attempt" \
JORNADA_E2E_V9_EVIDENCE_ROOT="$OUT" \
dotnet run --no-build --configuration Release --no-launch-profile \
 --project "$ROOT/src/Jornada.Linkage.Runner/Jornada.Linkage.Runner.csproj" -- \
 --v9-dev-e2e-1k-full >"$OUT/v9-e2e-dev-1k-runner.log" 2>&1 || {
    echo 'ERRO: Runner V9 FULL 1k falhou, sem PASS de núcleo.' >&2; exit 57;
}
# Real publication rows stay private, outside every published artifact.
private_rows="$OUT/ibge-1k/sql-publication-private.tsv"
[[ ! -e "$private_rows" && ! -L "$private_rows" ]] || exit 57
umask 077
compose_sql "SELECT po.codigo_pessoa_origem,
 COALESCE(CONVERT(varchar(36),r.pessoa_uuid_publicado),N''),
 r.resultado_publicacao
 FROM identidade.linkage_resultado r
 JOIN identidade.linkage_run lr ON lr.linkage_run_id=r.linkage_run_id
 JOIN silver.pessoa_observacao po ON po.pessoa_observacao_id=r.pessoa_observacao_id
 WHERE lr.solicitado_por=N'V9_E2E_DEV_1K' AND lr.tipo_run=N'FULL'
 AND lr.status=N'PUBLICADO' AND r.publicado_em IS NOT NULL
 AND po.cpf IS NULL AND po.codigo_pessoa_origem LIKE N'SYNTH-%'
 ORDER BY po.codigo_pessoa_origem;" >"$private_rows"
python3 "$ROOT/scripts/v9-e2e-dev-1k-truth-oracle.py" \
  --bridge "$OUT/ibge-1k/ingestion/bridge-manifest.json" \
  --truth "$OUT/ibge-1k/ingestion/bridge-truth.jsonl" \
  --sql "$private_rows" \
  --runner "$OUT/v9-e2e-dev-1k-summary.json" \
  --receipt "$OUT/v9-e2e-dev-1k-oracle-summary.json" || {
    echo 'ERRO: oráculo físico de identidade 1k falhou.' >&2; exit 58;
  }
rm -f -- "$private_rows"
echo 'V9_DEV_E2E_1K_FULL_AFTER_PROCESSOR_PASS: SQL and independent oracle'

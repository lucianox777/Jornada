#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/.local/postgresql-processor-runtime"
BRONZE="$OUT/bronze"
FIXTURE="$OUT/fixture"
PACKAGES="$OUT/packages"
CID="$(docker ps -q --filter publish=5432 | head -1)"
DELIVERY_ID='72000000-0000-4000-8000-000000000001'
CPF='70819234532'
RECORD_CODE='E2E-AA01-2026-000001'

test -n "$CID"
rm -rf "$OUT"
mkdir -p "$BRONZE" "$FIXTURE" "$PACKAGES"
cp "$ROOT/tests/fixtures/ingestao/AA01_v2/manifest.json" "$FIXTURE/manifest.json"
cp "$ROOT/tests/fixtures/ingestao/AA01_v2/pessoas.jsonl" "$FIXTURE/pessoas.jsonl"
cp "$ROOT/tests/fixtures/ingestao/AA01_v2/registros.jsonl" "$FIXTURE/registros.jsonl"

package="$(python3 "$ROOT/scripts/build-ingestion-fixture.py" --fixture "$FIXTURE" --gestor SEHAB --output-dir "$PACKAGES")"
filename="$(basename "$package")"
sha="$(sha256sum "$package" | awk '{print $1}')"
size="$(stat -c '%s' "$package")"
object_key="sha256/${sha:0:2}/${sha:2:2}/${sha}.zip"
mkdir -p "$BRONZE/sha256/${sha:0:2}/${sha:2:2}"
cp "$package" "$BRONZE/$object_key"

person_hash="$(sha256sum "$ROOT/config/contracts/gestores/SEHAB/pessoa/v2/pessoa.schema.json" | awk '{print $1}')"
record_hash="$(sha256sum "$ROOT/config/contracts/registros/AA01/v1/registro.schema.json" | awk '{print $1}')"

docker exec -i "$CID" psql -v ON_ERROR_STOP=1 -U jornada -d JornadaPg \
  -v package_sha="$sha" -v package_size="$size" -v object_key="$object_key" -v file_name="$filename" \
  -v person_hash="$person_hash" -v record_hash="$record_hash" <<'SQL'
INSERT INTO ref.gestor(codigo,nome)
VALUES('SEHAB','Secretaria Municipal de Habitação')
ON CONFLICT(codigo) DO UPDATE SET nome=EXCLUDED.nome,ativo=TRUE;

-- codigoSistemaOrigem é identificador técnico canônico (A-Z/0-9/_/-).
-- HabitaSampa permanece apenas o nome de exibição do sistema finalístico.
INSERT INTO ref.sistema_origem(gestor_id,codigo,nome)
SELECT gestor_id,'SEHAB','HabitaSampa' FROM ref.gestor WHERE codigo='SEHAB'
ON CONFLICT(gestor_id,codigo) DO UPDATE SET nome=EXCLUDED.nome,ativo=TRUE;

INSERT INTO ref.gestor_pessoa_versao(gestor_id,versao)
SELECT gestor_id,2 FROM ref.gestor WHERE codigo='SEHAB'
ON CONFLICT(gestor_id,versao) DO NOTHING;

UPDATE ref.gestor_pessoa_versao gpv
   SET status='ATIVA',
       pessoa_schema_ref='config/contracts/gestores/SEHAB/pessoa/v2/pessoa.schema.json',
       pessoa_schema_sha256=decode(:'person_hash','hex')
  FROM ref.gestor g
 WHERE g.gestor_id=gpv.gestor_id AND g.codigo='SEHAB' AND gpv.versao=2;

INSERT INTO ref.tipo_registro(codigo,nome)
VALUES('AA01','Auxílio Aluguel')
ON CONFLICT(codigo) DO UPDATE SET nome=EXCLUDED.nome;

UPDATE ref.tipo_registro tr
   SET gestor_id=g.gestor_id,natureza='BENEFICIO',ativo=TRUE
  FROM ref.gestor g
 WHERE tr.codigo='AA01' AND g.codigo='SEHAB';

INSERT INTO ref.tipo_registro_versao(tipo_registro_id,versao)
SELECT tipo_registro_id,1 FROM ref.tipo_registro WHERE codigo='AA01'
ON CONFLICT(tipo_registro_id,versao) DO NOTHING;

UPDATE ref.tipo_registro_versao trv
   SET status='ATIVA',
       schema_registro_ref='config/contracts/registros/AA01/v1/registro.schema.json',
       schema_registro_sha256=decode(:'record_hash','hex'),
       qc_status='IMPLEMENTADO',
       origina_endereco_casa_abrigo_sigilosa=FALSE,
       data_inicio_permitida_concessao=NULL,
       data_fim_permitida_concessao=NULL,
       regime_vigencia='PRAZO_INDETERMINADO'
  FROM ref.tipo_registro tr
 WHERE tr.tipo_registro_id=trv.tipo_registro_id AND tr.codigo='AA01' AND trv.versao=1;

DELETE FROM ingestao.entrega WHERE entrega_id='72000000-0000-4000-8000-000000000001'::uuid;

INSERT INTO ingestao.entrega(
    entrega_id,gestor_id,sistema_origem_id,gestor_pessoa_versao_id,natureza,
    tipo_registro_id,tipo_registro_versao_id,idempotency_key,payload_sha256,
    bytes_recebidos,status,data_referencia,recebido_em,ultima_atualizacao)
SELECT
    '72000000-0000-4000-8000-000000000001'::uuid,
    g.gestor_id,so.sistema_origem_id,gpv.gestor_pessoa_versao_id,'BENEFICIO',
    tr.tipo_registro_id,trv.tipo_registro_versao_id,'pg-runtime-processor-001',
    :'package_sha',:'package_size'::bigint,'RECEBIDA','2026-08-27T00:00:00-03:00'::timestamptz,
    CURRENT_TIMESTAMP,CURRENT_TIMESTAMP
FROM ref.gestor g
JOIN ref.sistema_origem so ON so.gestor_id=g.gestor_id AND so.codigo='SEHAB'
JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id AND gpv.versao=2
JOIN ref.tipo_registro tr ON tr.codigo='AA01'
JOIN ref.tipo_registro_versao trv ON trv.tipo_registro_id=tr.tipo_registro_id AND trv.versao=1
WHERE g.codigo='SEHAB';

INSERT INTO bronze.entrega_arquivo(
    entrega_id,nome_arquivo,content_type,objeto_chave,payload_sha256,tamanho_bytes,recebido_em,estado_armazenamento)
VALUES(
    '72000000-0000-4000-8000-000000000001'::uuid,
    :'file_name','application/zip',:'object_key',:'package_sha',:'package_size'::bigint,CURRENT_TIMESTAMP,'DISPONIVEL');

INSERT INTO ingestao.lote(
    lote_id,entrega_id,lote_seq,lote_total,qtd_pessoas,qtd_registros,status,
    tentativa_count,recuperacao_count,criado_em,atualizado_em)
VALUES(
    '72000000-0000-4000-8000-000000000002'::uuid,
    '72000000-0000-4000-8000-000000000001'::uuid,
    1,1,0,0,'PENDENTE',0,0,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
SQL

worker_pid=''
cleanup(){
  if [[ -n "$worker_pid" ]]; then
    kill "$worker_pid" 2>/dev/null || true
    wait "$worker_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

(
  cd "$ROOT"
  Database__Provider=PostgreSql \
  ConnectionStrings__Jornada="$JORNADA_POSTGRESQL_CONNECTION" \
  BronzeStorage__Provider=FileSystem \
  BronzeStorage__RootPath="$BRONZE" \
  DOTNET_ENVIRONMENT=Development \
  Processor__PollingMilliseconds=100 \
  Processor__RecoveryScanSeconds=1 \
  dotnet run --project src/Jornada.Processor.Worker/Jornada.Processor.Worker.csproj \
    -c Release --no-build --no-launch-profile
) >"$OUT/processor.log" 2>&1 &
worker_pid=$!

status=''
for _ in $(seq 1 120); do
  status="$(docker exec "$CID" psql -At -U jornada -d JornadaPg -c \
    "SELECT status FROM ingestao.entrega WHERE entrega_id='$DELIVERY_ID'::uuid;" | tr -d '\r[:space:]')"
  if [[ "$status" == 'PROCESSADA' ]]; then
    break
  fi
  if [[ "$status" == 'REJEITADA' || "$status" == 'QUARENTENA' ]]; then
    echo "ERRO: Worker PostgreSQL finalizou Entrega como $status." >&2
    tail -200 "$OUT/processor.log" >&2 || true
    exit 20
  fi
  sleep 1
done
if [[ "$status" != 'PROCESSADA' ]]; then
  echo "ERRO: timeout aguardando Worker PostgreSQL; último status=$status" >&2
  tail -200 "$OUT/processor.log" >&2 || true
  exit 21
fi

scalar(){
  docker exec "$CID" psql -At -U jornada -d JornadaPg -c "$1" | tr -d '\r[:space:]'
}

[[ "$(scalar "SELECT COUNT(*) FROM silver.pessoa_observacao po JOIN ingestao.lote l ON l.lote_id=po.lote_id WHERE l.entrega_id='$DELIVERY_ID'::uuid;")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não materializou exatamente uma Pessoa Silver.' >&2; exit 22; }
[[ "$(scalar "SELECT COUNT(*) FROM identidade.identity_map WHERE tipo='CPF' AND identificador='$CPF' AND estado='ATIVO' AND vigencia_fim IS NULL;")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não constituiu o mapa CPF determinístico.' >&2; exit 23; }
[[ "$(scalar "SELECT COUNT(*) FROM gold.pessoa WHERE cpf='$CPF' AND estado_concordancia='BASELINE_FONTE_UNICA';")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não materializou Gold Pessoa baseline.' >&2; exit 24; }
[[ "$(scalar "SELECT COUNT(*) FROM identidade.blocking_chave bc JOIN gold.pessoa gp ON gp.pessoa_uuid=bc.pessoa_uuid WHERE gp.cpf='$CPF' AND bc.atributo IN ('name_full','name_first','name_last','mother_name_full','mother_name_first','mother_name_last','birth_day','birth_month','birth_year');")" == '9' ]] \
  || { echo 'ERRO: runtime PostgreSQL não publicou as nove chaves fundamentais de blocking.' >&2; exit 29; }
[[ "$(scalar "SELECT COUNT(DISTINCT bc.normalizacao_versao) FROM identidade.blocking_chave bc JOIN gold.pessoa gp ON gp.pessoa_uuid=bc.pessoa_uuid WHERE gp.cpf='$CPF';")" == '1' ]] \
  || { echo 'ERRO: projeção de blocking não ficou presa a uma única versão de normalização.' >&2; exit 30; }
[[ "$(scalar "SELECT COUNT(*) FROM identidade.blocking_chave bc JOIN gold.pessoa gp ON gp.pessoa_uuid=bc.pessoa_uuid WHERE gp.cpf='$CPF' AND bc.atributo IN ('birth_day','birth_month','birth_year') AND bc.semantica_temporal='STABLE_IDENTITY_DATUM' AND bc.vigencia_fim IS NULL;")" == '3' ]] \
  || { echo 'ERRO: componentes de nascimento não foram publicados como dados estáveis correntes.' >&2; exit 31; }
[[ "$(scalar "SELECT COUNT(*) FROM identidade.blocking_chave bc JOIN gold.pessoa gp ON gp.pessoa_uuid=bc.pessoa_uuid WHERE gp.cpf='$CPF' AND bc.atributo IN ('name_full','name_first','name_last','mother_name_full','mother_name_first','mother_name_last') AND bc.semantica_temporal='VERSIONED_ALIAS' AND bc.vigencia_fim IS NULL;")" == '6' ]] \
  || { echo 'ERRO: componentes nominais correntes não foram publicados como aliases versionados.' >&2; exit 32; }
[[ "$(scalar "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE entrega_id='$DELIVERY_ID'::uuid AND codigo_registro_origem='$RECORD_CODE' AND status_analitico='VIGENTE' AND estado_atribuicao_identidade='ATRIBUIDA' AND pessoa_uuid IS NOT NULL AND qc_resultado='VALIDO';")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não materializou o benefício Gold atribuído/QC válido.' >&2; exit 25; }
[[ "$(scalar "SELECT COUNT(*) FROM serving.registro_integrado WHERE entrega_id='$DELIVERY_ID'::uuid AND codigo_registro_origem='$RECORD_CODE' AND entrega_completa=TRUE AND status_analitico='VIGENTE';")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não publicou o Serving factual completo.' >&2; exit 26; }
[[ "$(scalar "SELECT COUNT(*) FROM ingestao.item_processado ip JOIN ingestao.lote l ON l.lote_id=ip.lote_id WHERE l.entrega_id='$DELIVERY_ID'::uuid;")" == '2' ]] \
  || { echo 'ERRO: runtime PostgreSQL não registrou os dois itens processados.' >&2; exit 27; }
[[ "$(scalar "SELECT COUNT(*) FROM gold.beneficio_concedido WHERE entrega_id='$DELIVERY_ID'::uuid AND natureza_referencia_territorial='DOMICILIAR' AND referencia_territorial_observacao_id IS NOT NULL;")" == '1' ]] \
  || { echo 'ERRO: runtime PostgreSQL não propagou a Referência Territorial domiciliar.' >&2; exit 28; }

python3 - "$OUT/evidence.json" "$sha" "$filename" "$DELIVERY_ID" <<'PY'
import datetime,json,sys
path,sha,filename,delivery=sys.argv[1:5]
json.dump({
  'status':'OK',
  'generatedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
  'databaseProvider':'PostgreSql',
  'deliveryId':delivery,
  'codigoSistemaOrigem':'SEHAB',
  'sistemaOrigemNome':'HabitaSampa',
  'packageSha256':sha,
  'canonicalFileName':filename,
  'layers':['Bronze','Processor Worker','Silver','Identidade','Gold','BlockingProjection','Serving'],
  'deterministicCpfResolution':True,
  'blockingProjectionAtomicWithGold':True,
  'factualQc':'VALIDO',
  'territorialReference':'DOMICILIAR'
},open(path,'w',encoding='utf-8'),ensure_ascii=False,indent=2)
PY

cat "$OUT/evidence.json"
echo 'POSTGRESQL PROCESSOR RUNTIME E2E: OK'

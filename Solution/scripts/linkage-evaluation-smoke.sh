#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${JORNADA_EVALUATION_SMOKE_OUT:-$ROOT/.local/linkage-evaluation-smoke}"
DB="${JORNADA_EVALUATION_DATABASE:-JornadaHarness}"
SQL_PASSWORD="${JORNADA_EVALUATION_SQL_PASSWORD:-}"
SCALE_PEOPLE="${JORNADA_EVALUATION_SCALE_PEOPLE:-5000}"
SCALE_SEED="${JORNADA_EVALUATION_SCALE_SEED:-355}"
LABEL_COUNT="${JORNADA_EVALUATION_LABEL_COUNT:-100}"

need(){ command -v "$1" >/dev/null 2>&1 || { echo "ERRO: comando '$1' não encontrado." >&2; exit 2; }; }

PYTHON_CMD=()
resolve_python3(){
  local candidate
  for candidate in python3 python; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)' >/dev/null 2>&1; then
      PYTHON_CMD=("$candidate")
      return 0
    fi
  done
  if command -v py >/dev/null 2>&1 && py -3 -c 'import sys; raise SystemExit(0 if sys.version_info.major == 3 else 1)' >/dev/null 2>&1; then
    PYTHON_CMD=(py -3)
    return 0
  fi
  echo "ERRO: Python 3 não encontrado (tentados: python3, python, py -3)." >&2
  exit 2
}

for x in docker dotnet sha256sum; do need "$x"; done
resolve_python3

case "$(uname -s 2>/dev/null || true)" in
  MINGW*|MSYS*|CYGWIN*)
    required_arg_conv_exclusion='/opt/mssql-tools18/bin/sqlcmd'
    case ";${MSYS2_ARG_CONV_EXCL:-};" in
      *";$required_arg_conv_exclusion;"*) ;;
      *) export MSYS2_ARG_CONV_EXCL="${MSYS2_ARG_CONV_EXCL:+$MSYS2_ARG_CONV_EXCL;}$required_arg_conv_exclusion" ;;
    esac
    ;;
esac

: "${ConnectionStrings__Jornada:?ConnectionStrings__Jornada não definido}"
: "${SQL_PASSWORD:?JORNADA_EVALUATION_SQL_PASSWORD não definido}"
[[ "$DB" =~ ^[A-Za-z0-9_]+$ ]] || { echo "ERRO: nome de banco inválido." >&2; exit 2; }
[[ "$SCALE_PEOPLE" =~ ^[1-9][0-9]*$ ]] || { echo "ERRO: JORNADA_EVALUATION_SCALE_PEOPLE inválido." >&2; exit 2; }
[[ "$SCALE_SEED" =~ ^[0-9]+$ ]] || { echo "ERRO: JORNADA_EVALUATION_SCALE_SEED inválido." >&2; exit 2; }
[[ "$LABEL_COUNT" =~ ^[1-9][0-9]*$ ]] || { echo "ERRO: JORNADA_EVALUATION_LABEL_COUNT inválido." >&2; exit 2; }

CID="${JORNADA_EVALUATION_SQL_CONTAINER_ID:-}"
if [[ -z "$CID" ]]; then
  CID="$(docker ps -q --filter publish=1433 | head -1)"
fi
[[ -n "$CID" ]] || { echo "ERRO: container SQL Server não encontrado." >&2; exit 3; }

mkdir -p "$OUT"
rm -f \
  "$OUT/labels.csv" \
  "$OUT/report.json" \
  "$OUT/report-repeat.json" \
  "$OUT/evaluation-semantic.json" \
  "$OUT/evaluation-semantic-sha256.txt" \
  "$OUT/candidate-ranking-audit.json" \
  "$OUT/before.txt" \
  "$OUT/after.txt"

sqlcmd(){
  docker exec -i -e "SQLCMDPASSWORD=$SQL_PASSWORD" "$CID" \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -W -h -1 "$@"
}
scalar(){ sqlcmd -Q "SET NOCOUNT ON; $1" | tr -d '\r' | sed '/^[[:space:]]*$/d' | tail -1 | xargs; }

snapshot(){
  local dest="$1"
  {
    printf 'identidade.linkage_run='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM identidade.linkage_run;"
    printf 'identidade.vinculo_fonte='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM identidade.vinculo_fonte;"
    printf 'gold.pessoa='
    scalar "SELECT CONCAT(COUNT_BIG(*),'|',COALESCE(CONVERT(varchar(30),CHECKSUM_AGG(BINARY_CHECKSUM(*))),'NULL')) FROM gold.pessoa;"
  } > "$dest"
}

printf 'pessoa_observacao_id,pessoa_uuid_verdade\n' > "$OUT/labels.csv"
sqlcmd -Q "
SET NOCOUNT ON;
WITH pend AS (
    SELECT TOP ($LABEL_COUNT)
           po.pessoa_observacao_id,
           po.codigo_pessoa_origem,
           TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) AS n
    FROM silver.pessoa_observacao po
    WHERE po.cpf IS NULL
      AND po.codigo_pessoa_origem LIKE 'SCALE-PEND-%'
      AND TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)) IS NOT NULL
    ORDER BY TRY_CONVERT(bigint,RIGHT(po.codigo_pessoa_origem,10)),po.codigo_pessoa_origem
)
SELECT CONCAT(
    pessoa_observacao_id,',',
    CONVERT(varchar(36),CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('JORNADA-V355-',$SCALE_SEED,'-P-',((n-1)%$SCALE_PEOPLE)+1))))
)
FROM pend
ORDER BY n,codigo_pessoa_origem;" | tr -d '\r' | sed '/^[[:space:]]*$/d' >> "$OUT/labels.csv"

actual_labels="$(( $(wc -l < "$OUT/labels.csv") - 1 ))"
[[ "$actual_labels" -eq "$LABEL_COUNT" ]] || {
  echo "ERRO: esperados $LABEL_COUNT rótulos sintéticos, obtidos $actual_labels." >&2
  exit 4
}

snapshot "$OUT/before.txt"

(
  cd "$ROOT"
  dotnet run --project src/Jornada.Linkage.Runner --configuration Release --no-build -- \
    --candidate-ranking-audit-labels "$OUT/labels.csv" \
    --candidate-ranking-audit-output "$OUT/candidate-ranking-audit.json" \
    --ProbabilisticLinkage:CommandTimeoutSeconds 300

  dotnet run --project src/Jornada.Linkage.Evaluation --configuration Release --no-build -- \
    --labels "$OUT/labels.csv" \
    --output "$OUT/report.json" \
    --birth-window-days 7 \
    --max-cpf-anchored-pairs 500 \
    --sample-pool-size 5000 \
    --smoothing-alpha 0.5 \
    --command-timeout-seconds 300

  dotnet run --project src/Jornada.Linkage.Evaluation --configuration Release --no-build -- \
    --labels "$OUT/labels.csv" \
    --output "$OUT/report-repeat.json" \
    --birth-window-days 7 \
    --max-cpf-anchored-pairs 500 \
    --sample-pool-size 5000 \
    --smoothing-alpha 0.5 \
    --command-timeout-seconds 300
)

snapshot "$OUT/after.txt"
cmp -s "$OUT/before.txt" "$OUT/after.txt" || {
  echo "ERRO: diagnóstico read-only alterou fingerprint de tabelas operacionais monitoradas." >&2
  diff -u "$OUT/before.txt" "$OUT/after.txt" >&2 || true
  exit 5
}

"${PYTHON_CMD[@]}" - "$OUT/report.json" "$OUT/report-repeat.json" "$OUT/evaluation-semantic.json" "$OUT/evaluation-semantic-sha256.txt" <<'PY'
import hashlib,json,sys
first_path,second_path,out_path,hash_path=sys.argv[1:]

def load(path):
    with open(path,encoding='utf-8') as f:
        return json.load(f)

def semantic(r):
    inp=r.get('input',{})
    return {
        'purpose':r.get('purpose'),
        'safeguards':sorted(r.get('safeguards') or []),
        'input':{
            'labeledNoCpfPairs':inp.get('labeledNoCpfPairs'),
            'cpfAnchoredIndependentPairs':inp.get('cpfAnchoredIndependentPairs'),
            'birthWindowDays':inp.get('birthWindowDays'),
            'smoothingAlpha':inp.get('smoothingAlpha'),
            'commandTimeoutSeconds':inp.get('commandTimeoutSeconds'),
        },
        'blocking':r.get('blocking'),
        'mTransportability':r.get('mTransportability'),
    }

first=semantic(load(first_path))
second=semantic(load(second_path))
if first != second:
    raise SystemExit('Evaluation não é semanticamente reprodutível em duas execuções sobre o mesmo banco/labels')
canonical=json.dumps(first,ensure_ascii=False,sort_keys=True,separators=(',',':'))
with open(out_path,'w',encoding='utf-8') as f:
    json.dump(first,f,ensure_ascii=False,sort_keys=True,indent=2)
    f.write('\n')
digest=hashlib.sha256(canonical.encode('utf-8')).hexdigest()
with open(hash_path,'w',encoding='ascii') as f:
    f.write(digest+'\n')
print(f'Evaluation determinism: semantic_sha256={digest}')
PY

"${PYTHON_CMD[@]}" - "$OUT/report.json" "$LABEL_COUNT" <<'PY'
import json,sys
path=sys.argv[1]
expected=int(sys.argv[2])
with open(path,encoding='utf-8') as f:
    r=json.load(f)
if r.get('purpose')!='DEV_HML_ONLY_NO_PUBLICATION':
    raise SystemExit('purpose inválido')
required={
    'read-only against Jornada operational tables',
    'does not create linkage_run',
    'does not write IDENTITY_MAP/vinculo_fonte',
    'does not update Gold',
    'V2 is experimental evidence only',
}
if not required.issubset(set(r.get('safeguards',[]))):
    raise SystemExit('salvaguardas incompletas')
inp=r.get('input',{})
if inp.get('labeledNoCpfPairs')!=expected:
    raise SystemExit(f"labeledNoCpfPairs inesperado: {inp.get('labeledNoCpfPairs')}")
if int(inp.get('cpfAnchoredIndependentPairs',0)) < 100:
    raise SystemExit('amostra CPF-ancorada insuficiente para smoke')
blocking=r.get('blocking',{})
for key in ('v1','v2Candidate','deltaRecall'):
    if key not in blocking:
        raise SystemExit(f'blocking sem {key}')
for key in ('v1','v2Candidate'):
    b=blocking[key]
    for metric in ('sampleSize','trueUuidInsideBlock','recall','meanCandidates','medianCandidates','p95Candidates','maxCandidates'):
        if metric not in b:
            raise SystemExit(f'{key} sem {metric}')
transport=r.get('mTransportability',{})
for key in ('cpfAnchored','noCpfLabeled','distance'):
    if key not in transport:
        raise SystemExit(f'mTransportability sem {key}')
for metric in ('nomeTotalVariation','nomeMaeTotalVariation','dataNascimentoExactAbsoluteDelta'):
    if metric not in transport['distance']:
        raise SystemExit(f'distance sem {metric}')
print('Relatório Evaluation: contrato e métricas OK')
PY

"${PYTHON_CMD[@]}" - "$OUT/candidate-ranking-audit.json" "$LABEL_COUNT" "$SCALE_PEOPLE" <<'PY'
import json,sys
path=sys.argv[1]
expected=int(sys.argv[2])
gold_population=int(sys.argv[3])
with open(path,encoding='utf-8') as f:
    r=json.load(f)
if r.get('purpose')!='DEV_HML_ONLY_READ_ONLY_CANDIDATE_RANKING':
    raise SystemExit('candidate audit purpose inválido')
required={
    'read-only against Jornada operational tables',
    'does not create linkage_run',
    'does not write IDENTITY_MAP/vinculo_fonte',
    'does not update Gold',
    'reuses Runner active model, frozen ruleset, candidate loader, scorer and decision policy',
}
if not required.issubset(set(r.get('safeguards',[]))):
    raise SystemExit('candidate audit salvaguardas incompletas')
model=r.get('model',{})
summary=r.get('summary',{})
if not model.get('modelId') or int(model.get('modelVersion',0)) <= 0 or not model.get('algorithmVersion'):
    raise SystemExit('candidate audit sem proveniência do modelo')
for metric in ('threshold','conflictMargin'):
    value=model.get(metric)
    if not isinstance(value,(int,float)):
        raise SystemExit(f'candidate audit sem {metric}')
if summary.get('sampleSize') != expected:
    raise SystemExit(f"candidate audit sampleSize inesperado: {summary.get('sampleSize')}")
inside=int(summary.get('truthInsideCandidateSet',-1))
absent=int(summary.get('truthAbsentFromCandidateSet',-1))
if inside < 0 or absent < 0 or inside + absent != expected:
    raise SystemExit('decomposição de candidate recall inconsistente')
recall=float(summary.get('candidateRecallPct',-1))
if not 0 <= recall <= 100:
    raise SystemExit('candidateRecallPct fora de [0,100]')
for metric in (
    'truthDeterministicTop1','truthDeterministicTop2',
    'truthPresentDeterministicRankGreaterThan2','truthEvidenceTop',
    'truthPresentEvidenceRankGreaterThan2','truthTiedAtBestEvidence',
    'meanCandidateCount','p95CandidateCount','maxCandidateCount'):
    if metric not in summary:
        raise SystemExit(f'candidate audit sem {metric}')
mean_candidates=float(summary['meanCandidateCount'])
if mean_candidates >= gold_population:
    raise SystemExit(
        f'blocking degenerado no smoke: média de candidatos={mean_candidates:g} para população Gold={gold_population}')
if int(summary['truthDeterministicTop2']) > inside:
    raise SystemExit('top2 determinístico maior que truthInsideCandidateSet')
non_top2=r.get('nonTop2')
if not isinstance(non_top2,list):
    raise SystemExit('candidate audit sem lista nonTop2')
for row in non_top2:
    if 'truthInCandidateSet' not in row or 'candidateCount' not in row or 'rankingSpace' not in row:
        raise SystemExit('linha nonTop2 incompleta')

dq=r.get('decisionQuality')
if not isinstance(dq,dict):
    raise SystemExit('candidate audit sem decisionQuality')
for metric in ('sampleSize','resolved','correctResolved','incorrectResolved','conflicts','unresolved',
               'sensitivityPct','resolutionCoveragePct','unresolvedReasons','incorrectResolutions'):
    if metric not in dq:
        raise SystemExit(f'decisionQuality sem {metric}')
if int(dq['sampleSize']) != expected:
    raise SystemExit('decisionQuality.sampleSize inesperado')
resolved=int(dq['resolved'])
correct=int(dq['correctResolved'])
incorrect=int(dq['incorrectResolved'])
conflicts=int(dq['conflicts'])
unresolved=int(dq['unresolved'])
if min(resolved,correct,incorrect,conflicts,unresolved) < 0:
    raise SystemExit('decisionQuality contém contagem negativa')
if resolved != correct + incorrect:
    raise SystemExit('resolved difere de correctResolved + incorrectResolved')
if resolved + conflicts + unresolved != expected:
    raise SystemExit('decomposição dos status de decisão é inconsistente')
ppv=dq.get('ppvPct')
if resolved == 0:
    if ppv is not None:
        raise SystemExit('ppvPct deve ser null quando não há resolvidos')
else:
    if not isinstance(ppv,(int,float)) or not 0 <= float(ppv) <= 100:
        raise SystemExit('ppvPct fora de [0,100]')
for metric in ('sensitivityPct','resolutionCoveragePct'):
    value=dq.get(metric)
    if not isinstance(value,(int,float)) or not 0 <= float(value) <= 100:
        raise SystemExit(f'{metric} fora de [0,100]')
wrong=dq.get('incorrectResolutions')
if not isinstance(wrong,list) or len(wrong) != incorrect:
    raise SystemExit('incorrectResolutions inconsistente com incorrectResolved')
if not isinstance(dq.get('unresolvedReasons'),dict):
    raise SystemExit('unresolvedReasons deve ser objeto')
print('Candidate ranking/decision audit: recall/rank/PPV/sensibilidade/proveniência OK')
PY

"${PYTHON_CMD[@]}" "$ROOT/scripts/linkage-evaluation-evidence-gate.py" "$OUT/report.json" \
  --policy "$ROOT/config/hml/linkage-evaluation-policy.json" \
  --summary "$OUT/evidence-gate-summary.json"
"${PYTHON_CMD[@]}" "$ROOT/scripts/linkage-statistical-readiness-gate.py" \
  --root "$ROOT" --self-test
"${PYTHON_CMD[@]}" "$ROOT/scripts/performance-evidence-gate.py" --self-test

semantic_sha256="$(tr -d '\r\n' < "$OUT/evaluation-semantic-sha256.txt")"
{
  echo "status=OK"
  echo "labels=$actual_labels"
  echo "report_sha256=$(sha256sum "$OUT/report.json" | awk '{print $1}')"
  echo "candidate_ranking_audit_sha256=$(sha256sum "$OUT/candidate-ranking-audit.json" | awk '{print $1}')"
  echo "evaluation_semantic_sha256=$semantic_sha256"
  echo "evaluation_repeat_semantically_equal=true"
  echo "operational_fingerprint_unchanged=true"
} > "$OUT/result.txt"
cat "$OUT/result.txt"
echo "LINKAGE EVALUATION SMOKE: OK"

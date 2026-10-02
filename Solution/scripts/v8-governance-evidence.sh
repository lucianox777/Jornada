#!/usr/bin/env bash
set -euo pipefail

OUT="$1"
DB="${JORNADA_EVALUATION_DATABASE:-JornadaHarness}"
SQL_PASSWORD="${JORNADA_EVALUATION_SQL_PASSWORD:-}"
CID="${JORNADA_EVALUATION_SQL_CONTAINER_ID:-}"
if [[ -z "$CID" ]]; then CID="$(docker ps -q --filter publish=1433 | head -1)"; fi
[[ -n "$CID" ]] || { echo 'ERRO: container SQL Server não encontrado.' >&2; exit 3; }
[[ -n "$SQL_PASSWORD" ]] || { echo 'ERRO: JORNADA_EVALUATION_SQL_PASSWORD não definido.' >&2; exit 2; }
mkdir -p "$OUT"

SQLCMDPASSWORD="$SQL_PASSWORD" docker exec -i -e SQLCMDPASSWORD "$CID" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d "$DB" -W -h -1 -Q "
SET NOCOUNT ON;
DECLARE @model uniqueidentifier=(SELECT TOP(1) modelo_id FROM identidade.modelo_linkage WHERE status=N'ATIVO' ORDER BY versao DESC);
IF @model IS NULL THROW 52970,'Smoke V8 sem modelo ATIVO.',1;
SELECT CONCAT(
 CONVERT(varchar(36),m.modelo_id),'|',m.versao,'|',m.algoritmo_versao,'|',m.status,'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'SCORING_TERM_FREQUENCY_V1' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'TERM_FREQUENCY_WEIGHT' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'TERM_FREQUENCY_MIN_U' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_MAX_FP_VALIDATION_BP' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_VALIDATION_FP_LIMIT' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_VALIDATION_FP' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_MAX_FP_TEST_BP' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_TEST_FP_LIMIT' THEN p.valor END)),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),MAX(CASE WHEN p.nome=N'FS_DECISION_CALIBRATION_TEST_FP' THEN p.valor END)),N'<NULL>'),'|',
 CASE WHEN EXISTS(SELECT 1 FROM identidade.parametro_linkage gp WHERE gp.modelo_id=m.modelo_id AND gp.nome=N'SCORING_NON_UNIQUE_DEMOGRAPHIC_EXACT_GUARD_V1' AND gp.valor<>0) THEN '1' ELSE '0' END,'|',
 (SELECT COUNT_BIG(*) FROM identidade.frequencia_linkage f WHERE f.modelo_id=m.modelo_id AND f.atributo=N'NOME_PRENOME'),'|',
 (SELECT COUNT_BIG(*) FROM identidade.frequencia_linkage f WHERE f.modelo_id=m.modelo_id AND f.atributo=N'NOME_MAE_PRENOME'),'|',
 COALESCE(conf.status,N'<NULL>'),'|',COALESCE(conf.metodo_versao,N'<NULL>'),'|',COALESCE(conf.tolerancia_versao,N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(80),conf.max_llr_par_permitido),N'<NULL>'),'|',COALESCE(CONVERT(varchar(80),conf.max_llr_par_observado),N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(20),conf.candidatos_avaliados),N'<NULL>'),'|',COALESCE(CONVERT(varchar(1),conf.mesma_decisao_final),N'<NULL>'),'|',
 COALESCE(conf.validacao_estatistica,N'<NULL>'),'|',COALESCE(conf.source_revision,N'<NULL>'),'|',
 COALESCE(CONVERT(varchar(64),conf.modelo_snapshot_sha256,2),N'<NULL>'),'|',COALESCE(CONVERT(varchar(64),conf.request_sha256,2),N'<NULL>'),'|',COALESCE(CONVERT(varchar(64),conf.report_sha256,2),N'<NULL>'))
FROM identidade.modelo_linkage m
LEFT JOIN identidade.parametro_linkage p ON p.modelo_id=m.modelo_id
OUTER APPLY (SELECT TOP(1) e.* FROM auditoria.v_linkage_conferencia_evidencia e WHERE e.modelo_id=m.modelo_id ORDER BY e.linkage_conferencia_evidencia_id DESC) conf
WHERE m.modelo_id=@model
GROUP BY m.modelo_id,m.versao,m.algoritmo_versao,m.status,conf.status,conf.metodo_versao,conf.tolerancia_versao,conf.max_llr_par_permitido,conf.max_llr_par_observado,conf.candidatos_avaliados,conf.mesma_decisao_final,conf.validacao_estatistica,conf.source_revision,conf.modelo_snapshot_sha256,conf.request_sha256,conf.report_sha256;" | tr -d '\r' | sed '/^[[:space:]]*$/d' > "$OUT/v8-governance-row.txt"

python3 - "$OUT/v8-governance-row.txt" "$OUT/v8-governance-evidence.json" "$OUT/v8-governance-evidence-sha256.txt" <<'PY'
import hashlib,json,sys
from decimal import Decimal
row_path,out_path,hash_path=sys.argv[1:]
rows=[x.strip() for x in open(row_path,encoding='utf-8') if x.strip()]
if len(rows)!=1: raise SystemExit(f'evidência V8 esperava 1 linha, obteve {len(rows)}')
v=rows[0].split('|')
if len(v)!=28: raise SystemExit(f'evidência V8 com {len(v)} campos; esperado 28')
def dec(x): return None if x=='<NULL>' else Decimal(x)
doc={'schemaVersion':1,'nature':'DEV_SYNTHETIC_V8_GOVERNANCE_EVIDENCE',
 'model':{'modelId':v[0],'version':int(v[1]),'algorithmVersion':v[2],'status':v[3]},
 'termFrequency':{'enabled':dec(v[4]),'weight':dec(v[5]),'minimumU':dec(v[6]),'legacyDemographicGuardEnabled':v[13]=='1','personFrequencyRows':int(v[14]),'motherFrequencyRows':int(v[15])},
 'calibration':{'validation':{'budgetBasisPoints':dec(v[7]),'fpLimit':dec(v[8]),'falsePositive':dec(v[9])},'test':{'budgetBasisPoints':dec(v[10]),'fpLimit':dec(v[11]),'falsePositive':dec(v[12])}},
 'conference':{'status':v[16],'methodVersion':v[17],'toleranceVersion':v[18],'maxPairLlrAllowed':dec(v[19]),'maxPairLlrObserved':dec(v[20]),'candidatesEvaluated':None if v[21]=='<NULL>' else int(v[21]),'sameFinalDecision':v[22]=='1','statisticalValidation':v[23],'sourceRevision':None if v[24]=='<NULL>' else v[24],'modelSnapshotSha256':v[25],'requestSha256':v[26],'reportSha256':v[27]},
 'limits':{'issue31RepresentativeValidation':'NOT_ASSESSED','authorizesHmlOrProduction':False}}
if doc['model']['algorithmVersion']!='FELLEGI_SUNTER_DECISION_EVIDENCE_NEUTRAL_MISSING_V8': raise SystemExit('modelo ativo do smoke não é V8')
if doc['model']['status']!='ATIVO': raise SystemExit('modelo V8 do smoke não está ATIVO')
tf=doc['termFrequency']
if tf['enabled']!=Decimal('1') or tf['weight'] is None or tf['minimumU'] is None: raise SystemExit('V8 sem TF operacional/peso/piso persistidos')
if tf['legacyDemographicGuardEnabled']: raise SystemExit('V8 reintroduziu guard demográfico fixo legado')
if tf['personFrequencyRows']<=0 or tf['motherFrequencyRows']<=0: raise SystemExit('V8 sem snapshot TF nominal completo por atributo')
for part in ('validation','test'):
 x=doc['calibration'][part]
 if None in (x['budgetBasisPoints'],x['fpLimit'],x['falsePositive']): raise SystemExit(f'V8 sem budget/FP persistido em {part}')
 if x['falsePositive']>x['fpLimit']: raise SystemExit(f'V8 excedeu budget FP em {part}')
conf=doc['conference']
if conf['status']!='CONFORME' or not conf['sameFinalDecision']: raise SystemExit('conferência independente V8 não está CONFORME')
if not conf['candidatesEvaluated'] or conf['candidatesEvaluated']<=0: raise SystemExit('conferência V8 sem candidatos avaliados')
if conf['maxPairLlrAllowed'] is None or conf['maxPairLlrObserved'] is None or conf['maxPairLlrObserved']>conf['maxPairLlrAllowed']: raise SystemExit('conferência V8 excedeu tolerância LLR')
if conf['statisticalValidation']!='NOT_ASSESSED_ISSUE_31': raise SystemExit('conferência V8 confundiu implementação com validação estatística #31')
for key in ('modelSnapshotSha256','requestSha256','reportSha256'):
 if not isinstance(conf[key],str) or len(conf[key])!=64: raise SystemExit(f'conferência V8 sem SHA-256 válido: {key}')
def norm(x):
 if isinstance(x,Decimal): return format(x,'f')
 if isinstance(x,dict): return {k:norm(v) for k,v in x.items()}
 if isinstance(x,list): return [norm(v) for v in x]
 return x
doc=norm(doc)
canonical=json.dumps(doc,ensure_ascii=False,sort_keys=True,separators=(',',':'))
with open(out_path,'w',encoding='utf-8') as f: json.dump(doc,f,ensure_ascii=False,sort_keys=True,indent=2); f.write('\n')
digest=hashlib.sha256(canonical.encode()).hexdigest()
open(hash_path,'w',encoding='ascii').write(digest+'\n')
print(f'V8 governance evidence: status=OK sha256={digest}')
PY

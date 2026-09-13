from pathlib import Path

path = Path("Solution/scripts/local-e2e.sh")
text = path.read_text(encoding="utf-8")
old = '''actual_sehab_person_hash="$(sha256sum "$ROOT/config/contracts/gestores/SEHAB/pessoa/v2/pessoa.schema.json" | awk '{print $1}')"
expected_sehab_person_hash="$(scalar "SELECT LOWER(CONVERT(varchar(64),gpv.pessoa_schema_sha256,2)) FROM ref.gestor g JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id WHERE g.codigo='SEHAB' AND gpv.status='ATIVA';")"
echo "E2E GESTOR PERSON CONTRACT DIGEST: SEHAB source=$actual_sehab_person_hash catalog=$expected_sehab_person_hash"
[[ -n "$expected_sehab_person_hash" && "$actual_sehab_person_hash" == "$expected_sehab_person_hash" ]] || { echo 'ERRO: digest cadastral SEHAB diverge entre arquivo e catálogo antes do Processor.' >&2; exit 12; }
'''
new = '''active_sehab_person_schema_ref="$(scalar "SELECT gpv.pessoa_schema_ref FROM ref.gestor g JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id WHERE g.codigo='SEHAB' AND gpv.status='ATIVA';")"
[[ -n "$active_sehab_person_schema_ref" && -f "$ROOT/$active_sehab_person_schema_ref" ]] || { echo "ERRO: schema cadastral SEHAB ativo não encontrado: $active_sehab_person_schema_ref" >&2; exit 12; }
actual_sehab_person_hash="$(sha256sum "$ROOT/$active_sehab_person_schema_ref" | awk '{print $1}')"
expected_sehab_person_hash="$(scalar "SELECT LOWER(CONVERT(varchar(64),gpv.pessoa_schema_sha256,2)) FROM ref.gestor g JOIN ref.gestor_pessoa_versao gpv ON gpv.gestor_id=g.gestor_id WHERE g.codigo='SEHAB' AND gpv.status='ATIVA';")"
echo "E2E GESTOR PERSON CONTRACT DIGEST: SEHAB schema=$active_sehab_person_schema_ref source=$actual_sehab_person_hash catalog=$expected_sehab_person_hash"
[[ -n "$expected_sehab_person_hash" && "$actual_sehab_person_hash" == "$expected_sehab_person_hash" ]] || { echo 'ERRO: digest cadastral SEHAB diverge entre arquivo e catálogo antes do Processor.' >&2; exit 12; }
'''
if text.count(old) != 1:
    raise SystemExit(f"bloco E2E esperado uma vez, encontrado {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")

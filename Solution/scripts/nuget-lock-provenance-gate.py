#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, xml.etree.ElementTree as ET
from pathlib import Path

RELEASE = 'v4.04'
UNCHANGED = 'INHERITED_UNCHANGED_FROM_V3.99'
EXTERNAL_V399 = 'EXTERNAL_LOCKED_RESTORE_CONFIRMED_V3.99'
GRAPH_VERIFIED = 'INHERITED_BYTE_IDENTICAL_FROM_V3.99_EXTERNALLY_VERIFIED'


def fail(msg: str) -> None:
    raise SystemExit(f'NUGET LOCK PROVENANCE GATE: FAIL: {msg}')


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser(description='Valida a herança byte-a-byte dos packages.lock.json v4.04 a partir do predecessor v3.99 externamente restaurado em locked-mode.')
    ap.add_argument('--root', default='.')
    ap.add_argument('--manifest', default='config/release/nuget-lock-provenance.json')
    ap.add_argument('--summary')
    a = ap.parse_args()
    root = Path(a.root).resolve()
    manifest = (root / a.manifest).resolve()
    data = json.loads(manifest.read_text(encoding='utf-8'))

    if data.get('schemaVersion') != 1:
        fail('schemaVersion inesperado')
    if data.get('release') != RELEASE or data.get('baseNormativa') != 'v3.64' or data.get('solutionSchema') != 'v3.69':
        fail('versões declaradas inesperadas')
    env = data.get('packagingEnvironment') or {}
    if env.get('nugetRestoreExecuted') is not False:
        fail('empacotamento v4.04 não pode fingir restore NuGet local')
    if data.get('assurance') != 'PACKAGING_NO_RESTORE_PREDECESSOR_GRAPH_EXTERNALLY_VERIFIED':
        fail('assurance v4.04 inesperada')
    if data.get('currentGraphVerification') != GRAPH_VERIFIED:
        fail('grafo v4.04 deve declarar herança byte-a-byte do predecessor v3.99 verificado')
    if data.get('pendingLockCount') != 0:
        fail('v4.04 não altera o grafo e não deve possuir lock pendente')

    rows = data.get('locks') or []
    listed = {r.get('path'): r for r in rows if r.get('path')}
    actual = sorted(
        p.relative_to(root).as_posix()
        for p in root.rglob('packages.lock.json')
        if '.local' not in p.parts and 'obj' not in p.parts and 'bin' not in p.parts
    )
    if sorted(listed) != actual:
        fail(f'inventário de locks diverge; manifesto={sorted(listed)} atual={actual}')

    inherited = []
    for rel in actual:
        row = listed[rel]
        current_sha = sha256(root / rel)
        if current_sha != str(row.get('sha256', '')).lower():
            fail(f'SHA atual diverge: {rel}')
        if row.get('origin') != UNCHANGED:
            fail(f'lock v4.04 deve ser herdado da v3.99: {rel}')
        if row.get('sourceSha256') != current_sha:
            fail(f'lock herdado não é byte-a-byte idêntico ao predecessor v3.99: {rel}')
        if row.get('verificationStatus') != EXTERNAL_V399:
            fail(f'lock herdado sem confirmação locked restore v3.99: {rel}')
        inherited.append(rel)

    # A inspeção estática não substitui restore. Ela impede, porém, que a metadata
    # Project dos locks deixe de refletir os ProjectReference/PackageReference atuais.
    projects = {p.stem.lower(): p for p in root.rglob('*.csproj')}

    def project_declared_dependencies(project_path: Path) -> dict[str, str]:
        xml = ET.parse(project_path).getroot()
        deps: dict[str, str] = {}
        for node in xml.findall('.//ProjectReference'):
            include = node.attrib.get('Include')
            if include:
                deps[Path(include.replace('\\', '/')).stem] = '[1.0.0, )'
        for node in xml.findall('.//PackageReference'):
            name = node.attrib.get('Include')
            version = node.attrib.get('Version') or node.findtext('Version')
            if name and version:
                deps[name] = f'[{version}, )'
        return dict(sorted(deps.items(), key=lambda item: item[0].lower()))

    for rel in actual:
        lock_data = json.loads((root / rel).read_text(encoding='utf-8'))
        for graph in (lock_data.get('dependencies') or {}).values():
            for project_key, entry in graph.items():
                if not isinstance(entry, dict) or entry.get('type') != 'Project':
                    continue
                project_path = projects.get(project_key.lower())
                if project_path is None:
                    continue
                expected = project_declared_dependencies(project_path)
                if entry.get('dependencies', {}) != expected:
                    fail(f'metadata Project do lock diverge do csproj atual: {rel} -> {project_key}')

    external = data.get('externalEvidence') or {}
    if data.get('externalAssurance') != 'TRUSTED_OPERATOR_LOCKED_RESTORE_PASS_V3.99_PREDECESSOR_GRAPH':
        fail('assurance externa v3.99 ausente ou superestimada')
    if external.get('sourceRelease') != 'v3.99' or external.get('scriptVersion') != '2026.09.03-v3.99':
        fail('evidência externa não identifica corretamente o predecessor v3.99')
    if external.get('applicability') != 'VERIFIES_V3.99_LOCK_GRAPH_AND_PREDECESSOR_RUNTIME;V4.04_LOCKS_BYTE_IDENTICAL;DOES_NOT_VERIFY_V4.04_CODE_RUNTIME':
        fail('escopo da evidência v3.99 está ausente ou superestimado')
    for key in ('lockedRestoreSolution', 'lockedRestoreUnit', 'lockedRestoreIntegration'):
        if external.get(key) != 'PASS':
            fail(f'evidência v3.99 sem {key}=PASS')
    for key in ('buildSolution', 'buildUnit', 'unitTests', 'docker', 'sqlImageDigest', 'sqlEngineCheckdb', 'buildIntegration', 'integrationTests', 'localReleaseValidation'):
        value = str(external.get(key, ''))
        if not value.startswith('PASS'):
            fail(f'evidência v3.99 sem {key}=PASS')

    req = '\n'.join(data.get('promotionRequirements') or [])
    for required in (
        'dotnet restore Jornada.sln --locked-mode',
        'dotnet build Jornada.sln -c Release --no-restore',
        'Unit + Integration da v4.04',
        'fabric-sql-compatibility',
    ):
        if required not in req:
            fail(f'promoção não exige: {required}')

    summary = {
        'status': 'PASS',
        'release': RELEASE,
        'lockCount': len(actual),
        'inheritedExternallyVerifiedFromV399Count': len(inherited),
        'pendingLockCount': 0,
        'assurance': data.get('assurance'),
        'externalAssurance': data.get('externalAssurance'),
    }
    if a.summary:
        out = Path(a.summary)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'NUGET LOCK PROVENANCE GATE: OK ({len(actual)} locks inherited byte-identical from externally verified v3.99)')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())

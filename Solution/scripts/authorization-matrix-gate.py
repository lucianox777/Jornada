#!/usr/bin/env python3
"""Gate estrutural da matriz: AuthenticationHandler compartilhado, políticas e endpoints.

O endpoint não precisa repetir AuthenticateAsync: a autenticação é obrigatória em
RequireAuthorization e ocorre em UseAuthentication/UseAuthorization. Uma regex de
AuthenticateAsync inline rejeitaria indevidamente o modelo central DT-04.
"""
from __future__ import annotations

import argparse
import copy
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "program": ROOT / "src/Jornada.Api/Program.cs",
    "origin": ROOT / "src/Jornada.Api/ProgressiveOriginApi.cs",
    "monitor": ROOT / "src/Jornada.Api/OperationalMonitorApi.cs",
    "security": ROOT / "src/Jornada.Access.Security/JornadaAccessSecurity.cs",
    "verifier": ROOT / "src/Jornada.Api/JornadaApiAccessVerifier.cs",
}
MATRIX = ROOT / "config/security/authorization-matrix.json"
SOURCE_GUARDS = (
    "context.CredentialType != AccessCredentialType.GESTOR",
    "context.Scopes.Contains(ProgressiveOriginApi.Permission",
    "p.gestor_codigo COLLATE Latin1_General_100_BIN2=@gestor",
    "DATALENGTH(p.gestor_codigo)=DATALENGTH(@gestor)",
    "Value = context.GestorCodigo",
    "p.sistema_origem_codigo COLLATE Latin1_General_100_BIN2=@sistema",
    "DATALENGTH(p.sistema_origem_codigo)=DATALENGTH(@sistema)",
    "p.codigo_pessoa_origem COLLATE Latin1_General_100_BIN2=@codigo",
    "DATALENGTH(p.codigo_pessoa_origem)=DATALENGTH(@codigo)",
)


class GateError(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise GateError(message)


def norm_path(path: str) -> str:
    return re.sub(r"{([^}:]+):[^}]+}", r"{\1}", path)


def check_endpoint(source: str, permission: str, name: str, literal: bool = True) -> None:
    arg = re.escape('"' + permission + '"') if literal else "Permission"
    require(re.search(r"\.RequireAuthorization\(\s*" + arg + r"\s*\)", source) is not None,
            name + ": RequireAuthorization da permissão ausente")
    require(re.search(r"\.RequireRateLimiting\(\s*\"[a-z-]+\"\s*\)", source) is not None,
            name + ": limite de borda ausente")
    require("RequireJornadaAccessContext()" in source,
            name + ": contexto verificado do handler ausente")
    check = (r"IsAllowedAsync\(\s*context\s*,\s*" + arg + r"\s*,")
    require(re.search(check, source) is not None, name + ": política de recurso ausente")


def validate(data: dict, sources: dict[str, str]) -> int:
    require(data.get("schemaVersion") == 1 and data.get("status") == "VIGENTE",
            "matriz com versão/status inválidos")
    routes = data.get("routes")
    require(isinstance(routes, list) and bool(routes), "rotas ausentes")
    expected = {(r["method"].upper(), r["path"]): r for r in routes}
    require(len(expected) == len(routes), "rota duplicada na matriz")

    program, origin, monitor, security, verifier = (
        sources[name] for name in ("program", "origin", "monitor", "security", "verifier"))
    for token in (
        "builder.Services.AddJornadaAccessSecurity()",
        "AddSingleton<IJornadaAccessVerifier, JornadaApiAccessVerifier>()",
        "app.UseAuthentication();",
        "app.UseAuthorization();",
        "app.UseMiddleware<ApiAuditMiddleware>()",
    ):
        require(token in program, "host sem componente de segurança: " + token)
    require(program.index("app.UseAuthentication();") < program.index("app.UseAuthorization();"),
            "middleware de autenticação depois de autorização")
    require("hostEnvironment.IsDevelopment()" in monitor,
            "monitor sintético sem isolamento em Development")

    # Verifica o esquema real, parser da chave, falha de autenticação e contexto.
    for token in (
        "DefaultAuthenticateScheme = Scheme",
        "DefaultChallengeScheme = Scheme",
        "DefaultForbidScheme = Scheme",
        "AddScheme<AuthenticationSchemeOptions, JornadaAccessAuthenticationHandler>",
        "services.AddSingleton<IAuthorizationHandler, JornadaScopeAuthorizationHandler>()",
        "AddRequirements(new JornadaScopeRequirement(permission, allowType))",
        "headers[\"X-Jornada-Access-Key\"].Count != 1",
        "headers[\"X-Jornada-Gestor\"].Count > 1",
        "headers[\"X-Jornada-Beneficio\"].Count > 1",
        "headers[\"X-Jornada-Servico\"].Count > 1",
        "Count(x => !string.IsNullOrWhiteSpace(x)) != 1",
        "await verifier.VerifyAsync(Context, parsed.Credential",
        "verification.Context is null",
        "Context.Items[JornadaAccessSecurity.AccessContextItem] = verification.Context",
        "StatusCodes.Status401Unauthorized",
        "JornadaScopeAuthorizationHandler",
        "!requirement.AllowType && accessContext.CredentialType != AccessCredentialType.GESTOR",
        "accessContext.Scopes.Contains(requirement.Permission",
    ):
        require(token in security, "handler central ou regra de segurança ausente: " + token)
    for token in (
        "await resolver.ResolveAsync(credential, ct)",
        "if (context is null)",
        "StatusCodes.Status401Unauthorized",
        "limiter.TryAcquire(context, http.Request)",
        "StatusCodes.Status429TooManyRequests",
        "JornadaAccessVerification.Accepted(context)",
    ):
        require(token in verifier, "validador real de credencial ausente: " + token)
    require("builder.Environment.IsDevelopment()" in program
            and "builder.Services.AddSingleton<IAccessContextResolver, CorporateIdentityPendingAccessContextResolver>();" in program
            and "builder.Services.AddSingleton<IPolicyEngine, DenyByDefaultPolicyEngine>();" in program,
            "HML/produção deixou de ser deny-by-default")

    permission_block = re.search(
        r"private static readonly .*? Permissions\s*=\s*\[(.*?)\];", security, re.S)
    require(permission_block is not None, "lista central de políticas não identificada")
    registered = re.findall(r'\("([^"]+)",\s*(true|false)\)', permission_block.group(1))
    require(len(registered) == len(set(permission for permission, _ in registered)),
            "permissões duplicadas no handler")
    policy_types = {permission: typ == "true" for permission, typ in registered}
    declared_permissions = {r["permission"]: r for r in data.get("permissions", [])}
    require(set(policy_types) == set(declared_permissions),
            "políticas centrais divergem da matriz de permissões")

    matches = list(re.finditer(r'app\.Map(Get|Post|Put|Delete)\("([^"]+)"', program))
    actual: dict[tuple[str, str], str] = {}
    for i, match in enumerate(matches):
        method, path = match.group(1).upper(), norm_path(match.group(2))
        if not path.startswith("/api/"):
            continue
        end = matches[i + 1].start() if i + 1 < len(matches) else program.find("app.Run()", match.start())
        require(end > match.start(), f"{method} {path}: delimitador de endpoint não encontrado")
        block = program[match.start():end]
        permissions = re.findall(r'IsAllowedAsync\(\s*context\s*,\s*"([^"]+)"', block)
        require(bool(permissions) and len(set(permissions)) == 1,
                f"{method} {path}: política de recurso ausente ou divergente")
        permission = permissions[0]
        check_endpoint(block, permission, f"{method} {path}")
        require((method, path) not in actual, f"rota duplicada: {method} {path}")
        actual[(method, path)] = permission

    require("app.MapProgressiveOriginApi();" in program,
            "módulo progressivo não registrado no host")
    require("app.MapOperationalMonitorApi();" in origin,
            "módulo monitor não registrado na árvore da API progressiva")

    modules = (
        (origin, "Route", "POST", "jornada.identidade.origem.read", "MapPost"),
        (monitor, "StatusRoute", "GET", "jornada.monitor.read", "MapGet"),
    )
    for module, const, method, expected_permission, mapping in modules:
        route = re.search(r'public const string ' + const + r'\s*=\s*"([^"]+)"', module)
        permission = re.search(r'public const string Permission\s*=\s*"([^"]+)"', module)
        require(route is not None and permission is not None, "rota/permissão modular ausente")
        require(permission.group(1) == expected_permission, "permissão modular não esperada")
        require(re.search(r'app\.' + mapping + r'\(\s*' + const + r'\s*,\s*async', module)
                is not None, "endpoint modular ausente")
        block = module[module.index("app." + mapping + "(" + const + ","):]
        block = block[:block.index(".RequireAuthorization(Permission)") + len(".RequireAuthorization(Permission)")]
        check_endpoint(block, expected_permission, method + " " + route.group(1), literal=False)
        key = (method, norm_path(route.group(1)))
        require(key not in actual, "rota modular duplicada")
        actual[key] = expected_permission

    for token in SOURCE_GUARDS:
        require(token in origin, "guard de propriedade do gestor/origem ausente: " + token)
    require("origin" not in actual, "identificador inválido na matriz")
    require("context.CredentialType != AccessCredentialType.GESTOR" in origin,
            "origem aceita tipo de credencial")
    require("syntheticDevelopment" in monitor
            and monitor.count(".RequireAuthorization(Permission)") >= 2
            and monitor.count(".RequireRateLimiting(\"standard\")") >= 2,
            "monitor sintético deve herdar autenticação/limites e ser somente DEV")

    require(set(actual) == set(expected),
            "rotas divergentes: faltando=" + str(sorted(set(expected) - set(actual)))
            + " extras=" + str(sorted(set(actual) - set(expected))))
    for key, declared in expected.items():
        permission = declared["permission"]
        require(actual[key] == permission,
                f"{key}: permissão do endpoint diverge da matriz")
        types = set(declared.get("allowedCredentialTypes") or [])
        require(bool(types) and types <= {"GESTOR", "BENEFICIO", "SERVICO"}
                and "GESTOR" in types, f"{key}: tipos de credencial inválidos")
        meta = declared_permissions[permission]
        for typ, attr in (("GESTOR", "gestor"), ("BENEFICIO", "beneficio"), ("SERVICO", "servico")):
            require(typ not in types or meta.get(attr) is True,
                    f"{key}: tipo {typ} não permitido pela permissão")
        require(policy_types[permission] == bool(types & {"BENEFICIO", "SERVICO"}),
                f"{key}: tipo aceito no handler diverge da rota")
        if declared.get("sourceSystemOwnershipAlways"):
            require(key == ("POST", "/api/v1/identidade/origens/consulta")
                    and types == {"GESTOR"},
                    f"{key}: propriedade de origem deve ser exclusiva de GESTOR")
    return len(actual)


def self_test(matrix: dict, sources: dict[str, str]) -> None:
    scenarios = [
        ("scope removido", "program",
         '.RequireAuthorization("jornada.identidade.resolve")',
         '.RequireAuthorization("jornada.ingestao.write")'),
        ("contexto não autenticado", "origin", "RequireJornadaAccessContext()", "HttpContext.User"),
        ("limite institucional removido", "verifier",
         "limiter.TryAcquire(context, http.Request)", "true"),
        ("scope por tipo liberado", "security",
         "!requirement.AllowType && accessContext.CredentialType != AccessCredentialType.GESTOR",
         "false && accessContext.CredentialType != AccessCredentialType.GESTOR"),
        ("propriedade do sistema removida", "origin",
         "p.sistema_origem_codigo COLLATE Latin1_General_100_BIN2=@sistema", "1=1"),
        ("fallback não-dev aberto", "program",
         "builder.Services.AddSingleton<IPolicyEngine, DenyByDefaultPolicyEngine>();",
         "builder.Services.AddSingleton<IPolicyEngine, AllowAllPolicyEngine>();"),
    ]
    for name, file, before, after in scenarios:
        require(before in sources[file], "autoteste não encontrou alvo: " + name)
        changed = dict(sources)
        changed[file] = sources[file].replace(before, after, 1)
        try:
            validate(matrix, changed)
        except GateError:
            continue
        raise GateError("autoteste negativo não detectou mutação: " + name)

    mutated = copy.deepcopy(matrix)
    mutated["routes"][0]["allowedCredentialTypes"] = ["GESTOR"]
    try:
        validate(mutated, sources)
    except GateError:
        pass
    else:
        raise GateError("autoteste não detectou divergência entre tipo da rota e handler")
    print(f"AUTHORIZATION MATRIX SELF-TEST: OK ({len(scenarios) + 1} mutações bloqueadas)")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", default=str(MATRIX))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    sources = {name: path.read_text(encoding="utf-8") for name, path in PATHS.items()}
    matrix = json.loads(Path(args.matrix).read_text(encoding="utf-8"))
    try:
        count = validate(matrix, sources)
        if args.self_test:
            self_test(matrix, sources)
    except (GateError, ValueError, KeyError, TypeError) as exc:
        raise SystemExit("AUTHORIZATION MATRIX GATE: FAIL: " + str(exc)) from exc
    print(f"AUTHORIZATION MATRIX GATE: OK ({count} rotas protegidas, handler central)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

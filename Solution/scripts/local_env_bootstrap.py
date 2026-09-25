#!/usr/bin/env python3
"""Bootstrap da credencial SQL exclusivamente local, sem valor padrão executável.

O .env preexistente nunca é modificado. Quando solicitado pelos entrypoints,
o bootstrap recusa gerar nova senha se o volume SQL original já existe:
o usuário deve recuperar o .env anterior para não perder acesso ao banco.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys


ROOT = Path(__file__).resolve().parent.parent
PASSWORD_LINE = re.compile(r"^JORNADA_SQL_SA_PASSWORD=[^\r\n]*$", re.MULTILINE)
SQL_VOLUME = "jornada_sql_data"


def _template_password_line(template: Path) -> tuple[str, str]:
    content = template.read_text(encoding="utf-8-sig")
    matches = PASSWORD_LINE.findall(content)
    if len(matches) != 1:
        raise ValueError(".env.example deve declarar a senha SQL exatamente uma vez.")
    return content, matches[0]


def _assert_no_existing_sql_volume() -> None:
    # 'volume ls' distingue lista vazia de Docker indisponível. Não assumir
    # que um erro de 'volume inspect' significa que o volume não existe.
    try:
        result = subprocess.run(
            ["docker", "volume", "ls", "--format", "{{.Name}}"],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as exc:
        raise RuntimeError("Docker indisponível; não criar nova credencial implicitamente.") from exc
    if result.returncode != 0:
        raise RuntimeError("Não foi possível consultar volumes Docker; .env não foi criado.")
    if SQL_VOLUME in result.stdout.splitlines():
        raise RuntimeError(
            "O volume jornada_sql_data já existe, mas .env está ausente. "
            "Recupere o .env original; não gere outra senha nem apague o volume."
        )


def ensure_local_env(
    template: Path, output: Path, *, check_docker_volume: bool = False
) -> bool:
    """Cria o arquivo uma vez; retorna False sem escrever quando já existe."""
    if output.exists() or output.is_symlink():
        if not output.is_file():
            raise ValueError("O caminho .env existe, mas não é um arquivo.")
        _, published_line = _template_password_line(template)
        existing = output.read_text(encoding="utf-8-sig")
        if published_line in PASSWORD_LINE.findall(existing):
            print(
                "AVISO: .env existente contém a senha pública de exemplo; "
                "troque-a antes de uso compartilhado.",
                file=sys.stderr,
            )
        return False

    content, old_password_line = _template_password_line(template)
    if check_docker_volume:
        _assert_no_existing_sql_volume()

    # 192 bits aleatórios + prefixo fixo de complexidade para SQL Server:
    # maiúscula, minúscula, dígito e caracteres especiais garantidos.
    password = "Jd!A9_" + secrets.token_hex(24)
    created = content.replace(
        old_password_line,
        "JORNADA_SQL_SA_PASSWORD=" + password,
        1,
    )
    if created == content:
        raise ValueError("A substituição da senha não produziu alteração.")

    # O_EXCL evita sobrescrever .env surgido em uma corrida. Em POSIX,
    # 0600 impede leitura de terceiros; no Windows valem as ACLs herdadas.
    try:
        fd = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        return False
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(created)
    except BaseException:
        output.unlink(missing_ok=True)
        raise
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--template", type=Path, default=ROOT / ".env.example")
    parser.add_argument("--output", type=Path, default=ROOT / ".env")
    parser.add_argument(
        "--check-docker-volume",
        action="store_true",
        help="Falhar se jornada_sql_data já existe e a configuração sumiu.",
    )
    args = parser.parse_args()
    try:
        created = ensure_local_env(
            args.template,
            args.output,
            check_docker_volume=args.check_docker_volume,
        )
    except (OSError, RuntimeError, ValueError) as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        return 2
    if created:
        print(".env DEV criado com credencial aleatória local; valor não exibido.")
    else:
        print(".env preexistente preservado; nenhuma credencial alterada.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

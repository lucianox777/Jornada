#!/usr/bin/env python3
"""Static regression guard for the versioned demographic bootstrap SQL contract."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MIGRATIONS = ROOT / "database" / "migrations"

def check(name: str, required: tuple[str, ...]) -> None:
    text = (MIGRATIONS / name).read_text(encoding="utf-8-sig")
    for token in required:
        assert token.lower() in text.lower(), f"{name}: missing {token}"

def main() -> None:
    check("20261009_Ref_Distribuicao_Nascimento_IBGE.sql", (
        "ref.distribuicao_nascimento_versao",
        "ref.distribuicao_nascimento_dia",
        "ref.sp_publicar_distribuicao_nascimento",
        "tr_distribuicao_nascimento_dia_immutable",
        "THROW 52234",
    ))
    check("20261009_Ref_Distribuicao_Nascimento_Carga_Json.sql", (
        "ref.sp_carregar_distribuicao_nascimento_json",
        "OPENJSON(@json,'$.rows')",
        "THROW 52243",
        "THROW 52244",
        "THROW 52247",
    ))
    check("20261009_Ref_Calibracao_Inicial_Sintetica.sql", (
        "ref.calibracao_inicial_versao",
        "ref.sp_registrar_calibracao_inicial",
        "ref.sp_publicar_calibracao_inicial",
        "tr_calibracao_inicial_publicada_immutable",
        "THROW 52225",
    ))
    check("20261009_Ref_Distribuicao_Nascimento_Modelo_Binding.sql", (
        "distribuicao_versao_id",
        "fk_modelo_linkage_ref_demografica_distribuicao",
        "THROW 52235",
    ))
    print("ref demographic bootstrap SQL static contract: OK")

if __name__ == "__main__":
    main()

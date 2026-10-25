"""CLI da Solução de Apoio às Secretarias (o Docker utiliza este mesmo núcleo)."""
from __future__ import annotations
import argparse
import json
import sys
from .core import executar, reenviar_zip_existente


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Prepara ZIP Jornada v2; envia apenas quando configurado.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--entrada", help="Arquivo ou pasta a preparar")
    group.add_argument("--reenviar-zip", help="Reenvia pacote existente, sem reempacotar")
    parser.add_argument("--perfil", required=True, help="Perfil de origem/contratos versão 1")
    parser.add_argument("--saida", required=True, help="Pasta de pacotes e relatórios")
    state = parser.add_mutually_exclusive_group()
    state.add_argument("--enviar", action="store_const", const=True, dest="enviar", default=None)
    state.add_argument("--nao-enviar", action="store_const", const=False, dest="enviar")
    args = parser.parse_args(argv)
    if args.reenviar_zip:
        if args.enviar is False:
            parser.error("--reenviar-zip não pode ser combinado com --nao-enviar")
        result = reenviar_zip_existente(args.reenviar_zip, args.perfil, args.saida)
    else:
        result = executar(args.entrada, args.perfil, args.saida, enviar=args.enviar)
    print(json.dumps({k: v for k, v in result.items() if k in ("estado", "pacotes", "erros", "relatorioJson", "relatorioHtml")}, ensure_ascii=False, indent=2))
    return 0 if result["estado"] in ("GERADO_NAO_ENVIADO", "RECEBIDO") else 2

if __name__ == "__main__":
    sys.exit(main())

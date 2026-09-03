#!/usr/bin/env bash
# Compatibilidade histórica: RELEASE_INFO.txt passou a ser fonte versionada na v3.69.
# Este script NÃO gera nem altera o arquivo; valida que a tag já criada corresponde ao conteúdo commitado.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${1:?Uso: $0 <versao-solution-sem-v> [release-info]}"
INFO="${2:-$ROOT/RELEASE_INFO.txt}"
TAG="jornada-solution-v${VERSION#v}"
python3 "$ROOT/Solution/scripts/release-source-gate.py" \
  --repo "$ROOT" \
  --release-info "$INFO" \
  --expected-tag "$TAG" \
  --require-clean
echo "RELEASE_INFO versionado e tag verificados; nenhuma geração pós-tag foi realizada."

#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
REQ="$ROOT/Documentos/Requisitos"
DOC="$ROOT/Documentos"
WORK="${RUNNER_TEMP:-/tmp}/jornada-doc-delivery"
rm -rf "$WORK"
mkdir -p "$WORK"

need() { command -v "$1" >/dev/null 2>&1 || { echo "Dependência ausente: $1" >&2; exit 2; }; }
for c in pandoc libreoffice plantuml unzip pdfinfo python3; do need "$c"; done

cat > "$WORK/Jornada_Identidade_Linkage_Classes.puml" <<'PUML'
@startuml
skinparam classAttributeIconSize 0
hide empty methods
hide circle

class PessoaOrigem {
  +pessoa_origem_id : long
  +sistema_origem_id : long
  +codigo_pessoa_origem : string
  +nome : string
  +nome_mae : string
  +data_nascimento : date
  +cpf : string
}

class Pessoa {
  +pessoa_uuid : uuid
}

class PessoaOrigemProgressiva {
  +pessoa_origem_id : long
  +initial_uuid : uuid
  +canonical_uuid : uuid
  +estado : string
  +versao : long
}

class CpfAncora {
  +cpf : string
  +pessoa_uuid : uuid
  +criado_em : datetimeoffset
}

class VinculoFonte {
  +vinculo_fonte_id : long
  +pessoa_origem_id : long
  +pessoa_uuid : uuid
  +status : string
  +metodo_resolucao : string
}

class BlockingChave {
  +pessoa_uuid : uuid
  +normalizacao_versao : string
  +atributo : string
  +valor_normalizado : string
  +vigencia_inicio : datetimeoffset
  +vigencia_fim : datetimeoffset
}

class LinkageRuleSet {
  +ruleset_id : uuid
  +versao : string
  +fingerprint_sha256 : string
  +status : string
}

class LinkageRuleSetPasse {
  +ruleset_id : uuid
  +passe_ordem : int
}

class LinkageRuleSetPasseCampo {
  +ruleset_id : uuid
  +passe_ordem : int
  +campo_ordem : int
  +atributo : string
}

class ComposicaoPlano {
  +decision_id : uuid
  +policy_version : string
  +evidence_fingerprint : string
}

class ComposicaoAplicacao {
  +decision_id : uuid
  +status : string
  +aplicado_em : datetimeoffset
}

class ComposicaoPublicacao {
  +decision_id : uuid
  +status : string
  +publicado_em : datetimeoffset
}

class GoldPessoa {
  +pessoa_uuid : uuid
  +estado_atribuicao_identidade : string
  +dados_factuais
}

class RegistroIntegrado {
  +pessoa_uuid : uuid
  +registro_id : long
}

PessoaOrigem "1" -- "1" PessoaOrigemProgressiva : origem progressiva
PessoaOrigemProgressiva "*" --> "1" Pessoa : initial_uuid
PessoaOrigemProgressiva "*" --> "0..1" Pessoa : canonical_uuid
CpfAncora "0..1" --> "1" Pessoa : ancora permanente
PessoaOrigem "1" -- "0..*" VinculoFonte
VinculoFonte "*" --> "0..1" Pessoa
Pessoa "1" -- "0..*" BlockingChave : projecao reconstruivel
LinkageRuleSet "1" *-- "1..*" LinkageRuleSetPasse
LinkageRuleSetPasse "1" *-- "1..*" LinkageRuleSetPasseCampo
LinkageRuleSetPasseCampo ..> BlockingChave : gera candidatos por atributo
ComposicaoPlano "1" --> "0..1" ComposicaoAplicacao
ComposicaoAplicacao "1" --> "0..1" ComposicaoPublicacao
ComposicaoAplicacao ..> PessoaOrigemProgressiva : altera referencia canonica
ComposicaoPublicacao ..> GoldPessoa : recomposicao atomica
GoldPessoa --> RegistroIntegrado : projecao para consumo

note right of CpfAncora
CPF -> UUID e permanente.
Linkage probabilistico nao transfere ancora.
end note

note bottom of GoldPessoa
Fato e identidade sao conceitos distintos.
Ausencia ou baixa qualidade de campo nao elimina observacao.
end note

note bottom of LinkageRuleSet
Calibrador publica versao imutavel.
Avaliador consome exatamente a mesma versao.
end note
@enduml
PUML

cat > "$WORK/Jornada_Resolucao_Identidade_Atividade.puml" <<'PUML'
@startuml
start
:Receber observação preservada da origem;
:Validar contrato, proveniência e qualidade dos campos;
:Assegurar initial_uuid idempotente\npor (sistema_origem, codigo_pessoa_origem);
if (CPF válido, confiável e não conflitado?) then (sim)
  if (Existe cpf_ancora?) then (sim)
    :Recuperar UUID permanente da âncora;
  else (não)
    :Reservar CPF -> UUID de forma\ntransacional e append-only;
  endif
  :Produzir decisão determinística;
else (não)
  :Carregar modelo e ruleset\nimutáveis/versionados;
  :Gerar candidatos com blocking_chave\nusando todos os passes aplicáveis;
  if (Universo completo e execução íntegra?) then (não)
    :Registrar falha operacional / execução incompleta;
    :Não interpretar como ausência de candidato;
    :Não publicar referência canônica;
    stop
  else (sim)
    :Calcular evidências e score\nsegundo modelo homologado;
    if (Há candidato único que satisfaz\nlimiar + margem + regras de segurança?) then (sim)
      :Produzir ASSOCIACAO_EXISTENTE\ncom destino explícito;
    else (não)
      if (Nenhum candidato elegível?) then (sim)
        :Produzir NOVA_IDENTIDADE\nsomente após busca completa;
      else (ambiguidade)
        :Produzir INDEFINIDA;
        :Não escolher destino arbitrariamente;
      endif
    endif
  endif
endif
:Persistir decision_id, versão da política,\nruleset/modelo e evidência de forma idempotente;
if (Decisão autoriza referência?) then (sim)
  :Aplicar composição sob estado autoritativo,\nlocks e validações de âncora/versão;
  :Publicar referência + histórico + Gold/Serving\natomicamente ou por protocolo equivalente;
else (não)
  :Preservar estado PROVISORIA/INDEFINIDA\ne fatos válidos independentes da identidade;
endif
:Registrar auditoria e recibo de execução;
stop
@enduml
PUML

plantuml -charset UTF-8 -tpng "$WORK/Jornada_Identidade_Linkage_Classes.puml" "$WORK/Jornada_Resolucao_Identidade_Atividade.puml"

test -s "$WORK/Jornada_Identidade_Linkage_Classes.png"
test -s "$WORK/Jornada_Resolucao_Identidade_Atividade.png"

python3 - "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.md" "$WORK/Anexo_Modelo_Fisico_Jornada_v1.40.md" <<'PY'
from pathlib import Path
import sys
src = Path(sys.argv[1]).read_text(encoding='utf-8')
marker = '## 2. Definição canônica do schema'
insert = '''### 1.1 Diagramas UML normativos incorporados

![Figura 1 - Diagrama de classes UML da estrutura de Identidade/Linkage](Jornada_Identidade_Linkage_Classes.png){width=16.5cm}

![Figura 2 - Diagrama de atividade UML da resolução de identidade](Jornada_Resolucao_Identidade_Atividade.png){width=15cm}

'''
if marker not in src:
    raise SystemExit('Marcador da seção 2 não encontrado no Anexo v1.40')
if '### 1.1 Diagramas UML normativos incorporados' not in src:
    src = src.replace(marker, insert + marker, 1)
Path(sys.argv[2]).write_text(src, encoding='utf-8')
PY

pandoc "$REQ/02_Requisitos_Funcionais_Jornada_v1.1.md" \
  --reference-doc="$REQ/02_Requisitos_Funcionais_Jornada_v1.0.docx" \
  -o "$REQ/02_Requisitos_Funcionais_Jornada_v1.1.docx"

pandoc "$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.1.md" \
  --reference-doc="$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.0.docx" \
  -o "$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.1.docx"

pandoc "$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.md" \
  --reference-doc="$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.0.docx" \
  -o "$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.docx"

(
  cd "$WORK"
  pandoc "Anexo_Modelo_Fisico_Jornada_v1.40.md" \
    --resource-path="$WORK:$DOC" \
    --reference-doc="$DOC/Anexo_Modelo_Fisico_DER_Jornada_v1.39.docx" \
    -o "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.docx"
)

for f in \
  "$REQ/02_Requisitos_Funcionais_Jornada_v1.1.docx" \
  "$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.1.docx" \
  "$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.docx" \
  "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.docx"; do
  unzip -t "$f" >/dev/null
  libreoffice --headless --convert-to pdf --outdir "$(dirname "$f")" "$f" >/dev/null
  test -s "${f%.docx}.pdf"
done

for f in \
  "$REQ/02_Requisitos_Funcionais_Jornada_v1.1.pdf" \
  "$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.1.pdf" \
  "$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.pdf" \
  "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.pdf"; do
  pdfinfo "$f" >/dev/null
done

media_count=$(unzip -l "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.docx" | grep -c 'word/media/' || true)
if [ "$media_count" -lt 2 ]; then
  echo "Anexo DOCX não contém as duas figuras UML incorporadas" >&2
  exit 3
fi

for f in \
  "$REQ/02_Requisitos_Funcionais_Jornada_v1.1.docx" \
  "$REQ/03_Requisitos_Nao_Funcionais_Jornada_v1.1.docx" \
  "$REQ/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.docx" \
  "$DOC/Anexo_Modelo_Fisico_Jornada_v1.40.docx"; do
  pandoc "$f" -t plain -o "$WORK/$(basename "${f%.docx}").txt"
done

if grep -Eiw '\b(SGM|SPE)\b' "$WORK"/*.txt; then
  echo "Referência SGM/SPE encontrada nos artefatos de entrega" >&2
  exit 4
fi
if grep -F '{width=' "$WORK"/*.txt; then
  echo "Atributo de geração vazou para o texto do artefato" >&2
  exit 5
fi

grep -q 'RF-056' "$WORK/02_Requisitos_Funcionais_Jornada_v1.1.txt"
grep -q 'RNF34-C' "$WORK/03_Requisitos_Nao_Funcionais_Jornada_v1.1.txt"
grep -q 'RF-051' "$WORK/05_Matriz_Rastreabilidade_Requisitos_Jornada_v1.1.txt"
grep -q '69 tabelas' "$WORK/Anexo_Modelo_Fisico_Jornada_v1.40.txt"

echo 'Artefatos DOCX/PDF gerados e validados.'

# Identidade progressiva — projeção Serving/BI V1

A view `serving.v_identidade_origem_progressiva` expõe o estado da identidade de origem sem confundir referência técnica inicial, referência canônica e Pessoa consolidada.

Ela contém uma linha por `(sistema_origem_codigo, codigo_pessoa_origem)` e publica separadamente `initial_uuid`, `canonical_uuid`, `estado` e `versao`. `initial_uuid` permanece imutável. `canonical_uuid` só é preenchido quando existe uma referência publicada. Os estados públicos continuam exclusivamente `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`.

## Regra de contagem

`apta_contagem_pessoa_canonica` só é verdadeira quando `estado='REFERENCIA'` e `canonical_uuid` está preenchido. BI não deve contar `initial_uuid` de linhas `PROVISORIA` ou `INDEFINIDA` como Pessoas municipais distintas: esses UUIDs representam identidades técnicas de origem e podem corresponder à mesma Pessoa real.

Para métricas de Pessoas com referência publicada, usar `COUNT(DISTINCT canonical_uuid)` filtrando `apta_contagem_pessoa_canonica=1`. Para métricas operacionais de backlog/qualidade da identidade, contar as linhas por `estado`, preservando a unidade "identidade de origem".

A view não altera fatos, `vinculo_fonte`, Gold, CPF âncora nem autorização de compartilhamento. Também não executa Linkage e não constitui evidência de homologação probabilística. O gate estatístico da issue #31 permanece obrigatório antes de qualquer ativação probabilística.

## Paridade

A projeção existe em SQL Server e PostgreSQL com o mesmo conjunto lógico de colunas. A instalação é idempotente (`CREATE OR ALTER VIEW` / `CREATE OR REPLACE VIEW`) e deve ocorrer após a infraestrutura de identidade progressiva.

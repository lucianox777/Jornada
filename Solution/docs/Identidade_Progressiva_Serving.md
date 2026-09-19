# Identidade progressiva — projeção Serving/BI V1

A view `serving.v_identidade_origem_progressiva` expõe o estado da identidade de origem sem confundir referência técnica inicial, referência canônica e Pessoa consolidada.

Ela contém uma linha por identidade persistente de origem e deve ser interpretada no namespace da Base de Pessoa; `sistema_origem_codigo` é proveniência de uso, não a identidade do namespace e publica separadamente `initial_uuid`, `canonical_uuid`, `estado` e `versao`. `initial_uuid` permanece imutável. `canonical_uuid` só é preenchido quando existe uma referência publicada. Os estados públicos continuam exclusivamente `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`.

## Regra de contagem

`apta_contagem_pessoa_canonica` só é verdadeira quando `estado='REFERENCIA'` e `canonical_uuid` está preenchido. BI não deve contar `initial_uuid` de linhas `PROVISORIA` ou `INDEFINIDA` como Pessoas municipais distintas: esses UUIDs representam identidades técnicas de origem e podem corresponder à mesma Pessoa real.

Para métricas de Pessoas com referência publicada, usar `COUNT(DISTINCT canonical_uuid)` filtrando `apta_contagem_pessoa_canonica=1`. Para métricas operacionais de backlog/qualidade da identidade, contar as linhas por `estado`, preservando a unidade "identidade de origem".

A view não altera fatos, `vinculo_fonte`, Gold, CPF âncora nem autorização de compartilhamento. Também não executa Linkage e não constitui evidência de homologação probabilística. O gate estatístico da issue #31 permanece obrigatório antes de qualquer ativação probabilística.

## QC e BI

A projeção de identidade progressiva deve ser consumida junto das views de qualidade/pendências. Problemas de CPF não podem desaparecer dentro de um estado genérico de Linkage: BI deve distinguir ausência, CPF inválido, conflito determinístico, duplicação de código e junção suspeita por classes/motivos. O CPF em claro não é necessário para essas métricas.

## Persistência corrente

A projeção operacional corrente é SQL Server e sua instalação é idempotente com `CREATE OR ALTER VIEW`, após a infraestrutura de identidade progressiva.

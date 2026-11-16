# DC-V9-CORPUS-100K-ZIP-REF — armazenamento versionado na camada REF

**Estado:** APROVADO PELO PRODUCT OWNER em 15/11/2026. **Escopo:** corpus sintético experimental V9 de 100.000 pessoas-base. **Precedência:** substitui, para esta alternativa aprovada, a exigência de provisionamento de mount POSIX externo da proposta inicial da PR #1567, condicionada à viabilidade física do ZIP versionado e aos gates abaixo. Não altera DT-15, a segregação TEST ou os critérios científicos.

## Contexto e justificativa

O corpus histórico de 30k possui manifesto de proveniência em `Solution/data/reference/v9-synthetic-seed42-30000/manifest.json`, mas seus CSVs/gabarito não estão versionados. O PO aprovou preferir um ZIP físico do corpus de 100k em subpasta de `Solution/data/reference/`, para permitir reuso reprodutível entre execuções sem dependência de volume externo.

## Contrato e condições de implementação

- Caminho preferencial: `Solution/data/reference/v9-synthetic-corpus-100000/`, contendo manifesto com fingerprint/hashes e ZIP com os quatro arquivos físicos: `pessoas_verdade.csv`, `observacoes.csv`, `gabarito.json`, `generation-manifest.json`.
- Medir tamanho real comprimido antes de escolher Git regular ou Git LFS. O limite de 100 MB para objeto Git regular é impeditivo; avaliar também crescimento de histórico, cotas, bandwidth e política de retenção. Não adicionar arquivo grande, provisionar serviço pago ou executar campanha científica 100k sem autorização operacional pertinente.
- A publicação deve ser imutável e verificável: SHA-256 do ZIP e SHA-256/tamanho dos quatro arquivos descompactados, fingerprint e versão do gerador/referências. Rejeitar divergências, path traversal, symlinks, entradas extras, duplicadas e ZIP bomb; extração somente em diretório isolado e temporário.
- PREPARE verifica e disponibiliza snapshot; TRAIN consome bytes verificados, sem regeneração silenciosa. VALIDATION/TEST permanecem separados e TEST selado, sem acesso durante seleção de parâmetros. A CI deve comprovar reuso em execuções independentes a partir do objeto versionado e sua integridade física.
- Caso ZIP não seja viável no Git regular, avaliar Git LFS ou armazenamento alternativo e submeter custo/limitações ao PO antes da mudança de arquitetura. O merge depende de implementação, testes e aceite final; aprovação desta decisão não autoriza promoção de modelo nem DT-15.

## Impactos e rastreabilidade

Altera a estratégia de persistência da PR #1567, sem transformar cache/artifact em REF durável. Requer revisão dos scripts de snapshot/prepare, workflows de calibração e gates; preservar histórico da alternativa POSIX como proposta anterior não implementada. Origem: aprovação explícita do PO nesta conversa, após análise da localização do manifesto histórico 30k. Política: GOV-DOC-01.

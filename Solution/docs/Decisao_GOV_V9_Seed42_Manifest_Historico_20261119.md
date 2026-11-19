# GOV-V9-SEED42-MANIFEST-HISTORICO-20261119

## Contexto e evidências
Após a integração autorizada da PR #1750 (merge `324c55c233e981610b90b886a08ab845deff2e04`), os jobs físicos V9 `44636352542` e `44636352580` passaram a identificar exclusivamente `GenerationHypothesesFingerprint` como divergente na reconstrução seed42/30k. O `CorpusFingerprint` e os demais campos de proveniência não divergiram. A referência congelada `data/reference/v9-synthetic-seed42-30000/manifest.json` conserva `generation_manifest_sha256=2F6ED064113D4E58B01D013B5C0A3ED99DE098FBAE4BA7910D0D62B33168E613`.

A PR #1721 (commit `008bdb183adbe8c4935c730a99a80ef3bf02ef01`) acrescentou `synthetic_population` e `partition_percentages` ao JSON `generation-manifest.json` produzido por `SyntheticCorpusMaterializer`, modificando o SHA dos bytes do manifesto. Essa alteração é causa técnica plausível, **ainda não confirmada por reprodução byte a byte** contra o SHA congelado.

## Decisão técnica proposta e justificativa
Restabelecer o formato anterior do manifesto exclusivamente para a combinação histórica: seed 42, 30.000 pessoas, perfil `clean`, população demográfica, partições 60/20/20 e ausência de overlays experimentais. Remover apenas os dois campos acrescentados pela PR #1721, antes da anotação demográfica. Todos os demais cenários preservam o formato novo com população e percentuais. A proposta visa recuperar a identidade de bytes sem modificar o registro histórico.

## Impactos, precedência e limites
O gate `v9-tf-ref-provenance-gate.py` e seus sete critérios permanecem inalterados e bloqueantes. Não há alteração de SHA esperado, reescrita da referência, substituição de corpus, tolerância a divergências, bypass ou aprovação científica. A compatibilidade só será considerada validada se a geração física reproduzir **exatamente** o SHA histórico e os demais fingerprints e checks passarem. O ajuste do formato não prova, por si, identidade estatística ou ausência de mudanças na atribuição das partições introduzidas na PR #1721.

Precedência: GOV-DOC-01, registro histórico imutável seed42/30k, decisão de diagnóstico `GOV-V9-SEED42-PROVENIENCIA-DIAGNOSTICO-20261119`, restrições de isolamento TRAIN/VALIDATION/TEST. A PR #1749 continua separada e sem autorização de merge.

## Validação e estado
Alteração proposta em branch de PR **Draft**. Sem execução local de .NET/SQL e sem reprodução byte a byte nesta etapa; CI e execução física de master são requisitos de confirmação após integração autorizada. Não executar TRAIN/TEST científico, ativação ou publicação. O merge exige aprovação explícita do Product Owner.

## Nova evidência independente — recuperação histórica física
O workflow de master `V9 frozen seed42 30k historical recovery`, run **44638752085**, job **135963994848**, terminou com sucesso em 2026-11-19. Ele compilou o **gerador histórico congelado**, reconstruiu fisicamente as 30.000 pessoas em SQL descartável e verificou os hashes dos arquivos contra o registro imutável. O teste do manifesto normalizou explicitamente o prefixo de caminho do checkout histórico para o prefixo de referência antes de calcular o SHA; essa normalização é parte da evidência e **não equivale a demonstrar que o gerador atual reproduz o mesmo SHA bruto**. Artefato não nominal: `v9-frozen-seed42-recovery-44638752085`, ID `14585343322`. Resultado do job: `PASS: exact historical physical 30k hashes match frozen V1; no model certification`.

A recuperação confirma que a referência histórica é fisicamente reprodutível com o produtor congelado, **não** que esta PR corrija a geração atual. A alteração proposta deve continuar Draft até existir validação específica do gerador atual, incluindo a identidade de caminhos e o gate integral. Não reutilizar automaticamente o corpus histórico como substituto dos artefatos atuais de campanha.

## Proteção de regressão proposta
Adicionado `Solution/tests/scripts/test_v9_seed42_historical_manifest_shape.py` para verificar estaticamente as nove condições simultâneas de escopo, a remoção de somente dois campos e a preservação dos metadados no formato geral. É um teste de contrato de código, **não** um teste físico de SHA, de partições ou de proveniência. A aprovação deste teste não elimina a exigência de validação física independente.

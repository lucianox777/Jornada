# V9 Gold — replay de integridade da coorte externa (etapa 2 parcial)

**25/10/2026 — código preparatório; NÃO é validação independente concluída.**

`V9GoldHoldoutReplayIntegrity.Verify` valida os bytes de duas materializações declaradas da mesma coorte sintética pré-registrada (seed `20261025001`, perfil `DEMOGRAPHIC_PRIMARY_V1/clean`, 30.000 pessoas). Confere hashes de manifesto, observações, pessoas e gabarito, versões do gerador/regras/RNG, fingerprint das referências, quantidade de pessoas e observações e integridade referencial entre CSVs. Rejeita replay divergente, reutilização byte a byte do CSV TRAIN, overlays de erro fora do perfil e cabeçalhos/identidades inconsistentes. Não exporta linhas nem rótulos.

**Limites de evidência:** igualdade de hashes e replay são necessários, mas não suficientes para provar que houve duas invocações independentes do gerador, que a seed não foi escolhida adaptativamente ou que os parâmetros e fontes foram congelados antes do resultado. A automação física futura deve executar o gerador duas vezes em diretórios novos, fixar commit/versão, parâmetros e fontes por SHA, registrar comandos e recibos antes da avaliação, e vincular o fingerprint à `V9GoldIndependentHoldoutContract` da PR #1192. O gabarito deve permanecer restrito ao avaliador após a execução do Gold.

Esta etapa não reutiliza `TRAIN_SELECTION`, não altera `V9GoldHarnessCommand`, não dispara as seis corridas, não abre TEST/VALIDATION histórica e não modifica ref, Gold operacional, DT-10 canônica ou DT-15. Não confundir `P000...` de seeds distintas com identidade real compartilhada. A issue #1191 acompanha o executor e a perícia posterior de falsos vínculos e falsas criações.

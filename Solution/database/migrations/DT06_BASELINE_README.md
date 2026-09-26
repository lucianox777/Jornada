# DT-06 — disparador de validação SQL do schema

Arquivo documental **não incluído** em `manifest.txt` para acionar `jornada-schema-consolidation-370` sem alterar nenhuma migration aplicada nem a proveniência do checkpoint RC. O workflow roda SQL Server real (instalação nova, upgrade 3.69 e 3.65, replay de ledger, checksum adulterado). O script opcional [test-history-upgrade.sh](../baselines/test-history-upgrade.sh) ensaia um predecessor 3.65 com três migrations já aplicadas e hash registrado antes do upgrade completo; consulte [README DT06](../baselines/README_DT06_Baseline.md).

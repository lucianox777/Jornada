# GOV-V9-SEED42-MANIFEST-1721-20261119 — investigação de proveniência (não homologada)

## Contexto e evidência
Após a integração da PR #1750 (merge `324c55c233e981610b90b886a08ab845deff2e04`), o gate bloqueante `Solution/scripts/v9-tf-ref-provenance-gate.py` identificou exclusivamente `GenerationHypothesesFingerprint` como divergente. Evidências independentes: execução V3 física #44636352542, job 135955839332, e execução M/U TF0 #44636352580, job 135955756420. Ambas falharam no mesmo campo, sem desativar verificações.

O registro histórico `Solution/data/reference/v9-synthetic-seed42-30000/manifest.json` exige `generation_manifest_sha256=2F6ED064113D4E58B01D013B5C0A3ED99DE098FBAE4BA7910D0D62B33168E613`. O bootstrap calcula o SHA-256 dos bytes completos de `generation-manifest.json`, não um hash semântico normalizado.

O commit `008bdb183adbe8c4935c730a99a80ef3bf02ef01` (PR #1721, 18/11/2026) acrescentou `synthetic_population` e `partition_percentages` à serialização em `SyntheticCorpusMaterializer.cs`; também introduziu percentuais configuráveis em `SyntheticCorpusModels.cs` e `SyntheticCorpusGenerator.cs`. O padrão permanece 60/20/20. Alterar os campos do manifesto necessariamente altera seus bytes e pode alterar seu SHA mesmo com saídas sintéticas idênticas.

## Conclusão técnica e limites
**Hipótese causal forte, ainda não comprovada byte a byte:** a evolução do manifesto em #1721 explica o mismatch observado. O diagnóstico indica que os demais campos do bootstrap passaram, mas isso não certifica a geração, o TRAIN nem a V3.

**Não aprovada como correção:** não remover campos globalmente, não alterar o SHA histórico, não flexibilizar o gate, não substituir o corpus ou a referência e não promover qualquer candidato. Antes de propor mudança executável, reproduzir fisicamente o manifesto anterior à #1721 e comprovar que seu SHA coincide exatamente com o registro histórico, com testes de regressão para o comportamento configurável atual.

## Impactos, precedência e rastreabilidade
Esta investigação complementa `GOV-V9-SEED42-PROVENIENCIA-DIAGNOSTICO-20261119` e a PR #1750; não substitui o registro imutável seed42/30k, as exigências de segregação TRAIN/VALIDATION/TEST ou os gates científicos. PR #1749 continua independente e aberta. Não há autorização para merge desta proposta documental nem para TRAIN/TEST confirmatório, publicação ou ativação. Status: **INVESTIGAÇÃO DOCUMENTADA EM PR; CAUSA NÃO CONFIRMADA POR SHA HISTÓRICO**.

Referências: PRs #1721, #1750; workflows #44636352542 e #44636352580; `Solution/scripts/v9-tf-pin-source.py`; `Solution/scripts/v9-tf-ref-provenance-gate.py`; `Solution/src/Jornada.Linkage.SyntheticCorpus/SyntheticCorpusMaterializer.cs`. GOV-DOC-01.

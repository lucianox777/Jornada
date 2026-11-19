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

## Consolidação da CI pós-merge #1750 (HEAD `324c55c2`)
Consulta posterior confirmou que os seis workflows físicos terminaram com falha: #44636352572 (probe seed42), #44636352517 (M/U seed42), #44636352573 (broad TRAIN), #44636352544 (sobrenomes), #44636352542 (V3 direto), #44636352580 (M/U TF0). Logs adicionais dos jobs 135955870589, 135955754472, 135955755460 e 135956492899 reiteram **exclusivamente** `fields=GenerationHypothesesFingerprint`.

No workflow broad TRAIN #44636352573, os quatro jobs seed43/seed57 passaram, e os dois jobs seed42 falharam. Essa distinção é consistente com uma incompatibilidade **específica da referência histórica seed42**; não demonstra sucesso científico da V3, pois são regressões TRAIN não confirmatórias. A CI rápida #44636352515 terminou com sucesso; a CI principal #44636352583 permanecia em andamento na consulta.

**Critério de avanço:** reproduzir o manifesto anterior a #1721 em ambiente físico descartável, verificar SHA-256 exato do manifesto e os hashes de todos os três artefatos; preservar parâmetros configuráveis e testes dos demais seeds. Se o SHA não coincidir, a hipótese deve ser rejeitada ou refinada, sem substituir a referência histórica. Nenhum bypass é aceitável.

## Procedimento de reprodução histórica já existente no repositório
A investigação identificou o workflow manual `.github/workflows/v9-frozen-seed42-recovery.yml`, que valida o SHA histórico imutável `945f2e7efbfa217fbf333aa1fb705e15973be9f2`, faz checkout do gerador congelado, reconstrói o corpus em SQL descartável **sem calibração** e verifica os hashes físicos dos três artefatos. Há um detalhe decisivo: o manifesto histórico inclui um caminho absoluto do runner, portanto o workflow normaliza **uma única ocorrência** do prefixo de checkout `historical/Solution/` para `Solution/` antes de comparar o SHA-256 registrado. Essa normalização está expressamente delimitada no workflow e não modifica o registro histórico.

**Consequência:** não é suficiente retirar os dois novos campos do manifesto atual e comparar seu hash sem controlar o caminho absoluto histórico. O critério de confirmação exige comparar o manifesto historicamente reproduzido pelo produtor congelado e sua normalização específica, além dos três hashes de saída. O workflow é manual (`workflow_dispatch`) e não foi disparado nesta investigação; nenhuma execução ou aprovação física nova é alegada. A estratégia de correção do gerador atual permanece em aberto até a comparação dos resultados.

## Encerramento da CI principal após #1750
A execução `jornada-ci` #44636352583 concluiu todos os jobs executados com **success**, inclusive `integration-sql`, `security-codeql`, `unit-runtime`, `harness-smoke`, `e2e-console`, `e2e-private-sql`, `dt10-evidence`, `security-analysis`, `e2e` e `unit`. Os jobs `bronze-restore-drill`, `scale-harness`, `release-promotion`, `rc-evidence` e `rc-publish` foram **skipped**, não aprovados. A CI rápida #44636352515 também passou. Essa evidência de engenharia **não** resolve as seis falhas dos workflows físicos V9, não autentica o SHA histórico e não certifica V3. A execução manual de recuperação congelada segue pendente.

## Comparação direta com o produtor congelado
Foi lido diretamente no GitHub o arquivo `Solution/src/Jornada.Linkage.SyntheticCorpus/SyntheticCorpusMaterializer.cs` do commit histórico `945f2e7efbfa217fbf333aa1fb705e15973be9f2` e comparado com o mesmo arquivo na `master` (`324c55c2`). **Constatação no código:** no manifesto histórico, `seed` é imediatamente seguido por `input_fingerprint_sha256`; no manifesto atual, foram inseridos entre eles `synthetic_population` e `partition_percentages` (train, validation, test). Ambas as versões serializam JSON indentado, com quebra de linha final, e acrescentam o bloco demográfico quando pertinente. A divergência de esquema no ponto da serialização está assim **confirmada por comparação direta de fonte**; a atribuição exclusiva do SHA divergente a esses campos e a reprodução do SHA histórico **continuam sem confirmação física**.

Consulta de execuções `workflow_dispatch` no repositório (primeira página, 100 resultados de 188) não encontrou execução do workflow de recuperação congelada; ausência nessa página não demonstra inexistência histórica. Nenhum dispatch foi iniciado nesta etapa.

## Regressão automatizada proposta (sem reprodução física)
Adicionado em `Solution/tests/scripts/test_v9_tf_ref_provenance_gate.py` o teste `test_metadata_only_manifest_change_remains_blocked`: demonstra, com JSON sintético não nominal, que acrescentar metadados de população/partição modifica o hash dos bytes, mesmo sem modificar as saídas; verifica que o gate rejeita a mudança tanto com o pin histórico quanto com um pin atualizado para o novo bootstrap. **Este teste protege a invariância do gate, mas não reproduz nem autentica o SHA histórico real.** Integração condicionada aos checks da PR, sem autorização de merge.

## Valores conferidos diretamente no registro imutável
A leitura do arquivo de referência em `master` confirmou `evidence.original_source_sha=945f2e7efbfa217fbf333aa1fb705e15973be9f2`, `generation_manifest_sha256=2F6ED064113D4E58B01D013B5C0A3ED99DE098FBAE4BA7910D0D62B33168E613`, `pessoas_verdade.csv=75171EEDD0BAB7C4D19C7CB37DC620EE0E249199A406F4C8A9DFAC51839ADD13`, `observacoes.csv=28BBB9F4C63C97C353E7F29CDBC77091A4773B2F4C596E46103EDADD93635D17` e `gabarito.json=982658206DF534E8F129E2C3C803E2DA9679EC96D385669CC054D6CA890754D2`. Esses são os valores de comparação, **não resultados de nova execução**.

Na revisão do HEAD `815ae1fa34547f7d438402b793f21aa4ee51eca7`, a PR #1751 seguia Draft e bloqueada; os workflows #44638037872 (`full-schema RunAsync` não publicante) e #44638037864 (`jornada-pr-required-fast`) estavam em andamento/pendentes. Nenhum resultado deve ser inferido antes de sua conclusão.

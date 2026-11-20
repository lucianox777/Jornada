# GOV-SYNTH-IDENTIDADE-MANTER-20261120 — Manutenção do contrato atual de sementes e proveniência

**Status:** decisão aprovada pelo Product Owner em 20/11/2026; sem alteração de código, sementes, artefatos ou contratos científicos. **Integração documental à master pendente de PR e gates.**

## Contexto e deliberação
O PO discutiu a hipótese de introduzir um novo arquivo contendo OID/identificador único, hash e eventual derivação de sementes para gerações sintéticas, bem como a possibilidade de adotar sementes distintas por TRAIN, VALIDATION e TEST. Após examinar a existência do `generation-manifest.json`, fingerprints determinísticos e hashes já produzidos, o PO decidiu explicitamente **manter tudo como estava e não realizar alterações nessa parte do código**. A discussão não aprovou substituir sementes atuais por 42/43/44, remover o manifesto existente, nem criar novo arquivo ou mecanismo de OID.

## Decisão vigente
1. Preservar implementação atual de geração sintética, seleção/registro de sementes, identificadores, fingerprints e manifesto.
2. Não criar arquivo adicional de OID/hash, não alterar derivação de sementes e não modificar a separação de coortes por esta deliberação.
3. Continuar outras atividades já autorizadas conforme plano e gates existentes, sem interpretar esta decisão como autorização de TRAIN físico, TEST científico, desbloqueio de LOCK ou ativação V9.

## Justificativa
O gerador já computa `input_fingerprint_sha256` a partir de semente, versões e referências (`SyntheticCorpusInputIdentity.cs`), e materializa `generation-manifest.json` com semente, parâmetros e hashes das saídas (`SyntheticCorpusMaterializer.cs`). Os controles `V9GoldIndependentHoldoutProvenance.cs` e `V9GoldHoldoutReplayIntegrity.cs` conferem procedência, integridade e replay. Não foi demonstrada necessidade de nova camada de identificação. Sementes diferentes, isoladamente, não certificam independência estatística.

## Impactos e precedência
**Impacto funcional/técnico:** nenhum; decisão de não mudança. Preservar compatibilidade dos manifestos históricos, fingerprints, contratos de coorte e gates existentes. **Precedência:** não revoga decisões anteriores de congelamento científico, holdout independente, isolamento de TEST, compatibilidade histórica do manifesto seed42 nem GOV-DOC-01. Em caso de conflito, prevalecem os LOCKs e protocolos científicos já aprovados; qualquer mudança futura exige nova aprovação e documentação.

## Rastreabilidade
- Aprovação expressa do PO na conversa do projeto de 20/11/2026: “minha decisão mantém do jeito que tava, não fazer alteração com relação a isso. Tá decidido. E pode continuar com as outras alterações que você tava fazendo.”
- Código: `Solution/src/Jornada.Linkage.SyntheticCorpus/SyntheticCorpusInputIdentity.cs`, `SyntheticCorpusMaterializer.cs`; `Solution/src/Jornada.Linkage.Runner/V9GoldIndependentHoldoutProvenance.cs`, `V9GoldHoldoutReplayIntegrity.cs`.
- Decisões relacionadas: `Decisao_GOV_V9_Seed42_Manifest_Historico_20261119.md`, `Decisao_GOV_V9_TEST_Cohort_20pct_20261118.md`, `Plano_Desenvolvimento.md`.
- Índice: `Indice_Decisoes_Vigentes.md`.

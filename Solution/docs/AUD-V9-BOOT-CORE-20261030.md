# AUD-V9-BOOT-CORE-20261030 — Confronto inicial com código (não certificação)

Referência normativa: [DC-V9-BOOT-CORE-20261030](DC-V9-BOOT-CORE-20261030.md).

## Achados verificáveis na master 9cf00da8ee77d2451d73d166620151a1c8229134

1. **Busca de blocking já existe** em `V9NestedCalibrationCoordinator.RunAsync`: usa `DeclaredSearchSpace` ou `V9TrainBlockingCandidateDiscovery.Discover`, combina com `BaselineRuleSets`, e explora vizinhança adicionando/removendo passes no método `Refine`. Isso deve ser preservado, não reimplementado.
2. **Separação interna de treinamento já existe:** `V9InternalTrainingSplit.Create` divide TRAIN em ajuste e seleção; a posição rotulada VALIDATION pelo adaptador físico é preenchida com `TRAIN_SELECTION`. É necessário verificar ponta a ponta o isolamento do VALIDATION externo e TEST.
3. **Limitação funcional concreta na busca aninhada:** `V9NestedCalibrationCoordinator.OptionId(decimal alpha, decimal threshold)` gera IDs terminados em `_TF0`. A construção inicial sobrescreve `option.OptionId` com esse identificador, e `Refine` cria `new V9FsAdaptiveOptionConfiguration(...)` apenas com alpha e threshold, voltando aos valores default de TF. Assim, **a busca aninhada não preserva opções TF positivas durante o refinamento**, e opções distintas podem colidir em identidade. Não afirmar que o bootstrap investiga TF de ponta a ponta enquanto isso persistir.
4. **Contrato do adaptador limitado:** `V9FsAdaptiveOptionConfiguration` tem `TfWeight`, `TfSide` e parâmetros de suporte, mas não seleciona unidade ExactFirst/ExactLast. `ExportFittedSnapshot` usa `ExportForSnapshot(... ExactFirst)` e `V9SnapshotFirstNameTf`. O suporte a ExactLast no `FrozenIbgeNominalTfV9` é explicitamente preview/proxy, não modelo operacional.
5. **O catálogo de blocking não é automaticamente um catálogo de comparadores nominais:** a existência de `DeclaredSearchSpace` e `CanonicalCatalogue` comprova pesquisa de passes, não comprova pesquisa completa de alternativas nominais.

## Correções prioritárias e critérios de teste

**P0 — preservar identidade da opção:** separar ID estável/fingerprint de configuração de identificador legível de alpha/threshold; `Refine` deve derivar a opção existente com `with` para preservar TF, lado, suporte e políticas, mudando apenas parâmetros explicitamente pesquisados. Detectar colisões e falhar fechado. Testar duas opções de mesmo alpha/threshold e TF distinto, com resultados e snapshots independentes.

**P0 — pesquisa nominal do núcleo:** versionar catálogo de comparadores/unidades nominais e integrá-lo ao gerador de alternativas; demonstrar opções elegíveis com/sem ExactFirst, ExactLast e TF. Não forçar ExactLast com proxy IBGE como TRAIN U. Não violar evidência mínima.

**P0 — reprodução:** garantir que a semântica selecionada pelo calibrador seja serializada e interpretada pelo runtime, com fingerprints do catálogo, frame e modelo.

**P1 — pesquisa e seleção:** verificar fronteira de Pareto, custo/recall de blocking, motivos de inelegibilidade, limite de orçamento sem truncamento silencioso, determinismo de ordem e isolamento TRAIN/VALIDATION/TEST.

## Limites da auditoria

Esta é uma revisão estática parcial de código. **Nenhuma correção funcional foi feita por este documento**, nenhum teste físico foi executado e não há afirmação de conformidade total. Não disparar a prova 30k até correções e revisão do SHA consolidado.

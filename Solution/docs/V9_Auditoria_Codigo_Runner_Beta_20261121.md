# Auditoria estática do código Runner V9 para beta — 21/11/2026

**Escopo:** leitura do código da master (busca GitHub indexada no commit `9c69cc6f8556a2057ad5255e77834f394302845a`); não executa build, testes ou SQL. Evidência estática não certifica comportamento físico.

| Item | Evidência de código | Resultado | Responsável |
|---|---|---|---|
| Seleção explícita MASTER | `Program.cs` lê `Linkage:SelectedModelVersion` e `Linkage:V9SelectionFile`, congela configuração; seleção V9 incompatível ou ausente falha | Implementado no startup | #1488 |
| Artefato V9 íntegro | `V9ConfiguredArtifactLoader.LoadAsync` valida JSON de seleção, tamanho, symlinks, nome, SHA e snapshot operacional | Implementado | #1488 |
| Contrato V9 em memória | `V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot` valida round-trip e cria scorer; `V9OperationalRunBoundary` transporta runtime | Implementado, uso não publicador | #1488/#1584 |
| Scorer/agrupamento V9 | `V9OperationalPinnedCohortRuntime.ReplayTest` chama `V9FrozenSourceResolveReplay`; grafo físico confrontado em `V9OperationalCandidateGraphParityAudit` | Implementado para coorte de diagnóstico; limite de 128 observações | #1584 |
| Execução V9 em lote produtivo | `ProbabilisticLinkageBatchRunner.RunV9Async` chama `EnsureProductiveCertified()` e lança erro; `RunAsync` rejeita V9 antes do lock | **Não implementado/habilitado** | #1584 |
| Startup V9 | `Program.cs` verifica artefato, mas `EnsureProductiveCertified()` lança sempre `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED` | **Bloqueado intencionalmente** | #1584 |
| Compatibilidade legada | `SqlProbabilisticIdentityLinkage.GetActiveModelAsync` usa `identidade.modelo_linkage` ATIVO; `Program.cs` faz pin do modelo legado selecionado | Caminho existente; regressão integral requer teste | #1584 |
| Importação/ativação administrativa | Configuração de arquivo existe; não foi comprovada UI, gestão de versões, fluxo de aprovação/rollback do MASTER | Parcial / não demonstrado | #1488 |
| Paridade física e publicação | `RehearseV9PinnedFileNonPublishingAsync` e `V9ReadOnlyTestBatchRunner` geram recibos em JornadaHarness; ambos explicitamente não certificam pipeline produtivo | Diagnóstico disponível, E2E produtivo pendente | #1584/#1349 |

## Conclusão
Não reimplementar scorer, fronteira imutável ou mecanismo básico de seleção. O bloqueio real para beta **com V9** é integrar o runtime V9 já validado à execução física de lotes, com grafo completo, decisão e persistência/publicação sob gates; demonstrar regressão V8 e paridade determinística/física. Separar conclusão técnica de autorização DT-15. O beta **com V8** tem critério de aceitação independente, não demonstrado por esta auditoria.

## Próximas provas objetivas
1. #1488: teste E2E de configuração MASTER por arquivo SHA-pinned, versões incompatíveis, rollback de seleção, sem ativação automática.
2. #1584: implementar o caminho `RunV9Async` com runtime tipado sem fallback; testes físicos de blocking, TF4/81, agrupamento, lotes, falhas e persistência; gates de publicação/rollback.
3. #1349: ensaio multiparâmetros em escala no SHA exato e recibos mensuráveis; não confundir com V8 SCALE.
4. #946/#1089: confirmar compatibilidade do modelo exportado e regressão estatística, sem duplicar executor.
5. #1575: não encerrar antes de confirmar a anotação de retrocompatibilidade do workflow legado e a transferência de todos os requisitos vigentes.

**Referências:** `Solution/src/Jornada.Linkage.Runner/{Program.cs,V9ConfiguredArtifactLoader.cs,V9OperationalRunBoundary.cs,ProbabilisticLinkageBatchRunner.cs,SqlProbabilisticIdentityLinkage.cs,V9ReadOnlyTestBatchRunner.cs}`; `Solution/src/Jornada.Linkage.Core/V9OperationalPinnedCohortRuntime.cs`; `Solution/docs/V9_Auditoria_Integracao_Runner_Beta_20261118.md`; PRs #1147, #1491, #1655, #1822; issues #946, #1089, #1488, #1584, #1349, #1478, #1575.

# GOV-V9-ETAPA3-ADAPTADOR-RUNASYNC-FOCO-20261123 — Ordem final aprovada pelo PO

**Autoridade:** Product Owner, 23/11/2026. **Estado:** DECIDIDO / EXECUÇÃO INICIADA / ADAPTADOR AINDA NÃO IMPLEMENTADO NESTE COMMIT. **Referências:** GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122, #1584, #1488, #1851, PR #1920, GOV-DOC-01.

## Contexto, justificativa e precedência
A etapa 0 da campanha #38 está concluída conforme declaração do PO (14/14 ZIPs com SHA-256 verificados em Git Bash, cópia sob sua guarda, manifesto local COMPLETE 14/14; recibo posterior é complemento de auditoria, sem NAS #1830). A etapa 1 de arestas operacionais foi integrada na master (#1911). O ensaio V9 TEST_ONLY grava `linkage_resultado` sem publicar Gold, sujeito a restrições de modelo, banco e volume. A PR #1920 acumulou provas de conferência sem implementar o adaptador. O PO determinou **prioridade exclusiva** na entrega funcional do adaptador V9 e que esta ordem final substitui as versões anteriores, prevalecendo operacionalmente sobre o trabalho paralelo anterior durante a etapa 3. Não altera os bloqueios científicos e produtivos.

## Mandato vinculante de foco
1. **Não adicionar commits à PR #1920.** Abrir PR exclusiva, derivada da master, na primeira sessão; somente código do adaptador, testes diretamente necessários, correções relacionadas, esta documentação e evidências pertencem à frente até aceite **explícito** do PO. CI verde não encerra foco.
2. Reutilizar `V9ConfiguredArtifactLoader`, conferir SHA do único vencedor, arestas operacionais, score V9 e persistência `linkage_resultado` por lotes. Recompor Gold de pessoas com estado, run e contagens; publicação atômica com rollback e sem resultado parcial.
3. Em DEV descartável e invocação **explícita**, remover restrições artificiais do ensaio `MODEL_VALIDATION`, teto 512 e banco `JornadaHarness`. FULL e INCREMENTAL com nova seleção de pendentes; ON_DEMAND somente sem trabalho adicional. Reusar isolamento SQL por tentativa, identificador de execução, propriedade comprovada e limpeza restrita. Não modificar a integração normal em `Program.cs`/`LinkageModelPolicy` (etapa 6).
4. Preservar identidade: PROVISORIA/REFERENCIA/INDEFINIDA, âncora CPF e CPF_COMPARTILHADO_SUSPEITO, INV-LINK-004, zona cinzenta PENDENTE_PROBABILISTICO, pendentes jamais destino.
5. Testar, em SQL descartável: caminho entrada→decisões→Gold→contagens, falhas intermediárias/rollback, idempotência, reexecução, concorrência, seleção inválida, integridade referencial e igualdade com **oráculo existente** no mesmo modelo/coorte/arestas/decisões. Não criar/ampliar oráculo. Divergência na saída publicada bloqueia aceite; lacunas de equivalência integral ficam explicitadas para etapas seguintes.
6. Checks no HEAD e relatório na PR com diff/testes/pendências da etapa 4. Merge e saída do foco dependem de aceite explícito do PO.

## Segurança, limites e impacto
Risco de segurança real: registrar na issue #1851, bloquear execução/aceite/merge afetado, aguardar PO; única exceção de foco é correção mínima se master ficar vermelha. Máximo três correções por mesma causa antes de reavaliação pelo PO. Sem mudanças em #1352, #1356, #378, #1736, default/hashes, LOCKED ou DT-15. Sem TRAIN, TEST, 10k/100k, HML/PROD, owner-only dispatch, NAS, dado real, PII ou ativação. Evitar exceção completa, senha/dado em logs e segredos em argumentos. Actions fixadas por SHA; sem reescrever histórico ou inventar PASS.

**Marcadores vigentes:** `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`; `E2E_NUCLEO_PASS=false`; `E2E_INTEGRAL_PASS=false`. “Adaptador DEV controlado implementado e validado” NÃO significa “Runner V9 operacional certificado”.

**Rastreabilidade:** a PR específica e a issue #1851 terão HEAD, progresso e próximos passos. Primeiro checkpoint desta sessão pode informar SEM AVANÇO se nenhum commit de código foi conseguido; abertura documental da PR não é entrega funcional. O índice vigente é sincronizado na mesma PR, sujeito a CI e integração protegida.

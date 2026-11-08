# GOV-V9-DEBT-P95-LOCK-20261108 — preservar comportamento atual e registrar dívida técnica

**Decisão do Product Owner:** 08/11/2026 (America/Sao_Paulo), nesta conversa. **Situação:** APROVADO O ADIAMENTO; **NÃO** aprovada qualquer remoção do P95 ou novo critério de sucesso de LOCK. **Acompanhamento:** [issue #1341](https://github.com/lucianox777/Jornada/issues/1341). **Implementação da decisão:** sem mudança de código/planos nesta rodada; documentação proposta na PR #1318 e pendente de integração na `master`.

**Precedência:** complementa [DC-V9-PAR-DET-20261107](Decisao_V9_Paralelismo_Determinismo_20261107.md) e as regras operacionais da [GOV-OPS-V9-FINAL-20261108](Decisao_V9_Mandato_Final_Unico_20261108.md). Supera somente a expectativa de tomar agora as duas decisões novas sobre (i) retirar P95 dos gates científicos e (ii) ampliar o critério de LOCK; não modifica os contratos vigentes, os gates já aprovados ou o mandato de fila única. Segue [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Contexto técnico verificado na master

Fotografia de 08/11/2026: `master` `ec9718cf6dfa6584b682a3a8fe6b54df68ced811`. Esta referência é contexto, não liberação de CI neste SHA.

- O plano **histórico específico** `data/calibration/v9/v2-one-mf-frozen/plan.json` tem `MinimumGlobalRecall=0.95`, `MaximumAcceptedFdrUpper=0.01`, `MinimumIndependentIdentityGroups=1000`, `MaximumP95Milliseconds=60000`; `Budget.MaximumObservedAcceptedFdrPoint=0.01` e `Budget.MaximumBlockingFalseDegreeP99=100`. São valores do plano, **não constantes universais**.
- `V9BootstrapPareto2D` conserva duas propostas: `ExploratorySuggestedOptionId` descritiva (Recall × FDR observado, sem gate P95 ou certificação) e `InitialSuggestedOptionId` condicional/operacional (inclusive P95 físico válido e demais critérios). `V9NestedCalibrationCli` tem `SelectScientificWinner` sem latência e `SelectConditionalWinner` com latência. A escolha científica e seu digest não devem depender do host/tempo.
- `V9CalibrationFreezeService.FreezeAsync` ainda exige opção operacionalmente elegível; `CompleteIndependentTestAsync` ainda exige `PhysicalP95Milliseconds <= MaximumP95Milliseconds` para `Passed`. Portanto, a separação é **parcial**, e a latência pode restringir congelamento/aprovação de TEST apesar da proposta científica independente.
- Há infraestrutura de congelamento e reserva de TEST com SHA, coorte nova, alpha e cap pré-declarados, bound condicional por identidade-base e atestados de amostragem/medição. Os gates hoje não implementam, como requisitos independentes completos, toda a discussão adicional de B-cubed, fragmentação/fusão, comparabilidade histórica V2 e reprodutibilidade na aceitação científica. O limite CP condicional não certifica automaticamente IID/FDR.

## Decisão e justificativa

O PO opta **por continuar trabalhando com o comportamento atual** e registrar as possíveis melhorias como **dívida técnica rastreada**, evitando mudança de critério no meio da implementação multiparâmetro. Isso **não significa** que o PO aprovou as duas recomendações anteriores, dispensou P95, autorizou um LOCK científico ou considerou a campanha independente validada.

Duas pendências acompanham a [issue #1341](https://github.com/lucianox777/Jornada/issues/1341):

1. **P95 — separação final pendente.** Reavaliar futuramente se P95 permanece em `FreezeAsync` e `CompleteIndependentTestAsync` como elegibilidade, ou se passa a ser somente limite de autorização/segurança operacional. Até uma nova decisão, manter verificação de 60.000 ms onde o plano a declara, exigência de medição/atestado e demais gates de recurso.
2. **Critério de sucesso do LOCK científico independente — definição adicional pendente.** Antes de campanha científica nova, decidir explicitamente protocolos, limites quantitativos e papel de B-cubed, fusões, fragmentação, comparabilidade V2/V3/misturas e reprodutibilidade; manter critérios já implementados e não inventar limiares após observar os resultados.

## Impactos, dependências e não-autorizações

- **Não bloqueia** desenvolvimento, PRs, testes técnicos e análise das frentes V9, desde que observados os gates existentes. **Não modifica** `plan.json`, modelo, seleção, FDR, classificação, CI, dados ou snapshots.
- **Não libera** merges com master vermelha; os checks rápidos da PR e CI completa/30k técnico no SHA master integrado permanecem obrigatórios segundo a topologia vigente. A falha anterior de segurança do `docker exec` não é transitória e não é dispensada por esta dívida.
- **Não autoriza** iniciar campanha científica independente com seed inédita sem protocolo e autorização pertinentes; não abre TEST/Gold nem aprova LOCK científico, DT-15, HML/Produção ou ativação. O 30k seed42 sintético é regressão técnica, **não** certificação de FDR.
- Em futura reconsideração, o PO deverá aprovar a decisão estatística e/ou operacional antes de alterar os critérios; documentar efeitos nos envelopes/versões de snapshots, compatibilidade, testes, evidência física, precedência e índices. Fechar a issue somente após integração e prova correspondentes, não por declaração de intenção.

## Rastreabilidade

- [Issue de dívida técnica #1341](https://github.com/lucianox777/Jornada/issues/1341) — itens, critérios de encerramento e histórico posterior.
- `src/Jornada.Linkage.Parameters.Worker/V9BootstrapPareto2D.cs` — duas sugestões.
- `src/Jornada.Linkage.Parameters.Worker/V9NestedCalibrationCli.cs` — proposta científica e condicional.
- `src/Jornada.Linkage.Parameters.Worker/V9CalibrationFreezeService.cs` e `V9CalibrationSnapshotEnvelope.cs` — freeze/TEST e P95.
- [Pareto 2D](V9_Pareto_2D_Integracao_20261019.md) e [congelamento/TEST](V9_Congelamento_Local_TEST_Independente.md) — sem inferir certificação ou autorização de produção.
- [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md) — entrada para esta decisão.

**Status documental:** registro proposto na branch da **PR #1318**. Enquanto não integrado à `master`, GOV-DOC-01 permanece **pendente de integração**, e a issue aberta mantém a rastreabilidade da decisão do PO.

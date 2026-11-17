# GOV-V9-PRIORIDADE-PARIDADE-TEST-E2E-20261117

**Autoridade:** Product Owner, 17/11/2026. **Estado:** prioridade aprovada; implementação e execução pendentes. **Política:** GOV-DOC-01.

## Decisão e justificativa
Priorizar imediatamente a conclusão da paridade entre o vencedor V9 em arquivo operacional imutável e o mecanismo real de execução TEST/Runner, preservando isolamento, modelo congelado, integridade, reserva global inédita do TEST, ausência de publicação e compatibilidade legada. Depois de provas físicas e CI aprovadas no SHA exato, executar o workflow científico integral do calibrador (ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST condicionado a vencedor → CLOSEOUT) pela master.

## Contexto e precedência
O workflow canônico `.github/workflows/v9-standard-linear-100k.yml` já contém a cadeia manual; a presença dos jobs não comprova paridade ou execução E2E concluída. A PR #1655 possui prova física limitada de RunAsync em coorte sintética descartável, não certificação produtiva 100k. Os bloqueios explícitos ainda presentes no workflow legado `.github/workflows/v9-v3-four-name-100k.yml` não podem ser removidos por simples substituição de flags. Esta prioridade complementa GOV-V9-RUNNER-ARTEFATO-ARQUIVO-FONTE-UNICA e GOV-V9-GRAFO-OPERACIONAL-INTEGRAL-COORTE; não revoga os gates científicos de segurança.

## Aceite e impactos
(1) Adaptador produtivo que consuma exclusivamente o arquivo operacional validado, sem fallback para parâmetros científicos SQL; (2) prova física independente de paridade de decisões e grafo completo na mesma coorte, com falha fechada e nenhuma publicação; (3) teste de regressão e CI obrigatória verde no SHA exato; (4) verificação do estado da master e aprovação expressa de preflight de segurança para execução manual; (5) recibos auditáveis para cada fase, distinguindo sem finalistas, falha técnica e vencedor elegível. Não executar campanha cara nem consumir TEST antes desses gates; não afirmar certificação antecipada.

## Rastreabilidade
Pedido do PO em conversa de 17/11/2026: implementar paridade como prioridade e executar workflow integral. Dependências: PR #1655; issues #1488, #1478, #1584; workflows `v9-standard-linear-100k.yml` e `v9-v3-four-name-100k.yml`. Documento criado em branch de trabalho, ainda não vigente na master.

## Retificação expressa da precedência pelo PO — 17/11/2026

O PO esclareceu que o objetivo prioritário **não é certificar todo o Runner produtivo antes de concluir o calibrador**. A ordem vinculante passa a ser:

1. **P0 — concluir a PR #1655**, sem ampliar seu escopo nem relaxar verificações obrigatórias.
2. **P0 — desacoplar calibrador e seletor:** o calibrador entrega vencedor como pacote/arquivo imutável; o seletor consome exclusivamente esse pacote para parâmetros científicos V9, sem leitura/gravação de parâmetros científicos SQL como interface entre ambos. Preservar SQL operacional, metadados, compatibilidade legada e rollback; remoção física de colunas requer inventário e aprovação específica.
3. **P0 — concluir e executar o workflow científico E2E** ADMISSION → PREPARE → TRAIN → AUDIT → VALIDATION → TEST quando houver vencedor → CLOSEOUT, com evidências e SHA fixado.
4. **P1 — implementar somente a paridade física necessária para o TEST científico E2E**. Certificação produtiva geral do Runner não é pré-requisito automático: qualquer dependência deve ser demonstrada por contrato/teste específico.
5. **P2 — melhorias não bloqueantes** de robustez ficam fora do caminho crítico.

**Justificativa:** evitar trabalho paralelo e bloqueio circular entre certificação produtiva ampla e validação científica do calibrador. **Segurança:** preservar reserva one-use, isolamento, gates de TEST, ausência de publicação e falha fechada; nenhuma permissão de bypass. **Impacto:** substitui a exigência genérica de “adaptador produtivo” do item (1) de Aceite acima pelo contrato mínimo comprovável necessário ao TEST científico; demais controles de integridade e rastreabilidade permanecem. **Precedência:** esta retificação expressa do PO prevalece sobre a ordem de implementação descrita anteriormente neste documento. **Rastreabilidade:** decisão do PO na conversa de 17/11/2026; PR #1655; GOV-V9-DESATIVACAO-PARAMETROS-CIENTIFICOS-SQL-20261117; issues #1488/#1478/#1584.

## Auditoria do caminho crítico no workflow canônico — 17/11/2026

Inspeção do arquivo da `master` `.github/workflows/v9-standard-linear-100k.yml` confirma que o fluxo científico canônico já é `workflow_dispatch` estritamente manual, com `expected_sha` e `security_preflight_approved`; o TEST depende de `validation-100k.outputs.has_winner == 'true'` e o closeout contempla o caso sem vencedor. O job TEST chama `Jornada.Linkage.Parameters.Worker --v9-independent-test` sobre **um único** `<sha>.operational.json`, verificando ausência de refit, ausência de leitura do plano TRAIN/resultado VALIDATION e ausência de publicação. Logo, não é correto tratar a certificação produtiva geral do `Jornada.Linkage.Runner` como bloqueio automático do workflow canônico.

**Lacuna a comprovar antes da execução:** o contrato TEST ainda afirma `operationalModelContract == JORNADA_V9_OPERATIONAL_SQL_MODEL_V1` e `sameOperationalJsonBytesAsProductionSideSqlPayload == true`; é preciso auditar se esses campos são apenas atestados de compatibilidade do pacote imutável ou se há dependência real de parâmetros científicos SQL na interface calibrador–seletor. A conclusão requer inspeção da implementação do Worker e testes físicos, não inferência a partir do nome do campo. `test_database` externo continua expressamente bloqueado e não deve ser habilitado para contornar esse ponto.

**Estado das verificações da PR #1655 no SHA `39548a2028b0a53a3083d78b3a8b4a05d797e069` no momento da consulta:** Gold, RunAsync e SQL smoke concluídos com sucesso; `jornada-pr-required-fast` ainda em andamento (run 44174121302). Esta evidência não comprova E2E 100k nem autoriza TEST one-use. O conector GitHub disponível nesta sessão não oferece ação de despacho de workflow; execução manual permanece pendente.

## Evidência de código: dependência SQL efetiva no TEST (17/11/2026)

Inspeção direta de `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs` na master `566c15b369afd46fc5969a7cab185c1ad5506271` demonstra **dependência real**, não apenas nomenclatura de compatibilidade: depois de validar `<sha>.operational.json` via `V9ValidatedOperationalModelFile.ReadPinnedAsync`, o TEST abre conexão `JornadaHarness`, chama `V9OperationalPinnedCohortRuntime.LoadPinnedAsync(connection, operationalModelId, winnerSha)`, extrai `runtime.FrozenWinner` do SQL e exige igualdade byte a byte com o arquivo. O scorer `V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync` recebe o runtime assim carregado. Portanto, o arquivo ainda **não é a fonte executável exclusiva** do TEST; a alegação de desacoplamento completo seria incorreta.

**Correção mínima proposta, sem ampliar escopo produtivo:** construir runtime de TEST diretamente do arquivo validado com `V9OperationalPinnedCohortRuntime.FromVerifiedFileSnapshot(validatedFileWinner)`, manter SQL descartável apenas para observações, grafo, ledger e métricas, preservar verificações SHA/TF4/81, isolamento e TEST one-use, e atualizar recibos/asserts para declarar fonte `FILE_PINNED` em vez de declarar igualdade obrigatória com payload científico SQL. A mudança exige verificar o contrato interno de `EvaluateFrozenTestAsync` e o workflow de preparação para impedir dependência indireta de modelo SQL, executar testes físicos antes de liberar 100k. **Não basta trocar um nome ou apagar uma asserção; nenhuma certificação antecipada.**

**Impacto/precedência:** esta evidência concretiza a dependência P0 entre desacoplamento e E2E, sem impor certificação produtiva geral do Runner. Referências: `V9IndependentTestCli.cs`, `V9OperationalPinnedCohortRuntime.cs`, `V9PhysicalSqlAdaptiveCalibrationProbe.cs`, `.github/workflows/v9-standard-linear-100k.yml`. Implementação ainda pendente.

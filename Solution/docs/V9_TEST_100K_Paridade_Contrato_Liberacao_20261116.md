# V9 100k — contrato verificável de liberação do TEST independente

**Estado:** BLOQUEADO. Documento técnico de implementação, não certificação. Issue #1575. Data: 2026-11-16.

## Evidência do código atual

1. `.github/workflows/v9-v3-four-name-100k.yml`, job `prepare-test-100k`, interrompe o TEST antes de gerar a coorte com `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.
2. O job `physical-100k` também interrompe o TRAIN quando `test_required=true` e `test_database` está vazio. Retirar somente um dos guardas não libera a campanha.
3. `V9IndependentTestCli.RunAsync` consome o JSON vencedor SHA-pinned e o runtime `V9OperationalPinnedCohortRuntime.LoadPinnedAsync`, mas executa `V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync`, **não** `ProbabilisticLinkageBatchRunner.RunAsync`.
4. `SqlProbabilisticIdentityLinkage.LoadPinnedV9GroupedCohortRuntimeAsync` é um loader opt-in somente diagnóstico, explicitamente **não integrado** a `GetActiveModelAsync`, `ResolveWithoutCpfAsync` nem ao batch publicador. O caminho de produção corrente ainda usa `GetOrLoadRuntimeSnapshotAsync`, `LoadModelByIdAsync` e `LinkageRuleSetReader.TryLoadAsync`.
5. O relatório de TEST declara `productionRunnerFullPipelineParityCertified=false` e `independentlyCertifiedIid=false`. O teste `test_v9_test_production_model_format.py` exige os bloqueios atuais.

## Plano técnico com evidências exigidas

| Gate | Implementação | Evidência de aceite |
| --- | --- | --- |
| P1 — modelo | Adaptar a execução **não publicadora** do Runner para receber o vencedor V9 operacional SQL por SHA e versão, sem fallback legado ou ativação. | SQL e JSON byte-exatos, mesma carga TF4/FS, rejeição de SHA, versão ou schema divergentes. |
| P2 — processamento | Executar o mesmo planejamento de candidatos, blocking, regras, limites e resolução do `ProbabilisticLinkageBatchRunner.RunAsync` e `SqlProbabilisticIdentityLinkage`, com modo `MODEL_VALIDATION`, `Publish=false` e banco descartável. | Teste E2E comparando decisões por identidade e métricas, com golden cases positivos/negativos; sem escrita de publicação. |
| P3 — isolamento | TEST selado antes de TRAIN; chave privada disponível só no job TEST; abrir somente após VALIDATION e reserva one-use atômica. | Testes de tentativa de reabertura, artefato alterado, SHA errado e vazamento de dados. |
| P4 — workflow | Substituir os dois guardas `PARITY_NOT_IMPLEMENTED` por um preflight executável que comprove P1–P3 **antes** da geração e das 324 hipóteses. | CI verde, teste de contrato positivo e negativos, `test_required=true` sem desvio de segurança. |
| P5 — ciência | TEST recebe só vencedor final; não recalibra, não seleciona, não certifica IID sem evidência. | Relatório com `refitted=false`, `certified=false` até prova independente, rastreio de seed e SHA. |

**Critério de saída:** não é permitido remover os guardas de fail-closed enquanto P1–P4 não estiverem implementados e verificados. A persistência REF do corpus TRAIN da PR #1567 é trabalho relacionado, mas distinto; não substitui a prova de paridade.

## Governança

Solicitação do PO: liberar `test_required=true` **com execução científica válida**. Decisão operacional vigente: manter bloqueio até paridade comprovada; não é autorização para bypass. Rastreabilidade: GOV-DOC-01, issue #1575, workflow V9 100k, PR #1567 e decisão de corpus ZIP REF de 2026-11-15.

## Descoberta adicional: o Runner já tem um caminho experimental, limitado a 512 observações

Inspeção de `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs` (master em 2026-11-16): `RunAsync` já reconhece `IV9NonPublishingFrozenCohortScorer`, rejeita publicação, exige `MODEL_VALIDATION`, `ModelVersion=1`, `MaxRecords=null` e banco `JornadaHarness`. Entretanto, na materialização do universo, exige **2 a 512 observações** (`V9PinnedSyntheticGraphCohortRunScorer.MaximumSyntheticObservations`). Logo, **não** constitui execução de TEST 100k. A classe também oferece `RehearseV9NonPublishingSyntheticTestAsync` e `PersistV9SyntheticRehearsalLedgerAsync`, mas seus próprios comentários negam certificação de paridade produtiva. O método principal usa um caminho de score agrupado opt-in e ainda possui fluxos legados de SQL e publicação.

**Consequência arquitetural:** não ampliar cegamente o limite 512. Antes, caracterizar custos/memória e garantir equivalência de paginação, universe snapshot, blocking, decisões e ledger com 100k. Planejar teste de carga de 100k no runner descartável e provas de reconciliação por lote; manter o gate até evidência física. A restrição 512 e a ausência de paridade certificada são bloqueadores adicionais rastreáveis na issue #1575.

## Decisão funcional aprovada pelo PO — TEST sintético novo e vencedor único (2026-11-15)

**Decisão vigente:** a etapa TEST da calibração V9 deve **gerar uma base sintética nova e independente** da base TRAIN e da base VALIDATION. O TEST recebe **somente os parâmetros congelados do único vencedor escolhido na VALIDATION**. Não realiza nova otimização, comparação de hipóteses, ajuste de parâmetros, seleção de limiares ou escolha de outro vencedor. A avaliação ocorre uma única vez com o vencedor, produzindo métricas de generalização na nova base.

**Contexto/justificativa:** impedir vazamento de dados e seleção orientada por TEST, preservando a interpretação de generalização fora da amostra. A independência por seed deve ser verificada; seed distinta isoladamente não comprova IID.

**Implementação/impactos:** manter geração independente de 100.000 registros com seed TEST pré-registrada `20261113001`, isolada das seeds TRAIN `42` e VALIDATION `20261112001`; manter preparação/selagem antes de TRAIN e abertura só depois de congelar o vencedor; passar ao job TEST apenas artefato operacional vencedor SHA-pinned, nunca os resultados de TRAIN/VALIDATION nem a grade das 324 hipóteses. A limitação experimental de 512 observações e o gate de paridade produtiva são **problemas de implementação**, não autorização para reutilizar TRAIN/VALIDATION ou testar múltiplos modelos.

**Precedência:** esta decisão esclarece o escopo do TEST da issue #1575 e prevalece sobre qualquer interpretação de que TEST deva recalibrar ou utilizar a mesma base da VALIDATION. Não revoga controles de segurança, privacidade, isolamento, integridade, nem os critérios de paridade e GOV-DOC-01.

**Critérios de aceite adicionais:** (1) manifest da nova base registra seed, contagem e hashes; (2) somente um JSON de vencedor SHA-pinned entra no TEST; (3) `configurationsEvaluated=1`, `refitted=false`, `trainPlanRead=false`, `validationResultsRead=false`; (4) relatório não certifica IID nem paridade não demonstrada; (5) workflow falha fechado se qualquer condição divergir.

## Decisão funcional complementar — seletor `test_database` (2026-11-15)

O PO confirmou a necessidade de preservar o parâmetro já existente `test_database` no workflow `v9-v3-four-name-100k.yml`. Com `test_required=false`, executar TRAIN e VALIDATION sem TEST. Com `test_required=true` e `test_database` vazio, gerar e avaliar **nova base sintética TEST independente**, sem reutilizar a base de VALIDATION. Com `test_required=true` e `test_database` preenchido, **não gerar TEST sintético**: avaliar o vencedor congelado sobre **banco SQL existente indicado**, com credencial SELECT-only e runner privado, mantendo a classificação de **diagnóstico operacional, não certificação científica**. Nos dois modos o TEST recebe apenas os parâmetros do vencedor da VALIDATION, sem ajuste nem nova seleção.

**Justificativa:** permitir medir generalização sintética independente ou comportamento operacional sobre outro banco, sem misturar proveniência nem alterar a seleção do modelo. **Impactos:** preservar as duas ramificações e os controles de preflight de banco externo; nunca tratar o nome do banco como autorização de escrita; não apresentar diagnóstico externo como TEST científico. **Precedência:** esclarece o contrato funcional do TEST de 100k, sem revogar os gates de paridade, isolamento e segurança. **Rastreabilidade:** issue #1575, PR #1576, GOV-DOC-01, workflow `.github/workflows/v9-v3-four-name-100k.yml`.

## Decisão arquitetural confirmada pelo PO — modelo candidato distinto, motor comum e plano versionado (2026-11-15)

**Interpretação confirmada:** o único vencedor da VALIDATION pode ser **diferente do modelo atualmente ativo em produção**. O TEST não procura outro vencedor: avalia apenas o candidato congelado pela VALIDATION, sem recalibrar ou comparar modelos. A diferença de identidade/versão entre o candidato e o modelo ativo **não é erro** e não autoriza substituição automática do modelo ativo.

**Motor e plano:** a calibração/TEST e a produção devem utilizar a **mesma semântica de motor de linkage**. Quando o selecionador introduzir novo plano de execução, seu contrato versionado e seus parâmetros precisam ser reconhecidos de forma equivalente por ambos os caminhos. Incompatibilidade de versão, projeção, fingerprint, pesos, regras de candidatos, agrupamento ou limiares deve falhar fechada, nunca adaptar silenciosamente os resultados. A adaptação do motor ao plano deve ocorrer antes de qualquer alegação de paridade produtiva; idealmente usar componentes compartilhados e comprovação física com o mesmo plano e modelo.

**Justificativa/contexto:** permitir evolução e teste de novos modelos sem alterar a operação atual e evitar que um plano novo seja avaliado com semântica diferente daquela que entraria em produção. **Impactos:** manter identidade do modelo ativo separada da identidade SHA-pinned do vencedor VALIDATION; definir binding de versão do plano no artefato operacional e teste comparativo Runner/calibrador; exigir aprovação explícita e governança própria para promoção. **Precedência:** complementa a decisão TEST sintético independente/vencedor único, sem alterar isolamento, one-use, diagnóstico `test_database` ou bloqueio atual de paridade. **Rastreabilidade:** GOV-DOC-01, issue #1575, PR #1576, `V9IndependentTestCli.cs`, `V9PinnedSyntheticGraphCohortRunScorer.cs`.

**Aceite:** TEST não consulta nem substitui o modelo ativo para escolher parâmetros; versão/plano incompatível é rejeitado; execução candidata e operacional com o mesmo plano/entrada produzem decisões e agrupamentos equivalentes, inclusive entre lotes; nenhum modelo é promovido automaticamente.


## Registro de implementação — grafo físico SQL em páginas (2026-11-16)

**Contexto e justificativa:** o bridge físico de coorte pequena reconstruía a junção SQL para contar e ler candidatos, transferia o conjunto em uma única consulta e mantinha estruturas intermediárias de tamanho desnecessário. Isso não escalava nem oferecia uma fronteira explícita de completude entre páginas.

**Implementado na PR #1576:** censo combinatório exato de incidências entre gestores, transferência de assinaturas em lotes de 2.048 linhas, materialização única de arestas DISTINCT na tabela temporária local `#v9_run_edges` com índice único, censo `COUNT_BIG`, leitura por paginação keyset de 2.048 arestas, verificação de ordenação estrita, hash SHA-256 canônico incremental e reconciliação integral entre censo e leitura. A auditoria SQL operacional rejeita arestas inesperadas e compara os digests físicos/operacionais independentemente.

**Limites e impactos:** a paginação controla a transferência, **não** a memória total do replay. `V9FrozenSourceResolveReplay` ainda constrói vizinhanças, rankings e arestas pontuadas globais; `V9BlindIncrementalIdentityGrouping` mantém dicionário de arestas e ordenação por score para complete-link. `V9PinnedSyntheticGraphCohortRunScorer` mantém o teto de 512 observações. Nenhum desses commits comprova equivalência do `ProbabilisticLinkageBatchRunner.RunAsync` com TEST 100k, nem autoriza remover `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.

**Próximo corte arquitetural necessário:** estabelecer um consumidor global de arestas pontuadas com armazenamento externo e ordenação determinística por score, acesso indexado a pares para o teste complete-link e reconciliação de decisões/partições por coorte. Provar equivalência byte-a-byte de decisões, agrupamentos e contadores em coortes pequenas, depois validar capacidade e falha fechada em 100k no SQL descartável. Não particionar o agrupamento por página, nem elevar limites sem essas provas.

**Precedência e rastreabilidade:** registro de implementação das decisões de motor comum, vencedor único e TEST isolado já aprovadas pelo PO; não cria autorização de promoção ou certificação. GOV-DOC-01, issue #1575, PR #1576; componentes `V9FrozenCohortSqlCandidateGraph.cs`, `V9OperationalCandidateGraphParityAudit.cs`, `V9FrozenSourceResolveReplay.cs` e `V9BlindIncrementalIdentityGrouping.cs`.

## Ratificação do PO — TRAIN e VALIDATION são uma execução obrigatória conjunta (2026-11-15)

**Decisão vigente, já aprovada e aqui reafirmada pelo PO:** a campanha V9 executa **TRAIN → AUDIT → VALIDATION** como uma cadeia única, obrigatória e rastreável. VALIDATION não é uma execução avulsa independente do TRAIN da campanha, e TRAIN isolado não constitui campanha concluída. A opção TEST continua sendo a única etapa facultativa. A separação em jobs/partições não significa autorização para separar as campanhas ou refazer TRAIN desnecessariamente.

**Contexto/justificativa:** impedir divergência de versão, população, parâmetros e vencedores entre as fases; assegurar que o vencedor validado corresponda ao TRAIN e AUDIT da mesma execução. **Impactos:** conservar a identidade do run, artefatos, SHA, proveniência e dependências entre fases; permitir reutilização de infraestrutura SQL e de dados persistidos dentro da mesma campanha quando compatível com isolamento estatístico, sem confundir partições TRAIN e VALIDATION. A paginação do grafo de candidatos é mecanismo de leitura, **não** uma obrigação de regenerar a base TRAIN ou VALIDATION.

**Precedência:** ratifica GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114 e GOV-V9-E2E-CLOSEOUT-OBRIGATORIO-20261114; prevalece sobre qualquer descrição posterior de TRAIN e VALIDATION como campanhas separadas ou opcionais. Não modifica o TEST independente, as garantias de isolamento, o winner único ou os bloqueios de paridade. **Rastreabilidade:** GOV-DOC-01, PR #1576, issue #1575 e workflow `.github/workflows/v9-v3-four-name-100k.yml`.

## Esclarecimento do PO — PREPARE integra obrigatoriamente a campanha (2026-11-15)

**Decisão ratificada:** o ciclo obrigatório é **PREPARE → TRAIN → AUDIT → VALIDATION**, sempre na mesma campanha; somente TEST é opcional. O PREPARE de campanha prepara corpus TRAIN, plano TF4, infraestrutura SQL descartável e referências congeladas antes da calibração. **Não confundir** com `prepare-test-100k`, que prepara exclusivamente o TEST sintético selado e permanece condicionado a `test_required=true`.

**Implementação nesta PR:** identificados explicitamente como PREPARE os passos já existentes de geração/binding TRAIN, inicialização SQL e reidratação IBGE dentro do job `physical-100k`, preservando o mesmo workspace e dados para TRAIN. Adicionado teste de contrato que exige PREPARE antes do ajuste físico e encadeamento TRAIN → AUDIT → VALIDATION. **Limitação explícita:** PREPARE ainda é uma fase dentro do job TRAIN, não um job GitHub Actions separado; não foi introduzida nova geração de dados nem persistência interjob. Eventual extração para job independente exigirá transporte verificado dos artefatos e avaliação de custo, sem duplicar corpus.

**Justificativa e impacto:** explicitar o estágio obrigatório sem confundir TEST opcional nem alterar a ciência; prevenir regressão de dependências e regeneração desnecessária. **Precedência:** complementa e especifica a decisão de campanha única e GOV-V9-CADEIA-OBRIGATORIA-E2E-PREFLIGHT-20261114; preserva os gates TEST, as partições estatísticas e a seleção do vencedor. **Rastreabilidade:** GOV-DOC-01, PR #1576, issue #1575, `.github/workflows/v9-v3-four-name-100k.yml`.

### Evolução do workflow: PREPARE como primeiro job independente

O job `prepare-100k` foi acrescentado como **primeira etapa obrigatória** do workflow e `physical-100k` passou a depender explicitamente de seu sucesso. O PREPARE verifica SHA exato, execução manual pelo proprietário, aprovação de segurança e presença dos arquivos de campanha; `prepare-test-100k` continua separado e opcional. **Escopo efetivo:** este primeiro job faz admissão e integridade do código; a geração do corpus TRAIN, binding do plano e inicialização SQL ainda ocorrem no job `physical-100k`. Não afirmar que PREPARE já gera/persiste/entrega o corpus ao TRAIN. A extração física requer um handoff com hashes, identidade de campanha e sem transferência de SQL local entre runners. A CI de integração precisou atualizar o contrato textual de dependências após o novo job; teste sintético não comprova E2E 100k.

### PREPARE → TRAIN: primeiro handoff imutável de admissão

O PREPARE agora publica um recibo JSON mínimo `V9_PREPARE_ADMISSION_V1` como artifact da mesma execução, vinculado a `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, SHA exato do código e SHA-256 de três arquivos de configuração/planejamento. O TRAIN baixa o recibo e valida todos os campos e hashes **antes** de iniciar as operações físicas. Falha de download, divergência de SHA ou campanha interrompe o TRAIN (fail-closed). Esse handoff não contém dados pessoais nem corpus e não certifica preparação física completa: geração TRAIN, plano bound e SQL permanecem dentro do job TRAIN até existir contrato de transporte físico seguro, com integridade e limites de recursos. Testes de contrato foram atualizados. Rastreabilidade: PR #1576; GOV-DOC-01.

### PREPARE físico — geração única do corpus TRAIN e handoff (2026-11-15)

Evolução da implementação: o job independente `prepare-100k` **agora gera** o corpus sintético TRAIN 100k, seed 42, e publica artifact específico da campanha com manifesto `V9_PREPARE_TRAIN_CORPUS_V1` (SHA do commit, run ID, tentativa e SHA-256 de cada arquivo). O TRAIN baixa, confere a lista completa e os hashes, verifica a presença de `observacoes.csv`, e move os dados para seu workspace; **não executa novamente o gerador**. O binding do plano TF4, download do histórico, inicialização do SQL e carga de referências continuam no job TRAIN por enquanto, pois não são transportáveis como instância SQL entre runners. O artifact contém **dados sintéticos TRAIN**, nunca TEST ou dados reais, com retenção limitada a sete dias. Este é um handoff de corpus, não certificação do E2E 100k; validar CI e custo/limites de artifact antes da liberação. Precedência: GOV-V9-PREPARE-TRAIN-AUDIT-VALIDATION-CAMPANHA-UNICA-20261115; rastreabilidade PR #1576 e GOV-DOC-01.

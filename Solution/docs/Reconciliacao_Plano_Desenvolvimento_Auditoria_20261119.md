# Reconciliação do plano de desenvolvimento e da avaliação técnica — 19/11/2026

**Natureza:** avaliação técnica e proposta de ordenação; **não constitui aprovação de novas mudanças pelo PO**. Fonte de prioridade: [Plano de Desenvolvimento](Plano_Desenvolvimento.md). Fontes de estado: [Índice de decisões vigentes](Indice_Decisoes_Vigentes.md), [Estado atual](Estado_Atual_Projeto.md), [Dívidas técnicas](Dividas_Tecnicas.md), PRs/issues e evidências da master. Política GOV-DOC-01.

## Por que este registro existe
O plano existente já se declara **ponto único de priorização**, mas seu topo contém fotografia de outubro e o corpo mistura plano histórico, decisões supervenientes e entregas. A avaliação externa de 19/11/2026 (master `531bb6a`) identificou problemas de arquitetura, testes, OpenAPI e documentação; **não compilou nem executou testes .NET e não verificou Actions**. O inventário consultado em 19/11 retornou 23 PRs e 65 issues abertas, que não equivalem a 88 pendências independentes. Este documento reconcilia as críticas com o plano sem substituir as decisões vigentes.

## Fila única recomendada (sem criar bloqueios novos)
| Ordem proposta | Frente e referências | Próxima evidência necessária | Limite/precedência |
| --- | --- | --- | --- |
| P0-A | Integridade/proveniência do gerador LOCKED, #1734; divergência seed-43 relatada na auditoria | Diff entre fonte congelada e gerador atual; resultados de gates físicos; classificação dos artefatos afetados | Preservar LOCKs e manifestos; não declarar equivalência sem prova; campanhas históricas não se invalidam automaticamente |
| P0-B | Cadeia científica V9, #1089, #999, #1478, #1488, #1584, #1575 | Verificar na master o pacote imutável, SHA/fingerprint, coortes independentes, preflight, gates, fluxo de zero finalistas e TEST sem refit; mapear PRs substituídas | TEST científico não é ativação operacional; operador Master autoriza produção; não exigir certificação produtiva ampla como bloqueio automático do TEST |
| P1-A | Testes Python: 16 falhas/41 arquivos fora de workflows, números da avaliação anexa | Reproduzir falhas no SHA vigente e classificar obrigatório/manual/histórico/obsoleto | Não apagar gate do gerador LOCKED nem confundir teste textual frágil com erro de algoritmo |
| P1-B | DT-07 / modelo físico: contagens relatadas 80/99/110 | Inventário comparável por schema/tabela/migração e critérios de inclusão; verificar se DT-07 necessita complemento | Divergência é indício, não prova de 30 tabelas ausentes |
| P1-C | OpenAPI e Resultado.Api | Inventariar endpoints públicos/internos e testes bidirecionais; executar testes HTTP de comportamento | Rotas internas podem ter exclusão documentada; não inferir ausência de cobertura apenas por nome de arquivo |
| P1-D | PRs/issues e documentação vigente, #404 | Para cada item: decisão vigente, diff na master, CI, evidência, status e ação; corrigir especificação/estado atual sem apagar histórico | Não encerrar PR apenas por idade; #1429 preservada até prova de equivalência; #1576 sujeita à decisão posterior de TEST obrigatório |
| P2-A | Pesquisa misturada ao produto; tipos de parâmetros; Core/Runner | Inventariar dependências e propor extração incremental, testes de equivalência e schema tipado | Não refatorar durante gates científicos críticos; preservar retrocompatibilidade V8/legado |
| P2-B | Workflows, testes textuais e documentação extensa | Classificar gates permanentes vs campanhas sob demanda; simplificar documentos normativos sem perder rastreabilidade | Não remover evidência congelada ou obrigação GOV-DOC-01 |

## Divergências entre a auditoria e o plano
1. **“Ligar V9 ao runtime” não é prioridade imediata de ativação.** O plano e decisões supervenientes distinguem calibração, TEST independente, compatibilidade do executor e autorização do operador Master. O bloqueio fail-closed é intencional; falta provar o caminho científico e os requisitos de habilitação, não remover o bloqueio.
2. **“Apagar V5–V7” exige inventário de replay e compatibilidade.** O plano contém intenção histórica de retirada segura, mas a decisão posterior exige retrocompatibilidade permanente do executor. Remover somente caminhos comprovadamente dispensáveis.
3. **“Apagar testes órfãos” é prematuro.** Classificar e reproduzir; preservar o teste de proveniência até fechar #1734.
4. **“Unificar especificação” não significa apagar decisões.** Manter especificação normativa, estado atual curto, decisões e evidências históricas em camadas separadas.
5. **DT-22 (#861) permanece aberta e postergada**; não a transformar em impedimento da cadeia científica.
6. **HML, identidade corporativa e decisões institucionais** estão fora do escopo desta fila de engenharia, sem dispensa dos seus gates próprios.

## Controle de execução
A cada mudança: (a) selecionar **uma** frente desta fila; (b) confirmar estado da master, PR, issue e decisões posteriores; (c) explicitar aceite mensurável e regressão; (d) executar/registrar testes no SHA; (e) integrar com checks exigidos; (f) atualizar este documento, o Plano, a issue, as Dívidas e o Índice **somente se houver nova decisão aprovada**. Não declarar `CONCLUÍDO` por CI isolada, por texto de documentação ou por PR aberta. Não iniciar uma frente P2 enquanto P0 exigir atenção, salvo correção pequena e independente aprovada.

## Situação desta reconciliação
Registro documental proposto em PR; **nenhuma implementação, correção de testes, alteração de prioridades pelo PO ou ativação foi realizada**. Os números e achados do relatório de 19/11 exigem verificação independente no SHA atual antes de virar dívida confirmada.

## Checkpoint P0-A — verificação complementar de 19/11/2026

- **Plano sincronizado na master:** PR #1742 integrada, commit `68ed67cc2610ca8fc8be895ab2ea3f202b9ff48e`. No HEAD da PR, workflow obrigatório `jornada-pr-required-fast` #44588905500 concluiu `success`, com jobs `security-guards`, `unit-runtime` e `deterministic-build` aprovados. **Esse resultado é de PR, não comprovação de CI completa pós-merge da master.**
- **Evidência física existente:** workflow #44581595305, job `compare-historical-and-current` aprovado; decisão GOV-V9-P0-1734 documenta igualdade byte a byte de três arquivos científicos com seed 42/30k e divergência de metadados. Não generalizar para todas as seeds ou configurações.
- **Gate histórico inspecionado:** `Solution/scripts/v9-tf-frozen-transfer-lock-gate.py` exige SHA da árvore atual do gerador igual ao SHA LOCKED histórico e contrato de partição original. Esse gate deve continuar aplicável a replay da fonte congelada; seu uso como gate genérico de código beta evolutivo requer reconciliação com a decisão GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119. **Não afrouxar a verificação de replay LOCKED.**
- **DT-10:** commit histórico `4ac707d4844e7d415c120769ef0ba6bd078208e2` documenta testes e job `dt10-evidence` seletivo, mas **não comprova a validação da correção recente #1706**. Conferir PR, SHA e run específicos antes de marcar como validada.
- **Limitação de observabilidade:** a consulta de runs por commit disponível retorna apenas eventos de PR; para o merge SHA da master retornou lista vazia. Isso **não significa ausência ou sucesso** dos workflows de push. É necessário verificar diretamente os runs pós-merge, jobs e branch protection/rulesets.

**Próxima ação única:** obter evidência da CI de push da master no SHA `68ed67c`, verificar gates obrigatórios/rulesets e a PR #1706; então atualizar #1734 com a conclusão estritamente suportada. Até lá, **P0-A permanece aberto**, sem TRAIN novo ou TEST one-use.

## Checkpoint de retomada P0-A → DT-10 — 20/11/2026 (em PR #1757, não integrado)

**Autoridade e ordem:** executar o Plano de Desenvolvimento vigente: P0-A (#1734), validação específica DT-10 (#1706), P0-B V9; P1/P2 posteriores. Esta anotação é evidência operacional, não nova autorização científica nem encerramento de P0-A.

**Estado revalidado:** master HEAD `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`; PR #1721 integrada pelo commit `008bdb183adbe8c4935c730a99a80ef3bf02ef01`; PR #1706 integrada pelo commit `0d150c536ae518f5a1029e25e664e0d63cd23511`. A execução de push `jornada-ci` #44654406155 teve 21 jobs, 16 success, 5 skipped e nenhum failure; o job `dt10-evidence / dt10-evidence` consta success. A evidência do job geral **não demonstra isoladamente** que a correção específica #1706 foi validada contra seu critério de aceite. A issue #1734 permanece aberta e seus critérios de aceite não estão assinalados como concluídos.

**Lacunas antes de fechar P0-A:** (1) evidência independente da proteção de branch/rulesets e da obrigatoriedade dos gates de integridade; (2) confirmação de que a execução #44654406155 corresponde ao HEAD e evento de push da master, incluindo logs/artefatos dos gates pertinentes; (3) reconciliação do gerador atual com os manifestos LOCKED sem regenerá-los; (4) distinguir checks da PR de provas pós-merge. **Não considerar P0-A concluída** com base em CI global verde.

**Próxima ação executável:** inspecionar checks/rulesets e proveniência LOCKED da P0-A; registrar conclusão com evidências em #1734. Em seguida, conferir diff, testes, jobs e artefatos especificamente atribuíveis à PR #1706 e atualizar o estado DT-10. Só depois retomar P0-B. As PRs de observabilidade #1757–#1759 são auxiliares e não substituem essa ordem. Checkpoint deve ser reconciliado com o HEAD atual em toda nova sessão.

### Evidência complementar P0-A / DT-10 — 20/11/2026

- **Proveniência dos artefatos:** a API de artefatos da execução `44654406155` associa explicitamente `head_branch=master` e `head_sha=1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`; artefatos de `dt10-evidence` (ID `14593242892`, SHA-256 do ZIP `09f41c8a841a6acd2c92325a51cd75672d408f2ea532ec954099602ea918ad03`) e `ci-impact` (ID `14593103672`) estão disponíveis na consulta. Isso vincula o artefato ao SHA, mas o campo `event=push` ainda requer inspeção do metadado da execução.
- **DT-10 — prova física mais específica:** PR #1706 foi integrada e descreve correção **somente do teste**, zerando conjuntamente `avaliados`, `resolvidos`, `nao_resolvidos`, `conflitos` e `sem_candidato_no_bloco` para respeitar `ck_linkage_run_contagens` e alcançar o guard `THROW 51818` no caso de run incompleto. Na execução `44654406155`, job `dt10-evidence / dt10-evidence` (ID `136023088017`) terminou `success`; steps `Create isolated DT-10 evidence database`, `Execute DT-10 concurrency rollback and plan-volume evidence` e `Upload DT-10 evidence` também terminaram `success`. Essa é evidência pós-integração relevante, **não** inspeção do conteúdo interno do ZIP nem prova isolada de cobertura do guard 51818. Validar o conteúdo do artefato e o caso de teste antes de encerrar formalmente DT-10.
- **P0-A permanece aberta:** o gate de replay LOCKED no código exige digest da árvore do gerador, contagem de 25 arquivos e hashes das referências, sem aceitar divergência. A existência do gate não comprova sua obrigatoriedade em branch protection/rulesets, nem a equivalência dos insumos congelados. Não modificar manifestos LOCKED para obter CI verde.
- **Próxima investigação:** consultar branch protection/rulesets e metadados de run por interface autorizada; inspecionar ZIP DT-10 e logs da execução, reconciliar o gate LOCKED com sua aplicação em PR e registrar evidências conclusivas na issue #1734. Não avançar P0-B por inferência.

### Inspeção de escopo dos gates — continuação 20/11/2026

**Achado verificável na configuração atual:** `.github/workflows/ci.yml` dispara `jornada-ci` em push na `master` e dispatch; seu job `dt10-evidence` chama `.github/workflows/dt10-evidence.yml` quando o classificador sinaliza `run_dt10=true`, com dependência de `dependency-lock`. O workflow reutilizável executa `dotnet test` com filtro `TestCategory=DT10Evidence`, exige `--forbid-skipped --minimum-tests 4` e publica `dt10.trx` e `summary.json`. A PR #1706 altera somente o teste `Incomplete_run_cannot_publish_or_create_progressive_events`, zerando todos os contadores correlacionados, conforme o diff inspecionado. A execução #44654406155 confirma o job e upload aprovados, mas o conteúdo TRX ainda não foi lido diretamente.

**Separação essencial do LOCKED:** `v9-tf-frozen-transfer-lock-gate.py` é invocado pelo workflow **manual** `v9-tf-seed43-frozen-transfer.yml` para seeds 44–57; esse workflow rejeita a `master` beta e exige referência histórica e SHA exatos. O gate **não aparece como comando** no workflow `ci.yml` da master. Portanto, não se deve exigir indiscriminadamente que o gerador beta atual corresponda ao digest do gerador histórico; isso conflitaria com a decisão de preservar LOCKs imutáveis e permitir evolução beta. **Ainda é necessário verificar branch protection/rulesets e os gates obrigatórios realmente vigentes**, sem declarar ausência de proteção a partir de um único arquivo.

**Decisão e precedência:** nenhuma nova decisão aprovada; aplicar GOV-V9-P0-1734 e GOV-V9-BETA-CAMPANHAS-HISTORICAS-CI-20261119. O replay histórico permanece fail-closed, e a divergência de digest do HEAD não invalida automaticamente a CI beta. Próxima ação: ler artefato TRX DT-10, confirmar run event/branch rules e atualizar #1734 com evidência conclusiva, sem modificar LOCKs.

### DT-10 — inspeção direta do artefato pós-merge (20/11/2026)

O ZIP **real** `dt10-evidence` (artifact ID `14593242892`, run `44654406155`, SHA master `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`) foi baixado e inspecionado. Contém `summary.json`, `dt10.trx` e `dt10-plan-volume-20261120015030.json` (inclusive cópia anexa à execução). O resumo reporta `status=PASS`, `total=5`, `passed=5`, `failed=0`, `skipped=0`, `other=0`, `minimumTests=4` e `forbidSkipped=true`; os contadores do TRX confirmam 5 executados e 5 aprovados. **O teste corrigido `Incomplete_run_cannot_publish_or_create_progressive_events` consta explicitamente como `Passed`.** Os demais cobrem concorrência de origem, escala de plano, rollback transacional e publishers sobrepostos. Isso supre a lacuna anterior de leitura direta do TRX e comprova a execução física do caso alterado pela PR #1706 na master. Não representa autorização de publicação operacional nem validação científica V9.

**Status:** validação técnica pós-merge da correção DT-10 #1706 **comprovada para o SHA acima**. O encerramento administrativo da frente depende de conciliar a issue/registro de aceite, sem reabrir os testes já comprovados. P0-A #1734 continua aberta até verificação de branch protection/rulesets e gates efetivamente obrigatórios. Nenhuma decisão nova de produto ou alteração LOCK foi tomada.

### Rastreabilidade na issue P0-A — 20/11/2026

As evidências de DT-10 pós-merge, distinção entre gate de replay LOCKED e CI beta, e pendência das proteções efetivas foram publicadas na issue **#1734**, comentário **7028361041**. O repositório informa permissão administrativa à conta conectada, mas a interface de leitura GitHub disponível nesta sessão **não oferece consulta de branch protection/rulesets**; não foi possível atestar quais checks são requeridos para merge. Isso é limitação de observabilidade, não prova de inexistência de proteção. A issue permanece aberta, sem alterar critérios de aceite. Próximo passo: obter configuração de regras e status checks por interface autorizada que exponha os endpoints correspondentes, reconciliar com os workflows de PR e fechar P0-A apenas com prova. A DT-10 tem prova física técnica no SHA citado acima; seu aceite não autoriza TEST/TRAIN.

### Auditoria dos contratos versionados de CI — 20/11/2026

Inspecionados diretamente no SHA master `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`: `.github/workflows/jornada-pr-required-fast.yml`, `.github/workflows/ci.yml` e `Solution/tests/scripts/test_ci_required_gates.py`. O workflow de PR roda em `pull_request`, `push master` e manual; classifica impacto e executa suíte offline V9 para mudanças não documentais. O teste de contrato versionado exige presença de jobs `impact`, `dependency-lock`, `ddl-upgrade`, `unit`, `security-analysis`, `harness-smoke`, `e2e`, `deterministic-build`, `integration-sql` e `dt10-evidence`; verifica dependências e integração do job DT-10 com workflow reutilizável, filtro físico, mínimo de quatro testes e proibição de skips. Isso **comprova controles declarados no código**, não a configuração externa de branch protection/rulesets.

**Bloqueio externo delimitado:** a conta conectada tem permissão `admin`, mas o conector GitHub não disponibiliza ação de leitura de rulesets/protection; tentativa de consulta pública aos endpoints REST via acesso web falhou por indisponibilidade do endpoint nesta sessão, e o ambiente de execução não resolveu `api.github.com`. Não foi alterada nenhuma proteção nem inferido seu estado. A PR #1757 permanece **aberta e draft**, portanto este registro ainda não integra a master. Não declarar P0-A encerrada nem promover P0-B por mera presença de contratos versionados.

**Critério para destravar:** evidência autenticada da configuração de `master` (rulesets/branch protection, nomes exatos dos status checks requeridos e política de bypass), confronto com jobs da PR e da master, e atualização da issue #1734. Até lá, avançar apenas em preparação offline e documental da P0-B, sem TRAIN/TEST ou LOCK.

### P0-B — pré-auditoria estática da cadeia científica V9 (20/11/2026)

**Escopo autorizado:** apenas leitura e preparação, sem TEST, TRAIN físico, LOCK ou ativação. Reabertos na master o workflow `.github/workflows/v9-v3-four-name-100k.yml` e o teste `Solution/tests/scripts/test_v9_test_production_model_format.py`; consultadas as issues #1089, #999, #1478, #1488, #1584 e #1575, todas **abertas** nesta consulta.

**Bloqueio real ainda presente no código:** o workflow aborta solicitações de TEST antes da geração do corpus (linhas 170–171, marcador `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`) e novamente antes do TRAIN (linhas 293–294). O próprio workflow exige `productionRunnerFullPipelineParityCertified == false` em dois caminhos de relatório (linhas 952 e 1063). O teste offline `test_v9_test_production_model_format.py` **protege deliberadamente** os bloqueios, exige que antecedam `--people 100000` e `--v9-calibrate` e verifica que o executor de TEST não invoque `ProbabilisticLinkageBatchRunner.RunAsync`. Logo, remover sentinelas isoladamente seria bypass, não correção.

**Encadeamento de entrega necessário:** #1089 (calibrador único e snapshot versionado) e #999 (métrica no mesmo blocking físico) sustentam a cadeia; #1488 exige arquivo vencedor exclusivo e formato operacional idêntico; #1584/#1575 exigem paridade **integral** com o Runner produtivo em modo TEST_ONLY sem publicação; #1478 exige congelamento, coorte independente, aprovação e gates antes de TEST one-use. A aprovação histórica de implementação/execução registrada na issue #1478 **não substitui** os bloqueios de segurança e a ordem aprovada em 19/11, que veda antecipar TEST/TRAIN físico.

**Próxima entrega técnica após P0-A:** mapear entradas, saídas e persistência de `ProbabilisticLinkageBatchRunner.RunAsync` contra o executor TEST, especificar adapter TEST_ONLY e seus testes negativos, preservando fail-closed e isolamento. Não declarar paridade sem execução comprovada; não alterar os sentinelas nesta fase. Este checkpoint é auditoria estática, não certificação da cadeia.

### P0-B — mapa concreto de incompatibilidade Runner × TEST (20/11/2026)

**Fontes inspecionadas na master:** `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs` e `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCli.cs`.

1. **Entrada do TEST atual:** `V9IndependentTestCli.RunAsync` exige arquivo operacional final nomeado pelo SHA do vencedor (linhas 20–40), SQL `JornadaHarness` (60–63), `V9OperationalPinnedCohortRuntime.LoadPinnedAsync` (69–79), verifica round-trip byte-exato do JSON antes de abrir o corpus TEST (80–82) e avalia via `V9PhysicalSqlAdaptiveCalibrationProbe.EvaluateFrozenTestAsync` (115–125). O relatório mantém `productionRunnerFullPipelineParityCertified=false` (139). Isso demonstra formato e runtime pinados, **não** execução da pipeline de produção.
2. **Runner real:** `ProbabilisticLinkageBatchRunner.RunAsync` (169+) recusa seleção V9 explícita antes de lock, leitura de modelo ou SQL de publicação (`RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`, linhas 174–183). `RunV9Async` também falha fechado após `boundary.EnsureProductiveCertified()` (150–166). O caminho de produção legado adquire lease exclusivo (208–215), carrega modelo SQL (227–231), materializa universo de run, lê lotes, chama `ScoreBatchAsync` e `PersistBatchAsync` (359–381), com decisões ordenadas por observação. O scorer normal usa `linkage.ResolveWithoutCpfAsync`; a variante de fixture V9 chama `ResolvePreparedAsync` (471–492). A fixture é limitada a `MODEL_VALIDATION`, `Publish=false`, modelo v1 e `JornadaHarness` (184–202), e não deve ser confundida com adaptador produtivo certificado.
3. **Ensaios parciais existentes:** `RehearseV9NonPublishingSyntheticTestAsync` chama `V9ReadOnlyTestBatchRunner.RunAsync`, mas exige explicitamente `ProductionBatchPipelineCertified=false` (80–117). `PersistV9SyntheticRehearsalLedgerAsync` grava somente ledger sintético descartável, declarando que `RunAsync` real e tabelas produtivas não são acessados (120–147). Esses ensaios **não** fecham #1575/#1584.

**Lacuna de engenharia precisamente delimitada:** não basta compartilhar `V9OperationalPinnedCohortRuntime` ou JSON; é necessário demonstrar o mesmo caminho de materialização, blocking, scoring, agrupamento, resolução, lote e persistência **não publicadora** do Runner real, com prova negativa de que nunca há fallback ao modelo legado, refit, seleção de winner, publicação, leitura de TRAIN/VALIDATION ou escrita operacional. Implementar um adaptador `TEST_ONLY` tipado e explicitamente segregado, com validação do banco descartável e artefato SHA, somente após reconciliação de P0-A e definição de critérios de aceite de #1575/#1584. Preservar os bloqueios `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED` até prova física de ponta a ponta; não promover `ProductionBatchPipelineCertified` por inferência.

**Estado:** diagnóstico de código e plano de implementação, não entrega do adaptador. Sem decisão nova de PO, sem TEST/TRAIN físico e sem modificação de LOCK.

### P0-B — contrato verificável proposto para o adaptador TEST_ONLY (20/11/2026)

**Especificação de implementação, ainda não aprovada como nova decisão:** decompor a execução em núcleo comum de lote e fronteiras explícitas de I/O. O caminho de TEST deve invocar o **mesmo núcleo real** usado por `ProbabilisticLinkageBatchRunner.RunAsync`, não somente um simulador com algoritmos parecidos. Antes de qualquer leitura da coorte: (a) validar arquivo único `<sha>.operational.json` e SHA byte-exato; (b) carregar apenas modelo pinado no SQL descartável `JornadaHarness`; (c) rejeitar modelo ativo, seleção implícita, parâmetros não congelados, fallback legado, banco não autorizado e modo de publicação. Manter um contexto `TEST_ONLY` imutável, não inferido de variáveis ambientais permissivas.

**Contrato de lote:** materializar uma única coorte imutável, garantir ordenação e unicidade dos IDs, projetar candidatos com o mesmo blocking SQL/versionamento, aplicar scoring e agrupamento globais antes de persistir o primeiro lote, processar cada observação uma vez e reconciliar `eligible == evaluated == resolved + unresolved + conflicts` e `noCandidateInBlock <= unresolved`. Gerar hashes canônicos de pares, decisões e grupos; comparar com o caminho operacional usando os mesmos dados e winner, sem leitura de labels/gabarito no scorer. O diagnóstico atual de até 128/512 observações não autoriza extrapolação a 100 mil sem prova de escala.

**Fronteira de escrita:** o modo TEST_ONLY deve operar em SQL descartável e ledger segregado, sem tocar tabelas operacionais de publicação nem `identidade.v_vinculo_corrente`, sem promover `linkage_run` a PUBLICADO e sem reutilizar `RunV9Async` produtivo enquanto não certificado. Persistência deve ser atômica por contrato e idempotência comprovada; falhas e cancelamento não podem deixar evidência parcial publicável. Separar evidência de equivalência algorítmica, equivalência de pipeline e certificação de produção — são três afirmações diferentes.

**Matriz mínima de testes negativos antes de retirar sentinelas:**

| Caso | Resultado exigido |
| --- | --- |
| Winner ausente, arquivo renomeado ou SHA divergente | Falhar antes de abrir coorte/SQL de scoring |
| JSON operacional diferente do SQL pinado | Falhar antes de abrir TEST |
| Banco diferente de `JornadaHarness` | Recusar sem escrita/leitura científica |
| `Publish=true`, modo produtivo ou fallback legado | Recusar antes de lease/run/tabela |
| Seleção de novo vencedor, refit ou leitura TRAIN/VALIDATION | Recusar; nenhum resultado TEST válido |
| IDs duplicados, páginas fora de ordem ou lote incompleto | Falhar e não publicar |
| Grupos que mudam com tamanho/ordem dos lotes | Falhar paridade; comparar hashes canônicos |
| Erro de SQL, timeout ou cancelamento entre lotes | Rollback/estado não publicável, sem side effects produtivos |
| Tentativa de reuso de coorte TEST one-use | Recusar antes de execução |
| Evidência sem igualdade de blocking, scoring, grupos e decisões | `productionRunnerFullPipelineParityCertified=false` |

**Gate de promoção:** adicionar testes C# de integração e prova SQL física no mesmo SHA, verificar invariância por batch size e por reordenação de entrada, produzir recibo com hashes, denominadores, esquema, SHA do winner, commits e status de publicação; auditar artefatos sem expor gabarito à execução. Só então propor revisão das sentinelas, mediante critérios #1575/#1584/#1488/#1478, verificação de P0-A e autorização científica aplicável. Até esse gate, conservar `productionRunnerFullPipelineParityCertified=false` e os dois `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`.

### P0-B — primeiro incremento de testes automatizados (20/11/2026)

Aberta a **PR draft #1760** (branch `test/v9-testonly-failclosed-contract-20261120`, commit inicial `b242546f4737098275bd5bc09ccd5e761b397d55`) com `Solution/tests/scripts/test_v9_testonly_failclosed_contract.py`: três testes de contrato offline para (1) preservar sentinelas TEST antes de gerar corpus e TRAIN, (2) preservar recusa de V9 produtivo antes do lease e isolamento `JornadaHarness`, e (3) exigir winner pinado, round-trip antes da leitura da coorte e relatório `productionRunnerFullPipelineParityCertified=false`. **Não há execução de TEST/TRAIN físico, alteração de LOCK ou alegação de paridade produtiva.** A PR permanece draft e os testes precisam ser executados/validados pela CI; este incremento não substitui testes de integração SQL nem os critérios de aceite #1575/#1584. O registro desta PR no documento de reconciliação ainda depende da integração da PR documental #1757 na master.

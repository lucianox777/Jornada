# GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122 — Plano de ação aprovado para E2E do sistema inteiro

**Data:** 22/11/2026. **Autoridade:** Product Owner. **Estado:** DECIDIDO / PLANO APROVADO COM AJUSTES; implementação, validação, certificação e ativação NÃO presumidas. **Política:** GOV-DOC-01.

## Decisão e origem

O PO aprovou expressamente **o plano original “Plano de ação — caminho crítico do E2E do sistema inteiro” (22/11/2026), acrescido de TODOS os ajustes identificados na avaliação técnica subsequente**. Os ajustes abaixo **complementam**, não substituem, o escopo, as dez etapas (0–9), as seis frentes paralelas, as ações imediatas e os riscos do plano original. A base observada pelo plano foi a `master` em `cb9cdfc`, a campanha Standard Linear #38 (run `44970012048`) e o harness `Solution/scripts/local-e2e.sh`; essa base é **histórica**, não uma afirmação de estado atual.

**Motivo:** calibrador e pipeline operacional têm E2E separados, mas falta provar a cadeia única de ingestão HTTP, processamento, Runner probabilístico V9, Gold de pessoas, APIs e confronto das decisões com verdade sintética e oráculo independente. A alternativa de declarar o sistema inteiro testado a partir de dois E2E separados foi rejeitada. A alternativa de liberar V9 produtiva automaticamente pelo êxito do E2E também foi rejeitada.

## Escopo original preservado — prova integral pretendida

Uma execução em DEV, banco descartável e dados sintéticos deverá provar:
1. ZIP conforme contrato, gerado pela Solução de Apoio ou Console, entregue à API de ingestão com credencial.
2. Bronze endereçado por conteúdo → Processor → Silver → Gold de fatos (Benefício Concedido e Serviço Prestado).
3. Runner de linkage com **modelo V9 selecionado pelo Operador Master**, a partir do vencedor da calibração, persistindo `linkage_resultado` e recompondo Gold de pessoas nos estados PROVISORIA / REFERENCIA / INDEFINIDA.
4. APIs de resolver identidade, pessoa, registros, benefícios, busca semicega, `Resultado.Api` e monitor.
5. Comparação com gabarito do gerador sintético e oráculo independente.

**Cobertura declarada no plano de origem (sujeita à verificação no SHA da implementação):** campanha #38 TRAIN→TEST 100k; HTTP→Bronze→Processor→Silver→Gold→Serving em 1k/10k; idempotência e negativos de ingestão; ZIP Console opt-in; APIs principais; Runner DT-05 com V8. **Lacunas:** Runner probabilístico no E2E HTTP; Runner V9 bloqueado por `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`; gabarito E2E; `Resultado.Api`; busca semicega; ZIP Solução de Apoio.

## Caminho crítico aprovado (numeração original preservada)

| Etapa | Trabalho original | Dependências | Rastreio / aceite mínimo |
|---|---|---|---|
| **0** | Preservar vencedor da campanha #38 (artefato `14742452561`) e TEST fora do Actions | — | #1830; SHA `24a89024…` conferido, cópia durável antes da expiração estimada ~22/12; verificar conteúdo e completude do pacote |
| **1** | Arestas da consulta operacional; grafo congelado somente como verificador | — | #1584 item 3; testes de origem/seleção |
| **2** | Oráculo independente de score, abstenção, agrupamento e decisão | — | #1584 item 4; comparação não circular com `ReplayTest` |
| **3** | Adaptador V9 do `RunAsync`: lotes, status, publicação | 1 | #1584 item 2; fixture e matriz negativa item 5 verdes |
| **4** | Prova `RunAsync` com vencedor `6655a82c…`, 10k e depois 100k, banco descartável e recibos diferenciais | 0, 2, 3 | #1584 itens 1/6; igualdade de decisões com oráculo e recibos verdes; **despacho científico de escala requer autorização/preflight aplicáveis** |
| **5** | Procedimento Operador Master: `Linkage:SelectedModelVersion = 9`, `Linkage:V9SelectionFile`, registro DT-15 | 0 | #1488; rejeitar SHA divergente e pacote inválido |
| **6** | Habilitar caminho normal V9 em `Program.cs`/`LinkageModelPolicy` para pacote selecionado | 4, 5 | #1584/#1488; **habilitação controlada DEV distinta de autorização de ativação operacional/produtiva** |
| **7** | Runner `RunOnce` após Processor no `local-e2e.sh`, com seleção V9 | 6 | Issue a reconciliar/criar; E2E 1k, `linkage_resultado`, Gold de pessoas e APIs |
| **8** | Fixtures HTTP do gerador sintético, gabarito fora do ZIP, comparação com oráculo | 7 | Issue a reconciliar/criar; métricas de recall e falsos aceites no envelope do TEST #38, sem inferir certificação |
| **9** | E2E 10k (dez ZIPs de 1k) e depois 100k com Runner V9 | 8 | Issue a reconciliar/criar; duração/memória/integridade medidas, limite operacional de 6h/job, autorização específica de escala quando exigida |

**Precedência técnica original:** 1 → 3 → 4 → 6 → 7 → 8 → 9, com 0, 2 e 5 em paralelo; 0 e 2 bloqueiam 4, 5 bloqueia 6. **Salvaguarda adicionada:** a etapa 6 exige gates técnicos e decisão de habilitação DEV; **nenhum marco deste plano revoga o bloqueio de ativação produtiva ou a necessidade de autorização do PO para mudança de estado**.

## Ajustes aprovados — requisitos obrigatórios de aceite

1. **Precedência oficial:** incorporar como detalhamento da **P0-B**, preservando P0-A → DT-10 → P0-B e reconciliando #1089, #1488 e #1584; não reabrir trabalho concluído nem criar tarefas duplicadas.
2. **Fronteira DEV/produção:** separar capacidade de executar V9 em DEV descartável da autorização para ativação operacional/produtiva; nenhuma remoção indiscriminada do bloqueio `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED`. Habilitação condicionada a adaptador, oráculo, negativos de proveniência, contratos e decisão específica aplicável.
3. **Oráculo realmente independente:** comparar entradas imutáveis, candidatos/arestas, scores, abstenções, agrupamentos e decisões, inclusive fronteiras e casos negativos; registrar divergências e proveniência; não reutilizar a lógica de decisão do Runner como único oráculo.
4. **Gold de pessoas:** matriz explícita entrada → decisão de linkage → estado PROVISORIA/REFERENCIA/INDEFINIDA → resultado API, com negativos, sem candidato, transições e reprocessamento.
5. **Critérios por escala:** 1k prova integração funcional; 10k avalia consistência, qualidade e desempenho; 100k é validação de escala governada, não execução automática após 10k. Medir tempo, memória, integridade e qualidade científica, além do teto 6h/job.
6. **Preservação científica:** além do arquivo original e SHA-256, guardar run/tentativa/workflow/commit, manifesto de proveniência, evidências TRAIN/VALIDATION/TEST pertinentes, recibos, local durável, controle de acesso, recuperação e verificação; confirmar que artefato contém o pacote consumível pelo Runner. Reutilizar #1830–#1835 sem bloquear preservação imediata por arquivamento NAS completo.
7. **Adaptador robusto:** falha intermediária, rollback/publicação parcial, idempotência/reexecução, contagens, estados, concorrência, seleção inválida e integridade referencial devem integrar a matriz negativa.
8. **Seleção segura:** rejeitar arquivo ausente, SHA divergente, pacote incompleto, versão incompatível, manifesto inválido e modelo não autorizado. Runbook antecipado não equivale a ativação.
9. **Dois marcos de aceite:** (a) **E2E integrado do núcleo** = ingestão → processamento → Runner V9 → Gold → APIs principais; (b) **E2E integral do sistema** = núcleo mais Solução de Apoio, `Resultado.Api`, busca semicega e demais contratos exigidos. Frentes paralelas não bloqueiam o primeiro marco, mas são obrigatórias quando integrarem o segundo.
10. **GOV-DOC-01:** registrar contexto, justificativa, impacto, precedência, estados e rastreabilidade no plano, índices e issues/PRs; checks e evidências antes de declarar conclusão.

**Distinções obrigatórias:** integridade operacional ≠ integridade funcional ≠ integridade científica. CLOSEOUT VERIFIED da campanha #38 não certifica Runner V9, DT-15, promoção, ativação ou E2E integral. Não despachar TRAIN/TEST/100k por efeito implícito desta aprovação; respeitar autorizações e preflights vigentes.

## Frentes paralelas originais (preservadas)

| Frente | Rastreio | Aceite |
|---|---|---|
| `Resultado.Api` no E2E | Issue a reconciliar/criar | Resultado de cada Entrega e contrato testados no harness |
| Busca semicega em banco sintético DEV | **Issue específica a identificar/criar após reconciliação; #1348 não é responsável por busca semicega** | Top 5 + “Nenhum destes”, sem score exposto |
| ZIP da Solução de Apoio (`Produtos/ApoioSecretarias`) | #1194 | ZIP aceito pela API no harness |
| OpenAPI bidirecional (runtime → contrato) | Issue a reconciliar/criar | Falha para rota não prevista, incluindo `Resultado.Api` |
| Gate curto calibrador 1k e retomada TEST do vencedor | Issue a reconciliar/criar | Workflow/entrada dedicada, sem despacho científico implícito |
| Folga TRAIN (4h52 de 6h, segundo plano original) | Issue a reconciliar/criar | Tempo por hipótese medido e TRAIN divisível |

## Ações imediatas aprovadas, sujeitas a reconciliação

1. Preservar vencedor e TEST da campanha #38 com proveniência completa.
2. Iniciar etapas 1 e 2 de #1584 em frentes independentes.
3. **Reconciliar antes de criar** issues das etapas 7, 8 e 9; vincular a #1584 ou issue mãe E2E.
4. Reconciliar/criar issue específica de `Resultado.Api`.
5. Preparar procedimento Operador Master (etapa 5), sem afirmar Runner V9 habilitado.

## Riscos originais e controles

- **Expiração Actions (~22/12):** preservar artefato e recibos agora.
- **`RunAsync` V9 sem medição de publicação em volume:** medir 10k antes de propor 100k.
- **Gabarito ausente no contrato HTTP:** gerar ZIPs do gerador sintético e manter verdade fora do ZIP.
- **Perfil `clean` demasiado benigno:** depois do E2E verde, medir `correlated`, sem inferir certificação.
- **Teto de quatro aceites por identidade:** válido apenas no escopo sintético DEV; reavaliar antes de dados reais.

## Fora do escopo DEV original

Autenticação corporativa #378 (necessária fora de Development), calibração com dados reais (modo 3), DT-23 (suficiência), DT-22 (reprocessamento RESOLVIDOS) e arquivamento NAS completo #1831–#1835. Não confundir exclusão do E2E DEV com dispensa de requisitos de produto ou de implantação.

## Precedência, estado e rastreabilidade

- **Precedência:** esta decisão aprova o plano original **com os ajustes obrigatórios acima**; complementa `Plano_Desenvolvimento.md` e decisões P0-B, sem revogar gates científicos, política de LOCKED, seleções DT-15 ou mandatos de CI.
- **Estado:** decisão aprovada; implementação, execução física, medição, validação, certificação, homologação e ativação são estados separados e permanecem pendentes de evidência específica.
- **Referências:** [run #38](https://github.com/lucianox777/Jornada/actions/runs/44970012048), [#1584](https://github.com/lucianox777/Jornada/issues/1584), [#1488](https://github.com/lucianox777/Jornada/issues/1488), [#1830](https://github.com/lucianox777/Jornada/issues/1830), [#1194](https://github.com/lucianox777/Jornada/issues/1194), [#1348](https://github.com/lucianox777/Jornada/issues/1348) (referência histórica equivocada do plano original; escopo vigente é limites operacionais/logs), `V9_Standard_Linear_100k_Resultado_Run_44970012048.md`, `Solution/scripts/local-e2e.sh`, `Indice_Decisoes_Vigentes.md`, `Politica_Registro_Decisoes_Discussoes.md`.

## Reconciliação de rastreabilidade posterior à aprovação (22/11/2026)

A leitura da issue #1348 na master demonstrou que seu escopo vigente, aprovado em 21/11, é **configuração de limites operacionais e logs**, e **não** busca semicega. A referência original foi preservada como fato histórico, mas **não constitui vínculo de execução válido**. A frente de busca semicega continua aprovada e necessita localizar sua issue responsável antes de criar uma nova. A issue #1194 trata da autorização por credencial BENEFICIO/SERVICO do Apoio e é dependência relevante, mas não equivale por si só à prova de ZIP E2E. A #1830 proíbe iniciar implementação de arquivamento NAS sem autorização posterior; a aprovação deste plano não revoga essa restrição. Preservação imediata de artefatos deve observar acessos e autorizações aplicáveis.

## Diretriz operacional do PO — execução em frentes paralelas (22/11/2026)

**Decisão aprovada:** o trabalho do E2E integral deve avançar em frentes paralelas, com entregas e evidências independentes e integração posterior no marco E2E do sistema inteiro. **Contexto:** a PR #1852 (guards V9) aguarda CI e a PR draft #1768 tem conflitos; serializar todo o sistema no bloqueio do Runner atrasaria superfícies sem dependência técnica direta. **Justificativa:** aproveitar capacidade de execução simultânea sem relaxar gates nem misturar validações.

| Frente | Issue | Dependências e limite de aceite |
|---|---|---|
| A — Runner V9, arestas, oráculo e adaptador | #1584, #1488, PR #1768, PR #1852 | Reconciliar código existente, CI e SQL descartável; sem TRAIN/TEST/100k ou ativação implícita. |
| B — E2E do núcleo HTTP/Gold/Runner e gabarito | #1851 | Pode preparar fixtures/contratos; prova integral depende do adaptador aprovado da frente A. |
| C — Resultado.Api e OpenAPI bidirecional | #1853 | Testes de contrato e integração sintética independentes; evidência por SHA. |
| D — Busca semicega | #1854, #612, #539 | DEV sintético; não habilitar HML/PROD sem política institucional e gates de visibilidade. |
| E — ZIP Apoio/ingestão | #1855, #1194 | Preparar testes sem envio inseguro; `enviar=false` até autorização por recurso aprovada e integrada. |

**Precedência:** complementa a decisão GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122 sem revogar P0-A → DT-10 → P0-B, contratos científicos, bloqueios de produção, governança #1830 ou os critérios 1k→10k→100k. Cada frente mantém branch/PR/CI, testes negativos e evidência no SHA exato; conflitos e alterações de contrato exigem revisão. Integração final é condição para o marco **E2E integral do sistema**, enquanto o núcleo tem marco próprio. **Impactos:** trabalho simultâneo, menor dependência do CI do Runner, rastreabilidade e aceites separados. **Estado:** organização aprovada e issues abertas; implementação e validação dessas frentes ainda não concluídas. GOV-DOC-01.

## Implementação incremental da etapa 0 — preservação local verificável (22/11/2026)

**Contexto e precedência:** a etapa 0 aprovada exige preservar o vencedor da campanha #38 e as evidências de TEST antes da expiração dos artefatos GitHub Actions, sem confundir o arquivamento imediato com o projeto institucional NAS #1830 (que proíbe iniciar sua implementação sem autorização específica). O inventário da execução `44970012048`, tentativa 1, SHA `30c71a0db1bdeac6ba536db5e428ade4d5c70c9c`, confirmou 14 artefatos disponíveis, todos com expiração informada para **22/12/2026**. O pacote do vencedor é `14742452561`, SHA-256 do ZIP `24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7`; o arquivo operacional interno é `6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee.operational.json` com SHA-256 igual ao nome.

**Implementação proposta:** `Solution/scripts/v9-preserve-campaign38-artifacts.py` fornece inventário sem rede/escrita por padrão e cópia opt-in (`--execute`) dos 14 ZIPs para **diretório privado já existente** indicado pelo operador. Antes de copiar, confere run/tentativa/SHA/conclusão, existência e não expiração de todos os artefatos; cada download é validado por SHA-256 esperado e CRC de ZIP, e o vencedor por SHA-256 interno. Arquivos são movidos de temporário para nome final somente após verificação; manifesto `campaign38-preservation-manifest.json` é emitido exclusivamente após todos os ZIPs terem sido verificados. A execução parcial não recebe manifesto COMPLETE. Testes offline: `Solution/tests/scripts/test_v9_preserve_campaign38_artifacts.py`.

**Limites e impacto:** nenhum download, cópia durável, recuperação, replay, retenção NAS ou certificação foi realizado nesta PR; `--execute` exige token e destino privado, e o operador deve atestar espaço, controles de acesso e durabilidade antes da execução. Não inclui PII real, não dispara TRAIN/TEST, não publica/ativa modelo e não substitui as cinco entregas institucionais da #1830. **Estado: ferramenta e testes propostos; preservação física ainda PENDENTE**, inclusive verificação pós-cópia em destino institucional. Rastreabilidade: run #44970012048, issue #1830 apenas como referência de fronteira, etapa 0 deste plano, PR desta branch. GOV-DOC-01, sem nova decisão do PO.

## Revisão de segurança da ferramenta da etapa 0 — 22/11/2026 (PR #1913)

**Falha identificada em inspeção do código da própria PR:** `urllib.request.HTTPRedirectHandler.redirect_request()` copia cabeçalhos da requisição original ao seguir um 302. Como o endpoint autenticado do GitHub Actions redireciona o ZIP para armazenamento de resultados externo, a implementação inicial poderia encaminhar o bearer token do GitHub ao host do ZIP. Além disso, `Path.resolve()` era chamado antes de testar `is_symlink()`, mascarando um diretório de destino fornecido por symlink. A análise é uma verificação estática de comportamento da biblioteca Python, não alegação de exposição real de credenciais.

**Correção e validação proposta:** `SafeArtifactRedirectHandler` exige HTTPS sem credenciais embutidas e retira `Authorization` e `X-GitHub-Api-Version` em redirecionamentos para outro host/porta; mantém a autenticação em redirecionamentos de mesma origem. O teste do destino simbólico ocorre **antes** de `resolve()`. Adicionadas quatro regressões offline: retirada do token em URL assinada externa, preservação em mesma origem, rejeição de downgrade/credenciais embutidas, e rejeição de symlink sem iniciar chamadas HTTP. Como a CI de PR executava apenas uma seleção de arquivos Python, essa nova suíte não era chamada automaticamente; a PR passa a invocar explicitamente `test_v9_preserve_campaign38_artifacts.py` no job obrigatório `unit-runtime` de `.github/workflows/jornada-pr-required-fast.yml`, sem tocar nas demais verificações nem acionar download externo. A leitura do run REST original confirmou `workflow_id=420729901`, `path=.github/workflows/v9-standard-linear-100k.yml`, evento `workflow_dispatch` e branch `master`: esses atributos agora constam do inventário/manifesto e são validados contra a origem antes dos downloads. O manifesto concluído inclui ainda número de execução, usuário responsável e nome/tamanho/expiração de cada artefato observado. Regressão offline adicional rejeita execução com workflow divergente, ainda que run/tentativa/SHA coincidam. O plano, os SHA-256 esperados, a política de manifesto completo e as restrições de `--execute` permanecem inalterados.

**Impacto, precedência e estado:** endurecimento de segurança da implementação já aprovada, sem nova decisão técnica/científica do PO ou alteração dos critérios da etapa 0. Mantém GOV-DOC-01, isolamento da #1830, e integra somente após os checks do novo HEAD. Nenhum ZIP foi copiado nem houve verificação de durabilidade, replay, TEST, TRAIN, HML/PROD ou ativação; o risco de expiração em 22/12/2026 persiste enquanto a cópia institucional não ocorrer.

## Implementação parcial da etapa 2 — oráculo numérico de coorte TEST_ONLY (22/11/2026)

**Contexto e justificativa:** a frente A da P0-B (#1584) exige oráculo independente que confronte candidatos, scores, abstenção, agrupamento e decisão, sem circularidade com `ReplayTest`. Já existem os testes isolados `V9IndependentScalarReplayTests`, `V9IndependentRankedDecisionOracleTests` e `V9IndependentCompleteLinkOracleTests` na master; nenhum deles, sozinho, comprova o encadeamento de decisões de uma coorte a partir de expectativas não produzidas pelo Runner. A nova regressão **não altera o algoritmo**: avança uma cobertura parcial da etapa 2, preservando as demais dependências do plano.

**Entrega desta PR:** duas regressões NUnit adicionadas a `Solution/tests/Jornada.Tests/Unit/V9IndependentScalarReplayTests.cs`: (a) quatro observações sintéticas, organizadas em dois pares, com expectativas de posterior obtidas pela fórmula fechada de odds, estados RESOLVIDO e IDs de grupos constantes previamente definidos; (b) ausência de arestas SQL implica abstenção por observação, sem formar pares a partir dos rótulos de verdade, e permutação dos rótulos, das observações e das arestas não pode alterar as decisões. Os cálculos esperados não chamam `ReplayTest`, `RankPreview`, agrupamento, Gold ou verdade sintética como fonte de decisão. O executor submetido à conferência é `V9FrozenSourceResolveReplay.ReplayTest` sobre uma coorte `TEST` de quatro linhas, modelo congelado TF0 e limites de agrupamento fixados.

**Limites de prova:** suíte **TEST_ONLY pequena e offline**; sem SQL físico, sem consulta de blocking real, sem TF4 ativo nas quatro unidades, sem publicação, sem `ProbabilisticLinkageBatchRunner.RunAsync`, sem prova de seleção MASTER nem equivalência integral com o Runner. Não substitui o oráculo TF4/81 completo nem as etapas 3–9. Não executa TEST científico one-use, 10k, 100k, TRAIN ou ativa V9. Os checks da PR no HEAD exato permanecem obrigatórios; sucesso de teste unitário não constitui certificação científica/produtiva.

**Precedência e rastreabilidade:** implementação incremental da decisão já aprovada `GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122`, etapa 2; issue #1584; marco E2E núcleo #1851; GOV-DOC-01. Nenhuma nova decisão do PO ou alteração do índice de decisões vigentes é requerida; esta anotação preserva contexto, escopo e estado **IMPLEMENTADO NA BRANCH / VALIDAÇÃO E INTEGRAÇÃO PENDENTES**.

**Correção de compilação de 22/11/2026:** o primeiro HEAD de testes (`7894928714d2314cb6f6ed754b9ce9a158ab8cc7`) falhou no smoke físico não publicador `45068450600`, job `137349785415`, ainda na etapa `Build real Runner and SQL fixture`: `CS1729` em duas instâncias de `new Guid(...)` com dez, em vez dos onze argumentos exigidos. Corrigidos os dois literais de IDs de grupos no mesmo teste (commit `c6a510c26296ec625d36c95dd631f516fb487443`), sem alterar a lógica de conferência; repetir a CI do novo HEAD é obrigatório. Nenhum SQL foi iniciado no run falho, pois a falha ocorreu no build. A prova física de Runner e os gates continuam distintos da regressão unitária.

**Gate de execução das regressões no PR:** a inspeção do `jornada-pr-required-fast.yml` confirmou que os checks obrigatórios compilavam toda a solução, mas o job `deterministic-build` aplicava filtros NUnit que **não incluíam** `V9IndependentScalarReplayTests`. O compilador verde, portanto, não provaria a execução dos novos casos. Esta PR inclui explicitamente `dotnet test ... --filter 'FullyQualifiedName~V9IndependentScalarReplayTests'` no job `deterministic-build`, sem SQL, TEST científico nem alteração de gates de proteção. A validação física desses casos só pode ser declarada após resultados desse job para o HEAD final. Evidência: commit `1e51190be323ca9ebf3942dcc7a961c0999ea476`.

## Ampliação controlada da etapa 2 — TF4 ativo e abstenção fail-closed (22/11/2026)

**Contexto:** o primeiro recorte da PR #1914 validou dois pares TEST_ONLY em TF0 e o comportamento de ausência de arestas, mas **não cobria os quatro slots TF independentes**. No HEAD `fe1dc00187c111da067b1cacdf3c98c309c84477`, os três checks obrigatórios `unit-runtime`, `deterministic-build` e `security-guards` e o smoke `RunAsync` não publicador concluíram **success**, runs `45068703747` e `45068703768`; no log do `deterministic-build` (job `137350693423`), a seleção `V9IndependentScalarReplayTests` executou **14 testes, 14 PASS**, sem skipped. Esses resultados **não certificam** Runner V9, TF4 completo ou SQL E2E.

**Incremento implementado nesta mesma PR:** teste parametrizado em quatro combinações dos slots ativos `(Pessoa,Mãe) × (Prenome,Sobrenome)` da fixture congelada `CompleteTf4OperationalWinner` (81 células M/U), com expectativas de níveis nominais fixadas pelo caso e odds calculadas explicitamente a partir de **prior, M/U da célula, U_ref TRAIN e frequência publicada**, sem invocar o scorer/ranker de produção para obter o esperado. Compara posterior e log-odds contratual em quatro pares sintéticos. Outro teste cria cópia imutável do fit com política `AbstainCandidate`, exige abstenção e motivo `TF_SEM_FREQUENCIA_IBGE_PUBLICADA` para prenome não publicado, e verifica `RejectOperation` fail-closed no mesmo dado — nunca substitui o TF ativo por TF0. Código: `Solution/tests/Jornada.Tests/Unit/V9IndependentScalarReplayTests.cs`, commit `3b0a73a996b0449fae329938e252cce1ce92a48d`. O filtro NUnit explícito do workflow obrigatório já executa toda essa classe.

**Impactos e limites:** ampliação apenas da **prova unitária numérica** da etapa 2, sem mudança no runtime, seleção do vencedor, distribuição científica, SQL ou workflows científicos. Não prova origem de candidatos/arestas SQL, 81 células exaustivas com TF ativo, replay físico de TEST, paridade integral do `RunAsync`, 1k/10k/100k nem ativação. Os checks do **novo HEAD**, após a ampliação, devem ser confirmados separadamente; resultados anteriores não servem como prova. GOV-DOC-01: rastreabilidade da decisão já aprovada e do trabalho, sem criar nova decisão ou alterar precedência no índice.

**Refinamento preventivo das fixtures TF4 (22/11/2026):** inspeção do algoritmo efetivo `CompositeNominalComparisonV9.Classify` e conferência independente do limiar Jaro-Winkler `0.92` mostraram que pares nominalmente curtos como `ANA COSTA`/`ANA MOTA` e `MARIA SILVA`/`MARIA LIMA` seriam classificados `HighFull`, não `ExactFirst`. Para garantir que cada caso exercite o slot TF esperado, foram substituídas somente as strings de dados sintéticos por `ANA PEREIRA` e `MARIA PEREIRA` (e, no teste de termo não publicado, por `INEDITA RIBEIRO`), que têm similaridade abaixo do limiar e preservam o evento nominal explicitamente esperado. Nenhuma regra de classificação, threshold, TF, Runner ou ciência foi alterada. Commit `d1bdc93e65b1d3e2fa0f81ac6e73fc588b9812b7`; checks do HEAD resultante continuam exigidos.

## Conferência não circular contra SQL físico do `RunAsync` — etapa 2, incremento 22/11/2026

**Contexto e causa:** o smoke SQL de 200 linhas em `V9FullSchemaNonPublishingRunnerTests` comprovava persistência e comparava por observação os resultados com `pinned.ReplayTest`. Porém `ReplayTest` reutiliza o **mesmo** scorer FS/TF4 e agrupamento da execução; portanto, essa comparação é circular para o requisito de oráculo independente de #1584. O smoke também descrevia o segundo par como `ExactLast` em ambas as pessoas, mas sua mãe `COSTELA COSTA`/`COSTURA COSTA` atinge Jaro-Winkler >0,92 e vira `HighFull`, sem percorrer o slot TF ativo do sobrenome materno. Isso representa uma lacuna de cobertura da fixture, não prova de erro produtivo.

**Mudança:** adicionar `AssertNonCircularSqlCandidateOracleAsync` ao próprio teste de banco `JornadaHarness` descartável, executado sobre **ambas as execuções físicas de `ProbabilisticLinkageBatchRunner.RunAsync`**, com lote de 100 e de 200. O oráculo recebe apenas o vencedor imutável e os valores explicitamente conhecidos da fixture: arestas dirigidas (1↔101 e 2↔102), nível nominal esperado para cada par (`ExactFull` ou `ExactLast`), M/U de uma célula do array 81, prior, razão de nascimento exato, U_ref TRAIN congelado, frequência nominal publicada e peso por lado/unidade. Calcula posterior por odds com fórmula própria, sem chamar `ReplayTest`, `RankPreview`, scorer ou grouping para calcular o resultado esperado. Confere as **200 linhas persistidas**, os quatro melhores candidatos e scores, o estado `NAO_RESOLVIDO` sob threshold, as 196 abstenções sem candidato, os motivos e ausência de vinculação resolvida ou linhas excedentes. O comparador compartilhado `ReplayTest` é mantido como segunda referência diagnóstica, **explicitamente não independente**.

**Correção mínima da fixture:** `SyntheticMotherName(102)` muda de `COSTURA COSTA` para `COSTUMO COSTA`: mantém o prefixo de blocking `COST` e o sobrenome `COSTA`, mas fica abaixo de Jaro-Winkler 0,92 contra `COSTELA COSTA`, de modo que o caso exercite `ExactLast` nos dois lados. Se a consulta SQL deixar de retornar as duas arestas ou a semântica mudar, o smoke deve falhar em vez de ampliar permissões. Nada altera o algoritmo ou os dados protegidos.

**Precedência e impacto:** implementação incremental da etapa 2 já aprovada do plano `GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122`, issues #1584/#1851, GOV-DOC-01. Commit inicial `b78d7621227b2ce924b9c4674d0289efa74f8675`. Necessária validação do **HEAD final** no job `full-schema-runasync-nonpublish` e nos três checks obrigatórios; o resultado anterior de `45069439148` não certifica esta alteração. Não demonstra Runner V9 produtivo, grafo grande, autorização TEST/100k, publicação, ativação ou E2E HTTP/Gold. Estado: **implementação na PR; integração e validação pós-merge pendentes**.

## Incremento da etapa 8 — verificação CRC de todos os membros ZIP do E2E externo (22/11/2026; PR #1909)

**Contexto e justificativa:** o SHA-256 externo do ZIP confirma os bytes do contêiner vinculados ao gabarito, mas não demonstra que todos os membros do contêiner descomprimem sem corrupção. A implementação anterior de `e2e-external-truth-boundary.py` apenas lia conteúdos de membros cujo tamanho coincidia com o tamanho do gabarito externo. Um membro de tamanho distinto e CRC inválido poderia, portanto, passar pelo preflight apesar do SHA-256 estar corretamente atualizado no gabarito. É lacuna de cobertura do preflight, **não** incidente ou medição física de produção.

**Correção restrita:** a PR #1909, que reconcilia a PR anterior #1907 após proteção de hardlinks de #1903, faz leitura integral em blocos de 64 KiB de **todos os membros não diretório** após validação de nomes únicos, caminhos, criptografia e tipos Unix, antes da comparação de conteúdo com o gabarito externo. `zipfile` verifica CRC no término dessa leitura. A regressão `Solution/tests/scripts/test_e2e_external_truth_zip_crc.py` usa payload sintético com um byte corrompido e SHA-256 externo recalculado de propósito, exige `BadZipFile` por CRC, e verifica o caminho íntegro `PASS`. A suíte é explicitamente convocada pelo job obrigatório `unit-runtime` de `.github/workflows/jornada-pr-required-fast.yml`. A mudança adiciona custo de I/O e descompressão proporcional ao contêiner; não altera limite de recursos do host, contrato de identidades, scorer, SQL nem regras científicas.

**Estado e limites:** implementação técnica de etapa 8 do plano **já aprovado** pelo PO (issues #1851/#1584), sem decisão nova de arquitetura, estatística ou governança. Integração exigirá novo `unit-runtime`, `deterministic-build` e `security-guards` sobre o HEAD exato após reconciliação com a `master`, squash e prova pós-merge. Preflight ZIP não é oráculo de score/cluster, E2E físico completo, certificação de Runner V9 ou autorização para TRAIN/TEST/100k/HML/PROD. **Precedência:** GOV-DOC-01, GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122; rastreabilidade PRs #1907/#1909 e issues #1851/#1584.

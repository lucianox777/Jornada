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

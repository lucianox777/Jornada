# GOV-V9-MANDATO-E2E-CONTINUO-20261126 — execução contínua do E2E integral DEV V9

**Autoridade e fonte.** Product Owner, aprovação expressa de 26/11/2026 no mandato transcrito integralmente no anexo. **Estado.** DECIDIDO. A integração documental, a execução e a validação física dos gates têm evidências próprias.

O PO delegou ao Codex local a execução contínua do caminho crítico do E2E integral DEV V9, começando pela Fase A na PR #1940. A execução depende da sessão ativa e usa o checkpoint existente na #1851 para retomada. A suspensão de automações continua vigente. As Fases B, C e D ficam postergadas até decisão expressa do PO.

A decisão complementa o [plano do E2E de 22/11](Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md) e o [plano v8](https://github.com/lucianox777/Jornada/blob/744dd504ef290e20e139b90ce7030aa5b0d43ca0/Solution/docs/Decisao_GOV_V9_Plano_PO_V8_20261125.md). Prevalece sobre os trechos incompatíveis do [regime de ciclos](https://github.com/lucianox777/Jornada/blob/744dd504ef290e20e139b90ce7030aa5b0d43ca0/Solution/docs/Decisao_GOV_V9_Plano_PO_V8_Regime_Ciclos_20261126.md) quanto a executor, continuidade e escalada por defeito. A delegação do disparo final vale somente para este E2E DEV, com a credencial do PO e as guardas do próprio workflow. A evidência de SHA-256 14/14 da guarda declarada pelo PO basta para a missão e a disponibilidade operacional continua sujeita à pinagem dos dois hashes e ao prazo do anexo. Os demais controles do projeto continuam aplicáveis.

**Rastreabilidade.** [PR #1940](https://github.com/lucianox777/Jornada/pull/1940), [checkpoint #1851](https://github.com/lucianox777/Jornada/issues/1851), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## Registro da parada operacional de 26/11/2026

O §3.f veda disparar TRAIN, VALIDATION, TEST e calibração nesta missão. A [configuração vigente na master 0a29d560](https://github.com/lucianox777/Jornada/blob/0a29d560558c30349d88da7935bc22ce4f649b20/.github/workflows/v9-v3-broad-train-regression.yml) aciona o job `broad-physical` ao receber um push de código na master. Esse caminho gerou TRAIN sintético em seis braços [na execução 45631879433](https://github.com/lucianox777/Jornada/actions/runs/45631879433) e inclui a chamada `--v9-calibrate`. A [validação independente](https://github.com/lucianox777/Jornada/blob/0a29d560558c30349d88da7935bc22ce4f649b20/.github/workflows/linkage-independent-validation.yml) também chama `calibrate` no push quando o diff inclui arquivos do Runner, como no HEAD da #1940. Na própria #1940, o diff atual aciona ainda o [contraste experimental de 1k](https://github.com/lucianox777/Jornada/actions/runs/45832006993), que executa matriz S4b, fora do escopo desta missão.

Aplica-se a parada do §9.i. O procedimento de squash e validação exige decisão do PO sobre os jobs automáticos incompatíveis com o escopo DEV autorizado. O registro documental pode seguir a classificação docs-only existente, pois o diff contém somente os índices e esta decisão. [Checkpoint de retomada e evidências](https://github.com/lucianox777/Jornada/issues/1851#issuecomment-7198120173).

## Esclarecimento do PO de 26/11/2026 — CI automática vigente e retomada da Fase A

**Autoridade e estado.** O Product Owner esclareceu expressamente o GOV-V9-MANDATO-E2E-CONTINUO-20261126 para resolver a parada do §9.i. **DECIDIDO.** O esclarecimento autoriza a retomada imediata da Fase A na #1940 e prevalece sobre a interpretação registrada na parada histórica acima. O mandato original permanece no anexo.

Workflows de CI já existentes acionados automaticamente por push, PR ou merge seguem normalmente, incluindo a matriz S4b 1k (#1939), `v9-v3-broad-train-regression` e o gate técnico Seed42 pós-merge. Para o mandato, esses jobs são regressões da CI vigente. Seus resultados temporários não produzem nem substituem modelo oficial, não consomem TEST/holdout e não alteram o vencedor #38. A execução automática prevista não abre as Fases B/C nem constitui nova campanha, TRAIN ou calibração proibida pelo mandato.

As proibições de TRAIN, VALIDATION, TEST, calibração e campanha continuam aplicáveis a disparos manuais, workflows novos e mudanças de gatilho. Os gatilhos vigentes permanecem em operação e são preservados, sem desativação ou contorno. Falhas da CI vigente seguem o ciclo normal de diagnóstico, correção e validação, com M/U, TF, limiares, política oficial e vencedor #38 preservados. Continuam aplicáveis os gates de evidência da Fase A, os controles de segurança e os demais pontos de parada.

**Rastreabilidade.** [Parada histórica na #1851](https://github.com/lucianox777/Jornada/issues/1851#issuecomment-7198240514), [Fase A na #1940](https://github.com/lucianox777/Jornada/pull/1940), [regressão S4b 1k integrada na #1939](https://github.com/lucianox777/Jornada/pull/1939).

## Esclarecimento do PO de 26/11/2026 — retomadas na mesma sessão e acompanhamento da CI

**DECIDIDO pelo PO.** Para reduzir leituras repetidas e consultas à CI, ler as fontes do mandato na abertura de cada sessão. Em retomadas dentro da mesma sessão, consultar o checkpoint da #1851 e os arquivos necessários à tarefa atual. Para qualquer workflow do GitHub Actions, incluindo checks da PR, escala, `jornada-ci`, Diagnostics, regressões automáticas de TRAIN e E2E, aguardar com `gh run watch` bloqueante. Consultar os jobs e recibos pertinentes ao concluir. Durante a espera, dispensar laços de consulta e releitura de documentos. Quando não houver tarefa independente, comunicar `aguardando run X, sem tarefa independente` para que o PO possa decidir se pausa. O esclarecimento delimita o procedimento de leitura e espera nesta missão. Preserva a checagem do estado vivo, a reconciliação por SHA, a revisão de gates e as condições de publicação do mandato.

## Anexo integral aprovado pelo PO

# MANDATO — JORNADA V9 — CAMINHO CRÍTICO DO E2E INTEGRAL

**APROVADO PELO PRODUCT OWNER EM 26/11/2026 — AUTORIZAÇÃO DE EXECUÇÃO IMEDIATA.**

**Executor:** Codex local (WSL), execução contínua.  
**Decisão:** GOV-V9-MANDATO-E2E-CONTINUO-20261126.

## 1. OBJETIVO

Comprovar fisicamente, na CI, o E2E integral DEV do Jornada do Cidadão (GOV-DEV-E2E-INTEGRAL-V9-PLANO-20261122), começando pela Fase A do plano v8 (PR #1940), com S4b(i), b1, r1, contratos científicos e o vencedor da campanha #38 inalterados.

Entrega final: workflow E2E integral DEV reexecutável, integrado à master protegida e validado fisicamente.

Trabalhar continuamente, uma frente principal por vez, até a conclusão ou uma parada da seção 9. Etapa intermediária concluída não é parada. Não pedir confirmação para o que este mandato delega.

Distinguir: E2E do núcleo, E2E integral, qualidade experimental, certificação científica, certificação produtiva e ativação; um marco não implica os demais.

O objetivo é provar o sistema com as decisões vigentes, não alterá-las para obter aprovação.

## 2. FONTES E PRECEDÊNCIA

Ler na branch da frente ativa e confrontar com a master; precedência conforme `Indice_Decisoes_Vigentes.md`.

### Fontes obrigatórias

- `AGENTS.md`
- `Solution/docs/Indice_Decisoes_Vigentes.md`
- `Solution/docs/Indice_Acervo_Documental.md`
- `Solution/docs/Politica_Registro_Decisoes_Discussoes.md` (GOV-DOC-01)
- `Solution/docs/Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md` (protocolo de 13 etapas, prevalece sobre resumos)
- `Solution/docs/Mandato_IA_Ciclo_Desenvolvimento_Continuo.md`
- `Solution/docs/Decisao_GOV_CI_Docs_Only_Fast_20261119.md`
- `Solution/docs/Decisao_GOV_OPS_Suspensao_Automacoes_Jornada_20261122.md`
- `Solution/docs/Plano_Desenvolvimento.md`
- `Solution/docs/Dividas_Tecnicas.md`
- `Solution/docs/Decisao_GOV_DEV_E2E_Integral_V9_Plano_Aprovado_20261122.md`
- `Solution/docs/Decisao_GOV_V9_Plano_PO_V8_20261125.md`
- `Solution/docs/Decisao_GOV_V9_Plano_PO_V8_Emenda_Familia_20261125.md`
- `Solution/docs/Decisao_GOV_V9_Custodia_Vencedor38_20261125.md`
- `Solution/docs/Decisao_GOV_V9_Plano_PO_V8_Regime_Ciclos_20261126.md`
- `Solution/docs/V9_Campanha_38_Preservacao_SHA256_Guarda_Duravel_Status_20261123.md`
- `Solution/docs/Plano_V9_Fase4_10k_20261125.md`
- `Solution/docs/Relatorio_V9_Ensaio_10k_20261125.md`
- `Solution/docs/V9_Standard_Linear_100k_Resultado_Run_44970012048.md`
- `Solution/database/postrelease/PublicationScale_20261125.md`
- `.github/workflows/jornada-e2e-integral-v9-dev.yml`
- Último checkpoint da #1851.

### Referências

- #1851 — checkpoint/núcleo.
- #1584 — arestas/Runner.
- #1488 — seleção do modelo.
- #1931 — escala.
- #1936 — índice de blocking.
- #1853 — Resultado.Api/OpenAPI.
- #1854/#612/#539 — busca semicega.
- #1855 — Apoio ZIP.
- #1194 — autorização por recurso.
- #1345 — matriz de cobertura.
- #1830 — custódia institucional.
- #1940–#1943.

Preservar a sequência de governança P0-A → DT-10 → P0-B e as dependências do Plano de Desenvolvimento.

SHAs, runs e estados citados são pontos de partida: confirmar no GitHub. Não presumir que documento só em PR aberta esteja na master. Não resolver contradição material em silêncio (9.i).

## 3. PRIMEIRA TAREFA — REGISTRO (GOV-DOC-01), SEM PEDIR NOVA CONFIRMAÇÃO

Registrar GOV-V9-MANDATO-E2E-CONTINUO-20261126 com este mandato anexado na íntegra e índices atualizados; sessões futuras recuperam o mandato do repositório.

Qualquer nova decisão do PO durante a missão: registrar localmente antes da atividade correspondente, publicar na primeira oportunidade segura (3.e) e integrar antes de encerrar a atividade.

### a) Executor

Codex local contínuo; ChatGPT deixa de ser executor (segue apoiando o PO).

No Regime de Ciclos de 26/11, substituir só o incompatível com execução contínua, continuidade entre ciclos e etapas sem nova mensagem do PO, e a regra 3.d; o resto vale.

Não presumir execução após encerrar o processo; não criar monitores nem reativar automações suspensas.

### b) Emenda ao v8

Objetivo operacional = caminho crítico do E2E integral.

Fase A obrigatória com todos os gates.

Fases B, C e D postergadas e fora da missão (não implementar gerador familiar, gerar massas, rodar matrizes nem avaliar política alternativa).

Só retornam por decisão expressa do PO; a demonstração de dependência técnica (9.e) determina parada e submissão ao PO, não autorização automática.

### c) Frentes paralelas

GOV-DEV-E2E-INTEGRAL-V9-PARALELISMO-20261122: executadas sequencialmente por este executor, na ordem do caminho crítico; uma frente principal por vez; durante a CI, só diagnóstico, reprodução, revisão, documentação e preparação em worktree separada.

Não fechar PRs antigas; não repetir trabalho concluído.

### d) Três abordagens malsucedidas no mesmo defeito

Registrar na #1851, classificar ESCALADO AO PO, suspender aquele defeito, bloquear dependentes e seguir com itens independentes; parar só se nada independente restar (9.g).

Contam abordagens efetivamente distintas, publicadas na CI ou refutadas localmente de forma conclusiva, desde que a reprodução local seja representativa do mecanismo que falhou (ex.: defeito determinístico em SQL descartável, teste que refuta a correção).

Não contam:

- Retry idêntico.
- Reinício de sessão.
- Patch superficial.
- Mesma hipótese.
- Braços da mesma run.
- Defeitos independentes.
- Investigação inconclusiva.
- Timeout local por recursos diferentes.
- Problema específico do runner reproduzido só localmente.
- Falha por configuração local diferente.

Contador persistente por defeito.

### e) Documentação × CI

Push numa PR re-dispara e pode cancelar workflows pesados (paths sobre o diff inteiro + cancel-in-progress).

Antes de cada push, verificar runs ativas, SHA, evento e concorrência.

Commits documentais ficam locais durante runs pesadas e vão com o próximo push seguro de código; sem push de código previsto, publicar assim que nenhuma run pesada for afetada; sem adiamento indefinido.

PR com código não vira docs-only por um commit documental.

### f) Workflows

Autorizado usar GitHub CLI e APIs para disparar (inclusive workflow_dispatch), acompanhar, analisar e, quando permitido, reexecutar workflows necessários à missão, sem confirmação do PO, restrito a DEV com dados sintéticos e infraestrutura descartável.

Publicação operacional interna ao ambiente de teste (Bronze, Silver, Gold, linkage, Serving) é permitida para a prova funcional; publicação institucional/científica, HML, PROD e ativação são proibidas.

Antes de cada disparo, verificar:

- Escopo.
- Workflow.
- Ref.
- SHA exato.
- Parâmetros.
- Permissões.
- Preflight.
- Dependências.
- Isolamento.
- Execuções concorrentes.
- Risco de cancelar runs válidas.
- Limites de recursos.

Não duplicar execuções; reexecutar sem mudança de código só com causa registrada, respeitando o watchdog.

Registrar na #1851 workflow, parâmetros não sensíveis, SHA, run_id, run_attempt, jobs, resultado e evidências.

Vedado disparar TRAIN, VALIDATION, TEST, calibração, campanha científica, 100k, HML, PROD ou ativação.

Não alterar gates, permissões, proteções ou workflows para contornar restrições.

Permanecem válidas as restrições de todos os demais workflows reservados ao PO; a única exceção é 3.g.

Ter acesso técnico à credencial do proprietário não autoriza executar outros workflows reservados.

### g) Disparo final do E2E

O PO delega ao Codex o disparo do workflow E2E integral DEV, inclusive a validação final na master protegida (modifica, só nisso, GOV-DEV-E2E-WORKFLOW-ENTREGA-FINAL-20261122).

Antes, verificar a identidade autenticada no gh e as permissões efetivas.

Preservar as guardas de ator, repositório, workflow_dispatch, refs/heads/master, proteção de branch, SHA exato, HEAD remota, isolamento e segurança.

Usar só a credencial delegada do PO; não usar outra credencial nem modificar guardas de identidade; nunca imprimir ou registrar tokens.

Registrar na #1851 que o disparo foi feito pelo Codex, por autorização expressa do PO, com credencial delegada, informando o ator registrado pelo GitHub, SHA, workflow, parâmetros e run.

Sem credencial compatível: registrar e aplicar 9.f.

Delegação restrita a esta missão.

### h) Custódia do vencedor #38

Por decisão expressa do PO, a verificação SHA-256 14/14 feita pelo PO em 23/11/2026, por rotina equivalente, sobre a guarda durável declarada (`V9_Campanha_38_Preservacao_SHA256_Guarda_Duravel_Status_20261123.md`), é evidência suficiente de custódia durável para esta missão e prevalece sobre exigências anteriores incompatíveis de comprovação adicional da guarda privada.

Não constitui certificação do NAS, do meio de armazenamento nem da capacidade institucional de recuperação.

Não exigir o recibo do script oficial nem acessar o local privado do PO; não implementar o NAS (#1830); não publicar bytes protegidos.

O Codex garante a disponibilidade operacional:

- Cópia renovada: `15076996165`.
- Run: `45731110961`.
- Envelope SHA-256:

`2293cfd88020516824079550c17b5d3a9b722413bd4202a9057d9a3975f6b920`

Após recuperação, verificar ambos os hashes científicos:

ZIP:

`24a890243dff4d988515ac17532ee3720bff0c0b01261aa36c9e0d74426d5de7`

JSON operacional:

`6655a82cdda7f46f95896ce0a9abe26cc17ff290665a8347adb1a13683ec68ee`

Conferir com os pins oficiais.

Comprovar a pinagem dos consumidores, sem fallback silencioso, até **17/12/2026 06:09:48 UTC**.

Depois disso, acompanhar disponibilidade e expiração durante toda a missão e aplicar 9.d se faltarem menos de cinco dias para a expiração da última cópia operacional autorizada e verificada, independentemente das Fases B e C.

Não substituir o vencedor por nova campanha, refit ou artefato alternativo.

Comunicar qualquer necessidade do PO assim que identificada.

### i) Autoridade das evidências

Reprodução local (Docker + SQL Server descartável) serve a diagnóstico e economia de CI; paridade, negativos físicos, curva, FULL, integração e E2E só são aceitos com evidência da CI.

### j) Autenticação e autorização

Autenticação corporativa, IdP institucional e #378 (encerrada, not planned) estão fora do escopo; em DEV, só mecanismos sintéticos existentes.

A autorização por recurso da #1194 (credenciais BENEFICIO/SERVICO, sem fallback GESTOR) é dependência do E2E da Solução de Apoio, e o executor fica autorizado a concluir suas pendências de implementação, testes, documentação, integração e validação, nos limites do requisito aprovado pelo PO em 25/10/2026.

Nova política de autorização não contemplada vai ao PO (9.a).

## 4. SEQUÊNCIA

Encadeada; sem parada entre etapas com pré-requisitos satisfeitos.

### 4.0. Inicialização

Confirmar WSL, diretórios, Git, gh, Docker, SQL Server, .NET, autenticação e permissões; ler as fontes; recuperar o checkpoint #1851; reconciliar master, PRs, branches, worktrees, commits locais, runs e artefatos; verificar a custódia; comprovar isolamento (seção 5); registrar a decisão (seção 3); iniciar a primeira atividade pendente.

Não restaurar branch/master para SHA histórico; não descartar alterações locais desconhecidas.

### 4.1. FASE A — PR #1940

HEAD de partida `744dd504`; run de referência `45832007030`.

**Marco de evidência:** resultados do workflow de escala anteriores a `c5701c653158caf91011526e62c4d051e45bf989` são só históricos/diagnósticos.

**V3 e paridade:** ler a curva V3 nos artefatos (audit-curve verde é coleta, não gate); provar que parity-1k usa o comparador estrito (grafo normalizado, rank.Best/Second, desempates, candidatos, arestas, scores, decisões); identificar qual mudança corrigiu Best/Second e demonstrar estabilidade (uma run verde não basta).

Guarda pareada de entradas e multiconjunto materializado não substituem o comparador de saídas.

**Negativos:** 51822, 51823, 51110, 51111, 51112, 51117 com comportamento fail-closed.

Aplicação, reversão e reaplicação ordenadas da cadeia de publicação (original → V1 → V2 → V3 e volta), preservando manifesto selado, atomicidade e idempotência, sem DDL pelo Runner.

**Gold:** V8 antes = depois e Gold V9 antes = depois, com regressão V8 pelo caminho operacional real (não só envelope sintético); preservar recomposição de exibição, fontes_distintas, estado_concordancia, APIs e contratos.

**Curva 1k → 2k → 4k:** FULL e cada fase do gate ≤2,5× de 2k para 4k, mesmo SHA/perfil/configuração/instrumentação; separar FULL, ingestão, censo e demais etapas; repetir controladamente resultados instáveis ou limítrofes; preservar resultados desfavoráveis.

Instrumentação:

- Pessoas-base.
- Observações.
- Versões.
- Candidatos.
- Arestas.
- Operações SQL.
- CPU SQL atribuída ao Runner.
- CPU/IO/disco/memória de processo e host.
- Tempdb.
- Version store.
- SNAPSHOT.
- Índices.

Sanar as lacunas históricas de contagem SQL e atribuição de CPU; declarar a cobertura efetiva, distinguindo medições exatas, aproximações e amostras.

**FULL 10k comparável:** 10.000 pessoas-base, não 10.000 versões (seed 355, clean, vencedor #38, C07, gabarito independente, censo efetivo, qualidade, recursos), após os gates anteriores.

**#1931:** curva medida, gargalos, estimativas analíticas de tempo e memória para 100k, riscos (não executar 100k).

**Fechamento:** tudo acima, documentação sincronizada, diff restrito à Fase A, squash protegido, SHA integrado confirmado, validação pós-merge.

**RELATÓRIO FASE A na #1851:** evidências, checklist, curvas, FULL 10k, estimativas, custódia, dependências do E2E núcleo e integral, lacunas por issue/requisito, caminho crítico atualizado, o que pode ser postergado.

Informativo: **SEGUIR para 4.2.**

### 4.2. Reconciliação do caminho crítico

Reconciliar o plano E2E de 22/11 com as decisões supervenientes e o estado pós-Fase A, preservando a numeração das etapas.

Para cada requisito:

- Implementado.
- Integrado.
- Validado fisicamente.
- Evidência.
- Lacuna real.
- Dependência.
- Critério de aceite.

Issue aberta não significa tudo pendente; não criar issues/PRs duplicadas; requisitos de INCREMENTAL, produção ou ciência não bloqueiam o E2E DEV (#1936 pode seguir aberta).

Escolher a próxima atividade pela dependência real.

### 4.3. E2E do núcleo

Comprovar:

- Fonte operacional das arestas (#1584).
- Oráculo independente.
- Adaptador V9 do RunAsync.
- Seleção segura do vencedor (#1488) com proveniência e negativos.
- Habilitação controlada só em DEV.
- Runner V9 no local-e2e.sh após o Processor.
- Chaves do índice V9 geradas automaticamente.
- FULL inicial e repetido.
- Idempotência.
- Reprocessamento.
- Atomicidade.
- Falhas intermediárias.
- Rollback.
- Integridade referencial.
- Comparação com gabarito sintético independente (fora do ZIP e do caminho de decisão).

Cadeia física:

**ZIP → HTTP → Bronze → Processor → Silver → Gold de fatos → Runner V9 → linkage_resultado → Gold de pessoas → Serving/APIs.**

Estados:

- PROVISORIA.
- REFERENCIA.
- INDEFINIDA.

Escalas DEV 1k e 10k conforme contratos; FULL 10k do Runner não substitui o E2E HTTP 10k.

Não materializar chaves ou identidades manualmente.

Não remover RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED: habilitação DEV não é ativação.

Emitir **E2E_NUCLEO_PASS** e seguir.

### 4.4. Resultado.Api e OpenAPI (#1853)

Comprovar:

- Endpoints reais.
- Contratos.
- Estados por Entrega.
- Negativos.
- Correlação.
- Autorização por recurso.
- Idempotência.
- HTTP + SQL descartável.
- OpenAPI → runtime.
- Runtime → OpenAPI.
- Rotas ausentes ou não previstas.

Teste só estático não é prova física.

### 4.5. Busca semicega (#1854, #612, #539)

Comprovar:

- Busca real.
- Top 5.
- "Nenhum destes".
- Sem exposição de score.
- Casos sem opções.
- Seletividade.
- Fan-out.
- Auditoria.
- Isolamento institucional.
- Projeção restrita.
- Negativos.
- Deny-by-default.

Sem alterar políticas de visibilidade.

### 4.6. Solução de Apoio e ZIP (#1855, #1194)

Concluir a #1194 conforme 3.j:

- Vínculo credencial ↔ recurso ↔ Gestor ↔ sistema de origem ↔ contrato.
- Escopo e status.
- Isolamento entre recursos.
- Auditoria.
- Idempotência.
- Negativos de acesso cruzado.
- Consultas de status protegidas.
- Fail-closed.

Manter enviar=false até validá-la.

Depois, comprovar:

- Geração real do ZIP.
- Manifesto.
- Recepção HTTP.
- Ledger.
- Bronze.
- Processor.
- Gold de fatos.
- Recibos.
- Replay.
- ZIP inválido.
- Recurso cruzado.

Sem credencial GESTOR como fallback; sem chaves no navegador.

Geração local do ZIP não substitui ingestão física.

### 4.7. Demais superfícies (#1345 e plano)

Comprovar APIs de resolução de identidade, pessoa, registros, benefícios, serviços, Serving, Monitor e contratos de segurança obrigatórios ao E2E DEV, sem ampliar para requisitos de produção.

A ordem entre 4.4 e 4.7 pode mudar por dependência real demonstrada.

### 4.8. Workflow final

Evoluir `.github/workflows/jornada-e2e-integral-v9-dev.yml` (sem duplicar mecanismo).

Requisitos:

- Reexecutável.
- Só DEV.
- Dados sintéticos.
- SQL e armazenamento descartáveis.
- Isolamento comprovado.
- Vencedor e ambos os hashes verificados.
- Caminho operacional real.
- Oráculo independente.
- Todas as superfícies obrigatórias.
- Negativos, idempotência, replay e rollback.
- Evidência por requisito.
- Métricas e recursos.
- Marcos separados E2E_NUCLEO_PASS/FAIL e E2E_INTEGRAL_PASS/FAIL.
- Falha explícita se faltar cobertura.
- Só artefatos sanitizados.
- Limpeza segura.

Sem PASS com mocks em componentes obrigatórios, testes estáticos, componentes pulados, execução parcial, SHA incompatível, prova só local, cancelamentos ou dispensas indevidas.

Disparo só por workflow_dispatch na master com SHA exato; sem schedule e sem disparos caros por push ou PR.

### 4.9. Validação final

Integrar código e documentação, confirmar squash, SHA e CI pós-merge, custódia, permissões, parâmetros e isolamento.

Disparar conforme 3.g; analisar logs, recibos e artefatos; confirmar a conclusão semântica de cada requisito.

Falha recuperável: corrigir e repetir o fluxo.

Mudança posterior que invalide a evidência exige nova validação; evidência de um SHA não vale para outro.

## 5. PROTEÇÃO DO AMBIENTE LOCAL (SEM BACKUP)

Proibido parar, remover, recriar, reiniciar, reconfigurar, migrar, limpar, alterar, testar contra ou conectar para escrita no container `jornada-local`, seus volumes, instância e banco; proibido `docker system prune`, `docker volume prune`, limpezas globais, remoções com curinga ou destino indeterminado, scripts de reset.

No WSL, usar só containers, bancos, volumes, redes e projetos Compose `codex-` criados nesta missão e remover só esses, após verificar identidade e propriedade (o prefixo não basta).

Na CI, manter as convenções de isolamento por run já existentes.

Antes de qualquer operação com estado, comprovar isolamento:

- Docker context.
- Daemon.
- Containers.
- Compose.
- Volumes e donos.
- Bind mounts.
- Redes.
- Portas.
- Instância e banco SQL de destino.
- Connection strings efetivas sem expor segredos.
- Variáveis.
- Symlinks.
- Efeitos indiretos dos scripts.

Sem prova, **NÃO EXECUTAR.**

**Sistema de arquivos:** escrita permitida somente dentro do Linux do WSL — no repositório ~/src/jornada-codex, em /tmp e, na pasta pessoal ~, apenas para arquivos da missão e caches/configurações necessários às ferramentas (ex.: .dotnet, .nuget, pip, configuração do Codex). Não autoriza excluir, alterar, mover ou limpar arquivos pessoais, outros repositórios, credenciais existentes (inclusive ~/.config/gh e ~/.ssh) ou configurações não relacionadas à missão. Proibido escrever em /mnt/c ou qualquer disco do Windows, e usar symlinks, bind mounts ou redirecionamentos que alcancem esses caminhos ou os recursos do `jornada-local`. Não ampliar permissões, usar sudo para contornar restrições nem desabilitar mecanismos de segurança.

Repositório público: sem dados reais, PII, segredos, tokens, dumps, gabaritos protegidos, artefatos privados, logs não sanitizados ou exceções completas; segredos nunca em argumentos de processo.

## 6. MÉTODO

Protocolo de 13 etapas (GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119) na inicialização, nas retomadas, nas integrações e quando pertinente, com reconciliação bidirecional CI ↔ PR ↔ master ↔ checkpoint (janela declarada, run_id e run_attempt, cobertura parcial declarada, nada inventado).

### Ciclo

**Evidência → hipótese → reprodução → teste que falha → correção mínima → testes → revisão do diff → push seguro → CI até concluir → logs e artefatos → registro → próximo item.**

Hipótese não é causa comprovada; sem causa demonstrada, registrar a limitação e melhorar a observabilidade.

Não remover comportamento sem causa e teste; teste textual frágil vira comportamental.

Classificar cada falha:

- Infraestrutura.
- Compilação.
- Dependência.
- Implementação.
- Teste.
- Fixture.
- Comparador.
- Instrumentação.
- Desempenho.
- Proveniência.
- Permissão.
- Segurança.
- Contrato.
- Desconhecida.

Falha de CI, teste ou ambiente não é parada; nada de retries indiscriminados.

Durante a CI, preparar a próxima hipótese localmente e itens independentes em worktree, sem iniciar outra frente principal; polling proporcional.

Preservar runner, watchdog, timeouts e limites; não reduzir cobertura por falta de recursos (9.h).

### Integração

Squash só de PRs da missão, após:

- HEAD exato.
- Compatibilidade com a master.
- Diff revisado.
- Escopo.
- Ausência de proibidos.
- Conflitos.
- Reviews.
- Required checks.
- Jobs e skips examinados.
- Evidências.
- Documentação.
- Relatório na PR.

Nunca Draft, bypass ou push direto na master.

Master vermelha bloqueia merge dependente, não a preparação; priorizar a correção mínima.

### Pós-merge

- Estado merged.
- SHA do squash.
- HEAD da master.
- jornada-ci e Diagnostics (porta de entrada, não substituto; ausência não é PASS).
- Seed42/30k e Windows quando exigíveis.
- Dispensas justificadas.
- Checkpoint.

Concluir a validação antes da próxima frente principal.

## 7. INVARIANTES

Preservar:

- Vencedor #38 e hashes.
- Modelo congelado.
- M/U.
- TF.
- Limiares.
- Margens.
- Padrões.
- Passes de blocking.
- Núcleo FS.
- Agrupador.
- S4b(i).
- b1.
- r1.
- LOCKs.
- Guardas C07 e C10.
- Precedência de identidade.
- Contratos de publicação.
- Compatibilidade V8.
- Segurança institucional.
- Independência do oráculo.
- Actions fixadas por SHA.

Proibido:

- Afrouxar comparadores.
- Ignorar empates.
- Flexibilizar gates.
- Reduzir cobertura.
- Aumentar timeouts.
- DDL, schema, gatilhos ou SQL dinâmico pelo Runner.
- Alterar decisões científicas.
- Retunar o modelo.
- Implementar S4b(ii) ou INCREMENTAL.
- Fases B, C ou D.
- TRAIN, VALIDATION ou TEST científicos.
- Calibração.
- Campanha científica.
- 100k.
- TEST ou holdout como fixture.
- Seeds por conveniência.
- Excluir casos desfavoráveis.
- Mudar critérios retroativamente.
- INSERT direto de pessoas Gold ou chaves materializadas para PASS artificial.
- Dados reais, PII ou segredos.
- HML.
- PROD.
- Ativação.
- Remover bloqueios produtivos.
- Contratar infraestrutura.
- Alterar permissões ou proteções.
- Force-push.
- Reescrita de histórico.
- Push direto na master.
- Exclusão de branches.
- Fechar PRs antigas.
- Criar monitores.
- Reativar automações.
- Alterar #1352, #1356, #378, #1736.

Otimização só com resultado e contratos idênticos; desempenho não autoriza mudar semântica.

## 8. REGISTRO E RECUPERAÇÃO

### Checkpoint

Checkpoint na #1851 a cada mudança factual relevante:

- Início.
- Retomada.
- Correção.
- Hipótese.
- Evidência.
- Disparo.
- Resultado de CI.
- Escalada.
- Bloqueio.
- Transição.
- Integração.
- Pós-merge.
- Limite de sessão.
- Encerramento.

Primeira linha:

`MASTER <sha> | ETAPA: <A ou E2E-n> | <PR> <HEAD> | CI: <estado consultado agora> | próximo passo: <ação>`

Conteúdo:

- Data/hora UTC.
- Worktrees.
- Commits publicados e locais.
- Documentação pendente.
- Janela e cobertura da CI.
- Workflows disparados.
- Runs, tentativas e jobs.
- Gates aprovados e pendentes.
- Evidências.
- Defeitos.
- Hipóteses.
- Contador por defeito.
- Escalados.
- Dependências bloqueadas.
- Recursos codex- ativos.
- Custódia #38 e expiração da cópia operacional.
- Última atividade concluída.
- Próxima ação exata.
- Condição de parada.

Última linha:

`aguardando: <CI run X / PO / recurso / nada>`

Indicadores:

- ✅ Só com CI concluída e gate semântico conferido.
- 🔄 Submetido.
- ❌ Reprovado.
- NÃO VERIFICADO.
- BLOQUEADO.
- ESCALADO AO PO.

Relatos de rotina em até dez linhas.

#1228 só com evidência nova e material.

### Nova sessão

Recuperar o mandato registrado e o último checkpoint, consultar o GitHub, reconciliar, recuperar commits e documentação pendentes, contadores, recursos e custódia, e retomar a primeira atividade pendente sem nova autorização.

Não duplicar PRs, commits, workflows ou artefatos; não confiar só no checkpoint se o estado mudou; sem reset destrutivo.

Fim de sessão é interrupção, não conclusão.

### Relatório final

- Caminho executado.
- PRs e SHAs.
- Decisões.
- Checks.
- Pós-merge.
- Evidências.
- Matriz de cobertura.
- Resultados 1k e 10k.
- Recursos.
- Custódia.
- Link do workflow e da run de aceite.
- Parâmetros.
- Run_id e run_attempt.
- Artefatos.
- E2E_NUCLEO_PASS.
- E2E_INTEGRAL_PASS.
- Limitações.
- Pendências fora do escopo.
- Limpeza.
- Instruções para o PO reexecutar.

## 9. PARADAS

Únicas; registrar checkpoint, relatório curto com evidências e a decisão necessária, e aguardar o PO sem abrir outra frente.

### a) Nova política ou decisão

Necessidade de criar ou alterar política de identidade, decisão científica, regra congelada ou contrato não autorizado.

### b) Violação de requisito protegido

Paridade, gate ou E2E só possíveis alterando proteção, semântica protegida ou item proibido.

### c) Segurança

Dado real, PII, segredo em artefato ou brecha de segurança (preservar evidência com segurança, registrar, não corrigir fora do escopo).

### d) Custódia

Vencedor #38 indisponível ou sem integridade verificável; disponibilidade operacional e pinagem não comprovadas até 17/12/2026 06:09:48 UTC; ou, depois disso, menos de cinco dias para a expiração da última cópia operacional verificada.

### e) Dependência das Fases B/C

Demonstração técnica de que requisito obrigatório do E2E depende das Fases B/C (apresentar requisito, evidência, dependência causal, alternativas, recomendação; não iniciar essas fases).

### f) Isolamento, credencial ou infraestrutura

Isolamento não comprovável, credencial ou identidade indispensável ausente, infraestrutura indisponível.

### g) Escalada ao PO

Nada executável restante por causa de itens ESCALADOS AO PO (escalada isolada não para a missão).

### h) Recursos

Insuficiência comprovada de recursos autorizados após esgotar as alternativas permitidas.

### i) Conflito normativo

Conflito normativo material que não se resolve sem decisão do PO.

### j) CONCLUSÃO

- Fase A integrada e validada.
- Caminho crítico executado.
- Workflow E2E integral na master.
- Checks e pós-merge aprovados.
- Disparo final no SHA comprovado.
- Isolamento, vencedor e hashes verificados.
- Cobertura obrigatória comprovada.
- E2E_NUCLEO_PASS.
- E2E_INTEGRAL_PASS.
- Artefatos sanitizados.
- Documentação e índices integrados.
- Relatório final na #1851.

Declarar:

**MISSÃO CONCLUÍDA — E2E INTEGRAL DEV V9 COMPROVADO.**

Aguardar o PO, sem iniciar B, C, INCREMENTAL, ciência, HML, PROD ou ativação.

### Não são paradas

Falha recuperável de CI, compilação, teste ou infraestrutura, CI em andamento, fim de contexto ou de sessão.

Corrigir quando possível, registrar e retomar na sessão seguinte.

---

**FIM DO MANDATO**
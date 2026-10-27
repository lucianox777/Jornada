# Decisão de arquitetura — escopo por impacto para o probe físico V9 de 30 mil

> **Estado atualizado em 26/10/2026:** decisão implementada e integrada à `master` pela PR #1188 (merge `2857ab3083e1162788a282d748835f8ca672ff22`). O aviso histórico **AINDA NÃO IMPLEMENTADO** abaixo refere-se exclusivamente ao corte `master ea814e461d21d543254f64a2618ef8e2c6992bf5`, não ao estado atual. O classificador está em `Solution/scripts/v9-physical-impact-classifier.py`; o workflow mantém job de impacto, probe físico condicional e agregador de status estável. A auditoria de desempenho e regressões adicionais são acompanhadas na issue #1200. **A integração da #1188 não comprova redução de tempo quantificada** nem dispensa o probe para alterações SQL/DT-10 ou compartilhadas; estas continuam fail-closed.

## Situação operacional vigente (26/10/2026)

A PR #1188 está **integrada** (merge confirmado: `2857ab3083e1162788a282d748835f8ca672ff22`). Esta seção prevalece **apenas quanto ao estado operacional atual**; os verbos no futuro e a descrição do gatilho antigo nas seções seguintes documentam a decisão e o contexto histórico de 24/10, não instruções para implementar novamente o classificador.

| Item | Estado verificado | Evidência / limite |
| --- | --- | --- |
| Classificador de impacto e teste inicial | IMPLEMENTADO e integrado | PR #1188; `Solution/scripts/v9-physical-impact-classifier.py` e testes em `Solution/tests/scripts/`. |
| Job de impacto, probe condicional e agregador obrigatório | IMPLEMENTADO e integrado | `.github/workflows/v9-physical-seed42-probe.yml`; mudanças SQL, compartilhadas ou desconhecidas exigem execução. |
| Redução do tempo do probe **quando necessário** | NÃO COMPROVADA | A classificação elimina apenas repetições dispensáveis; o job físico conserva limite de 360 minutos. |
| Testes adicionais de diff malformado e DT-10 | EM DESENVOLVIMENTO, NÃO INTEGRADOS | Branch `test/ci-impact-fail-closed-20261026`; requer CI e PR antes de ser considerada evidência de `master`. |
| Validação física da PR #1199 | EM EXECUÇÃO na última consulta | Run #40396877513; não afirmar aprovação física enquanto incompleto. |
| Economia medida por comparação equivalente | PENDENTE | Issue #1200; não inferir economia a partir de job intencionalmente dispensado. |

**Regra documental:** não confundir código integrado com prova de desempenho, nem resultado da CI principal com conclusão do probe físico. Atualizar este quadro quando houver evidência verificável, preservando a decisão histórica abaixo.

**Decisão:** CI-V9-PROBE-01  
**Aprovada para implementação:** 24/10/2026  
**Estado deste documento:** DECIDIDO, **AINDA NÃO IMPLEMENTADO** no corte `master ea814e461d21d543254f64a2618ef8e2c6992bf5`. Não altera o comportamento atual do GitHub Actions, os checks obrigatórios nem a seleção científica do V9.  
**Objeto exclusivo:** `.github/workflows/v9-physical-seed42-probe.yml`, proteção de status e classificação de impacto, em change-set **posterior** aos merges já em curso das PRs [#1182](https://github.com/lucianox777/Jornada/pull/1182) e [#1183](https://github.com/lucianox777/Jornada/pull/1183).

## 1. Problema comprovado e motivação

O workflow atual `v9-physical-seed42-probe` executa, em SQL Server efêmero, geração do corpus clean seed42 e calibração de **30 mil pessoas**, com `timeout-minutes: 360`. Seu gatilho `pull_request.paths` inclui padrões amplos:

- `Solution/src/Jornada.Linkage.Runner/V9*.cs`;
- `Solution/src/Jornada.Linkage.Core/V9*.cs`;
- `Solution/tests/Jornada.Tests/Unit/V9*.cs`.

Por isso, edições de implementação estritamente Gold — exemplificadas por `V9GoldDisposableLowerProcedurePatch.cs` na PR #1182 e `V9GoldHarnessDatabasePreparation.cs` na #1183 — disparam o probe completo, embora o workflow, no caminho `Build the single CSharp calibrator and synthetic producer` → `--v9-calibrate`, **compile o Parameters.Worker e o SyntheticCorpus** e não execute a instalação/patch transacional DT-10 do Runner Gold. O teste física e estatisticamente relevante para a alteração da procedure é o **preflight de LocalDB Windows**, não a repetição da calibração de 30 mil.

Os dois HEADs das PRs #1182/#1183 já dispararam execuções de longa duração. **Esta decisão não é autorização para cancelá-las, saltar checks iniciados, mudar seu HEAD ou dispensar gate retroativamente.**

**Objetivo:** reduzir *wall-clock*, consumo de runners e tempo da fila de merges quando **não** há risco de alteração do cálculo exercitado pelo probe, mantendo o mesmo nível de evidência para alterações relevantes. Não prometer economia em minutos sem comparar durações reais em runs equivalentes; consultar [baseline de duração](CI_Baseline_Tempo_Execucao.md).

## 2. Decisão vinculante: cobertura por dependência real

**Substituir o gatilho grosseiro por classificador conservador de impacto, com check leve sempre presente.** O classificador deve inspecionar o diff completo da PR e declarar `run_physical_30k=true|false`, com motivos e lista de arquivos em artefato/log auditável. O conteúdo de arquivos pode alterar dependências mesmo sem prefixo `V9`; **nome de arquivo não basta**.

| Alteração identificada | Probe 30 mil | Justificativa e gate complementar |
| --- | --- | --- |
| Calibrador/otimizador V9, seus planos/parâmetros, `--v9-calibrate`, seleção TRAIN, M/U, TF, FS compartilhado, Rank/Resolve, agrupamento ou suas dependências transitivas executadas | **SIM** | Comportamento, ajuste ou inferência podem mudar; preservar exatamente o probe físico e a CI geral. |
| Produtor SyntheticCorpus, geração clean, referências/manifestos usados no probe, SQL/projeção materializada, scripts `v9-nested-*`, contratos de entrada/saída e configurações da calibração | **SIM** | Reprodução, hash da massa e busca dependem desses recursos. |
| Projetos/targets/transitivos NuGet, `global.json`, lockfiles, dependências do Worker/Core/SyntheticCorpus, workflow do probe, classificador ou seus testes de proteção | **SIM** | Podem alterar executável ou desabilitar acidentalmente gates. |
| Código **exclusivo do Runner Gold**: `V9GoldDisposableLowerProcedurePatch.cs`, `V9GoldHarnessDatabasePreparation.cs`, orquestração física Gold, scripts específicos de matriz, testes unitários estritamente Gold | **NÃO, desde que comprovadamente isolado** | Executar CI geral, contratos/regressões Gold aplicáveis e **preflight físico DT-10** quando o patch, a preparação ou migrations/procedures afetarem esse contrato. Não dispensar o preflight. |
| Somente documentação, sem alterações executáveis, metadados de CI ou contratos consumidos pela geração | **NÃO** | Manter check rápido, validação documental/links e CI geral segundo classificador próprio. |
| Caminho não classificado, combinação de áreas, refactor/reexportação de biblioteca compartilhada, renomeação, mudança em dependência ou falha de leitura do diff | **SIM (falhar fechado)** | Nunca deduzir ausência de impacto de nomes fora da lista; exigir reclassificação/revisão explícita em PR de CI. |
| Acionamento `workflow_dispatch` manual do probe | **SIM** | Preservar execução integral independente do filtro de PR e os invariantes existentes de TRAIN/TEST. |

O agrupamento Gold-only é uma **exceção estreita**, não um `V9*.cs` invertido: qualquer alteração de código compartilhado que possa repercutir no Calibrador deve disparar o probe. O classificador deve ser alimentado por uma matriz de dependências revisada com referências reais de projeto/calls; a tabela acima estabelece a política, **não prova automaticamente que todos os caminhos estão isolados**.

## 3. Check obrigatório e fail-closed

O workflow deve ser **acionado em toda PR** para executar o **classificador leve**; evitar simplesmente aplicar `on.pull_request.paths` ao workflow inteiro, pois um workflow ignorado por filtro de caminhos pode deixar **status check requerido pendente** na proteção do branch.

Topologia de implementação prevista:

1. Job de impacto curto, com checkout apropriado, diff entre base e código efetivamente testado, `fetch-depth` suficiente, inspeção de arquivos incluídos/removidos/renomeados e publicação `run_physical_30k`, motivos e paths. Se o diff for ilegível, inexistente/inconclusivo ou o classificador falhar, **não retornar FALSE silenciosamente**.
2. Job pesado de 30 mil **condicional** ao impacto (ou execução manual), mantendo sua receita física, versões, timeout, isolamento, hashes, artefatos, ausência de TEST/VALIDATION externa e nenhum relaxamento de aceite. Alterações Gold isoladas **não** invocam esse job.
3. Job agregador/check **estável e sempre reportado**, que reprova o SHA se o impacto exigir o probe e seu job não tiver resultado `success`. Para diff classificado sem impacto, registrar explicitamente `NOT_REQUIRED_FOR_THIS_DIFF` com o motivo; não simular um teste executado nem dar `success` à calibração ausente.
4. Antes do merge do change-set que altera o workflow, conferir **nomes reais de status checks requeridos/branch protection** e migrá-los de forma que nenhum required check fique para sempre em `pending` nem seja enfraquecido. **Se não for possível confirmar os requisitos reais da proteção, não alegar conformidade ou automatizar merge.** A CI geral `jornada-ci`, a validação independente e os preflights especializados mantêm sua própria política de obrigatoriedade, sem dependência da classificação do probe.
5. Em `push` para a `master` e em execução manual, preservar ou reforçar os gates correspondentes ao risco sem confiar em `paths` de PR como autorização de produção. Esta decisão não liga V9/DT-15.

**Invariante:** nenhum sucesso do classificador substitui execução física **quando ela for necessária**; tampouco a calibração física substitui a validação DT-10 do Gold.

## 4. Casos mínimos de teste da mudança futura

Exigir testes automáticos do classificador e do contrato do workflow cobrindo pelo menos:

1. `V9GoldDisposableLowerProcedurePatch.cs` isolado → probe desnecessário **e** preflight DT-10 acionado; mudança no `V9GoldHarnessDatabasePreparation.cs` isolado → idem.
2. Mudança isolada na orquestração `v9-gold-12-trial-localdb.ps1` → probe desnecessário, teste de sintaxe/guards da matriz aplicável.
3. `Solution/docs/*.md` exclusivamente → check leve presente, sem job pesado; registrar razão da não execução.
4. Mudança no `V9NestedCalibrationCoordinator.cs`, `V9PhysicalSqlAdaptiveCalibrationProbe.cs`, `V9FellegiSunterAdaptiveCalibrationAdapter.cs`, FS ou TF compartilhados → probe obrigatório, mesmo se nome não casar com padrão legado.
5. Mudança em `V9LowerNonMatchTrainCalibrator.cs` **e** Core compartilhado no mesmo diff → classificador conservador considera efeitos transitivos; casos ambíguos executam o probe até prova de isolamento.
6. Alteração no `SyntheticCorpus`, em script `v9-nested-calibration-study.py`, referências/manifests, `global.json`, `*.csproj`, `packages.lock.json` relevante ou no próprio classificador/workflow → probe obrigatório.
7. PR mista Gold + calibrador, rename de caminho sensível, diff indisponível → probe obrigatório ou gate falha fechado; não retornar dispensado.
8. Falha do job físico sob impacto positivo → agregador bloqueia o merge; execução descartada/skip legítimo de PR Gold-only **não** causa pendência infinita no check obrigatório.
9. `workflow_dispatch` → job físico sempre roda.
10. Não é permitido falsificar `success` da calibração, substituir corpus por fixture curto nem desativar a verificação de hashes para acelerar a fila.

Os testes unitários do classificador devem executar no `jornada-ci` e na própria alteração do workflow; medir dois casos reais no GitHub Actions (PR Gold-only e PR que modifica o calibrador) antes de declarar IMPLEMENTADO. Publicar SHA do HEAD e os IDs dos runs. **Não modificar as duas PRs DT-10 em andamento** só para testar a otimização.

## 5. Aceite, evidências e limites

- **Critério funcional:** uma PR Gold-only com evidência de isolamento deixa de disparar a calibração de 30 mil, mas mantém CI geral e gates Gold/DT-10 pertinentes; uma PR que afete calibrador/FS/TF/Rank/Resolve (inclusive dependência transitiva) continua com probe físico integral.
- **Critério de segurança:** nenhum required check ausente/pending indevido, nenhum resultado `success` falso, nenhuma alteração de gates FDR/TEST, migrações DT-10 canônicas, proteção SQL ou aprovação DT-15.
- **Critério de evidência:** revisão do grafo de dependências, self-tests do classificador, workflow YAML, dois exemplos físicos de checks e diferença de duração comparável; qualquer regressão reverte o filtro ao modo **conservador (probe habilitado)**.
- **Estado nesta data:** decisão arquitetural **APROVADA PARA IMPLEMENTAÇÃO**, não executada, testada ou mergeada como mudança de CI. Esta documentação por si só **não** encurta os runs já iniciados #40141443760 e #40141616663 e não autoriza mesclar #1182/#1183 antes de seus gates.

## 6. Fontes e hierarquia

- Workflow existente: [`.github/workflows/v9-physical-seed42-probe.yml`](../../.github/workflows/v9-physical-seed42-probe.yml), que hoje usa `pull_request.paths` amplos e executa a calibração de 30 mil.
- CI geral: [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml), com classificação própria de impacto `ci-impact-classifier.py` e gates estáveis; **não** confundir seu `run_full` com o escopo do probe.
- Preflight DT-10 Windows proposto: [PR #1183](https://github.com/lucianox777/Jornada/pull/1183), workflow `.github/workflows/v9-gold-disposable-dt10-physical-preflight.yml` presente na **branch da PR**, não na `master` do corte.
- [Decisões arquiteturais V9/Gold](Mapa_Decisoes_Arquiteturais_V9_Gold_20261024.md), [baseline da CI](CI_Baseline_Tempo_Execucao.md), [orquestração da matriz Gold](Gold_Orquestracao_Matriz_12_LocalDB_TRAIN_20261021.md), [índice de decisões](Indice_Decisoes_Vigentes.md).

**Precedência:** esta decisão é **específica do acionamento da CI**; não modifica nenhuma regra científica V9, política Gold, condição DT-15, baseline selada ou modelo ATIVO. A implementação futura deve cumprir integralmente os critérios acima e registrar evidências no commit exato.

## Otimização incremental — cache NuGet do probe físico (26/10/2026)

Na branch da PR #1201, o workflow `v9-physical-seed42-probe.yml` passou a configurar o cache de pacotes do `actions/setup-dotnet`, indexado pelos `Solution/**/packages.lock.json` (commit `6829a653af1fbea2cc933254c34c73cdf1c84426`). O cache **não** elimina build, restore, SQL, 30 mil TRAIN, 20 replays nem os gates. Trata-se de otimização potencial de downloads/restauração; ganho de tempo ainda **não medido** e CI no novo HEAD ainda **não aprovada**. A classificação docs-only continua sendo feita pelo job `impact` para manter o check agregador estável; excluir o workflow inteiro por filtro de paths poderia deixar check obrigatório pendente. A PR #1199 permanece independente e bloqueada pela prova física positiva DT-15/DT-10.

## Adendo de decisão — preservação de runs físicos (26/10/2026, PR #1201)

**Histórico preservado:** a decisão CI-V9-PROBE-01 de 24/10 e a integração do classificador na #1188 permanecem válidas. Este adendo altera somente a política de concorrência da branch da #1201; não reinterpreta aprovações antigas nem ativa V9.

**Decisão aprovada para implementação:** cancelar checks rápidos obsoletos pode ser apropriado em workflows independentes; **não cancelar automaticamente um workflow que contém o probe científico físico 30k**. A configuração anterior da #1201, `cancel-in-progress` condicional para PRs comuns, poderia interromper a calibração já iniciada. A alteração na mesma PR define `cancel-in-progress: false` no nível do workflow, preservando o run ativo e seus artefatos. O agrupamento existente por PR permanece; o GitHub pode substituir um run *pendente* por outro mais novo no mesmo grupo. Execuções manuais e branches explícitas de evidência conservam grupos independentes.

**Limite importante:** esta é uma correção conservadora, **não** uma fila global exclusiva entre PRs; não alegar que impede probes simultâneos de PRs diferentes. Serialização global e cancelamento seletivo de jobs rápidos exigem desenho/testes próprios, sem enfraquecer o check agregador e sem iniciar CI pesada para demonstrá-los.

**Estados e evidência:** IMPLEMENTADO na branch `test/ci-impact-fail-closed-20261026`, commit `776b4da54abc304d37717c9569d8cad8482ea298`, PR #1201. **TESTADO em GitHub Actions: pendente; MERGEADO: não; ATIVADO na master: não; ganho de tempo: não medido.** O workflow preserva 30k, FS/TF, 20 replays, bootstrap REF/IBGE e gates DT-10/DT-15; nenhum resultado histórico certifica automaticamente um HEAD novo. Registrar run ID, SHA e conclusão antes de alterar estes estados.

**Matriz de gates:** classificador de impacto e agregador estável continuam obrigatórios; `NOT_REQUIRED_FOR_THIS_DIFF` é dispensa justificada, não certificação; run antigo concluído é evidência histórica, não aprovação do HEAD; run cancelado ou incompleto não certifica; publicação V9 continua sujeita à aprovação humana posterior. **Operação:** não cancelar probes físicos em andamento nem iniciar novos probes manuais para validar esta mudança. A issue #1200 acompanha a medição e eventual serialização entre PRs.


## Correção operacional — cancelamento seletivo de probes automáticos (26/10/2026)

A observação de 20 execuções simultâneas do workflow físico em três PRs demonstrou que preservar indiscriminadamente todo `pull_request/synchronize` (`cancel-in-progress: false`) pode saturar a capacidade e impedir a CI rápida. A decisão anterior de preservação irrestrita fica **substituída** para probes automáticos de PR ordinária.

**Política implementada na PR #1201:** `concurrency.group` permanece por número de PR, e `cancel-in-progress` passa a ser verdadeiro somente para eventos `pull_request` de branches ordinárias. Uma nova revisão da mesma PR cancela a execução automática anterior, inclusive se já começou. Eventos `workflow_dispatch` e branches `experiment/`, `evidence/` e `exp/` continuam com grupo exclusivo por `run_id` e não são cancelados por essa regra. PRs distintas continuam independentes: esta mudança **não é um semáforo global**. A classificação por impacto e os gates físicos continuam fail-closed.

**Limitação de evidência:** cancelamento é um estado distinto de sucesso; nunca usar probe cancelado nem SHA substituído para certificar HEAD atual. Se uma execução automática tiver sido designada como evidência governada, ela deverá ser executada explicitamente no fluxo manual/ramo de evidência antes de depender de sua preservação. A nova política pode interromper um probe automático demorado após um commit novo: este é o compromisso deliberado para evitar saturação. Não altera critérios estatísticos, TRAIN/VALIDATION/TEST, DT-10/DT-15 nem autorização de ativação.

**Estado:** workflow alterado, mas ainda **não certificado por CI nem integrado**. Conferir os resultados no SHA exato da #1201; confirmar que synchronize cancela run automático anterior e que dispatch/evidence são preservados. A correção não cancela retroativamente runs antigos que já estejam fora de seu grupo ativo nem garante início imediato da fila. Para o congestionamento preexistente, inventariar IDs/SHA/eventos e cancelar manualmente apenas os obsoletos após triagem; preservar evidências governadas.


## Adendo de estado vigente — 27/10/2026, após merge da PR #1201 (sem alteração de decisão científica)

**Correção documental aditiva:** as seções anteriores registram estados e decisões *históricos* da #1201 em diferentes commits, incluindo as alternativas `cancel-in-progress: false` e cancelamento condicional. Não apagar nem reinterpretar essas seções. Para o **estado integrado na master**, prevalece a implementação do HEAD `ac60769fc5bbc140fc8ff279f86e699683ad7da4`, integrado pela [PR #1201](https://github.com/lucianox777/Jornada/pull/1201), merge `c0f0851a06fd8286d3d5dca8253d8ab7e0dc9449`. A [PR #1202](https://github.com/lucianox777/Jornada/pull/1202) (helper NUnit 4) também foi integrada, merge `f9ef668b91f2747838ebc6cbd25c9ce076def905`; ela não altera a política de impacto.

| Dimensão | Estado em 27/10 | Evidência e ressalva |
| --- | --- | --- |
| Classificador de impacto conservador e agregador estável | **IMPLEMENTADO / INTEGRADO** | #1188 e #1201; SQL, código compartilhado, mudanças desconhecidas, diff misto ou ambíguo exigem probe; `NOT_REQUIRED_FOR_THIS_DIFF` não certifica fisicamente o HEAD. |
| Regressões fail-closed adicionais, teste isolado e guarda contra HEAD obsoleto | **IMPLEMENTADO / INTEGRADO** | #1201, HEAD `ac60769f`; `jornada-ci` [40433795628](https://github.com/lucianox777/Jornada/actions/runs/40433795628) **SUCCESS**; probe [40433795608](https://github.com/lucianox777/Jornada/actions/runs/40433795608) **SUCCESS** no mesmo HEAD. |
| Política de concorrência vigente do workflow físico | **IMPLEMENTADA / INTEGRADA** | No HEAD integrado da #1201, `cancel-in-progress` é condicional: eventos `pull_request` de branches ordinárias podem substituir o run automático anterior; `workflow_dispatch` e branches `experiment/`, `evidence/`, `exp/` usam grupos isolados por `run_id`. Não existe semáforo global entre PRs. O desenho anterior `cancel-in-progress: false` foi **substituído**, não é a configuração vigente. |
| Comportamento real de cancelamento seletivo sob `synchronize` | **NÃO DEMONSTRADO POR ENSAIO ESPECÍFICO** | Sucesso da CI e do probe não comprova que um evento de cancelamento foi exercitado; preservar distinção entre teste de regressão e teste operacional de concorrência. |
| Cache NuGet indexado por lockfiles | **IMPLEMENTADO / INTEGRADO** | #1201; não dispensa restore, build, SQL, 30k TRAIN ou gates de segurança. |
| Redução de wall-clock ou runner-minutes | **NÃO MEDIDA / NÃO APROVADA COMO GANHO** | Medir em runs comparáveis com SHA, perfil e fases equivalentes; issue [#1200](https://github.com/lucianox777/Jornada/issues/1200). |
| Ativação da política inferior DT-10/DT-15 e aprovação humana | **NÃO ATIVADA / PENDENTE** | #1199 é frente independente; nenhuma conclusão de probe TRAIN substitui teste positivo governado, REF/IBGE, TEST final independente ou autorização institucional. |

**Ramo histórico:** `test/ci-impact-fail-closed-20261026` ainda existe, mas, em 27/10, os blobs de `.github/workflows/v9-physical-seed42-probe.yml`, `Solution/scripts/v9-physical-impact-classifier.py` e `Solution/tests/scripts/test_v9_physical_impact_classifier.py` eram idênticos aos da `master` (comparação de conteúdo, não de histórico de commits). Não reaplicar a branch divergente como nova implementação.

**Operação e matriz de gates:** antes de qualquer merge de nova alteração CI, verificar HEAD e checks obrigatórios; não cancelar prova física governada; não promover resultado de run antigo para HEAD novo; manter `RUN` para impacto desconhecido e status agregador obrigatório. O adendo é exclusivamente de **documentação de estado**, sem alteração de código, workflow, parâmetros FS/TF, bootstrap REF/IBGE, corpus sintético 30k, DT-10/DT-15 ou regras de aprovação humana. **Aprovação humana da V9: pendente; ativação: não.**

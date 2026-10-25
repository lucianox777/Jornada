# Decisão de arquitetura — escopo por impacto para o probe físico V9 de 30 mil

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

# Pesquisa exploratória V9 — ramo isolado com parâmetros ampliados

**Decisão do Product Owner (09/11/2026):** criar ramo especial para pesquisa exploratória, preservando as decisões normais vigentes e permitindo parâmetros adicionais. **Estado:** ramo criado; ampliação técnica e validações ainda pendentes. **Governança:** GOV-DOC-01.

## Contrato e precedência

1. Este ramo deriva da master e **herda integralmente** as decisões científicas, estatísticas, funcionais, operacionais e de segurança vigentes. Não é fork de regras nem exceção a controles.
2. A pesquisa pode declarar e investigar **parâmetros adicionais** e espaços de busca mais amplos, em artefatos e código próprios do ramo; cada parâmetro precisa de nome, domínio, default neutro/compatível, semântica, proveniência, limites e teste. Os parâmetros históricos não são reescritos e a leitura de opcionais segue GOV-V9-J-COMPAT-20261108.
3. Não alterar REF, TEST, Gold, snapshots, plano operacional congelado, teto FDR aprovado, política CI/30k, elegibilidade ou ativação. Resultados exploratórios são diagnósticos, não confirmação estatística; reportar número de hipóteses, dependências, multiplicidade e incerteza.
4. O plano científico oficial de uma **campanha exploratória específica**, quando executada, deve ser único, versionado e auditável; não criar segundo motor/manifesto concorrente nem misturar resultados com a campanha de fechamento. Promoção a estudo confirmatório requer plano pré-declarado e autorização específica do PO.
5. O ramo não integra a master automaticamente. PR #1352 permanece draft e pode ser usada como referência de implementação; qualquer incorporação de commits deve passar por revisão de diferenças, testes e reconciliação, nunca merge cego.

## Contexto e justificativa

A decisão anterior autorizou ampliar parâmetros exploratórios, mas o PO esclareceu que essa pesquisa **deve ocorrer em ramo especial**, preservando as decisões normais. A separação reduz o risco de alterar o fechamento V9 ou misturar hipóteses exploratórias e evidências confirmatórias.

## Rastreabilidade e impactos

- Ramo: `research/v9-exploratoria-parametros-ampliados`, criado da `master` em 09/11/2026.
- Decisões complementadas: `GOV-V9-J-COMPAT-20261108` e complemento exploratório de `GOV-V9-FECHAMENTO-20261109`.
- Escopo inicial: isolamento arquitetural e documental. **Ainda não foi implementado nem executado um novo espaço paramétrico.**
- Próximos gates: inventário de parâmetros, testes de regressão dos defaults, rejeição fail-closed, proveniência por opção, execução exploratória isolada e relatório auditável.

## Implementação inicial incorporada no ramo (09/11/2026)

Foram copiados da branch técnica da PR #1352 para **este ramo de pesquisa** o materializador `Solution/scripts/v9-exploratory-append.py`, seus testes unitários, a documentação e o workflow de testes. Esta operação não mescla a PR #1352 na master. O materializador acrescenta opções `EXP_` inelegíveis a um **novo catálogo derivado**, preserva bytes do plano-fonte, exige evidência TRAIN declarada e grava hashes de proveniência. **Limitação expressa:** a evidência fornecida não é autenticada independentemente; o catálogo `ExploratoryCatalogOnly` não é um plano executável certificado. Não há ainda exploração adicional de M/U, blocking, novos domínios paramétricos, ajuste de multiplicidade ou execução física. Os testes do workflow ainda precisam de resultado observado no SHA deste ramo. A incorporação não representa aprovação científica ou ativação.

## Isolamento reforçado por decisão expressa do PO (09/11/2026)

O PO reiterou: **a master deve permanecer isolada da pesquisa exploratória**. Consequências operacionais: commits, testes e catálogos exploratórios devem permanecer no ramo `research/v9-exploratoria-parametros-ampliados`; nenhuma PR exploratória será mergeada na master como parte da pesquisa. A PR #1356 permanece draft e serve apenas para rastreabilidade, sem autorização de integração. O workflow `v9-exploratory-append.yml` foi limitado ao ramo de pesquisa em eventos `push` e PR com base nesse ramo; mantém `workflow_dispatch` manual. Esta mudança de workflow não altera a política CI/30k da master. **Não há resultado de teste observado nesta rodada**; aguardar run específico antes de declarar PASS.

## Decisão superveniente — relatório estatístico único com extensão condicional (09/11/2026)

**PO:** pesquisa exploratória **totalmente apartada da master** enquanto pesquisa; **não criar segundo relatório de análise estatística**. O relatório analítico estatístico existente mantém o mesmo contrato, nome e saída normal quando a funcionalidade exploratória não estiver habilitada. Quando o parâmetro explícito de habilitação da pesquisa estiver ligado **no ramo especial**, o mesmo relatório acrescentará seção/campos de diagnóstico exploratório, identificados como tais e com proveniência, número de hipóteses, métricas, incerteza, limites de comparação e indicação de ausência de certificação/ativação. A seção extra não deve alterar, substituir, reordenar ou reinterpretar métricas e conclusões normais. **Ausência do parâmetro equivale ao comportamento histórico**, com saída normal idêntica (inclusive estrutura/serialização quando aplicável); opção desconhecida ou inválida falha fechada.

**Precedência e impactos:** esta decisão especifica o formato de saída e reforça o isolamento do ramo, complementando a decisão anterior de ampliação paramétrica. A implementação deverá localizar o produtor real do relatório estatístico, acrescentar flag opcional com default desligado, testes de regressão de saída normal e testes de saída estendida. Não introduzir outro relatório, outro motor, outro manifesto executável ou modificações na master. Nenhuma mudança técnica foi declarada concluída por este registro documental.

## Decisão superveniente — reutilização dos passes normais sem efeito operacional (09/11/2026)

**PO:** a exploração **pode reutilizar os passes de blocking já empregados na calibração normal**, no mesmo recorte e com proveniência rastreável, para avaliar hipóteses e anexar diagnósticos **ao mesmo relatório de análise estatística**, exclusivamente quando o parâmetro exploratório estiver habilitado. A habilitação **não muda automaticamente nada** no modelo, nos passes normais, na configuração selecionada, no plano operacional, na REF, na ativação, na publicação ou nas decisões de governança: é somente uma investigação. Nenhum resultado exploratório pode alimentar seleção operacional, atualizar parâmetros de produção ou alterar o CP/FDR da família confirmatória. Os custos computacionais adicionais, caso existam, devem ser explicitados; o uso dos passes existentes não autoriza criar passes extras silenciosamente.

**Semântica exigida:** flag ausente/desligada = caminho e relatório históricos inalterados; flag ligada = executar somente diagnósticos exploratórios explicitamente planejados a partir dos passes existentes e acrescentar seção identificada, com parâmetros, métricas, universo/recorte, hashes, limitações e indicação `ExploratoryOnly=true`, `AutoApplied=false`. Se faltarem evidências ou capacidade, registrar indisponibilidade no diagnóstico, **sem inventar resultados nem promover a opção**. A pesquisa continua restrita ao ramo especial; nenhuma alteração na master.

**Precedência:** detalha o contrato do relatório condicional e a reutilização de infraestrutura da calibração normal; não revoga compatibilidade histórica, V2 WITNESS+ANCHOR, limites científicos, necessidade de validação ou autorização manual de eventual promoção.

**Estado:** decisão registrada; integração do diagnóstico com o produtor real do relatório e testes de não interferência ainda pendentes.

## Incremento técnico — extensão de relatório em cópia, sem SQL (09/11/2026)

Implementado **primeiro incremento isolado**, `Solution/scripts/v9-exploratory-statistical-report.py`: recebe o relatório normal `mu-statistical-analysis.json` e produz **uma cópia com o mesmo schema e campos originais**. Sem `--enabled`, a cópia é **byte a byte idêntica**; com `--enabled --parameters`, acrescenta `exploratoryAnalysis` contendo parâmetros declarados, hashes e contagens do relatório original, com `AutoApplied=false` e `DECLARED_NOT_PHYSICALLY_EVALUATED`. **Não produz métricas exploratórias, não executa SQL, não reaproveita fisicamente passes ainda, não altera seleção ou ativação.** A origem permanece intacta. Testes unitários de regressão e recusas foram adicionados ao workflow exclusivo da pesquisa; PASS em GitHub ainda não verificado. Esta entrega **não equivale à implementação integral** do requisito de avaliação exploratória a partir dos passes normais; a integração ao produtor real do relatório e o cálculo físico ficam pendentes.

## Incremento de comparação em passes reutilizados (09/11/2026)

O pós-processador do **mesmo** relatório agora admite `ExploratoryParameters: [{"Name":"OptionId","Values":["..."]}]` e compara **somente opções já fisicamente medidas pela calibração normal**, com `CandidatePairsSha256`, batch, margem e `blockingPasses` idênticos. Registra recall/FDR observados e deltas descritivos; não refaz SQL, não ajusta parâmetros, não recalcula FDR de certificação e não muda a seleção. Se não houver opção comparável, o resultado explícito é `NO_COMPARABLE_EXISTING_MEASUREMENTS`, sem números fabricados. Testes adicionados para frame idêntico e passes divergentes. **Limite importante:** não implementa a medição de combinações novas fora do catálogo executado pela calibração normal, nem integração automática com o produtor C#; o script ainda requer invocação explícita em cópia do relatório. O gate GitHub não foi comprovado verde neste SHA, e a PR permanece draft e isolada da master.

## Aprovação expressa do PO — decisões 1B, 2B e 3B (09/11/2026)

**1B — espaço de pesquisa:** a investigação pode avaliar **novas combinações de parâmetros** sobre os **mesmos pares candidatos produzidos pelos passes normais**, sem alterar blocking, plano normal, seleção ou ativação. Não confundir comparação de opções já medidas com avaliação de combinações novas: esta última exige scorer/fit efetivos e evidência física.

**2B — orçamento:** a pesquisa tem **orçamento próprio, explícito, versionado e configurável** para número de combinações, memória, tempo e paralelismo. Falta ou violação de limites implica falha fechada; o orçamento normal não é modificado.

**3B — análise estatística:** o **mesmo relatório** deve incluir, apenas quando a pesquisa estiver habilitada, recall, FDR, incerteza, comparações, multiplicidade e dependência entre observações. Diagnósticos não certificados devem declarar suas limitações; nunca alegar UCB válida para pares dependentes sem método e evidência por identidade-base. Não há promoção automática, consumo de TEST ou inferência confirmatória.

**Rastreabilidade e precedência:** decisões expressas do PO, complementares às regras de isolamento, compatibilidade e relatório único; não revogam os gates científicos ou GOV-DOC-01. A PR #1356 permanece draft, sem merge na master. A implementação integral somente estará concluída quando a avaliação real das novas combinações, os testes e a integração condicional ao produtor do relatório estiverem verificados.

## Incremento de implementação das decisões 1B/2B (09/11/2026)

Criado `Solution/scripts/v9-exploratory-research-grid.py` e testes específicos. O planejador exige `ResearchOnly=true`, `Enabled=true`, SHA-256 do frame de pares candidatos da calibração normal e orçamento explícito de `MaximumConfigurations`, `MaximumMemoryMiB`, `MaximumSeconds` e `MaximumParallelism`. Aceita domínios numéricos limitados de `SmoothingAlpha`, `DecisionThreshold`, `TfWeight` e `ConflictMargin`, produz grade determinística de IDs `EXP_`, com `EligibleForSelection=false`, `PhysicallyEvaluated=false`, `Certified=false` e hash do plano. Rejeita parâmetros desconhecidos, valores inválidos e grade acima do limite. **Não há scorer, fit, leitura física de SQL, enforcement de memória/tempo durante avaliação ou cálculo de incerteza**: orçamento e grade são validação de planejamento, não execução. Workflow de pesquisa atualizado para testes, sem resultado verde comprovado. Não considerar a PR concluída ou aprovada cientificamente até os itens pendentes serem implementados e verificados.

## Preparação de ensaio físico isolado (09/11/2026)

Adicionado `.github/workflows/v9-exploratory-physical-research.yml`, exclusivo de `workflow_dispatch`, com guarda de ref do ramo research, checkout no SHA, SQL Server descartável com credencial mascarada, corpus conhecido seed42, pin IBGE, calibrador físico C# existente e análise condicional em cópia do **mesmo** relatório. Inclui comparação estrutural do relatório normal e coleta de artefatos sem CSV bruto. **Não foi executado nesta rodada; não há PASS físico.** A execução preparada **reutiliza apenas opções já medidas**, não executa a grade de novos parâmetros 1B, nem estima incerteza por conglomerados/multiplicidade 3B. Além disso, GitHub Actions pode exigir que o workflow exista na branch default para aceitar `workflow_dispatch`; por isolamento da master, não foi adicionado workflow à default. Portanto, a possibilidade de disparo precisa ser verificada antes de declarar o ensaio operacional. Nenhum merge ou alteração da master foi realizado.

## Correção de comparabilidade e CI (09/11/2026)

O relatório exploratório passa a reconhecer o SHA do conjunto de pares físicos também em `candidatePriorTrainingAudit.PhysicalCandidatePairsSha256` e exige **MuPolicy idêntica** além de batch, margem, passes e frame físico; caso contrário não declara comparação. Cada delta é rotulado descritivo, sem intervalo independente por identidade-base ou correção de multiplicidade. O workflow de testes da pesquisa passou a observar alterações no workflow físico. **Não houve disparo físico nem PASS verificado nesta rodada**; o ambiente local não alcança a rede GitHub diretamente e o conector disponível não expõe disparo de workflow. A execução de combinações novas e a análise 3B seguem pendentes. Não confundir preparação de CI com evidência experimental.

## Disparo de execução física pelo ramo research (09/11/2026)

Para remover a dependência de `workflow_dispatch` na branch default, o workflow `.github/workflows/v9-exploratory-physical-research.yml` passou a aceitar `push` **somente** na branch `research/v9-exploratoria-parametros-ampliados` e **somente** quando o próprio arquivo do workflow muda. O commit `022b79fc0e7c7506a1eea0aa8aaf5652ec0af109` satisfaz o filtro e solicita a execução no GitHub Actions, sujeita às políticas/permissões de Actions. O workflow conserva a verificação do SHA exato, banco descartável e exclusão de recursos. **O conector disponível não permite listar runs de push nem confirmar conclusão**; nenhum PASS ou resultado físico deve ser afirmado sem run/artifact verificável. A execução proposta ainda mede apenas a calibração normal e comparações exploratórias dessas medições; não executa novas combinações 1B, não produz inferência 3B completa e não fecha a PR. Nenhum merge ou alteração na master.

## Decisão superveniente do PO — exploration é exclusivamente relatório (08/11/2026, orientação mais recente)

**Decisão vigente e prevalente:** o parâmetro `exploration` **não altera absolutamente nada** na execução da calibração, nos parâmetros, nos passes de blocking, na geração dos pares candidatos, nos ajustes, na busca, nos limites de recursos, na seleção, na ativação ou nas saídas operacionais. Ele **só altera o relatório final**, acrescentando análises descritivas sobre resultados que a calibração normal já produziu. Com `exploration` desabilitado, o relatório é byte a byte idêntico. Com `exploration` habilitado, o mesmo relatório recebe seção identificada como exploratória, sem inventar medições ou inferência certificada.

**Precedência e impacto:** esta decisão esclarece e restringe as interpretações anteriores 1B/2B/3B que previam executar novas combinações físicas por exploração. O planejador `v9-exploratory-research-grid.py` permanece apenas como artefato de propostas, sem direito a executar, alterar o plano ou integrar a calibração; nenhuma grade deve ser executada em nome de `exploration`. A execução física pertinente é a calibração normal, cuja evidência pode ser reutilizada no relatório exploratório. O workflow físico de pesquisa não constitui uma segunda modalidade de calibração e não deve ser tomado como obrigação de execução de novas configurações. Qualquer execução de combinações adicionais exigirá outro mandato explícito, separado de `exploration`.

**Justificativa:** preservar invariância operacional e comparabilidade da calibração normal, com observabilidade analítica opcional. **Rastreabilidade:** esclarecimento expresso do PO nesta conversa; PR #1356; código `Solution/scripts/v9-exploratory-statistical-report.py`. **Validação pendente:** comprovar em CI a invariância byte a byte e o relatório condicional sobre evidência física existente. Nenhum PASS físico novo é afirmado.

## Implementação do contrato report-only (08/11/2026)

O pós-processador aceita `--exploration` como alias de ativação de **relatório**, sem requerer declaração externa de parâmetros: extrai `OptionId` apenas de linhas já medidas (`fitSnapshotSha256`) no `mu-statistical-analysis.json`, exige o schema normal e falha se não houver medição. O modo desabilitado continua copiando os bytes originais. O workflow físico de pesquisa passa a usar somente `--exploration`, sem plano exploratório nem segunda sessão. Testes de regressão cobrem ausência de opções medidas e comparações condicionais automáticas. Commits `05b3a9f5`, `ea610f43`, `c9965148`. **Limite:** isto integra a opção ao pós-processamento/workflow de pesquisa, não ainda à escrita interna do relatório no C#; não há PASS de CI confirmado. Nenhum dado ou decisão de calibração é modificado.

## Integração C# report-only (08/11/2026)

No ramo isolado, o comando `--v9-calibrate` aceita opcionalmente `--exploration` **após o diretório de saída**. Sem a flag, os argumentos e o fluxo anteriores permanecem; com a flag, a busca, os pares, o ajuste, a seleção e os demais artefatos são gerados pelo mesmo caminho, e somente após a escrita de `mu-statistical-analysis.json` é acrescentada a seção `exploratoryAnalysis` com SHA do relatório normal, contagem e IDs de opções já existentes, sem estimar novas medidas. Implementação: commit `eaa63886`, `V9NestedCalibrationCli.cs`. Esta integração C# é distinta do pós-processador Python de pesquisa e deliberadamente não declara certificação estatística. **Build, execução física e equivalência byte a byte no CI ainda não foram comprovados.**

## Integração física no mesmo processo e correção de CI

O workflow isolado agora chama o calibrador C# com `--v9-calibrate <plano> <csv> <resultado> --exploration` **na própria sessão física**, em vez de gerar um segundo relatório por script Python. A verificação do workflow exige `exploratoryAnalysis` no `mu-statistical-analysis.json` final e seus marcadores de não ativação, não certificação e invariância operacional. Corrigido também o diretório de trabalho da descoberta de testes Python. O filtro `push` contempla alterações no CLI C# e no workflow, além do acionamento manual existente. Commit `cd4c2f5c`. **Não afirmar PASS físico ou equivalência dos demais artefatos até haver logs e artefatos verificáveis.**

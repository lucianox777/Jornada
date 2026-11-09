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

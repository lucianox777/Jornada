# Identidade progressiva — inventário de impacto e plano de conclusão V1

Estado: contrato, persistência, cutover transacional do UUID inicial, âncora CPF permanente, composição reversível, publicação atômica de Gold/Serving, APIs, BI e continuidade histórica publicada já foram implementados e homologados estruturalmente em SQL Server e PostgreSQL. `ADR_Identidade_Progressiva.md` contém a decisão normativa consolidada. A V1 usa diretamente `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`, sem camada de compatibilidade com vocabulário anterior. A ativação probabilística real permanece bloqueada pela issue #31.

## Contratos e persistência

| Arquivo / área | Estado atual e trabalho restante |
|---|---|
| `src/Jornada.Contracts/ProgressiveIdentity.cs` | Núcleo V1 com UUID inicial imutável e estados `PROVISORIA`, `REFERENCIA`, `INDEFINIDA`. Resultados da execução continuam `NOVA_IDENTIDADE`, `ASSOCIACAO_EXISTENTE`, `INDEFINIDA`. |
| `src/Jornada.Processor.Worker/ProgressiveIdentityOriginStore.cs` | Persistência real e backfill paginado para SQL Server/PostgreSQL. Leitura é fail-closed para estados fora do contrato V1. |
| `database/Jornada_Identidade_Progressiva.sql` e equivalente PostgreSQL | Schema V1 nativo, histórico append-only, guardas de versão e procedimento transacional. |
| `database/migrations/20260908_Identidade_Progressiva_Processor.sql` e equivalente PostgreSQL | Cutover fail-closed após backlog zero; novas observações asseguram `initial_uuid` dentro da transação do Processor. |
| `identidade.cpf_ancora` e writers SQL Server/PostgreSQL | Âncora CPF permanente integrada. CPF admitido recupera sempre o mesmo UUID e não pode ser transferido por composição probabilística. |
| `IdentityComposition*` | Planejamento, ledger, leitura autoritativa, aplicação, recomposição e publicação atômica implementados, com replay, rollback e conservação da autoridade factual. |
| `database/Jornada_Fase1.sql` e `database/postgresql/Jornada_Processor_Persistence_Core.sql` | `identidade.vinculo_fonte.status='RESOLVIDO'` permanece: esse estado significa atribuição de uma observação e não deve ser confundido com `REFERENCIA`. |
| Linkage Parameters/Calibration/Scoring | Infraestrutura diagnóstica disponível, sem autorização de ativação. Issue #31 continua sendo o gate estatístico independente. |

## Superfícies externas e derivadas

| Arquivo / área | Estado atual e trabalho restante |
|---|---|
| `src/Jornada.Api/ProgressiveOriginApi.cs`, `docs/API.md` e `openapi/jornada-v1.openapi.json` | Leitura explícita de `initial_uuid`, `canonical_uuid`, estado e versão já existe, com autorização por Gestor e sem alterar silenciosamente a semântica dos GETs existentes. |
| `serving.v_identidade_origem_progressiva` | Projeção operacional separa referência inicial, referência canônica, estado e aptidão para contagem de Pessoa. |
| `serving.v_identidade_composicao_historico_publicado` | Continuidade histórica read-only expõe apenas composições efetivamente `PUBLICADA`; preserva todos os pares históricos e não escolhe sucessor arbitrário após separação ou ambiguidade. Implementada nos dois providers. |
| `bi/Jornada.SemanticModel/definition/tables/IdentidadeProgressiva.tmdl` | Modelo BI V1 consome a projeção Serving e diferencia contagem de identidades de origem de `COUNT(DISTINCT canonical_uuid)` para referências publicadas. |
| Gold, Serving e Possibilidades | Fatos permanecem independentes da referência progressiva; recomposição somente pela fronteira transacional válida. Identidade não altera elegibilidade ou possibilidade. |
| Fusões/separações e aliases | Execução estrutural, histórico reversível e continuidade publicada existem; UUID histórico não é reciclado e separação não escolhe sucessor arbitrário. Ativação probabilística real continua bloqueada pela issue #31. |
| Testes e gates | Cobertura de concorrência, rollback, idempotência, segurança, fatos independentes, contagem BI, continuidade histórica e paridade entre providers está integrada aos gates. |

## Sequência de conclusão

**1 — contrato e semântica V1: implementado.** UUID inicial, três estados progressivos e separação conceitual entre referência, resultado de resolução e vínculo factual.

**2 — armazenamento inicial: implementado.** SQL Server e PostgreSQL com chave estável de origem, UUID inicial imutável, versão, histórico append-only, backfill paginado e testes reais.

**3 — cutover do Processor: implementado.** O cutover recusa ativação antes do backfill histórico completo e assegura novas referências na mesma transação do `vinculo_fonte`. Rollback não deixa resíduos.

**4 — âncora CPF universal: implementado.** `identidade.cpf_ancora` é fonte permanente para CPF admitido, com imutabilidade, concorrência, UUID órfão e correção governada sem transferência silenciosa de âncora.

**5 — publicação de referência e composição reversível: implementado estruturalmente.** Ledger, leitura fechada, aplicação, recomposição, publicação atômica Gold/Serving e continuidade histórica publicada estão implementados e homologados nos dois providers. Isso não autoriza ativação probabilística real.

**6 — APIs e BI: implementado estruturalmente.** A API de origem progressiva, as projeções Serving e o modelo semântico BI expõem explicitamente identidade de origem, referência canônica, estado e métricas de contagem sem duplicar Pessoas. A continuidade histórica publicada é preservada sem eleger sucessor arbitrário.

**7 — validação e ativação: pendente.** Corpus representativo e rótulos independentes; recall, calibração, falsos vínculos, erros de composição, subgrupos, variância, escala e aprovação institucional. A issue #31 permanece obrigatória antes de qualquer ativação probabilística real.

## Invariantes de aceitação

- UUID inicial único por identidade de origem e persistente entre versões.
- Estados progressivos somente `PROVISORIA`, `REFERENCIA`, `INDEFINIDA`.
- `REFERENCIA` significa referência canônica estabelecida, não certeza absoluta de identidade civil.
- `RESOLVIDO` pode existir em `identidade.vinculo_fonte` porque ali descreve atribuição de observação; não é estado progressivo.
- Ausência de CPF não impede UUID inicial e não equivale a `INDEFINIDA`.
- Execução incompleta não publica referência.
- CPF permanente nunca é transferido, reciclado ou substituído por decisão probabilística.
- Fatos válidos sobrevivem à ausência, conflito ou mudança de referência.
- UUID histórico nunca é reciclado; separação não redireciona dados para sucessor arbitrário.
- BI conta Pessoas referenciadas por `canonical_uuid` distinto, nunca por UUID inicial provisório.
- CI e testes não autorizam por si só ativação de modelo probabilístico.
- Não há módulo de Regularização Cadastral nem decisão humana obrigatória caso a caso.

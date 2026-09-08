# Identidade progressiva — inventário de impacto e plano de conclusão V1

Estado: contrato, persistência e cutover transacional do UUID inicial já implementados em SQL Server e PostgreSQL. `ADR_Identidade_Progressiva.md` contém a decisão normativa consolidada. Como a solução ainda não foi publicada, a V1 usa diretamente `PROVISORIA`, `REFERENCIA` e `INDEFINIDA`, sem camada de compatibilidade com vocabulário anterior.

## Contratos e persistência

| Arquivo / área | Estado atual e trabalho restante |
|---|---|
| `src/Jornada.Contracts/ProgressiveIdentity.cs` | Núcleo V1 com UUID inicial imutável e estados `PROVISORIA`, `REFERENCIA`, `INDEFINIDA`. Resultados da execução continuam `NOVA_IDENTIDADE`, `ASSOCIACAO_EXISTENTE`, `INDEFINIDA`. |
| `src/Jornada.Processor.Worker/ProgressiveIdentityOriginStore.cs` | Persistência real e backfill paginado para SQL Server/PostgreSQL. Leitura é fail-closed para estados fora do contrato V1. |
| `database/Jornada_Identidade_Progressiva.sql` | Schema SQL Server V1 nativo, histórico append-only, guardas de versão e procedimento transacional. |
| `database/postgresql/Jornada_Identidade_Progressiva.sql` | Paridade PostgreSQL do schema V1 e das guardas. |
| `database/migrations/20260908_Identidade_Progressiva_Processor.sql` e equivalente PostgreSQL | Cutover fail-closed após backlog zero; novas observações asseguram `initial_uuid` dentro da transação do Processor. |
| `src/Jornada.Processor.Worker/IdentityResolutionCoordinator.cs` | CPF continua com precedência determinística. A publicação futura de referência probabilística permanece desativada. |
| `src/Jornada.Processor.Worker/SqlProcessorRepository.cs` / `PostgreSqlProcessorRepository.cs` | Persistência factual e de vínculos segue independente da identidade progressiva. Próxima integração relevante: tornar a âncora CPF permanente fonte universal dos writers e das correções. |
| `src/Jornada.Processor.Worker/*IdentityMapRepository.cs` | Preservar trava determinística por CPF e impedir reaproveitamento inseguro. Evoluir em conjunto com a âncora permanente. |
| `database/Jornada_Fase1.sql` e `database/postgresql/Jornada_Processor_Persistence_Core.sql` | `identidade.vinculo_fonte.status='RESOLVIDO'` permanece: esse estado significa atribuição de uma observação e não deve ser confundido com `REFERENCIA`. |
| Linkage Parameters/Calibration/Scoring | Infraestrutura diagnóstica disponível, sem autorização de ativação. Issue #31 continua sendo o gate estatístico independente. |

## Superfícies externas e derivadas

| Arquivo / área | Impacto restante |
|---|---|
| `docs/API.md` e `openapi/jornada-v1.openapi.json` | Definir leitura explícita de referência inicial/canônica sem alterar silenciosamente a semântica dos GETs existentes. Referência dividida nunca pode retornar sucessor arbitrário. |
| `src/Jornada.Api/Program.cs` | Expor referências apenas com autorização e finalidade adequadas; uma `REFERENCIA` não amplia automaticamente o compartilhamento de dados. |
| Schemas de contratos | Quando a referência for publicada externamente, incluir estado e natureza da referência mantendo fato/atribuição separados. |
| Gold, Serving, BI e Possibilidades | Preservar fatos mesmo sem referência canônica, evitar dupla contagem quando houver composição e recompor derivados apenas com decisão transacional válida. |
| Fusões/separações e aliases | Ainda precisam de política de sobrevivência, eventos reversíveis e resolução histórica unívoca ou explicitamente ambígua. |
| Testes e gates | Manter cobertura de concorrência, rollback, idempotência, segurança, fatos independentes e paridade entre providers. |

## Sequência de conclusão

**1 — contrato e semântica V1: implementado.** UUID inicial, três estados progressivos e separação conceitual entre referência, resultado de resolução e vínculo factual.

**2 — armazenamento inicial: implementado.** SQL Server e PostgreSQL com chave estável de origem, UUID inicial imutável, versão, histórico append-only, backfill paginado e testes reais.

**3 — cutover do Processor: implementado.** O cutover recusa ativação antes do backfill histórico completo e assegura novas referências na mesma transação do `vinculo_fonte`. Rollback não deixa resíduos.

**4 — âncora CPF universal: próxima etapa.** Integrar `identidade.cpf_ancora` aos writers SQL Server/PostgreSQL e aos caminhos de correção governada. Um CPF admitido deve sempre recuperar o mesmo UUID permanente, sem transferência de âncora.

**5 — publicação de referência e composição reversível.** Implementar executor de decisão somente após política/modelo aprovados; depois, fusão/separação, aliases, recomposição Gold/Serving e histórico de composição.

**6 — APIs e BI.** Expor referência inicial/canônica, estados e ambiguidade histórica sem misturar identidade com fato ou elegibilidade/possibilidade.

**7 — validação e ativação.** Corpus representativo e rótulos independentes; recall, calibração, falsos vínculos, erros de composição, subgrupos, variância, escala e aprovação institucional. A issue #31 permanece obrigatória antes de qualquer ativação probabilística real.

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
- CI e testes não autorizam por si só ativação de modelo probabilístico.
- Não há módulo de Regularização Cadastral nem decisão humana obrigatória caso a caso.

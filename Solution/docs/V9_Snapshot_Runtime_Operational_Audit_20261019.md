# V9 — contrato executável de snapshot e auditoria do caminho operacional (2026-10-19)

O snapshot portátil agora tem um consumidor explícito de projeção e blocking em `Jornada.Linkage.Runner/V9FittedSnapshotExecutionRuntime.cs`. O consumidor é opt-in, não é registrado em DI e não remove o bloqueio operacional V9.

## Consumir sem estimar novamente

1. Carregar os bytes e o SHA previamente fixado por `V9FittedSnapshotExecutionRuntime.Load`. O codec de #1157 continua validando schema, parâmetros, TF e conteúdo integral.
2. O consumidor valida a identidade física da projeção. Para `V9_CALIBRATION_BLOCKING_PROJECTION_V1`, identifica o comprimento máximo de prefixos exclusivamente pelo fingerprint exato de uma das variantes conhecidas de 1 a 64; não aplica um default quando o snapshot diverge. A variante legada aceita somente a identidade de projeção suportada existente.
3. `Project` executa os mesmos projetores de contratos. `Plan` aplica os passes na ordem congelada, omite passes com atributo ausente e não transforma ausência em wildcard. Features fora do vocabulário daquela projeção falham antes da execução.
4. `BuildCandidateUuidQuery` exige schema e fingerprint obtidos do contexto materializado real. A consulta reutiliza o construtor parametrizado existente: INTERSECT entre atributos, UNION entre passes, aliases históricos de nomes/prefixos e somente versão corrente para nascimento. Os guards de quantidade de parâmetros continuam ativos.
5. Ler a união completa, sem truncamento, sob a projeção fixada e criar `V9RetrievedSnapshotCandidateFrame` vinculando SHA do fit, schema e fingerprint materializados. `ResolveRetrievedCandidatesPreview` rejeita frame de outro fit/projeção e universo declarado incompleto; reutiliza `V9FittedSnapshot.ResolvePreview`, que já chama o mesmo kernel FS/TF/Rank/Resolve C#. Não há um segundo scorer ou ajuste.

O manifesto materializado e a completude do frame continuam sendo evidências externas que o chamador precisa autenticar. Um conjunto de strings declarado pelo chamador não demonstra existência/cobertura de índices físicos nem proveniência SQL. A API apenas constrói SQL; não abre conexão ou executa consultas.

O índice operacional legado não contém as chaves V9 de prefixos. Passar sua identidade para um fit V9 é erro explícito; não se converte em consulta vazia, pendência de identidade ou fallback. O caminho público legado de `BlockingProjectionCandidateQueryBuilder` continua recusando a projeção opt-in V9.

## Auditoria do fluxo existente

| Etapa | Caminho inspecionado | Situação real |
|---|---|---|
| API síncrona | `Jornada.Api/SqlApiServices.cs`, `SqlIdentityResolutionService` | Sem CPF retorna `CPF_AUSENTE_CONSULTA_NAO_DISPARA_LINKAGE`; CPF consulta a âncora. Não dispara FS V9. |
| Processor | `IdentityResolutionCoordinator.cs`, `SqlProcessorRepository.cs` | CPF válido segue âncora determinística; ausência admitida retorna `AGUARDA_LINKAGE_SOB_DEMANDA`. Nenhum score probabilístico automático. |
| Carga operacional | `SqlProbabilisticIdentityLinkage.cs` | Carrega modelo SQL, parâmetros escalares, TF histórico e ruleset. Ainda não carrega envelope/fit portátil V9 nem configuração de agrupamento. |
| Gate V9 | `Jornada.Linkage.Core/ProbabilisticLinkagePolicy.cs` | `RequireCertifiedOperationalAlgorithm` recusa V9 antes do fallback V8 em criação, Rank e Resolve operacional. Preservado. |
| Comparação e decisão experimental | `V9FittedSnapshot.cs`, `ProbabilisticLinkagePolicy.cs` | Fit imutável usa m/u conjunto, nascimento e TF congelados; Resolve compartilha thresholds, margem log-odds, conflitos e guards existentes. Consumidor novo acrescenta projeção/passes executáveis. |
| Reavaliação | `ProbabilisticLinkageBatchRunner.EligibleFromWhereSql` | INCREMENTAL inclui fontes sem vínculo, pendentes, `NAO_RESOLVIDO` e `CONFLITO`, permitindo reconsideração após novas referências. Filtra observações sem CPF. |
| Persistência/publicação | `ProbabilisticLinkageBatchRunner` | Materializa universo; valida contagem, modelo/versão e resultados pertencentes ao run; publicação progressiva preserva precedência de CPF e correções governadas. Preview novo não escreve nesses caminhos. |
| Aprovação DT-15 | migrations `20261002_DT15_Human_Model_Approval.sql`, `20261003_DT15_Human_Approval_Dossier_Binding.sql` | Aprovação append-only exige decisor, motivo, ação, fingerprint exato do modelo, modelo ativo-base e dossiê; mudanças invalidam aprovação. Isso não constitui binding operacional automático do novo envelope V9. |

## Verificação e pendências reais

Os testes `V9FittedSnapshotExecutionRuntimeTests` verificam chaves concretas de prefixos e campos, ordem dos passes, ausência de wildcard, parâmetros reais da consulta, rejeição de contexto legado e frame divergente, paridade com Resolve compartilhado e mudança efetiva de resultado ao alterar threshold/margem. São testes de contrato e de decisão; não constituem ensaio físico SQL ou prova de independência estatística.

Continua pendente a integração operacional governada: materialização/manifesto físico V9 compatível, vínculo aprovado entre modelo SQL e conteúdo integral do envelope/fit/agrupamento, consumidor operacional desse vínculo, evidência independente suficiente e autorização DT-15. Essas condições devem preceder qualquer retirada do gate. Build, congelamento local, preview, consulta construída e testes verdes não publicam nem ativam a V9.

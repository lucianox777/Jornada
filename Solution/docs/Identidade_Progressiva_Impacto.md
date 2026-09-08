# Identidade progressiva — inventário de impacto e plano de migração

Base examinada: `master` f10c94a66e489f73efe8b4983a596505d43f47f0 (PR #34). Estado: análise estática e primeira implementação de domínio; não é comprovação de paridade operacional nem autorização de implantação. A ADR_Identidade_Progressiva.md é a referência normativa da evolução. Os caminhos são arquivos reais da árvore; o inventário não afirma que todos os chamadores ou procedimentos já foram migrados.

## Contratos e persistência

| Arquivo / área | Comportamento atual e mudança necessária |
|---|---|
| `src/Jornada.Contracts/IdentityLinkageContracts.cs` | `InternalIdentityResolution` aceita UUID nulo; `IIdentityMapRepository` resolve por CPF; fallback não recebe referência inicial de origem. Acrescentar contrato versionado e contexto estável sem alterar silenciosamente o retorno CPF. |
| `src/Jornada.Contracts/ProgressiveIdentity.cs` | Novo núcleo puro. Estados e transições testados; faltam executor transacional e persistência. Não registrar como serviço operacional nesta etapa. |
| `src/Jornada.Processor.Worker/IdentityResolutionCoordinator.cs` | CPF válido usa rota determinística; CPF ausente admitido retorna `NAO_RESOLVIDO`, UUID nulo, aguardando Linkage sob demanda. Integrar criação idempotente por origem, sem novo UUID a cada retry e sem retirar a trava de CPF. |
| `src/Jornada.Processor.Worker/SqlProcessorRepository.cs` | Resolve/cria CPF e persiste vínculos; conflito do mapa suspende atribuições Gold/Serving e pode remover projeção Gold da pessoa. Revisar efeitos de conflito e recomposição, sem substituir suspensão por fusão automática. |
| `src/Jornada.Processor.Worker/PostgreSqlProcessorRepository.cs` | Persiste Silver, resolução CPF, vínculo e fatos em transação serializável; sem CPF retorna UUID nulo. Implementar semântica equivalente à SQL Server, incluindo retransmissão, lock de origem, backfill e recuperação. |
| `src/Jornada.Processor.Worker/PostgreSqlIdentityMapRepository.cs` | Mapa determinístico e trava de consistência. Preservar precedência CPF e impedir reaproveitamento inseguro de identificador conflitado. |
| `src/Jornada.Processor.Worker/IngestionProcessor.cs` e `IProcessorRepository.cs` | Worker publica pacote validado; lease/fencing e transação pertencem ao provider. Não criar UUID fora da transação nem antes de conhecer a chave estável da origem. |
| `database/Jornada_Fase1.sql` | DDL, constraints, views e procedimentos canônicos de identidade, correção e Gold. Migração aditiva de referência inicial, estado versionado, histórico e aliases, com backfill, testes de upgrade e rollback. Revisar constraints que exigem UUID nulo em estados pendentes. |
| `database/postgresql/Jornada_Processor_Persistence_Core.sql` | `identidade.pessoa` usa `ATIVO/EM_CONFLITO/INATIVO`; `vinculo_fonte` exige UUID apenas em `RESOLVIDO`. Preservar estado operacional separado e criar constraints/índices equivalentes. Instalação dupla e migração em banco povoado. |
| `src/Jornada.Linkage.Parameters.Worker/PostgreSqlCandidateSampler.cs`, `CandidateSamplingEngine.cs` | Amostragem diagnóstica por fontes e candidatos. Reutilizar desenho e pesos sem confundir amostra com decisão de fusão. |
| `src/Jornada.Linkage.Parameters.Worker/CandidateWeightedEstimator.cs`, `CandidateLabeling.cs` | Estimador com rótulos independentes, sem parâmetros operacionais. Manter isolado até validação; UUID diferente não é rótulo negativo automático. |
| `src/Jornada.Linkage.Parameters.Worker/PostgreSqlLinkageCalibrator.cs` | Piloto com gates de validação. Preservar bloqueio de ativação e substituir estimadores somente mediante validação metodológica. |
| `src/Jornada.Operational.Sql/PostgreSqlBirthBlockingQuery.cs` e `src/Jornada.Contracts/BirthBlockingPlan.cs` | Blocking compartilhado e versionado. Não inferir inexistência de duplicata a partir de universo incompleto; medir recall antes da publicação real. |

## Superfícies externas e derivadas

| Arquivo / área | Impacto |
|---|---|
| `docs/API.md` e `openapi/jornada-v1.openapi.json` | Manter compatibilidade da resolução CPF e GETs; criar contrato explícito para referência inicial, estado e resolução histórica. Nunca devolver alias dividido como unívoco. |
| `src/Jornada.Api/Program.cs` | Revisar handlers de Pessoa, resolução, consulta por UUID, autorização e limites. Não ampliar acesso entre origens apenas porque uma hipótese foi criada. |
| `config/contracts/pessoa.schema.json` e schemas relacionados | Versionar campos novos e manter contratos anteriores até migração dos consumidores. Não inferir UUID inicial de nome ou CPF. |
| `database/Jornada_Fase1.sql` — `gold.pessoa`, `serving.v_pessoa`, BI, fatos e Possibilidades | Separar referência de origem, atribuição canônica e estado. Preservar fatos, evitar dupla contagem de aliases e invalidar derivados quando a composição mudar. |
| `database/postgresql/Jornada_Processor_Persistence_Core.sql` e `Jornada_Resultado_Core.sql` | Paridade de projeções e constraints entre providers. |
| `docs/ADR_Linkage_Multievidencia_Universal.md` e `ADR_Linkage_Sunter_Multievidencia_Qualidade_v4.06.md` | Complementar a política sem revogar a universalidade das evidências ou a necessidade de calibração. |
| `docs/PostgreSQL_Linkage_Candidate_Sampling.md` e `PostgreSQL_Linkage_Independent_Labels.md` | Preservar escopo diagnóstico, quadro de fontes, pesos, proveniência e limites de representatividade. |
| `scripts/technical-closure-gate.py`, `analyzer-cleanliness-gate.py`, gates de arquitetura e workflows CI | Atualizar invariantes somente depois da respectiva migração; não desabilitar checks para permitir a mudança. |
| Testes SQL, PostgreSQL, API, Processor, Linkage, BI e E2E | Regressões de estados, idempotência, concorrência, referências históricas, fusão/separação, preservação factual e segurança, mantendo testes legados. |

## Sequência de execução

**Fatia 1 — contrato e decisão (este PR).** ADR, inventário, núcleo puro e regressões. Nenhuma mudança de DDL ou comportamento operacional. Gate Release com warnings como erros e regressões existentes. A integração não ativa a arquitetura.

**Fatia 2 — armazenamento inicial.** Definir modelo relacional, criar migração aditiva em SQL Server e PostgreSQL, chave única de origem, UUID inicial imutável, estado/versionamento e histórico. Testar concorrência, retransmissão, nova versão da mesma origem, rollback, backfill e instalação repetida. Não preencher `RESOLVIDA` retroativamente sem evidência de execução.

**Fatia 3 — Processor.** Constituir/reutilizar UUID inicial na mesma transação do registro de origem e vincular fatos sem modificar proveniência. Manter CPF prioritário. Adicionar leitura compatível que diferencie referência inicial de identidade canônica. Testar primeira entrada sem CPF, retransmissão, CPF posterior, CPF inválido, conflito, lote repetido, lease perdido e E2E Bronze→Silver→Gold/Serving. Não habilitar fusão probabilística.

**Fatia 4 — publicação da resolução.** Executor com modelo congelado aprovado, universo completo, decisões idempotentes e controle de concorrência. Inconclusão não escolhe candidato. Aplicação transacional e recomposição. Reavaliação não redefine identidade como provisória. Sem modelo aprovado, nenhuma resolução probabilística real é publicada.

**Fatia 5 — composição reversível.** Fechar política de sobrevivência e aliases, implementar eventos de fusão/separação, resolução histórica unívoca ou explicitamente ambígua, reatribuição por observação e recomposição Gold/Serving. Provar que nenhum fato desaparece, UUID não é reciclado e divisão não redireciona dados para a pessoa errada. Versionar GETs e BI e testar autorização.

**Fatia 6 — validação/ativação.** Corpus representativo e rotulado independentemente; recall, calibração, falsos vínculos, erros de fusão/separação, subgrupos, variância, escala, segurança, migração povoada e aprovação institucional. Ativar somente versões/limiares aprovados. A issue #31 permanece responsável pela validação estatística.

## Invariantes de aceitação

UUID inicial único por identidade de origem e persistente entre versões. Retries concorrentes não criam duplicatas. Estados públicos somente `PROVISORIA`, `RESOLVIDA`, `INDEFINIDA`. Ausência de CPF não equivale a indefinição; ausência de candidato após busca completa pode produzir nova identidade resolvida. Execução incompleta não publica resultado. UUID histórico nunca é reciclado. Fatos sobrevivem a fusões/separações. Referência dividida não retorna sucessor arbitrário. Não há rótulo negativo inferido apenas de UUID diferente nem ativação de modelos por passar CI. O sistema não exige decisão humana caso a caso, mas mantém política validada, auditoria e correção automática reversível.

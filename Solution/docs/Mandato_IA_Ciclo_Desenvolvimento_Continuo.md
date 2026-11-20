# Mandato operacional da IA — ciclo de desenvolvimento contínuo da Jornada do Cidadão

**ID:** MANDATO-IA-CICLO-CONTINUO-20261119  
**Autoridade:** Product Owner, solicitação expressa no chat do Projeto Jornada.  
**Estado:** mandato operacional aprovado quanto à elaboração e às instruções de acompanhamento; arquivo registrado em PR, ainda não integrado à `master`.  
**Documento canônico de decisão:** [GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111 e aditivo GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md).  
**Política superior:** [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).  
**Aplicação:** toda interação operacional com a IA neste projeto; o mandato não pressupõe que a IA esteja executando entre mensagens.

## 1. Missão permanente

Em cada interação, a IA deve executar **simultaneamente** dois ciclos coordenados: **observação contínua e cumulativa do GitHub/CI** e **avanço técnico efetivo do projeto**. A IA não deve limitar-se a repetir status de checks, nem avançar no código ignorando as execuções, os incidentes e a integridade da `master`. O resultado de uma rodada deve ser verificável por links, SHAs, runs, jobs, arquivos, testes e documentos.

«Continue» significa prosseguir nas tarefas já autorizadas, com iniciativa técnica, respeitando as decisões e as restrições vigentes. Não é autorização para ignorar proteção de branch, executar ciência, promover release, alterar LOCK ou realizar merge fora do mandato aprovado.

## 2. Fontes de verdade e precedência

Antes de atuar, consultar os documentos oficiais no GitHub, na versão realmente acessível da `master` e, quando pertinente, na branch de trabalho: `AGENTS.md`, este mandato, [protocolo de correção e integração](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md), `Indice_Decisoes_Vigentes.md`, `Indice_Acervo_Documental.md`, especificações e documentos técnicos da frente, além de issues/PRs e evidências Actions. Se houver divergência, identificar qual versão está integrada e qual é apenas proposta. O histórico do chat é contexto, **não** prova de execução nem substituto documental.

Este mandato é **instrução operacional derivada**; não revoga decisões científicas, de segurança, produto ou governança. Em conflito, explicitar a precedência e solicitar decisão do PO apenas quando ela for necessária.

## 3. Algoritmo obrigatório para cada interação

### Fase A — entendimento e sincronização

1. Identificar o pedido, o objetivo da rodada, as autorizações expressas já concedidas e seus limites; distinguir proposta, decisão, implementação, medição, validação, integração e ativação.
2. Consultar a HEAD exata da `master`, branch/ref, data/hora e SHAs relevantes. Conferir PRs abertas, Draft/Ready, conflitos, base, revisões, checks, issues bloqueadoras e dependências.
3. Verificar se outra PR já resolve a demanda, se há trabalho duplicado ou substituído e se documentos de governança já registram a decisão.
4. Se o acesso à API estiver indisponível, registrar a limitação; nunca transformar lembrança de rodada anterior em estado atual.

### Fase B — inventário acumulativo de GitHub Actions

5. Consultar as execuções **de todo o escopo relevante do repositório**, incluindo `master`, PRs e workflows de engenharia/físicos, e não apenas as últimas PRs. Usar listagem global com paginação e janela temporal explícita, quando disponível. Reconciliar com o checkpoint anterior e manter janela retrospectiva que capture execuções antigas ainda ativas.
6. Chave única da execução: `run_id`. Registrar também `run_attempt` para diferenciar reexecuções. Campos mínimos: workflow, evento, branch, SHA, PR, timestamps, status, conclusão, URL, jobs, última observação, classe de impacto e observações. Uma reexecução não duplica a contagem de runs; seu histórico de tentativas é mantido à parte.
7. Classificar cada run **uma única vez** na observação atual:
   - **Aprovadas:** `completed/success`.
   - **Em execução:** `in_progress`.
   - **Enfileiradas/aguardando:** `queued`, `pending`, `waiting`, `requested`.
   - **Reprovadas/bloqueadas:** `completed/failure`, `timed_out`, `action_required`, com subtipo.
   - **Outras/indeterminadas:** `cancelled`, `skipped`, `neutral`, `stale`, status/conclusão ausentes ou desconhecidos.
8. Validar a identidade contábil: `total de run_id distintos = aprovadas + em execução + enfileiradas + reprovadas + outras`. Produzir recortes por SHA, `master`, PR, workflow, categoria e período. Não misturar execução de engenharia com certificação científica.
9. Inspecionar os jobs e steps das execuções reprovadas, críticas, bloqueadas ou suspeitas, inclusive as que ainda estão em andamento. Um job pode falhar antes da conclusão do workflow; `skipped` não é `success`. Capturar `job_id`, nome, conclusão, step, evidência segura e relação causal. Memory Batch e A2 são **exemplos históricos**, não incidentes presumidamente ativos.
10. Comparar o inventário com o checkpoint anterior: novas execuções, transições fila→execução→conclusão, falhas novas e resolvidas, runs estagnados, reruns e diferenças por SHA. Sem checkpoint, declarar **linha de base inicial**. Sem API global paginada, declarar **inventário parcial**, com lista do que foi efetivamente consultado; jamais afirmar total global.
11. Nunca apresentar os valores históricos 19/15/6/1 como atuais. Nunca declarar CI geral verde a partir de apenas três PRs ou de um único SHA.

### Fase C — triagem, investigação e desenvolvimento

12. Priorizar incidentes por risco de integridade, segurança, regressão na `master`, bloqueio de PR, impacto científico e dependências; diferenciar falhas de produto, teste, infraestrutura, permissão, proveniência, segurança e causa desconhecida.
13. Para cada falha prioritária: identificar `run_id` e `job_id`, SHA, erro mínimo seguro, hipótese causal e evidência; reproduzir em teste/fixture isolada quando possível; propor e implementar correção mínima sem enfraquecer gate.
14. Preferir corrigir na PR existente. Não abrir branches/PRs redundantes nem empilhar mudanças sem justificativa. Acrescentar teste negativo/de regressão e documentação pertinente.
15. Enquanto CI estiver em execução, **continuar outra tarefa independente autorizada**: revisar código, melhorar testes, preparar reconciliação, corrigir defeito conhecido ou documentar decisões. Não fazer polling repetitivo sem informação nova; não encerrar a rodada apenas com «CI em andamento» se houver trabalho técnico viável.
16. Retry somente para erro infraestrutural classificado e nos limites do `jornada-ci-watchdog`. Não cancelar runs válidos, não mascarar erro determinístico como infra, não disparar campanhas físicas caras sem causa corrigida.

### Fase D — validação, integração e pós-merge

17. Verificar no **HEAD exato** da PR os gates rápidos exigidos (`classify-impact`, `unit-runtime`, `security-guards`, `deterministic-build` conforme configuração vigente), regressões específicas, conflitos, revisões e documentação. Status de commit anterior não é prova para HEAD novo.
18. Integrar por **squash merge** somente quando houver autorização aplicável do PO, elegibilidade e checks. Preservar regras de proteção, não fazer push direto ou forçado à `master`.
19. Após o merge, registrar SHA de integração e conferir a HEAD atual da `master`. Verificar `jornada-ci`, seed42/30k quando o classificador indicar impacto, Windows e validação independente quando exigíveis, e os demais workflows pertinentes **naquele SHA**. Distinguir `success`, `failure`, `skipped`, não disparado e pendente.
20. Se falhar, iniciar novo ciclo de triagem e correção; se ainda não houver execução visível, declarar **pós-merge não comprovado**, não `PASS`. Encerrar incidente apenas com critérios e evidências de aceite atendidos.

### Fase E — documentação e resposta

21. Para toda decisão do PO, atualizar o documento canônico e o índice de decisões vigentes, além do índice do acervo quando pertinente. Registrar autoridade, data, justificativa, alternativas, impactos, precedência, estados distintos e evidências. Propostas não aprovadas devem ser rotuladas como propostas.
22. Commitar documentos na mesma PR da implementação sempre que possível. A PR ou o chat não substituem o documento; sem merge, dizer **registrado em PR, não integrado**.
23. Responder com painel da CI e escopo de cobertura; delta desde a rodada anterior; falhas de jobs e ações; desenvolvimento realmente efetuado; arquivos/PRs/SHAs; resultados de testes; bloqueios; próximo passo objetivo. Não confundir intenção, implementação, CI em andamento e validação comprovada.

## 4. Modelo de relatório por interação

```text
JORNADA — CHECKPOINT DA INTERAÇÃO
Observado em: <timestamp e fuso>
master HEAD: <sha verificado ou indisponível>
Cobertura: GLOBAL COMPLETA / PARCIAL (<janela, filtros, paginação, limitação>)
Execuções únicas: <N> | Aprovadas <A> | Em execução <E> |
  Enfileiradas <Q> | Reprovadas <F> | Outras <O>
Identidade: N = A + E + Q + F + O [verificada/não verificável]
Delta: <novos runs, transições, falhas e resoluções, ou sem baseline>
Falhas por job: <run_id/job_id, causa confirmada/hipótese, ação>
PRs e SHAs: <HEAD, checks, conflitos, autorização, estado>
Trabalho realizado: <arquivos/commits/testes/PRs, ou nenhum>
Pós-merge: <SHA, workflows aplicáveis e resultado ou pendência>
GOV-DOC-01: <decisões, documentos, índice e status de integração>
Bloqueios: <precisos, sem presumir autorização>
Próxima ação: <objetiva e executável>
```

O painel deve incluir categoria **Outras** para não classificar cancelamentos/skips como sucesso. A contagem representa **execuções únicas no recorte informado**, não «total histórico do GitHub» sem consulta completa. Relatar separadamente a quantidade de jobs falhados; ela não é igual à quantidade de workflows falhados.

## 5. Persistência e não duplicação

O checkpoint operacional deve permitir retomar observações sem dupla contagem: janela temporal, último cursor/página, timestamp, conjunto de `run_id`, tentativa atual, SHA, estado anterior/atual, jobs críticos e transições. Preservar histórico sem sobrescrever evidência; não registrar segredos, PII ou logs sensíveis. Atualizações transitórias de CI são registros operacionais, não novas decisões do PO.

A automação `.github/workflows/ci-watchdog.yml` e a política de recuperação limitada permanecem independentes. **O chat não executa sozinho entre mensagens**; só afirmar acompanhamento horário ou persistente quando houver automação efetivamente criada e confirmada. Caso a ferramenta não exponha listagem global de Actions, comunicar a cobertura parcial e não fabricar checkpoint global.

## 6. Limites inegociáveis

- Não executar TRAIN/TEST confirmatório, 100k, Gold, publicação, ativação, mudança de LOCK/referência ou alteração estatística sem autorização específica e gates correspondentes.
- Não contornar branch protection, reduzir checks, alterar resultados, desabilitar provenance gate ou tratar `skipped` como aprovado.
- Não reescrever histórico científico nem expor dados reais, credenciais ou segredos.
- Não afirmar que um documento está na `master` enquanto estiver apenas em PR.
- Não prometer execução futura em segundo plano sem ferramenta agendada e confirmação.

## 7. Critérios de aceite deste mandato

- Documento versionado em PR, com link no índice pertinente e apontador de agente.
- Em cada rodada: consulta real ao GitHub, inventário com cobertura explícita e evidências, acompanhamento por job e avanço técnico quando viável.
- Contagens sem duplicidade, estados mutuamente exclusivos, deltas apenas com baseline real.
- Falhas tratadas até prova pós-merge no SHA aplicável, preservadas autorizações e governança.
- O mandato é considerado **documentado** quando commitado; **integrado** apenas após merge; **operacionalmente comprovado** apenas quando houver checkpoints reais seguindo o procedimento.

## 8. Rastreabilidade

Origem: decisão do PO de recuperar o controle acumulativo da CI por interação, seguida de pedido de mandato Markdown detalhado. Complementa `GOV-OPS-CORRECAO-SQUASH-CI-MASTER-20261111`, `GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119`, `GOV-DOC-01` e [CI — Reuso e agente de reparo](CI_Reuso_Release_e_Agente_Reparo.md). Não substitui o documento canônico de decisão.

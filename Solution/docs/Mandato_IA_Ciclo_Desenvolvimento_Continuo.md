# Mandato operacional específico da IA — Jornada do Cidadão

**ID:** GOV-OPS-MANDATO-JORNADA-RETOMADA-20261120  
**Projeto/repositório:** Jornada do Cidadão — `lucianox777/Jornada`  
**Autoridade:** Product Owner, restauração e continuidade entre conversas aprovadas em 20/11/2026.  
**Estado:** mandato aprovado e integrado pela PR #1773 (squash `5d8fcb3dffb8ad04203414224278933bce70db1b`); comprovação de gates pós-merge permanece separada da integração.  
**Fonte normativa principal:** [GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), aditivo específico de 13 etapas, e [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## 1. Escopo e precedência

Este mandato é **exclusivo do Jornada do Cidadão**. Substitui, no âmbito deste projeto, a redação genérica anteriormente proposta neste mesmo arquivo e **não** autoriza aplicar diretrizes a outros repositórios. O aditivo canônico de 13 etapas prevalece em caso de divergência; decisões posteriores aprovadas e integradas prevalecem no respectivo escopo. Não revoga regras científicas, de segurança, proteção de branch, autorização de merge ou GOV-DOC-01.

Não existe autorização implícita para TRAIN, TEST, 100k, alteração de LOCK, publicação, ativação, alteração de dados protegidos ou bypass de gates. A expressão “continue” significa retomar somente trabalho já autorizado. Não afirmar execução autônoma entre mensagens sem automação real configurada e comprovada.

### Hierarquia de leitura e resolução de conflitos

1. Respeitar instruções de maior autoridade, legislação, controles de segurança e autorizações específicas do PO; nenhuma instrução documental pode dispensá-los.
2. Consultar as **decisões do PO aprovadas e integradas**, seus escopos e precedências no índice oficial e documentos temáticos. Uma proposta ou decisão registrada apenas em PR não se torna automaticamente norma integrada.
3. Para o **procedimento técnico de 13 etapas**, o texto integral do aditivo `GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119` no documento canônico prevalece sobre qualquer resumo deste mandato.
4. Este mandato operacional detalha a **retomada entre conversas** sem modificar o protocolo técnico; `AGENTS.md` e as Instruções do Projeto ChatGPT são **apontadores de ativação**, não fontes alternativas de requisitos.
5. Se uma decisão posterior aprovada modificar expressamente uma regra, registrar sua precedência e alcance conforme GOV-DOC-01; não resolver contradições silenciosamente nem atribuir vigência à branch não integrada.

## 2. Inicialização e retomada obrigatórias — inclusive em conversa nova

Toda interação operacional, inclusive após limite de contexto, troca de agente ou interrupção, começa pela **recuperação do estado vivo no GitHub**, sem depender do histórico da conversa:

1. Consultar `AGENTS.md`, este mandato, o documento canônico, GOV-DOC-01, `Solution/docs/Indice_Decisoes_Vigentes.md`, o Plano de Desenvolvimento e os documentos de decisão pertinentes.
2. Consultar o HEAD/SHA da `master`, as PRs e issues relevantes, seus branches, diffs, reviews, conflitos, checks e as evidências de integração. Distinguir conteúdo integrado de proposta em PR.
3. Identificar a **PR da frente de trabalho efetivamente em andamento**, por evidências e decisões documentadas; reutilizá-la preferencialmente para correções e continuidade. **Não presumir que #1757 seja PR permanente de desenvolvimento**: ela é a frente documental deste mandato. Nenhuma PR numericamente fixa foi comprovada como destino universal das alterações.
4. Recuperar do documento/PR/checkpoint **já existente** o objetivo, a última etapa comprovadamente concluída, pendências, bloqueios, referências de SHA/run/job/artefato e próxima ação. Reconciliar sempre com `master` e decisões posteriores. Se a PR tiver sido integrada/fechada, localizar a sucessora por evidência, sem inventá-la.
5. Quando o estado não puder ser recuperado integralmente, declarar a lacuna e reconstruir somente a partir de registros verificáveis. Não repetir decisões, reabrir tarefas concluídas ou criar PRs, arquivos de estado, bancos ou monitores novos apenas porque a conversa anterior terminou.

### Reconciliação bidirecional obrigatória na inicialização — decisão PO de 20/11/2026

**ID:** GOV-OPS-RECONCILIACAO-INICIAL-CI-PRS-20261120. **Estado:** DECIDIDO pelo PO; implementação documental integrada pela PR #1774 (squash `cc5f758bec3a74232e15b775cff6b16dde4ab559`); validação pós-merge ainda não comprovada. **Contexto:** a troca de conversas pode perder o vínculo entre incidentes de CI, PRs abertas ou integradas, decisões e próximos passos, mesmo quando os registros individuais existem. **Justificativa:** tornar executável e verificável o contrato já aprovado nas 13 etapas, sem duplicar mecanismos.

Na abertura de **cada interação operacional**, antes de iniciar nova implementação, executar a reconciliação **nos dois sentidos**, dentro de uma janela de observação declarada e com cobertura verificável:

1. Ler HEAD exato da master, índices e decisões integradas, PRs e issues relevantes, checkpoint anterior e a janela retrospectiva necessária para incluir runs ainda ativos; inventariar runs globais com paginação **quando a interface permitir**, deduplicando por `run_id` e preservando `run_attempt`, workflow, evento, branch, SHA, timestamps, conclusão e jobs.
2. Para cada run crítico, falho, bloqueado ou com skips, correlacionar o `jornada-ci-diagnostics` ao run original e SHA quando existir; consultar os jobs/gates originais. O Diagnostics é evidência **somente leitura**, limitada à `jornada-ci` concluída, com artefato de retenção de sete dias; não é inventário de PRs nem prova autônoma de aprovação.
3. **CI → PR:** procurar PR(s) relacionadas por evidência verificável de SHA, evento, commits e merge; distinguir SHA de HEAD de PR do SHA de squash da master. Associação ambígua permanece indeterminada; não inferir uma única PR apenas pelo SHA.
4. **PR → CI:** percorrer PRs abertas e as encerradas/integradas relevantes à janela e às pendências; conferir Draft/Ready, revisão, conflito, checks no HEAD exato, substituições, decisões e, para merges, gates pós-merge no SHA integrado. PR sem run ou sem Diagnostics continua no inventário como pendência, não como PASS.
5. Confrontar ambos os inventários com o checkpoint anterior, decisões vigentes, issues e documentação; identificar lacunas, transições, incidentes ainda abertos, validações pendentes e a **próxima atividade autorizada**. Não reabrir tarefa concluída nem transformar falha em retry sem classificação.
6. Reportar janela, horário, cobertura total ou **PARCIAL**, contagens verificáveis, relações confirmadas/ambíguas, PRs sem prova, jobs falhos, SHA/run/attempt, pendências e próximo passo. Se a integração não listar runs globais paginados, declarar explicitamente a limitação e **não** certificar reconciliação total.

**Impacto e limites:** apenas esclarecimento do procedimento inicial existente; não cria workflow, monitor, banco, novo checkpoint, execução científica, autorização de merge ou alteração de gates. A PR #1758 permanece proposta independente de inventário read-only, sem aprovação implícita. **Precedência:** detalha a inicialização deste mandato e as etapas 2–7, 10–13 do protocolo canônico de 13 etapas, sem substituí-las; GOV-DOC-01 permanece obrigatório. **Rastreabilidade:** PR #1773, #1757, #1758; `.github/workflows/ci-diagnostics.yml`; `Solution/scripts/ci-failure-checkpoint.py`; índice de decisões vigente; CI e PR #1774, squash `cc5f758bec3a74232e15b775cff6b16dde4ab559`; CI rápida pré-merge run `44825286460` (quatro jobs aprovados); pós-merge ainda não comprovado.

A PR é um ponto de continuidade **não exclusivo**; `master`, decisões e evidências reais continuam fontes de verdade. Checkpoints operacionais existentes podem ser atualizados no próprio fluxo documental autorizado, sem sistema paralelo.

## 3. Ciclo obrigatório de 13 etapas do Jornada

**Regra de fidelidade normativa:** executar as **13 etapas integralmente, com todas as condições, classificações, exceções, campos de evidência e limites do texto original** na seção `GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119` → `Contrato de inicialização — obrigatório em cada mensagem operacional` do [documento canônico](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md). A lista numerada abaixo é **somente um mapa de navegação, não uma especificação alternativa**. É proibido inferir que a ausência de um detalhe neste mapa dispensa sua execução. Em caso de diferença, aplicar a redação integral do protocolo canônico, inclusive o contrato de checkpoint persistente e os limites posteriores às 13 etapas.

**Exemplos de requisitos que não podem ser omitidos:** janela de observação e paginação; identidade `run_id` com `run_attempt` separado; classificação exaustiva de `timed_out`, `action_required`, `cancelled`, `skipped`, `neutral`, `stale` e desconhecidos; inspeção de jobs/steps/logs e skips; transições e baseline verificável; classificação da causa antes de retry; HEAD exato, reviews e autorização de squash; pós-merge no SHA integrado com `jornada-ci`, seed42/30k, Windows e validação independente **quando exigíveis**; reporte de cobertura e estado de integração. O protocolo original é a fonte dos critérios exatos.

**Mapa não normativo das etapas:**

1. Interpretar pedido, autorização e limites.
2. Ler repositório vivo, `master`, PRs, issues e decisões.
3. Inventariar runs relevantes da CI na janela declarada, com paginação quando disponível.
4. Deduplicar por `run_id`, preservando `run_attempt`, SHA, jobs e proveniência.
5. Contar estados exclusivos: aprovadas, em execução, aguardando, reprovadas/bloqueadas e outras/indeterminadas; `skipped` não é aprovação.
6. Inspecionar jobs, steps, logs e artefatos das falhas, bloqueios e skips críticos.
7. Comparar transições com checkpoint compatível; sem anterior, declarar linha de base.
8. Classificar causa, reproduzir, corrigir minimamente e testar; retry apenas conforme política infra.
9. Continuar desenvolvimento independente autorizado enquanto a CI executa, evitando polling redundante e PRs duplicadas.
10. Validar HEAD exato da PR, checks, reviews, conflitos, documentação e autorização antes de squash merge.
11. Verificar na `master` o SHA integrado e gates pós-merge exigíveis, sem confundir checks da PR, skips ou ausência de execução com sucesso.
12. Registrar decisões aprovadas e sincronizar índices oficiais conforme GOV-DOC-01; diferenciar DECIDIDO, IMPLEMENTADO, MEDIDO, VALIDADO e ATIVADO.
13. Reportar janela, cobertura, contagens verificadas ou parciais, falhas por job, mudanças, SHA, PR, runs, bloqueios e próxima ação.

## 4. CI existente — reutilização, não duplicação

Usar os workflows reais e vigentes do Jornada, especialmente `jornada-ci`, `jornada-pr-required-fast`, `jornada-ci-diagnostics`, `jornada-ci-watchdog` e os gates seletivos aplicáveis. Conferir arquivos e gatilhos no SHA consultado. O watchdog existente em `.github/workflows/ci-watchdog.yml` é agendado no minuto 17 de cada hora e permite recuperação **somente** de falhas de infraestrutura, sob guardas; não é agente autônomo nem prova de CI verde.

**Não criar novo monitor, workflow de saúde, armazenamento de checkpoint ou mecanismo de retomada por padrão.** Uma lacuna técnica comprovada exige análise de impacto e nova autorização específica do PO. A proposta #1758 de inventário read-only é avaliação separada, não pré-requisito para retomar conversas. A proposta de monitor #1759 não deve ser integrada conforme decisão documentada na PR #1772. A proposta genérica #1771 não substitui este mandato.

Quando a interface disponível não permitir inventário global paginado ou inspeção de jobs, reportar **COBERTURA PARCIAL / NÃO VERIFICADO**, nunca números inventados ou certificação sem evidência. Distinguir `jornada-ci` e gates de teste dos workflows de observação.

## 5. Continuidade, governança e fechamento

Concentrar alterações na PR existente da frente correspondente sempre que tecnicamente adequado; evitar branches paralelas e não fazer push direto na `master`. Não modificar workflows ou decisões científicas para facilitar a recuperação do contexto. Preservar documentação histórica e registrar precedência, justificativa, impacto, autorização, PR, SHA e validação real. Esta versão documental só passa a valer na `master` após integração e checks.

**Instruções do Projeto ChatGPT:** devem conter um apontador curto para o repositório e este mandato, exigindo consulta ao GitHub em cada conversa. Não copiar integralmente o mandato para as Instruções do Projeto nem presumir que um documento versionado altera automaticamente as configurações do ChatGPT.

## 6. Retomada específica por CI Diagnostics e fechamento de integração (ratificação PO, 20/11/2026)

**Decisão GOV-OPS-DIAGNOSTICS-RETOMADA-POSCI-20261120 — DECIDIDA; integrada pela PR #1773 (squash `5d8fcb3dffb8ad04203414224278933bce70db1b`), sem inferir validação pós-merge.** Restaurar o ciclo Jornada existente, sem criar monitor, automação, novo checkpoint, workflow, política de exclusão de branch ou fechamento artificial de PR.

- Após **squash merge autorizado**, registrar a PR como **merged** (estado fechado pelo próprio GitHub) e o SHA integrado da `master`. **Não existe segundo fechamento de PR após o Diagnostics**. Não confundir a branch Git remanescente, a PR já integrada e o incidente operacional ainda pendente. Nenhuma exclusão automática adicional de branch foi aprovada.
- No pós-merge, consultar **primeiro o `jornada-ci-diagnostics` correspondente à execução `jornada-ci` e ao SHA integrado**, quando houver resultado disponível. Relacionar obrigatoriamente `workflow`, `run_id`, `run_attempt`, `head_sha`, resultado e jobs. O `incident_id` é estável para o mesmo run/SHA; o artefato do Diagnostics é **somente leitura**, não autoriza merge, não contém logs completos e expira após sete dias.
- O Diagnostics é a **porta de entrada do diagnóstico da CI principal**, não a única fonte de verdade nem prova suficiente de sucesso. Consultar os jobs/logs originais, os gates pós-merge exigíveis, checks independentes e o inventário acumulativo do protocolo canônico. Ausência, atraso, expiração, falha do próprio Diagnostics ou execução mais recente de outro SHA **não** significam PASS. Na falta do artefato, recuperar diretamente os registros verificáveis do GitHub e declarar lacunas.
- Em falha comprovada, priorizar causa, reprodução e correção mínima; reutilizar a PR **se ainda aberta**, ou preparar PR de correção sobre a `master` atual **se a anterior já foi integrada**, sempre com autorização e checks no HEAD exato. Repetir squash e pós-CI; não declarar incidente resolvido sem evidência dos gates aplicáveis. Enquanto aguarda CI, prosseguir em frentes independentes autorizadas.
- A concorrência da `jornada-ci` é por `github.workflow` e `github.ref`, com `cancel-in-progress: false`; **não garante fila FIFO nem preservação de todas as execuções pendentes**. O Diagnostics dispara por `workflow_run: completed`, mas não bloqueia outros workflows nem tem exclusão global. Artefatos são vinculados a runs, não formam histórico acumulativo permanente. Não consultar somente o último Diagnostics: correlacionar todos os runs relevantes por identidade, janela e transições, com cobertura declarada.
- Retomada entre conversas significa **consulta efetiva ao GitHub em cada interação operacional**; instruções versionadas não criam execução em segundo plano nem modificam automaticamente as configurações do Projeto ChatGPT. O mandato específico e o protocolo canônico prevalecem sobre propostas genéricas, respeitando GOV-DOC-01 e decisões posteriores integradas.

**Justificativa:** preservar o mecanismo de correção orientada por evidências, impedir perda de incidentes em desenvolvimento paralelo e evitar controles duplicados. **Impacto:** somente explicitação do procedimento; sem alteração de CI, proteção de branch, política científica ou automação. **Precedência:** detalha o protocolo de 11/11 e o aditivo canônico de 19/11 sem substituir suas 13 etapas. **Rastreabilidade:** `.github/workflows/ci.yml`, `.github/workflows/ci-diagnostics.yml`, `Solution/scripts/ci-failure-checkpoint.py`; PR documental desta decisão; SHA e gates de integração pendentes.

## 7. Histórico e rastreabilidade

- **11/11/2026:** GOV-OPS original, CI seletiva, watchdog horário existente e instruções específicas do Projeto.
- **19/11/2026:** aditivo PO de 13 etapas, documentado na PR #1757.
- **20/11/2026:** redação genérica deste arquivo e proposta genérica #1771 identificadas como desvio de escopo. O PO aprovou recuperar o mandato **exclusivo do Jornada**, com retomada obrigatória entre conversas e sem novo monitor por padrão.
- **Estado de integração:** documentação integrada pela PR #1757 (squash `556370b44363b5c3a10029d0cb38a77b39485682`); conferir precedência de decisões posteriores na `master`. A decisão GOV-CI-PRESERVAR-MASTER-ENCERRAR-PR1759-20261120 permanece independente e deve ser reconciliada pela PR #1772.

- **20/11/2026:** PO ratifica recuperação do fluxo Jornada por Diagnostics correlacionado a run/SHA, correção e novo squash, sem fechamento adicional de PR, exclusão de branch ou novo workflow; documentação proposta em PR específica, integração pendente.

## 8. Protocolo de continuidade autônoma em sessão e recuperação por checkpoint (PO, 22/11/2026)

**ID:** GOV-OPS-CONTINUIDADE-AUTONOMA-WORK-20261122. **Autoridade:** Product Owner (PO), determinação expressa no Projeto Jornada, em 22/11/2026 (BRT), intitulada *Protocolo de continuidade autônoma — ChatGPT Work*. **Estado:** **DECIDIDO** quanto ao procedimento; aplicação em sessões e recuperações depende de ferramentas/permissões efetivamente disponíveis. A edição deste documento registra o procedimento, **não** comprova execução permanente fora da sessão nem conclusão do E2E V9. **Norma de registro:** GOV-DOC-01.

### Contexto e motivação

Retomadas após encerramento de sessão, falha operacional ou esgotamento de créditos podem perder o objetivo, o último SHA aprovado, jobs ainda em andamento e pendências. Os checkpoints e a reconciliação CI ↔ PR já fazem parte do protocolo de 13 etapas e do mandato Jornada, mas o PO reafirmou a **obrigação de executar até o próximo impedimento real e persistir um estado recuperável antes da interrupção quando o limite for detectável**. Evita reinício do zero, duplicação de PR/commit/runs e abandono voluntário de etapas já autorizadas. Não constitui aprovação de execução científica ou entrega E2E antecipada.

### Procedimento obrigatório (ratificação e especialização de retomada)

1. **Trabalho contínuo durante execução ativa:** enquanto houver recursos, permissões e tarefa autorizada, continuar sem parar voluntariamente em marcos intermediários; executar as etapas de engenharia, CI, revisão, correção, documentação e integração permitidas. Suspender apenas quando houver impedimento técnico, autorização humana exclusiva, custo/limite indisponível, segurança ou gate não satisfeito.
2. **Checkpoint persistente antes de perder o contexto:** atualizar o mecanismo de continuidade **já existente** (na frente E2E atual, issue [#1851](https://github.com/lucianox777/Jornada/issues/1851)), documentando objetivo, entregas concluídas e evidência, etapa ativa, pendências, PRs/branches/SHAs, runs e tentativas, jobs/últimas conclusões, janela de consulta, cobertura total/parcial, bloqueios e **primeira ação executável**. Não criar um sistema novo ou PR/commit duplicado apenas para armazenar estado. O checkpoint operacional **não substitui documentação canônica de decisões**.
3. **Antecipação condicional:** caso a ferramenta informe proximidade de limite de créditos ou encerramento, priorizar salvar o checkpoint. Se a informação não existir, **não alegar previsão de limites ou promessa de persistência após a interrupção**; deixar checkpoints incrementais ao finalizar mudanças verificáveis.
4. **Recuperação idempotente:** em cada retomada, ler primeiro o checkpoint mais recente da missão existente, e então reler `master` e índice, PRs, issues, checks e runs GitHub atuais; reconciliar por SHA/run_id/run_attempt e validar precedência de decisões integradas. Avançar da primeira pendência real; não repetir passos concluídos nem recriar artefatos, PRs, commits ou workflows. Atualizar o checkpoint somente diante de alteração factual relevante.
5. **Fechamento e limite:** continuar até concluir com **checks exigíveis e evidência pós-merge no SHA integrado**, ou registrar bloqueio que exija intervenção humana. Nunca declarar etapa validada por CI do SHA anterior, `skipped`, PR Draft, teste componente, ausência de resultado ou mero checkpoint.

### Escopo, precedência, impactos e salvaguardas

- **Complementa, sem substituir**, o contrato canônico de 13 etapas [GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), a reconciliação inicial deste mandato, o Diagnostics pós-merge e GOV-DOC-01. O protocolo completo prevalece sobre este resumo quando exigir campos ou gates adicionais.
- **Não revoga** [GOV-OPS-SUSPENSAO-AUTOMACOES-JORNADA-20261122](Decisao_GOV_OPS_Suspensao_Automacoes_Jornada_20261122.md): o trabalho ocorre **somente durante uma execução/sessão efetivamente ativa**; ao encerrar a sessão, não existe processamento autônomo garantido, agendamento, alerta ou retomada programática criada por este documento. Ao reabrir a missão, retomar sob demanda ou por modo Work disponível e autorizado.
- **Não autoriza** criar novo monitor, novo watchdog, disparar workflow E2E owner-only, TRAIN/TEST/100k, alterar LOCKED, publicar ou ativar V9 em DEV/HML/PROD, editar banco original, burlar revisão/ruleset ou integrar PR Draft. Atos exclusivos do PO e todas as proteções permanecem.
- **Impacto operacional:** exige que a continuidade seja recuperável e verificável, sem suplantar decisões científicas nem o inventário CI paginado. **Rastreabilidade inicial:** [issue #1851](https://github.com/lucianox777/Jornada/issues/1851), [checkpoint pós-#1921](https://github.com/lucianox777/Jornada/issues/1851#issuecomment-7090205138), [PR #1921](https://github.com/lucianox777/Jornada/pull/1921) (squash `810d5d805c61f4549bc055dbb7ebb2eb45997449`) e [PR Draft #1920](https://github.com/lucianox777/Jornada/pull/1920). Esta é uma decisão de **governança de continuidade**, não um novo gate científico.

# Mandato operacional específico da IA — Jornada do Cidadão

**ID:** GOV-OPS-MANDATO-JORNADA-RETOMADA-20261120  
**Projeto/repositório:** Jornada do Cidadão — `lucianox777/Jornada`  
**Autoridade:** Product Owner, restauração e continuidade entre conversas aprovadas em 20/11/2026.  
**Estado:** DECIDIDO pelo PO; implementação documental nesta PR #1757, **não vigente na master até integração**.  
**Fonte normativa principal:** [GOV-OPS-INTERACAO-IA-CI-ACUMULATIVA-20261119](Decisao_GOV_OPS_Correcao_Erros_Squash_CI_Master_20261111.md), aditivo específico de 13 etapas, e [GOV-DOC-01](Politica_Registro_Decisoes_Discussoes.md).

## 1. Escopo e precedência

Este mandato é **exclusivo do Jornada do Cidadão**. Substitui, no âmbito deste projeto, a redação genérica anteriormente proposta neste mesmo arquivo e **não** autoriza aplicar diretrizes a outros repositórios. O aditivo canônico de 13 etapas prevalece em caso de divergência; decisões posteriores aprovadas e integradas prevalecem no respectivo escopo. Não revoga regras científicas, de segurança, proteção de branch, autorização de merge ou GOV-DOC-01.

Não existe autorização implícita para TRAIN, TEST, 100k, alteração de LOCK, publicação, ativação, alteração de dados protegidos ou bypass de gates. A expressão “continue” significa retomar somente trabalho já autorizado. Não afirmar execução autônoma entre mensagens sem automação real configurada e comprovada.

## 2. Inicialização e retomada obrigatórias — inclusive em conversa nova

Toda interação operacional, inclusive após limite de contexto, troca de agente ou interrupção, começa pela **recuperação do estado vivo no GitHub**, sem depender do histórico da conversa:

1. Consultar `AGENTS.md`, este mandato, o documento canônico, GOV-DOC-01, `Solution/docs/Indice_Decisoes_Vigentes.md`, o Plano de Desenvolvimento e os documentos de decisão pertinentes.
2. Consultar o HEAD/SHA da `master`, as PRs e issues relevantes, seus branches, diffs, reviews, conflitos, checks e as evidências de integração. Distinguir conteúdo integrado de proposta em PR.
3. Identificar a **PR da frente de trabalho efetivamente em andamento**, por evidências e decisões documentadas; reutilizá-la preferencialmente para correções e continuidade. **Não presumir que #1757 seja PR permanente de desenvolvimento**: ela é a frente documental deste mandato. Nenhuma PR numericamente fixa foi comprovada como destino universal das alterações.
4. Recuperar do documento/PR/checkpoint **já existente** o objetivo, a última etapa comprovadamente concluída, pendências, bloqueios, referências de SHA/run/job/artefato e próxima ação. Reconciliar sempre com `master` e decisões posteriores. Se a PR tiver sido integrada/fechada, localizar a sucessora por evidência, sem inventá-la.
5. Quando o estado não puder ser recuperado integralmente, declarar a lacuna e reconstruir somente a partir de registros verificáveis. Não repetir decisões, reabrir tarefas concluídas ou criar PRs, arquivos de estado, bancos ou monitores novos apenas porque a conversa anterior terminou.

A PR é um ponto de continuidade **não exclusivo**; `master`, decisões e evidências reais continuam fontes de verdade. Checkpoints operacionais existentes podem ser atualizados no próprio fluxo documental autorizado, sem sistema paralelo.

## 3. Ciclo obrigatório de 13 etapas do Jornada

Aplicar integralmente as 13 etapas do aditivo canônico:

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

## 6. Histórico e rastreabilidade

- **11/11/2026:** GOV-OPS original, CI seletiva, watchdog horário existente e instruções específicas do Projeto.
- **19/11/2026:** aditivo PO de 13 etapas, documentado na PR #1757.
- **20/11/2026:** redação genérica deste arquivo e proposta genérica #1771 identificadas como desvio de escopo. O PO aprovou recuperar o mandato **exclusivo do Jornada**, com retomada obrigatória entre conversas e sem novo monitor por padrão.
- **Estado de integração:** correção proposta na PR #1757; verificar checks, conflitos e precedência da `master` antes de qualquer merge. A decisão GOV-CI-PRESERVAR-MASTER-ENCERRAR-PR1759-20261120 permanece independente e deve ser reconciliada pela PR #1772.

# MANDATO-IA-CICLO-CONTINUO-20261119 — diretriz operacional

- Origem: mandato apresentado pelo responsável do projeto em 20/11/2026.
- Autoridade: responsável autorizado, sujeita às regras locais e GOV-DOC-01.
- Estado: **diretriz solicitada; adoção documental proposta nesta PR; automação e certificação não comprovadas**.
- Precedência: complementa a governança vigente; não substitui decisões científicas, autorizações específicas, branch protection nem gates.
- Repositório: lucianox777/Jornada; branch principal: master.

## Objetivo e justificativa
Evitar conclusões sem evidência sobre saúde da integração, perda de contexto entre conversas e duplicação de ações. Conciliar observabilidade cumulativa da CI, trabalho técnico previamente autorizado e documentação versionada. Conversa não é fonte de verdade operacional.

## Contrato operacional
1. Em cada retomada, descobrir registro persistente de continuidade, validar integridade e timestamp, revalidar SHA master, PRs, jobs, runs, tentativas e autorizações na fonte primária. Se ausente, reconstruir com cobertura declarada; nunca presumir autorização.
2. Inventariar CI com paginação, janela UTC, IDs únicos de runs e tentativas separadas; classificar sucesso, andamento, espera, falha/bloqueio e indeterminação. Skipped, cancelled e neutral não são sucesso.
3. Investigar jobs, steps e artefatos; separar observadores/relatórios de suítes. Uma resposta vazia de consulta parcial não demonstra ausência.
4. Corrigir causas com testes de regressão, respeitando prioridades, segurança e custo. 'Continue' não autoriza merge, publicação, destruição ou ensaios científicos especiais.
5. Antes de merge: verificar SHA exato, revisões, gates e autorização. Depois: obter SHA integrado e avaliar pipelines exigíveis no evento/branch corretos, incluindo jobs e condições de skip.
6. Classificar saúde por SHA e cobertura: VERDE COMPROVADO, VERMELHO, PENDENTE, NÃO VERIFICADO ou COBERTURA PARCIAL. Falhar fechado diante de coleta incompleta.
7. Registrar checkpoints duráveis com projeto, esquema, timestamp, SHA, tarefa, último passo concluído, próxima ação, ponteiros, cobertura, gates, bloqueios, autorizações oficiais, cursor/janela e integridade. Atualizar após marcos e antes de encerrar, com controle de conflitos e sem segredos.
8. Para ponteiros ausentes: procurar ID/tentativa, depois SHA/evento/branch/workflow e paginação, jobs/logs/artefatos, checkpoints, e outras interfaces autorizadas. Declarar filtros, permissões e limites.
9. Decisões aprovadas exigem documento oficial e índice GOV-DOC-01; checkpoints não se tornam decisões automaticamente.

## Monitor cumulativo — requisito, não evidência de implantação
Antes de implementar, inventariar workflows e watchdogs existentes, especialmente PRs #1759 e #1770, para evitar duplicidade. Se lacuna confirmada e autorizada, especificar monitor de leitura com nome/caminho/agendamento configuráveis, credencial mínima, checkpoint persistente, reconciliação de tentativas, detecção de transições, verificação pós-merge por SHA e relatórios estruturados. Não disparar testes caros, reruns, alterações ou releases por padrão. Exigir testes de paginação, duplicidade, estados desconhecidos, permissões, skips, SHA divergente e artefatos expirados; comprovar primeira execução real e agendamento antes de afirmar supervisão contínua.

## Evidências e limitações no momento do registro
- Master observado em consulta de commits: `3a329e494d8d4feabd951181c29020bc12d61cd7` (20/11/2026); revalidar antes de qualquer ação.
- PR #1759: draft aberta, não mesclável na consulta; propõe prova pós-merge fail-closed, sem comprovação de execução.
- PR #1770: draft aberta, checkpoint de seis runs/doze jobs com cobertura explicitamente parcial.
- Nenhum inventário paginado completo, execução de CI, instalação de monitor ou certificação da master foi realizado nesta rodada.
- Estado da saúde master: **NÃO VERIFICADO**; estado da documentação: **proposta em branch/PR até integração**.

## Impactos e critérios de aceite
Aumenta rastreabilidade, reduz falsos positivos e exige persistência verificável. A implementação só estará concluída após revisão, integração, verificação dos gates e prova operacional aplicável. Não autoriza TRAIN/TEST, alteração de LOCKED, publicação ou merge automático.

## Rastreabilidade
Mandato do responsável `MANDATO-IA-CICLO-CONTINUO-20261119`; política GOV-DOC-01; issues #1734 e #1365; PRs #1759 e #1770. Revisão do modelo: 20/11/2026.

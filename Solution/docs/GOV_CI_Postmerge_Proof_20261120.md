# GOV-CI — comprovação pós-merge no SHA exato da master

**ID:** GOV-CI-POSTMERGE-PROOF-20261120. **Estado:** PROPOSTA técnica, não aprovada para integração. **Motivo:** o PO solicitou comprovação de que a CI funciona depois do merge, não apenas checks de PR. **Precedência:** GOV-DOC-01, protocolo de interação IA e mandato de ciclo contínuo da PR #1757.

## Evidência observada e lacuna

Uma consulta inicial por commit no conector GitHub não retornou execuções para `ceef81e` (PR #1754, SHA abreviado) nem para `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb` (PR #1755); **essa consulta inicial foi posteriormente superada por evidência autenticada para o segundo SHA**. O run `jornada-ci` **#44654406155**, no SHA integral `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb`, foi confirmado como `completed/success`: **21 jobs, 16 success, 5 skipped e 0 failing** (issue #1734, comentário #7027978770; checkpoint #14594211056). Os cinco jobs skipped não equivalem a testes executados. Não foi comprovado nesta análise um run `jornada-pr-required-fast` com evento `push` nesse mesmo SHA; tampouco a situação do commit `ceef81e`. Os workflows versionados `.github/workflows/ci.yml` (`jornada-ci`) e `.github/workflows/jornada-pr-required-fast.yml` (`jornada-pr-required-fast`) declaram gatilho `push` na `master`. A existência do gatilho também não comprova execução bem-sucedida.

## Proposta implementada

`Solution/scripts/ci-master-postmerge-proof.py` consulta a API Actions com filtro SHA exato, `master` e evento `push`, e exige runs `completed/success` de **ambos** os workflows. Sem run, sem permissão, paginação incompleta, falha ou execução pendente, retorna não verificado. Testes offline integrados ao job `classify-impact` cobrem sucesso, ausência, pendência, falha, PR/manual e SHA divergente. A prova é read-only, não executa nem reinicia CI.

### Correção de precedência dos runs (20/11/2026)

Na revisão da proposta, foi identificado que a primeira implementação aceitava qualquer run histórico `success` do mesmo workflow/SHA, mesmo diante de um run posterior falho ou pendente. O verificador agora considera **o run de maior ID** entre os candidatos autenticados com mesmo SHA, branch `master` e evento `push`; uma execução mais recente falha ou pendente não pode ser mascarada por sucesso anterior. Um run posterior aprovado pode substituir falha anterior. Testes offline específicos cobrem ambos os sentidos; **CI do HEAD corrigido e execução autenticada ainda precisam ser confirmadas**. A alteração não modifica as exigências de proteção da master nem executa qualquer ação científica.

## Uso operacional

```sh
GH_TOKEN=<credencial-de-leitura> python3 Solution/scripts/ci-master-postmerge-proof.py --sha <SHA-INTEGRAL-DA-MASTER>
```

**Não colocar token em arquivos versionados.** O script valida a presença dos dois workflows, não substitui a análise de jobs/skips, classificadores de impacto, gates físicos e certificação científica. Se houver >100 runs para o SHA, falha fechado. **Estado de evidência por SHA:** para `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb` há prova positiva do `jornada-ci` completo (run #44654406155), **mas não prova conjunta dos dois workflows exigidos por este script**. Para `ceef81e`, permanece sem comprovação específica nesta análise. Não confundir evidência parcial de um workflow com sucesso do verificador combinado.

## Rastreabilidade e aceite

Autoridade: pedido de verificação pós-merge do PO nesta conversa. Impacto: aumenta observabilidade, sem mudar políticas de merge nem iniciar TRAIN/TEST. Aceite exige prova no SHA integral da master, inspeção de jobs, relatório com IDs/URLs e documentação dos resultados. PR e testes offline não equivalem a prova pós-merge.

## Monitor periódico proposto na mesma PR

O workflow `.github/workflows/jornada-ci-health-monitor.yml` foi adicionado à PR #1759 com `schedule` horário (`23 * * * *`) e `workflow_dispatch`. Executa o verificador existente contra o SHA atual da `master`, com token `actions:read`/`contents:read`, testes unitários, evidência JSON e falha fechada quando os workflows obrigatórios não forem comprovados. **Estado: implementação em PR, não integrada, agendamento e execução real ainda não comprovados.** O monitor verifica apenas os dois workflows exigidos pelo verificador; **não substitui inventário global cumulativo**, watchdog de recuperação, testes científicos, DR, escala ou release. A limitação de paginação da consulta (100 runs) continua fail-closed e deve ser ampliada em etapa posterior se necessária. A ausência de um dos workflows deve ser investigada antes de tratar como falha de produto.

## Validação de CI pré-merge da proposta (20/11/2026)

No HEAD `348401d83b1a229707296677b796fc2851e6ef5b` da PR #1759, o workflow `jornada-pr-required-fast` run **#44662747130** concluiu com `completed/success`: os quatro jobs `classify-impact`, `unit-runtime`, `deterministic-build` e `security-guards` concluíram `success`. O log de `classify-impact` confirma execução dos testes do verificador pós-merge no conjunto de 23 testes. Esta é **prova de CI pré-merge da PR**, não execução do monitor horário, não prova dos dois workflows no SHA pós-merge e não prova de branch protection/rulesets. A PR continua em rascunho, sem integração.

## Evidência de falha fechada legível por máquina (20/11/2026)

Revisão da integração entre o script e o workflow horário identificou que erros da API antes da avaliação produziam somente stderr; com `tee postmerge-health.json`, o arquivo de evidência podia ficar vazio mesmo quando o upload `if: always()` fosse acionado. A correção na PR #1759 emite JSON mínimo na saída padrão com `sha`, `verified=false`, `state=unverified` e `error_type`, sem mensagens brutas de exceção ou credenciais, preservando exit code **2** e falha fechada. O workflow continua com `set -o pipefail`, portanto o arquivo não converte falha em sucesso. Teste offline adicional simula exceção e verifica JSON, código de saída e ausência de vazamento de detalhes. Commits `3d4c1585aaa00e0a51c0b89745378b85aff9983b` e `8d7c39a37a644c89fc9836239039b2f4790f5cff`. **Implementação na PR, testes ainda sem comprovação de execução no novo HEAD; não é prova pós-merge.**

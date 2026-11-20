# GOV-CI — comprovação pós-merge no SHA exato da master

**ID:** GOV-CI-POSTMERGE-PROOF-20261120. **Estado:** PROPOSTA técnica, não aprovada para integração. **Motivo:** o PO solicitou comprovação de que a CI funciona depois do merge, não apenas checks de PR. **Precedência:** GOV-DOC-01, protocolo de interação IA e mandato de ciclo contínuo da PR #1757.

## Evidência observada e lacuna

Em consulta por commit no conector GitHub, não foram retornadas execuções para os commits squash `ceef81e` (PR #1754, SHA abreviado) e `1734ee976dfb8940689bd4ba9480b4e5f6c7d9cb` (PR #1755). **Isso não prova que Actions não executou**: o conector tem cobertura parcial. Os workflows versionados `.github/workflows/ci.yml` (`jornada-ci`) e `.github/workflows/jornada-pr-required-fast.yml` (`jornada-pr-required-fast`) declaram gatilho `push` na `master`. A existência do gatilho também não comprova execução bem-sucedida.

## Proposta implementada

`Solution/scripts/ci-master-postmerge-proof.py` consulta a API Actions com filtro SHA exato, `master` e evento `push`, e exige runs `completed/success` de **ambos** os workflows. Sem run, sem permissão, paginação incompleta, falha ou execução pendente, retorna não verificado. Testes offline integrados ao job `classify-impact` cobrem sucesso, ausência, pendência, falha, PR/manual e SHA divergente. A prova é read-only, não executa nem reinicia CI.

## Uso operacional

```sh
GH_TOKEN=<credencial-de-leitura> python3 Solution/scripts/ci-master-postmerge-proof.py --sha <SHA-INTEGRAL-DA-MASTER>
```

**Não colocar token em arquivos versionados.** O script valida a presença dos dois workflows, não substitui a análise de jobs/skips, classificadores de impacto, gates físicos e certificação científica. Se houver >100 runs para o SHA, falha fechado. O estado pós-merge dos merges #1754/#1755 permanece **não comprovado** nesta sessão até evidência autenticada ou execução observada.

## Rastreabilidade e aceite

Autoridade: pedido de verificação pós-merge do PO nesta conversa. Impacto: aumenta observabilidade, sem mudar políticas de merge nem iniciar TRAIN/TEST. Aceite exige prova no SHA integral da master, inspeção de jobs, relatório com IDs/URLs e documentação dos resultados. PR e testes offline não equivalem a prova pós-merge.

## Monitor periódico proposto na mesma PR

O workflow `.github/workflows/jornada-ci-health-monitor.yml` foi adicionado à PR #1759 com `schedule` horário (`23 * * * *`) e `workflow_dispatch`. Executa o verificador existente contra o SHA atual da `master`, com token `actions:read`/`contents:read`, testes unitários, evidência JSON e falha fechada quando os workflows obrigatórios não forem comprovados. **Estado: implementação em PR, não integrada, agendamento e execução real ainda não comprovados.** O monitor verifica apenas os dois workflows exigidos pelo verificador; **não substitui inventário global cumulativo**, watchdog de recuperação, testes científicos, DR, escala ou release. A limitação de paginação da consulta (100 runs) continua fail-closed e deve ser ampliada em etapa posterior se necessária. A ausência de um dos workflows deve ser investigada antes de tratar como falha de produto.

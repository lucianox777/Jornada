# GOV-V9-CAMPANHA-STANDARD-LINEAR-100K-AUTORIZACAO-CONDICIONAL-20261121

**Data:** 21/11/2026  
**Autoridade:** Product Owner (aprovação expressa nesta conversa)  
**Estado:** AUTORIZADA CONDICIONALMENTE — DESPACHO NÃO LIBERADO; CAMPANHA NÃO INICIADA POR ESTA DECISÃO  
**Repositório:** `lucianox777/Jornada`  
**SHA exato aprovado:** `ed5137daa484679bef7c8dd42bf77e61460c9189`

## Contexto e decisão expressa

Após recuperação da CI da master, encerramento da P0-A #1734, aceite da DT-10 e preparação do caminho V9 Standard Linear, o PO declarou:

> Autorizo a campanha científica V9 Standard Linear 100k para o SHA identificado, condicionando seu despacho à confirmação do preflight e à inexistência de falhas bloqueantes nas verificações pendentes.

A autorização refere-se **exclusivamente** ao workflow `.github/workflows/v9-standard-linear-100k.yml` no SHA acima, com rota padrão de referência IBGE congelada (`test_database` vazio) e população oficial (`synthetic_population` vazio). Inclui a sequência científica definida no workflow, com TEST independente obrigatório se VALIDATION produzir vencedor. Não se estende a outro SHA, workflow, campanha, promoção ou ativação.

## Justificativa e impactos

Evitar repetir saneamentos técnicos já comprovados e permitir a campanha científica somente após verificação de segurança, integridade e regressões pendentes. A decisão **não equivale** a atestação do preflight, nem a ordem de despacho imediato. A execução permanece manual e owner-only, com `expected_sha` igual ao SHA de execução, `security_preflight_approved=true` apenas após aprovação efetiva do preflight, e tentativa 1.

## Condições bloqueantes antes do despacho

1. Confirmar que `master` permanece no SHA aprovado; qualquer avanço de HEAD exige nova autorização específica para o novo SHA. A integração deste registro por PR, por si só, mudará o HEAD: **não despachar no SHA novo usando esta autorização**.
2. Concluir e reconciliar as verificações pendentes no SHA aprovado, especialmente `v9-v3-broad-train-regression` run #44918552297, que estava `in_progress` na consulta de registro. Avaliar falhas auxiliares da #1584 quanto a impacto bloqueante, sem presumir dispensa automática nem transformar dívida secundária em gate novo.
3. Proprietário confirmar preflight efetivo do workflow, incluindo SQL descartável isolado, ausência de Gold/Serving, TLS e certificado efêmero, credenciais, capacidade, integridade dos insumos, fingerprints LOCKED e disponibilidade dos recursos, conforme [runbook](Runbook_Operacao.md#operação-do-calibrador-v9).
4. Confirmar que os checks e regras de proteção obrigatórios permanecem satisfeitos e que não há falha bloqueante nas verificações relevantes.
5. Registrar evidências de cada condição e despacho na trilha operacional, antes da execução.

## Precedência e limites

Prevalecem GOV-DOC-01, manifests e seeds científicos LOCKED, decisões de três coortes independentes, segregação TRAIN/VALIDATION/TEST, TEST obrigatório com vencedor, integridade e gates fail-closed. Não autoriza alterar manifests/fingerprints, contornar proteção, executar em SHA distinto, promover modelos, ativar Gold/Serving ou declarar paridade operacional #1584 comprovada sem evidência física.

## Rastreabilidade

- [Issue P0-A #1734](https://github.com/lucianox777/Jornada/issues/1734) (encerrada).
- [Issue P0-B #1584](https://github.com/lucianox777/Jornada/issues/1584) (aberta para paridade operacional).
- [CI principal no SHA aprovado](https://github.com/lucianox777/Jornada/actions/runs/44918552365) (success).
- [Regressão ampla pendente na consulta](https://github.com/lucianox777/Jornada/actions/runs/44918552297).
- [Workflow canônico](https://github.com/lucianox777/Jornada/blob/ed5137daa484679bef7c8dd42bf77e61460c9189/.github/workflows/v9-standard-linear-100k.yml).
- [Runbook](Runbook_Operacao.md#operação-do-calibrador-v9).

**Situação operacional:** autorização condicional concedida pelo PO; preflight e inexistência de falhas bloqueantes ainda não atestados; nenhum disparo científico realizado nesta atividade.

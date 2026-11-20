# Revisão de paginação de jobs — inventário cumulativo — 2026-11-20

**Tipo:** correção técnica da proposta PR #1758; não certificação da master nem nova autorização científica.

## Defeito confirmado por revisão estática
Em `Solution/scripts/ci-cumulative-inventory.py`, `failing_jobs` comparava `(page - 1) * PAGE_SIZE + len(items)` com `total_count`, sem verificar a quantidade efetivamente observada nas páginas anteriores. A implementação também não rejeitava IDs de jobs duplicados entre páginas. Essa combinação poderia aceitar cobertura inconsistente quando a paginação muda durante a coleta.

## Correção proposta
- Contagem cumulativa `scanned` de jobs efetivamente retornados.
- Rejeição fail-closed de IDs ausentes, não inteiros ou duplicados.
- Rejeição de paginação vazia ou com excesso sobre `total_count`.
- Regressão offline com 101 jobs e duplicação entre páginas.

**Commits:** `44234745ff340e392f692eb10d088d987b174349`, `b4bb1f9a56a47c8343f2fcafa6a053de6f81b443`.

## Regressão adicional

- `721bd7e72dc4c91ed802eeb8753e09eda9bc2b44`: rejeição de job sem ID e alteração de `total_count` durante paginação. Testes adicionados, ainda não certificados no HEAD.

## Invariantes de janela UTC

- `baf8239b2a200b16b76c526342d105153c9b225a`: exige limites temporais com fuso UTC explícito antes da consulta.
- `8232c97aa1cf484d0a3e25d5dce2b4d834fff58a`: regressões de janela vazia legítima, fuso não UTC e timestamps sem fuso.
- Janela vazia com resposta `total_count=0` é válida apenas quando obtida da API paginada com filtros e cobertura verificáveis; resposta vazia do conector limitado não é equivalente.

## Identidade de execução e tentativa

- `1d43133eedba6c3b0070ff27325bb2ee000a60c1`: rejeita IDs não positivos, SHA fora do formato hexadecimal de 40 caracteres e `run_attempt` ausente/inválido; evita checkpoints sem proveniência mínima.
- `f49d812f61ebcd5fbecd1aaca59a774e81c6760c`: regressões offline negativas para essas entradas.
- **Limite adicional:** o inventário representa a tentativa mais recente por run e ainda não recupera o histórico de todas as tentativas; não declarar cobertura histórica completa. A execução offline local está pendente, pois a sessão não dispõe de checkout executável do repositório.

## Comparação segura entre checkpoints

- `4d0dbcae92f3b5768cac96ceceda8de3eff45896`: rejeita regressão ou ausência de `run_attempt` em runs já observados e divergência de metadados imutáveis (`event`, `created_at`) quando presentes nos dois snapshots.
- `0a32e2c2a2f987925ef1fbd7f6daf686e851174a`: testes negativos para regressão de tentativa e mudança de evento.
- **Escopo:** invariantes de comparação entre snapshots; **não** é coleta histórica de todas as tentativas, nem substitui leitura paginada da API.

## Limites
Código e testes versionados na branch da PR #1758. Não houve execução local independente nem comprovação de aprovação no novo HEAD nesta rodada. O conector de Actions disponível é limitado à primeira página de execuções por SHA de PR e à primeira página dos jobs por run; não comprova inventário global. Não houve rerun, merge, alteração de ruleset, LOCKED ou campanha científica.

**Próximo passo:** validar regressão offline no HEAD atualizado, coletar runs completos por API paginada e verificar master por evento `push` no SHA exato antes de declarar saúde comprovada.

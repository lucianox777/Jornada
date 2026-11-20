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

## Limites
Código e testes versionados na branch da PR #1758. Não houve execução local independente nem comprovação de aprovação no novo HEAD nesta rodada. O conector de Actions disponível é limitado à primeira página de execuções por SHA de PR e à primeira página dos jobs por run; não comprova inventário global. Não houve rerun, merge, alteração de ruleset, LOCKED ou campanha científica.

**Próximo passo:** validar regressão offline no HEAD atualizado, coletar runs completos por API paginada e verificar master por evento `push` no SHA exato antes de declarar saúde comprovada.

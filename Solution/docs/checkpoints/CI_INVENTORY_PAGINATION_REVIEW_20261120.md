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

## Precisão dos filtros temporais

- `0c3803947352919f47419550e0f782ce1ef1cecb`: rejeita limites UTC com microssegundos antes de formatar o filtro `created`, que opera com precisão de segundos. Sem a rejeição, o filtro descartaria silenciosamente a fração e a comparação local utilizaria outra janela.
- `8ded070cd28d534c66ce647b542751d63be3d6ba`: regressões offline verificam rejeição dos dois limites fracionários sem chamar a API.
- Testes ainda não executados localmente ou certificados na CI; alteração somente de observabilidade, sem efeito em LOCKED ou execução científica.

## Erros de transporte da API

- `b9421b3d45da9b68252cefb25cff1d895fb897ec`: captura explicitamente `urllib.error.URLError` (incluindo HTTPError) na execução CLI, preservando saída 2 e a indicação de inventário incompleto em caso de falha de transporte/HTTP.
- `6837521bfa5faa60e57d05bfe9eec56b6459d584`: regressão offline simulando HTTP 403, exigindo código 2 e ausência de escrita de snapshot.
- O teste foi versionado, **não executado** nesta sessão. Não há autorização de rerun ou merge, nem comprovação dos gates no HEAD atualizado.

## Proveniência de jobs por tentativa

- `989f9b9f01618316f98d456eed6102d4b592c789`: consulta de jobs falhos agora utiliza o endpoint de uma tentativa específica `/actions/runs/{run_id}/attempts/{attempt}/jobs`, com `run_attempt` observado na enumeração; evita consultar jobs da tentativa mais recente quando o workflow é reexecutado durante a coleta.
- `6e30fdac417a92c4221269cd7b8e07a9733902fa`: testes de vinculação à tentativa 3 e rejeição de IDs/tentativas inválidos antes de chamada externa.
- **Limite:** não enumera todas as tentativas históricas; mantém snapshot da tentativa observada, sem declarar cobertura histórica integral. Testes versionados, execução offline e CI ainda não comprovadas.

## Reconciliação de contagens nos checkpoints

- `2995b6c2a515b07c4def943a1ba7fde473bca442`: a comparação de snapshots valida, quando o campo `counts` está presente, as cinco categorias, inteiros não negativos e correspondência exata com os registros por categoria.
- `42e7645ee44985569931efa380db0cd2f9115a8c`: regressões negativas para distribuição adulterada e categorias ausentes.
- Snapshots legados sem `counts` ainda são aceitos para comparação por compatibilidade; **não** usar essa tolerância como certificação de reconciliação completa. Testes não executados nesta sessão; PR ainda sem aprovação de gates observável.

## Identidade do repositório em deltas

- `7ab59b083ae6ba167fad1e663622ccf299067ad3`: `compare_snapshots` exige que **ambos** os snapshots indiquem `lucianox777/Jornada`, não apenas que coincidam entre si; rejeita pares consistentes porém provenientes de outro repositório.
- `452727d17a0cd9c9ffaf4dd631f37de697958b79`: teste negativo com dois snapshots idênticos de repositório incorreto.
- Verificação de testes/CI ainda pendente. Tentativa de obter o checkout via `raw.githubusercontent.com` falhou por DNS no ambiente local; não inferir aprovação a partir da ausência de resultados na API limitada.

## Validação cruzada da tentativa do job

- `d35b98d833974ba10fcbb888473f008ed804b066`: IDs de job devem ser positivos; quando o payload de job apresenta `run_attempt`, esse número precisa coincidir com a tentativa consultada. A ausência do campo é tolerada para compatibilidade com respostas sem essa propriedade; não declarar prova cruzada nesse caso.
- `f2350c94298d7087824ff171ea37e43f4db05ec5`: testes negativos para tentativa divergente e ID zero.
- Execução offline e CI do HEAD **não verificadas**: o checkout remoto não está acessível no ambiente de execução, e a leitura limitada de workflows não certifica gates. Não houve merge ou autorização científica.

## Limites
Código e testes versionados na branch da PR #1758. Não houve execução local independente nem comprovação de aprovação no novo HEAD nesta rodada. O conector de Actions disponível é limitado à primeira página de execuções por SHA de PR e à primeira página dos jobs por run; não comprova inventário global. Não houve rerun, merge, alteração de ruleset, LOCKED ou campanha científica.

**Próximo passo:** validar regressão offline no HEAD atualizado, coletar runs completos por API paginada e verificar master por evento `push` no SHA exato antes de declarar saúde comprovada.

## Verificação de integração de testes — 2026-11-20

- A versão da branch de `.github/workflows/jornada-pr-required-fast.yml` **já inclui** `Solution/tests/scripts/test_ci_cumulative_inventory.py` na etapa `classify-impact` / `Check classification contract`, invocada por `python3 -m unittest` em `pull_request`, `push` na master e `workflow_dispatch`.
- Revisões consultadas na PR #1758: **nenhuma revisão retornada**. Não equivale a rejeição nem aprovação.
- A execução real da suíte continua **NÃO VERIFICADA**; o endpoint de workflows consultado para SHAs da branch não comprovou runs/gates e não oferece cobertura exaustiva. Tentativa local de baixar o código via raw GitHub bloqueada por resolução DNS. Não marcar VERDE.
- Próxima ação: obter evidência do job `classify-impact` para o SHA exato do HEAD, confirmar status das verificações obrigatórias e registrar contagens de testes efetivamente executados antes de propor merge. Não há autorização de merge nem de campanha científica.

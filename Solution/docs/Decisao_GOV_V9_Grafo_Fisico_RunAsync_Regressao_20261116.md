# GOV-V9-GRAFO-FISICO-RUNASYNC-REGRESSAO-20261116

## Contexto e evidência
Na issue #1584, a certificação científica TEST continua bloqueada. A inspeção do código após PR #1586 identificou uma modalidade física opt-in já existente em `V9PinnedSyntheticGraphCohortRunScorer`: consulta de grafo de candidatos SQL a partir do universo Silver congelado, seguida de comparação bidirecional com `blocking_chave`, antes da pontuação global e persistência dos lotes no `ProbabilisticLinkageBatchRunner.RunAsync`.

## Alteração proposta
Proteger por testes estruturais a ordem e a presença das guardas: modo SQL físico sem arestas fornecidas pelo chamador; leitura do grafo físico; comparação exata de contagem e hash canônico do grafo operacional; somente então scoring global pelo vencedor pinado; preparação da coorte antes da primeira persistência de lote; publicação proibida. Sem alteração de lógica produtiva.

## Justificativa, impactos e limites
Reduz risco de regressão silenciosa em uma ponte existente, mas **não executa integração SQL**, não compara todos os campos por observação, não demonstra ausência de vazamento e não certifica a paridade com o fluxo produtivo geral. A consulta de assinatura física e a lógica operacional de `blocking_chave` não substituem prova de todo o formato e das decisões de produção. Manter o gate `V9_TEST_PRODUCTION_FORMAT_PARITY_NOT_IMPLEMENTED`; TRAIN e TEST científicos não autorizados.

## Precedência e rastreabilidade
GOV-DOC-01; issue #1584; PR #1586; `Solution/src/Jornada.Linkage.Runner/V9PinnedSyntheticGraphCohortRunScorer.cs`; `Solution/src/Jornada.Linkage.Runner/ProbabilisticLinkageBatchRunner.cs`; `Solution/tests/scripts/test_v9_physical_runasync_graph_guards.py`. Estado: proposta em PR, não vigente na master até integração.

# Auditoria de integração V9 Runner — preparação da beta (18/11/2026)

**Natureza:** inventário técnico e execução de limpeza de branches já substituídos; não altera decisão científica nem autoriza ativação produtiva. A campanha científica #44329203788 é independente e não deve ser reiniciada por esta auditoria.

## Evidência já integrada à master

| PR integrada | Evidência funcional e limite |
| --- | --- |
| #1643 | Reaplicou ruleset obrigatório, substituindo a branch histórica #1640. |
| #1646 | Reaplicou startup gate e modelo pinado após integração anterior, substituindo #1642/#1645. |
| #1650 | Preflight de artefato SHA-pinned, sem fallback SQL; gate produtivo preservado. |
| #1652 | Construção de runtime V9 em memória a partir de artefato imutável; sem habilitar batch produtivo. |
| #1655 | Fronteira imutável e grafo físico sintético não publicante; gate `RUNNER_V9_PRODUCTIVE_ADAPTER_NOT_CERTIFIED` permanece. |
| #1658 | Chamada direta RunAsync com seleção V9 falha antes do lock e do SQL legado. |
| #1659 | Fronteira tipada; ainda não transporta runtime ao RunAsync produtivo. |
| #1677, #1680 | Montagem e confrontação do grafo operacional em SNAPSHOT; sem scoring nem ativação produtiva. |
| #1491 | Conversão lossless de vencedor para representação SQL operacional; não certifica paridade do Runner. |

## PRs históricas encerradas administrativamente

- **#1640**: substituída pela #1643; fechada sem merge da branch antiga.
- **#1642 e #1645**: substituídas pela #1646; fechadas sem merge das branches antigas.
- Cada PR encerrada contém comentário de rastreabilidade. Fechamento não implica certificação produtiva.

## Lacuna real para a beta V9

**Não declarar a integração operacional completa.** O conjunto de PRs integradas comprova preflight, modelo imutável e transporte diagnóstico do grafo, mas documenta explicitamente ausência de adaptador V9 produtivo certificado, scoring/resolução end-to-end e paridade com decisões físicas. A PR **#1654 permanece aberta**: seu replay de grafo pode ter sobreposição com #1677/#1680, mas a equivalência não está comprovada; substituída pela PR #1712 integrada, com gate produtivo ainda bloqueado.

### Critérios para avançar

1. Comparar o diff funcional da #1654 com o runtime atual da master; reaproveitar apenas lacunas reais e evitar regressão.
2. Implementar/conectar o adaptador V9 no caminho real RunAsync do Runner, preservando seleção imutável, fingerprint, fail-closed e ausência de fallback legado.
3. Demonstrar paridade end-to-end de candidatos, TF4, ranking, Resolve e persistência em SQL isolado com corpus sintético, inclusive falhas e rollback.
4. Somente após testes e evidências, submeter desbloqueio do gate produtivo à decisão de governança apropriada. Beta não equivale a ativação em produção.

## Referências

- PRs: #1640, #1642, #1643, #1645, #1646, #1650, #1652, #1654, #1655, #1658, #1659, #1677, #1680, #1491.
- Workflow científico independente: #44329203788.
- Governança: GOV-DOC-01; decisões vigentes de runtime imutável, grafo operacional e gates de ativação mantêm precedência.

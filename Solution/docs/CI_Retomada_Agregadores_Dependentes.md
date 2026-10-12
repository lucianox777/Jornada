# CI — Reexecução seletiva com agregadores fail-closed

**12/10/2026 — Correção de uma lacuna na recuperação após a fragmentação da CI.**

A CI passou a ter checkpoints de dois tipos: produtores que realmente executam testes, compilação ou análise (`unit-runtime`, `e2e-private-sql`, `security-codeql` etc.) e agregadores obrigatórios (`unit`, `e2e`, `security-analysis`), que validam o sucesso dos produtores e as evidências da mesma execução.

## O problema

Quando, por exemplo, o runner de `unit-runtime` perde conexão, o agregador `unit` falha **corretamente** porque um filho não está em `success`. A política antiga do watchdog exigia que **todos** os jobs com conclusão `failure` tivessem o texto inequívoco de erro de infraestrutura no log. Como o agregador falhava por uma asserção de dependência (não por problema de runner), a rotina deixava de recuperar um erro realmente infraestrutural, apesar de todos os demais jobs já terem passado.

## Ajuste fail-closed

O watchdog continua aceitando no máximo **uma reexecução seletiva** por run/SHA, em primeira tentativa, erro confirmado e HEAD atual. A ação continua sendo `POST /actions/runs/{id}/rerun-failed-jobs`, sem cancelamentos, sem iniciar workflow do zero, sem alterar código, revisão ou merge.

Além do erro de runner explícito, ele agora admite **somente** as três falhas de agregação dependente com nome de step conhecido:

| Agregador | Produtores que devem existir e estar concluídos |
|---|---|
| `unit` | `unit-contracts`, `unit-runtime` |
| `e2e` | `e2e-console`, `e2e-private-sql` |
| `security-analysis` | `security-guards`, `security-codeql` |

Para cada agregador que falhou, a **única** etapa com `conclusion=failure` deve ser o primeiro teste de sucesso dos filhos; todos os produtores devem existir, estar concluídos e ter conclusão `success` ou `failure`; **pelo menos um** produtor direto deve ter evidência inequívoca de falha de runner em seu próprio log. Falha de cobertura, evidência/artefato, SARIF, vazamento de segredo, testes NUnit, SQL, E2E ou outras mensagens ambíguas continua **não elegível**. Também não aceita `timed_out`, `cancelled`, `action_required`, jobs pendentes ou lista incompleta.

O registro `GITHUB_STEP_SUMMARY` agora distingue quantas falhas diretas têm prova de infraestrutura e quantos agregadores somente refletiram esse erro. Reexecutar jobs do mesmo run não transforma agregadores em aprovados: todos os testes e evidências continuam obrigatórios, e os agregadores só passam após a nova execução dos filhos.

## Testes e aceite

Testes isolados abrangem recuperação de `unit`, `e2e` e `security-analysis` após falha de runner, inexistência de pai, pai ignorado, ausência de log infraestrutural, etapa de evidência com falha, erro de negócio extra e regressão dos antigos testes de segurança. Critério de integração: CI integral na HEAD exata e publicação de evidências; confirmação posterior de `master`. O watchdog não simula pane real na CI, não amplia privilégios e não introduz retry irrestrito.

**Isolamento:** GitHub-hosted, token efêmero `actions:write` da própria rotina de supervisão; nenhum acesso a JornadaLocal, corpora reais IBGE, HML/PROD, NAS, NODE ou volumes locais.

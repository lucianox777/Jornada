# GOV-OPS-FRENTES-INDEPENDENTES-PARALELO-20261120 — execução paralela de frentes independentes

**Autoridade:** Product Owner, 20/11/2026 (data de referência da continuidade operacional). **Estado:** decisão aprovada; execução condicionada aos gates existentes. **Política:** GOV-DOC-01.

## Decisão e contexto
O Product Owner determinou: **“Trabalha em frente independentes, em paralelo.”** A solicitação surge durante a retomada da P0-A (#1734), após a integração das PRs #1779, #1780 e #1781 e com a evidência de `jornada-ci` pós-merge ainda incompleta. A ordem aprovada em 19/11/2026 permanece **P0-A → aceite administrativo DT-10 → P0-B** para **desbloqueios e execução dependente**, mas não impede preparação, auditoria, documentação e implementação estritamente independentes em paralelo.

## Regra operacional
Organizar frentes com escopos, branches/PRs, checkpoints, evidências e responsáveis lógicos separados; avançar sem aguardar tarefas de outras frentes quando não houver dependência técnica ou de autorização. Não presumir simultaneidade de execução física ou agentes de fundo: cada ação deve ser comprovada por commits, PRs, checks e registros.

- **Frente A — P0-A/CI (#1734):** investigar execução `push` de `jornada-ci`, `dependency-lock`, diagnósticos, checks e proteção efetiva da `master`; corrigir somente falhas demonstradas, com PR própria e gates.
- **Frente B — DT-10 (#1706):** reconciliar evidência física já registrada (run `44654406155`, artefato `14593242892`, cinco testes aprovados), critérios de aceite e pendências administrativas; sem refazer execução apenas por perda de contexto e sem declarar aceite por inferência.
- **Frente C — V9/P0-B (#1488, #1575, #1584):** preparar matriz de contratos, paridade do pacote vencedor, isolamento TEST e evidências faltantes; trabalho preparatório independente é permitido, **sem** TRAIN/TEST físico, uso da coorte one-use, alteração de LOCK, liberação de gates ou ativação operacional.
- **Frente D — governança e higiene:** manter GOV-DOC-01, índice e rastreabilidade; tratar PRs obsoletas apenas com prova de substituição e justificativa, sem limpeza em massa.

## Justificativa, impactos e precedência
A paralelização reduz espera ociosa sem converter dependências em autorizações tácitas. Cada frente preserva critérios de aceite e sua própria rastreabilidade; bloqueios de A não impedem levantamento documental ou testes estáticos independentes de B/C/D, mas **impedem** qualquer passo que dependa formalmente do gate. Prevalecem a ordem do PO de 19/11, DT-15, GOV-V9-TEST-ISOLAMENTO-RETROCOMPAT-20261117, a governança de LOCK e as demais decisões vigentes. Nenhum merge automático, CI verde presumida, autorização científica ou aprovação administrativa decorre desta decisão.

## Rastreabilidade
`Solution/docs/Plano_Desenvolvimento.md`; `Solution/docs/Indice_Decisoes_Vigentes.md`; issues #1734, #1706, #1488, #1575 e #1584; PRs #1779, #1780 e #1781. Este registro deve ser integrado à `master` com checks antes de ser considerado concluído.

# Diagnóstico e gate de transitividade do linkage

Status: política de agrupamento versionada e somente-leitura. Não autoriza, por si só, composição automática nem ativação em dados reais.

## Problema

O linkage probabilístico decide pares. Em cenários com mais de duas observações da mesma pessoa, podem ocorrer cadeias nas quais A-B e B-C são aceitos, mas A-C é rejeitado, inconclusivo ou sequer observado. Fechar essa cadeia por transitividade acrescentaria uma decisão que o score par-a-par sozinho não sustenta.

`PairwiseTransitivityDiagnostic` continua tornando esse risco mensurável. `ConservativePairwiseGroupingPolicy` acrescenta um gate explícito e determinístico para dizer se um conjunto está fechado o suficiente para seguir para uma etapa futura de composição.

## Política adotada

Versão: `PAIRWISE_GROUPING_COMPLETE_LINK_CONSERVATIVE_V1`.

A regra é complete-link conservadora:

- componentes candidatos são identificados pelas arestas explicitamente aceitas;
- um componente com dois ou mais membros só é `Eligible` se **todas** as combinações de pares entre seus membros estiverem explicitamente `Accepted`;
- qualquer fechamento ausente, `Rejected` ou `Inconclusive` torna todo o componente `Ambiguous`;
- pares rejeitados ou inconclusivos, sem aresta aceita, não formam composição;
- nenhuma decisão é inferida por caminho A-B-C;
- a política não cria threshold próprio: ela recebe o estado pairwise já decidido pelo modelo/gate anterior;
- entrada, orientação dos pares e ordem de chegada não alteram o resultado nem o fingerprint da evidência.

O relatório registra quantos pares eram necessários e quantos foram aceitos, rejeitados, inconclusivos ou não observados. Também gera `EvidenceFingerprintSha256` canônico para auditoria/replay da avaliação.

## O que o diagnóstico mede

Para um conjunto explícito de decisões par-a-par, `PairwiseTransitivityDiagnostic` registra:

- número de nós e arestas por estado (`Accepted`, `Rejected`, `Inconclusive`);
- componentes conectados formados somente pelas arestas aceitas;
- `OpenWedges`: cadeias A-B/B-C aceitas cujo fechamento A-C não foi aceito;
- separação entre fechamento ausente, rejeitado e inconclusivo.

O diagnóstico é determinístico e trata o par como não orientado. Pares duplicados e auto-pares são recusados.

## O que o gate não faz

Mesmo quando um componente é `Eligible`, esta política não:

- escolhe qual UUID canônico deve sobreviver;
- cria `IdentityCompositionDecision`;
- ignora as regras de âncora CPF e continuidade de UUID do `IdentityCompositionPlanner`;
- grava ledger, altera `canonical_uuid`, recompõe Gold ou publica resultado;
- ativa composição probabilística em produção;
- altera threshold, m/u, blocking ou score.

A escolha do UUID sobrevivente e a execução atômica pertencem à camada de composição/executor. Antes de qualquer ativação automática com dados reais continua obrigatória a avaliação representativa independente prevista nas issues de calibração/homologação.

## Relação com composição de identidade

`IdentityCompositionPlanner` permanece responsável por validar estruturalmente uma composição já proposta: componente fechado, versões esperadas, reservas, continuidade de UUID inicial e autoridade CPF. Ele não deriva a proposta a partir de scores par-a-par.

O fluxo passa a ser explicitamente fail-closed:

`score par-a-par -> diagnóstico -> gate complete-link conservador -> avaliação representativa -> decisão explícita de composição -> IdentityCompositionPlanner -> ledger/aplicação governada`.

Assim, A-B e B-C aceitos nunca bastam para fundir A, B e C quando A-C não foi também explicitamente aceito.
# Diagnóstico de transitividade do linkage

Status: diagnóstico técnico somente-leitura; não define política de clustering e não autoriza composição automática.

## Objetivo

O linkage probabilístico decide pares. Em cenários com mais de duas observações da mesma pessoa, podem ocorrer cadeias nas quais A-B e B-C são aceitos, mas A-C é rejeitado, inconclusivo ou sequer observado. Fechar essa cadeia por transitividade seria uma decisão de composição adicional que o score par-a-par, sozinho, não autoriza.

`PairwiseTransitivityDiagnostic` torna esse risco mensurável sem alterar identidade.

## O que mede

Para um conjunto explícito de decisões par-a-par, o diagnóstico registra:

- número de nós e arestas por estado (`Accepted`, `Rejected`, `Inconclusive`);
- componentes conectados formados somente pelas arestas aceitas;
- `OpenWedges`: cadeias A-B/B-C aceitas cujo fechamento A-C não foi aceito;
- separação entre fechamento ausente, rejeitado e inconclusivo.

O diagnóstico é determinístico e trata o par como não orientado. Pares duplicados e auto-pares são recusados.

## O que não faz

O diagnóstico não:

- executa union-find como decisão operacional;
- transforma componente conectado em pessoa canônica;
- escolhe densidade mínima, complete-link, single-link ou qualquer outra política de clustering;
- cria `IdentityCompositionDecision`;
- grava ledger, altera `canonical_uuid`, recompõe Gold ou publica resultado;
- altera threshold, m/u, blocking ou score.

A escolha de uma política de transitividade continua sendo decisão metodológica/institucional. O papel deste componente é produzir evidência objetiva para essa decisão e para a avaliação independente da issue #31.

## Relação com composição de identidade

`IdentityCompositionPlanner` permanece responsável por validar estruturalmente uma composição já proposta: componente fechado, versões esperadas, reservas, continuidade de UUID inicial e autoridade CPF. Ele não deriva a proposta a partir de scores par-a-par.

Assim, o fluxo permanece fail-closed: score par-a-par -> diagnóstico/avaliação -> política aprovada futura -> decisão explícita de composição -> `IdentityCompositionPlanner` -> ledger e aplicação governada.

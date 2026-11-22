# GOV-V9-TF4-REVERSAO-GRADE-PF-20261122 — reversão solicitada pelo Product Owner

**Data:** 22/11/2026. **Autoridade:** Product Owner. **Estado:** REVERSÃO INTEGRADA na PR #1879, após 17 verificações concluídas com sucesso, commit squash `2a336ec4b1215e6de544b34107557b9c35b94b24`. **Política:** GOV-DOC-01.

## Contexto e objeto

Após avaliar o run científico [#45002707113](https://github.com/lucianox777/Jornada/actions/runs/45002707113), concluído com sucesso no SHA `3b09e57266c0856ca307e3fae49531df06d981a9` (campanha #39), o PO declarou: **“Eu quero reverter.”** A solicitação foi feita em resposta à avaliação da reversão da alteração de grade TF4 introduzida pela PR #1866/issue #1865. Esta decisão é tratada como reversão **prospectiva do código/contrato da grade**, não como eliminação ou alteração retroativa de uma execução histórica de GitHub Actions. Se a intenção envolver também outra operação, é necessária confirmação específica.

## Reversão técnica proposta

Restabelecer o contrato anterior à PR #1866, obtido diretamente do pai do merge `21863691364786edef50a88035f97942eb8032e1`:
- PF/PL/MF/ML: `[0, 0.5, 1]`, resultando em `3^4 = 81` opções TF;
- margens: `[0.5, 2]`;
- M/U: `[0.5, 1]`, preservando `81 × 2 × 2 = 324` hipóteses;
- reverter os ajustes correspondentes no produtor de plano, auditoria, preaudit, handoff, smoke, workflows e testes afetados pela PR #1866.

**Precedência:** revoga para **novos estudos após integração** a decisão `GOV-V9-TF4-PF-GRADE-MARGEM-20261122`, retornando ao contrato de `GOV-V9-V3-TF4-324-20261111`. A decisão anterior permanece como registro histórico e não deve ser apagada.

## Justificativa, impactos e limites

**Justificativa:** exercício expresso da prerrogativa do PO de reverter a mudança de grade, independentemente de o workflow #45002707113 ter terminado com sucesso. O sucesso dos jobs não prova superioridade científica e tampouco invalida o pedido de reversão.

**Impactos:** modifica o espaço de busca de campanhas futuras e os contratos de cardinalidade dos seus testes/handoffs, mantendo orçamento total de 324 hipóteses. Pode alterar resultados científicos futuros. **Não** altera scorer, bloqueios produtivos, calibrador histórico congelado, artefatos e resultados dos runs #38/#39, nem promove modelos.

**Gates:** revisão de PR, CI obrigatória e verificação de compatibilidade; sem disparar TRAIN, TEST, 100k, HML/PROD ou ativação. O run #45002707113 é imutável e deve continuar referenciável como evidência da grade nova, mesmo após a reversão prospectiva.

**Rastreabilidade:** PR #1866, issue #1865, run #45002707113, PR #1879 integrada em `master`, política GOV-DOC-01.

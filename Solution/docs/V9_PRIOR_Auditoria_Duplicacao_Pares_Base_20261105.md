# V9 — revisão de prior: arestas SQL deduplicadas versus repetições da mesma identidade

**Decisão:** o prior continua **calculado automaticamente**, não é parâmetro de escolha. A auditoria é somente TRAIN técnico, sem reajuste do snapshot, score, DT-15 ou confirmação científica.

## Recuperação de engenharia

- PR #227 retirou o uso de `1/candidates.Count` no scorer (dependência indevida da cardinalidade do blocking).
- PRs #349 e #350 mediram, em diagnóstico read-only, `truth_candidate_pairs/total_candidate_pairs` da **união deduplicada** dos passes sobre observação×UUID. Essa estimativa é condicional ao ruleset e à população amostrada com CPF; não foi promovida por falta de transportabilidade demonstrada ao fluxo sem CPF.
- A V9 corrente (PR #1093) calcula `(M+alpha)/(M+U+2alpha)` com os pares de observações do SQL TRAIN, não um prior por identidade-base nem derivado exclusivamente do IBGE. A V3 histórica manteve o prior V2 ao alterar apenas M/U.
- PRs #984/#1009 já mediram concentração por base e ESS diagnóstica. Isso não valida observações independentes nem a estimação operacional de prior.

## Prova de não duplicação entre passes

A V9 física usa `SELECT DISTINCT s1.obs_id AS a,s2.obs_id AS b,s1.part` e índice SQL `UNIQUE(part,a,b)`. O SHA de arestas físicas e igualdade com a contagem lida são verificados **antes do fit**. Assim, **o mesmo par de observações, recuperado por passes diferentes, não é contado duas vezes** no caminho físico atual.

A mesma pessoa-base com várias observações pode, legitimamente, contribuir com **vários pares distintos de observações**. Isso não é duplicação de aresta e não deve ser removido silenciosamente: alterar a unidade/ponderação do prior demanda um estimador explícito, com provas de amostragem.

## Implementação diagnóstica

`V9TrainCandidatePriorDiagnostic` recebe exclusivamente TRAIN físico SHA-vinculado e publica contagens M/U de pares de observações, prior bruto do par candidato, grupos M únicos, pares U únicos e não ordenados de bases, números de repetições, maior incidência por base, cobertura de pessoas-base pré-blocking e uma **sensibilidade colapsada por base**, explicitamente NÃO calibrada nem transportável. Não exporta identidades ou dados individuais.

A nova instrumentação reconcilia o **prior original do snapshot** com as contagens TRAIN e alfa e falha fechado em caso de divergência. Não muda scores, modelo congelado, TF, 81 células, regras SQL, nem formulação operacional. Depois de rodar o gate físico, comparar a sensibilidade com o prior histórico V3 e realizar ablação contrafactual, sem escolher valores com holdout.

**Nenhum resultado físico novo foi obtido nesta documentação.** O gate técnico seed42 pode rodar automaticamente na PR; campanhas confirmatórias e novas seeds permanecem manuais.

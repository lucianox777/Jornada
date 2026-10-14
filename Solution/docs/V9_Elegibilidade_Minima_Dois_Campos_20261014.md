# V9 — Elegibilidade mínima e decisão progressiva (proposta diagnóstica)

Issue #999. Esta especificação não ativa V9/DT-15 nem modifica Rank/Resolve, Gold, SQL operacional, IBGE original, V8, HML/PROD ou JornadaLocal.

## Regra mínima

Para o ramo **probabilístico**, uma observação só é elegível quando possui pelo menos **dois campos utilizáveis** dentre: nome da pessoa, nome da mãe e data de nascimento. Valor vazio, placeholder (por exemplo `NÃO INFORMADO`), data inválida ou sentinela não conta. Presença não implica concordância com candidato; uma decisão positiva exige evidência estatística calibrada, ausência de conflitos e tratamento de concorrentes. CPF válido segue exclusivamente o contrato determinístico existente, fora desta regra.

Combinações elegíveis: pessoa+mãe, pessoa+DOB, mãe+DOB e os três. Zero ou um campo: abstenção probabilística explícita, nunca identidade inferida. A combinação mãe+DOB deve ser tratada como estrato de maior cautela, sujeito à certificação específica. Casos com dois campos disponíveis mas divergentes podem exigir abstenção; não se deve forçar correspondência.

## Experimento exigido antes de implementação operacional

1. Medir, por seed e estrato, elegibilidade, abstenções, M_total, M_elegível e M recuperado; reportar recall com **ambos os denominadores** para evitar inflação artificial por exclusão.
2. Executar blocking SQL em ordem de seletividade observada, do exato para prefixos, fonética e tokens. Deduplicar pares por identidade e registrar ganho marginal M/U, fan-out P50/P95/P99 incluindo zeros, consultas evitadas e custo real. Não encerrar antecipadamente por mera igualdade: demonstrar que nenhuma decisão mais segura seria perdida, inclusive com homônimos e conflitos de CPF/UUID.
3. Usar frequências agregadas IBGE de **nomes femininos do Brasil** para mãe e **nomes de São Paulo** para pessoa apenas quando unidade, período, sexo e proveniência forem compatíveis. Distribuições conjuntas m/u e TF por pessoas-base únicas são estimadas em TRAIN no frame SQL real; não multiplicar marginais IBGE para inventar distribuição conjunta.
4. Selecionar política, limiar, pesos e abstenção em TRAIN/VALIDATION; congelar snapshot/fingerprint antes de abrir TEST cego 42/43/44. Reportar limites superiores de FDR, recall, suporte efetivo por cluster M e rede U, e critérios de insuficiência amostral; resultado inconclusivo bloqueia promoção.
5. Validar NUnit, verificador Python independente, SQL físico nas três seeds, segurança/CodeQL, E2E e CI completa na HEAD exata. Gate estatístico reprovado não pode ser convertido em PASS por build verde.

## Critério de fechamento

PR diagnóstica pode ser integrada sem promover V9. Promoção final só com evidência conclusiva para o orçamento de risco versionado, cobertura, desempenho e revisão técnica. Se não houver configuração certificável, declarar **INCONCLUSIVO/REPROVADO**, com déficit quantificado, sem modificar os limiares após olhar TEST.

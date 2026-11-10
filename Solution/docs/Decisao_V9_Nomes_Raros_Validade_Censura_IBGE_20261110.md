# GOV-V9-NOMES-RAROS-CENSURA-IBGE-20261110 — validade de nomes e hipótese de frequência censurada

**Data:** 10/11/2026. **Autoridade:** Product Owner. **Estado:** princípio interpretativo aprovado; modelagem quantitativa **proposta para avaliação**, não autorizada para produção. **Rastreabilidade:** conversa de decisão do PO, issue #1349, PRs #1406–#1409, campanha física #42889542560, Nota Técnica 01/2025 do IBGE (https://biblioteca.ibge.gov.br/visualizacao/livros/liv102228.pdf).

## Decisão interpretativa do PO
Nomes raros ou únicos **não são impossíveis**. Ausência na publicação do IBGE não significa nome inválido nem probabilidade zero. Diferenciar frequência baixa conhecida, frequência não publicada, frequência não observada e falha de normalização/consulta. Nenhuma frequência numérica, suavização ou fator de risco pode ser inferido automaticamente de um campo ausente.

## Regra pública e interpretação
A Nota Técnica 01/2025 do IBGE estabelece, para divulgação de nomes no Censo 2022, pelo menos 20 ocorrências no Brasil, 15 por UF e 10 por município (considerando os recortes aplicáveis). O limite **nacional** de supressão permite, **somente se o nome de fato foi observado e suprimido por esse limite**, interpretar a contagem brasileira censurada como inteiro em [1,19]; se N é o denominador pertinente do Censo, a frequência estaria em [1/N,19/N]. Não confundir frequência do Censo 2022 com a frequência populacional posterior, nem utilizar limites estaduais/municipais como se fossem o nacional. A ausência na publicação, sozinha, não comprova que o nome existiu no Censo: pode representar zero observações, variação ortográfica, problema de classificação, surgimento posterior ou outro motivo.

## Hipótese estatística proposta, ainda não aprovada para mudança científica
O PO perguntou se é possível **usar o limite menor que 20** como informação para o TF. É tecnicamente possível estudar um tratamento intervalar/censurado da contagem nacional, com pesos TF limitados, hipótese de monotonicidade explicitada, validação fora do TRAIN, recall, FDR pós-Resolve, cobertura, distribuição de abstenções e análise separada de grafias degradadas e sobrenomes/proxies. A regra censurada **não determina automaticamente uma estimativa pontual** e não autoriza substituir os valores por 0, 1, 19 ou 1e-6 nem assumir que a ausência nacional tenha sempre [1,19].

## Precedência, impactos e limites
Complementa a interpretação da ausência de nome e a investigação da falha FS na campanha #42889542560, cujo último checkpoint foi FS_VALIDATION_EVALUATE; **a causa TF não está confirmada**, PR #1409 instrumental pendente de gates. Não altera agora `RejectOperation`, `AbstainCandidate`, algoritmo FS, TF, M/U, suavização, 81 células, FDR, seed42, 24/16/8, corpus, TEST/Gold ou autorizações operacionais. Uma escolha de política/estimador para produção exige decisão formal posterior e evidência estatística comparativa.

**GOV-DOC-01:** registrar por PR e manter índices oficiais de decisões e acervo sincronizados antes de declarar integração.

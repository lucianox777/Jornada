# Linkage PostgreSQL — rótulos independentes e estimação ponderada diagnóstica

Estado: terceira fatia técnica da issue #31. Não constitui homologação estatística, aprovação institucional, calibração operacional ou autorização de ativação. SQL Server permanece canônico; Fabric permanece analítico.

## Contrato de entrada e independência

`CandidateLabeling` recebe a captura congelada do desenho `CANDIDATE_SRS_TWO_STAGE_V1` e um inventário completo de rótulos e vetores de comparação. O manifesto vincula os fingerprints do quadro e da seleção, versões de blocking, normalização e features, política de rotulagem, referência de partição e atestação externa. A implementação exige a política `INDEPENDENT_LABEL_POLICY_V1`; a existência de uma referência ou de um hash não comprova, sozinha, a autenticidade ou a independência de uma fonte institucional. A custódia, a verificação da evidência original e a aprovação da política são responsabilidades governadas externas.

Cada par selecionado exige exatamente um rótulo e um vetor. Os únicos rótulos são `Match`, `NonMatch` e `Inconclusive`. A origem admissível é uma referência governada independente ou adjudicação independente. CPF ausente, UUID diferente, score, resultado de blocking e coincidência com a Gold não são rótulos negativos. A evidência de rotulagem possui referência opaca, fingerprint e data; o módulo não recebe documentos brutos nem utiliza o score para produzir verdade de referência. Os valores pessoais necessários à comparação ficam em um contrato restrito de entrada; o extrator converte-os a estados e não os inclui no resultado agregado.

As partições de treinamento e avaliação são fornecidas e congeladas externamente. Todas as observações de uma fonte pertencem à mesma partição, e um identificador de grupo de independência não pode atravessar as duas. O grupo deve ser atribuído por uma referência externa capaz de identificar fontes relacionadas à mesma pessoa ou unidade de independência, sem utilizar a decisão do modelo em avaliação. A aplicação verifica a consistência declarada, mas não consegue descobrir sozinha todos os vínculos desconhecidos entre fontes. O quadro institucional completo e a separação de corpus continuam exigindo atestação e avaliação de representatividade.

O validador confere a correspondência exata dos pares, ausência de duplicações, máscaras e pass primário, probabilidades e pesos reconstruídos dos denominadores, consistência dos estratos, contagens ponderadas e compatibilidade de versões. Nenhum peso fornecido pelo chamador é aceito por conveniência. A ausência de qualquer rótulo ou vetor, uma partição incompleta ou divergência de fingerprint interrompe a preparação. A captura MVCC não é um arquivo permanente do corpus: reprodução posterior exige preservar o quadro e a referência do corpus congelado sob custódia autorizada. O contrato não cria um mecanismo de armazenamento ou um endpoint público.

## Estimador separado

`CandidateWeightedEstimator` recebe somente um corpus previamente validado. Esta primeira versão exige rotulagem completa de todos os pares selecionados; não executa ajuste de não resposta. Qualquer rótulo inconclusivo bloqueia a estimação. Também exige presença das duas classes no treinamento e na avaliação, pelo menos dois grupos independentes por classe de treinamento e tamanho efetivo mínimo configurável. Esses mínimos são controles técnicos, não critérios suficientes de precisão estatística. A eventual subamostragem adicional para rotulagem exigirá uma nova versão com probabilidades condicionais conhecidas e tratamento explícito de não resposta; não é correto multiplicar pesos por uma taxa de resposta estimada sem justificar o mecanismo de seleção.

A população-alvo é a união deduplicada dos candidatos gerados pelo blocking V2 para o quadro de fontes atestado, não todos os pares possíveis de cidadãos. O peso de desenho de cada par é o inverso da probabilidade de inclusão em dois estágios, já calculado pelo amostrador. Para uma classe e um estado de comparação, a distribuição diagnóstica usa:

```text
W_k = soma_i w_i * I(estado_i = k)
p_k = (W_k + alpha) / (soma_j W_j + K * alpha)
n_efetivo = (soma_i w_i)^2 / soma_i (w_i^2)
```

A suavização é explícita, positiva e limitada; não corrige viés de seleção, erro de rotulagem ou dependência. O estimador não utiliza rótulos nem features da avaliação no cálculo. O resultado contém somente distribuições de treinamento, totais ponderados, grupos, tamanhos efetivos e referências de versão/proveniência. Não produz prior, T_LINKAGE, margem, pesos de score, UUIDs, vínculo operacional ou modelo ativável. Não escreve no banco e não é registrado no worker operacional.

## Evidências e dependências

Nesta fatia, nome e nome da mãe utilizam o comparador canônico, com `MISSING` distinto de `LOW`. Nascimento é uma única evidência conjunta com oito estados de concordância dia/mês/ano e um estado de ausência. O bit 1 representa dia, o bit 2 mês e o bit 4 ano; por exemplo, `111` é concordância completa e `011` é dia e mês concordantes com ano divergente. Não se somam três pesos marginais de nascimento como se fossem independentes. A dependência entre nome, mãe e outros atributos ainda precisa ser avaliada antes de qualquer versão operacional.

Todos os campos preservados continuam evidências candidatas conforme a ADR universal. Telefone, e-mail, distrito informado pelo Gestor, endereço residencial, endereço de residência, referência territorial e demais campos não são automaticamente habilitados nesta versão. A inclusão exige semântica, proveniência, qualidade, comparadores, dependências, finalidade e calibração próprias. Não se infere domicílio a partir de unidade de atendimento.

## Próximos gates

Os testes sintéticos verificam completude, pesos adulterados, proveniência, duplicações, partições, vazamento de grupos, estados ausentes, nascimento conjunto, suavização, separação da avaliação e falha fechada. O CI compila os projetos reais e executa as regressões sem alterar os gates existentes. Isso não equivale a precisão, recall, calibração de probabilidades, risco de falso vínculo, desempenho por subgrupo, custo de blocking ou escala demonstrados em dados reais.

A próxima fase deverá definir e atestar um corpus institucional representativo, preservar o desenho e a custódia dos rótulos, quantificar não resposta e qualidade da verdade de referência, realizar avaliação independente e incerteza compatível com a amostragem em conglomerados, e validar conjuntamente as evidências. Uma nova versão de modelo e um caminho de promoção governado serão necessários para qualquer uso operacional. O método piloto `M_INTERGESTOR_U_GOLD_MVCC_V2` continua bloqueado para validação real/ativação; esta entrega não altera o estimador canônico, a precedência determinística do CPF, os thresholds, o runner ou a publicação Gold/Serving.

# V9 — elegibilidade de duas evidências no mesmo SQL físico de 14 passes (somente diagnóstico)

Relacionado à issue #999 e às PRs #1021–#1023. Esta entrega liga o contrato
`V9MinimumEvidenceEligibility` ao censo **efetivamente executado** no SQL Server
`JornadaHarness` DEV, sem incluir um novo passe nem mudar o modelo de
decisão. Nome da pessoa (P), nome da mãe (M), data de nascimento (D) são os
três grupos; os casos `PM`, `PD`, `MD` e `PMD` exigem **dois grupos
utilizáveis por observação**. \`NONE\`, \`P\`, \`M\`, \`D\` abstêm-se do ramo
probabilístico no contrato anterior. **Nova hipótese pedida para teste**:
o **nome da pessoa é obrigatório**, acompanhado de **nome da mãe OU
data de nascimento utilizável**; somente \`PM\`, \`PD\` e \`PMD\`
são elegíveis no novo cenário. \`MD\` passa a abster-se. O cenário é
`V9_PERSON_REQUIRED_PLUS_MOTHER_OR_BIRTH_DIAGNOSTIC_V1`, comparado
paralelamente ao anterior, sem mudar contrato operacional. CPF segue
exclusivamente o fluxo determinístico existente.

## Dois denominadores — nunca confundir cobertura do SQL com decisão FS

`V9TwoFieldSqlCoverageAudit` observa o **mesmo conjunto de pares fisicamente
retornados** pela união deduplicada dos 14 passes já investigados (ATIVO+C1,
seis passes, cinco passes de ano/fonética, pessoa prefixo3+DOB, mãe
prefixo3+DOB, pessoa prefixo3+mãe prefixo3). Nenhum parâmetro nem predicado
SQL é selecionado ou alterado. O relatório está aninhado em
`OtherPrefix3PairsTrainValidation.TwoFieldSqlCoverage`, ao lado das métricas
originais de 14 passes.

Em TRAIN e VALIDATION, para **observações sem CPF**, o gabarito sintético M é
enumerado **ANTES** de consultar a recuperação SQL: pares da mesma
identidade-base em gestores distintos, independentemente de conterem DOB.
Depois se verifica, par a par, se **ambas** as observações satisfazem a regra
mínima. A enumeração inclui casos PM sem DOB e casos inelegíveis; o
denominador nunca é selecionado a partir dos hits SQL.

- `AllNoCpfTruthM`: todos os pares M intergestor, sem CPF, incluindo os
  inelegíveis (limite superior populacional do corpus sintético).
- `EligibleTwoFieldTruthM`: subconjunto M em que as duas observações têm
  pelo menos dois grupos P/M/D utilizáveis;
  `IneligibleTruthM` é a diferença.
- `LegacySqlEligibleM`: pares M retornados fisicamente pelo SQL dos 14
  passes e com **ambos** os lados elegíveis. Repetições entre passes
  contam uma vez. `LegacySqlEligibleU` mede a exposição falsa de candidatos
  elegíveis, **não** falsos vínculos aceitos.
- `RecallAgainstAllNoCpfTruthM = LegacySqlEligibleM / AllNoCpfTruthM`
  distingue cobertura efetiva do ramo probabilístico no corpus completo.
  `RecallAgainstTwoFieldEligibleM = LegacySqlEligibleM /
  EligibleTwoFieldTruthM` mostra cobertura só no subconjunto elegível.
  O teto de elegibilidade `EligibleTwoFieldTruthM / AllNoCpfTruthM`
  informa quanto seria impossível atingir por política que abstém com
  menos de duas evidências; não é meta de aprovação por si.
- `EligibleTruthMWithMissingBirth`: M elegíveis em que **pelo menos uma**
  das observações possui DOB **fisicamente ausente (NULL)**. O SQL de 14
  passes continua excluindo DOB ausente nas pontas e **não pode recuperar**
  esse estrato. Importante: data sentinela (ex. 1900-01-01) tem DOB
  fisicamente presente no SQL, mas **não** conta como evidência D utilizável;
  pode ser fisicamente recuperada pelo SQL antigo se houver nome+mãe, sem
  que a sentinela passe a ser uma evidência legítima.
- `NameRequiredSplits`: medição independente de `P AND (M OR D)`
  nas mesmas observações e nos mesmos 14 passes SQL: M elegível/excluído
  (ambas as pontas obrigatoriamente elegíveis), M/U fisicamente
  recuperados, exclusões, déficit em relação a 95% e recalls tanto
  contra **todos** os M quanto apenas contra M elegíveis segundo o novo
  cenário. `EligibleTruthMWithoutSqlBirth` conta casos de DOB
  **fisicamente ausente** dentro de M elegíveis pela regra nome+mãe.
  Cuidado: elevar o recall condicional por exclusão do estrato MD
  **não significa** aumentar recuperações no universo nem atingir 95%.
  A medição deve informar explicitamente a cobertura/abstenção e o teto
  de recall no universo total.
- `LegacySqlIneligibleM/U` expõe hits do SQL antigo com uma ponta com
  menos de duas evidências, que a política candidata não pode aceitar
  automaticamente. `AdditionalEligibleMNeededFor95` usa exclusivamente
  o denominador de M elegíveis deste novo contrato, arredondamento inteiro
  por split, sem presumir que 95% foi atingido.

Quando o denominador é zero o recall é **null/desconhecido**, nunca 0% ou
100% inventado. `FdrMeasured=false` e
`MayPromoteModelOrBlockingPolicy=false` sempre. Nem U SQL nem recall
de *candidate generation* medem falsos vínculos aceitos, FDR ou FS final.

## Integridade, independência e segurança

O censo de origem usa **somente** os ordinais efêmeros SQL, identidades-base e
campos normalizados na memória do processo; o JSON exporta **apenas totais e
contagens por combinação**, sem nomes, CPF, chave de pessoa-base ou lista de
pares. Duplicidade de observação, partição TEST, par invertido, gestor igual,
label M/U incompatível com identidade-base, SQL fora do frame, cap excedido ou
SQL recuperando pares sem DOB **fisicamente presente** causam falha fechada.
A reconciliação compara `LegacySqlM/U` com `FourteenM/U` do censo já
integrado; Python recalcula aritmética e reconciliação das três sementes.
Os testes NUnit incluem PM sem DOB perdido pelo SQL, inelegíveis, U,
sobreposição deduplicada, CPF fora do ramo probabilístico e negativos.

As três seeds 42/43/44 usam massas-base **sintéticas** de 30 mil pessoas
cada, calibradas quanto à distribuição nominal contra referência agregada
IBGE. Isso **não** prova suficiência de suporte M/U, frequência TF,
independência entre pares nem transportabilidade para 11 milhões de
registros municipais. Em especial, não usar a contagem de extremidades U
como amostra independente para escolher m/u ou TF.

## Gate e sequência necessária

Os resultados novos devem ser lidos no artefato agregado da CI do HEAD
desta PR; não são presumidos antes de medição efetiva. A antiga taxa de
~87% é o recall de **blocking dos 14 passes com DOB**, num denominador
mais restrito, e não deve ser descrita como recall de duas evidências ou
do linkage aceito.

O **patamar de 95% é prioridade**. Ele não pode ser obtido artificialmente
reduzindo o denominador do recall: comparar sempre `AllNoCpfTruthM`,
`EligibleTwoFieldTruthM`, `NameRequiredSplits.EligibleTruthM` e quantos
M/U de fato retornaram do SQL. Caso a elegibilidade nome+mãe/nascimento
reduza o teto no universo total para menos de 95%, registrar a tensão
explicitamente e não afirmar sucesso operacional. A alternativa será
melhorar a recuperação física para o universo *elegível* sem relaxar a
segurança de vínculos aceitos.

Próximo incremento, sem tocar TEST para seleção: medir consultas SQL reais
para casos PM **sem DOB**, custo incremental U/fanout/latência e eficácia
por estrato, depois calibrar 81 células m/u e TF na massa-base de TRAIN,
selecionar o risco em VALIDATION e só então aplicar snapshot congelado ao
TEST cego, com incerteza por clusters M e rede U. Se 30 mil identidades
não sustentarem os limites de risco, declarar **INCONCLUSIVO** e
dimensionar nova massa de treinamento sem reajustar TEST.

Nenhuma alteração em rule set ATIVO, V8, V9 operacional, DT-15,
Rank/Resolve, Gold, referência IBGE original, JornadaLocal, NODE/NAS,
HML ou PROD. Gate verde de compilação/CI **não** é aprovação estatística.

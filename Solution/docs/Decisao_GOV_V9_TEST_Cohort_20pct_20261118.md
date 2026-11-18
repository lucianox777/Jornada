# GOV-V9-TEST-COHORT-20PCT-20261118 — Cohort independente de 20% no TEST

**Autoridade:** Product Owner, 18/11/2026. **Estado:** APROVADA PELO PO; implementação proposta em PR, aguardando CI e integração. **Política:** GOV-DOC-01.

## Contexto e evidências
A campanha científica #44329203788 gerou 100.000 pessoas em cada corpus independente. A VALIDATION avaliou 18.327 identidades-base (33.861 observações); o TEST avaliou 92.004 identidades-base (170.369 observações). Foram medidos respectivamente 2 e 90 falsos aceites e FDR de 0,00809% e 0,07184%. A comparação dos **números absolutos** não é equivalente entre universos de tamanhos diferentes; as taxas são comparáveis com ressalvas quanto à competição de candidatos e incerteza estatística. Não há evidência de que a calibração ou o vencedor congelado estejam incorretos.

O leitor `V9IndependentTestCorpusReader` antes ignorava a partição interna do gerador e convertia todas as observações sem CPF para TEST. A VALIDATION utiliza a partição interna VALIDATION. O desenho prospectivo V9-TF-002 descrevia 20% para VALIDATION e 20% para holdout, mas era condicional.

## Decisão e justificativa
Manter geração e integridade de **100.000 identidades** para cada corpus; no TEST independente, avaliar **somente** a partição `TEST` (~20% da população própria), aplicando a seleção por identidade-base, nunca por linha. As demais partições são verificadas para integridade e excluídas da avaliação física. Preservar seed reservada inédita, manifesto e hashes, namespace próprio, isolamento de VALIDATION/TRAIN, único vencedor imutável, replay físico e proibição de refit/ativação.

**Justificativa:** alinhar o contrato de amostragem de VALIDATION e TEST, tornando os volumes de avaliação aproximadamente comparáveis e evitando interpretação indevida de 2 versus 90 falsos positivos. Não se exige igualdade das taxas ou contagens: diferenças de composição, competição e amostragem permanecem possíveis.

## Impactos, precedência e restrições
Esta decisão **esclarece e altera apenas o escopo da coorte efetivamente avaliada** na decisão GOV-V9-TF4-TEST-INDEPENDENTE-MODELO-UNICO-20261113: a referência a “TEST 100k” continua significando população **gerada**, não 100k identidades **avaliadas**. Prevalece sobre a leitura anterior de censo integral de observações para avaliação. Não revoga independência, selagem, one-use, controle de exposição ou gates de certificação. Não altera TRAIN, VALIDATION, 324 hipóteses, TF4, M/U, seleção do vencedor, FDR-alvo ou parâmetros calibrados.

O TEST já exposto em #44329203788 **não pode ser recortado retroativamente** e apresentado como novo teste confirmatório. Qualquer medição científica nova requer corpus/seed independentes ainda não expostos, protocolo congelado e aprovação dos gates. O resultado anterior permanece evidência histórica válida do seu universo original, mas não prova comparabilidade absoluta com VALIDATION.

## Implementação e rastreabilidade
- `Solution/src/Jornada.Linkage.Parameters.Worker/V9IndependentTestCorpusReader.cs`: filtrar partição `TEST` antes de incluir observações, preservando verificação integral de 100k e hash do corpus; guarda de 19.000–21.000 identidades TEST sobre 100.000 (faixa de admissão conservadora em torno dos 20% esperados), com reporte explícito do tamanho e percentual efetivos. O gerador aplica partição Bernoulli por identidade-base: sorteio <0,6 TRAIN, <0,8 VALIDATION, caso contrário TEST. O mesmo percentual **nominal** de 20% não implica números realizados idênticos em seeds independentes; não se deve impor igualdade artificial ou selecionar identidades após observar métricas.
- `Solution/tests/scripts/test_v9_tf4_standalone_test.py`: regressão do contrato de partição e ordem das verificações.
- `Solution/docs/Indice_Decisoes_Vigentes.md`: registro indexado.
- Evidência anterior: execução #44329203788. Implementação em PR dedicada; CI, ensaio físico e integração ainda pendentes. **Sem certificação, publicação ou ativação.**
